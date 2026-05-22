# Análise do Desafio — Arquiteto de Software

## 1. Contexto do Desafio

Um comerciante precisa de um sistema para **controlar seu fluxo de caixa diário**, com duas capacidades principais:

- **Lançamentos**: registrar débitos e créditos.
- **Consolidado diário**: consultar o saldo consolidado por dia.

O escopo de negócio é deliberadamente simples. O que está sendo avaliado é a **profundidade das decisões arquiteturais**, não a complexidade do domínio.

---

## 2. Persona — Carlos Mendes, comerciante

Embora o desafio peça a entrega de **serviços** (não uma aplicação com interface), mapear o usuário final ajuda a tomar decisões melhores sobre design de API, nomenclatura de endpoints e contratos de dados. A implementação do frontend está documentada em Evoluções Futuras (seção 13).

### 2.1. Perfil

| Atributo | Detalhe |
|---|---|
| Nome | Carlos Mendes |
| Idade | 47 anos |
| Ocupação | Dono de mercearia de bairro |
| Faturamento | ~R$15.000/mês |
| Controle atual | Caderno físico + planilha Excel básica |
| Literacia digital | Baixa — usa celular (WhatsApp, app de banco) mas não sabe o que é API ou Postman |

### 2.2. Necessidades principais

- Saber quanto entrou e saiu no dia de forma rápida
- Ver o saldo consolidado sem precisar fazer conta
- Não errar nas contas do caixa
- Gastar menos tempo com controle manual

### 2.3. Frase-chave

> "Eu só quero saber se o dia fechou no azul ou no vermelho."

---

## 3. Jornada Diária do Usuário

| Etapa | Momento | Ação | Endpoint correspondente |
|---|---|---|---|
| Abertura do caixa | 7h | Registra troco inicial como crédito | `POST /api/lancamentos` |
| Lançamentos | Ao longo do dia | Registra cada venda (crédito) ou despesa (débito) | `POST /api/lancamentos` |
| Olhada rápida | Intervalos | Quer ver como está o dia | `GET /api/consolidado/{data}` |
| Fechamento | 19h | Confere saldo consolidado, compara com caixa físico | `GET /api/consolidado/{data}` |
| Histórico | Eventual | Compara dias anteriores, prepara dados p/ contador | `GET /api/consolidado?de={data}&ate={data}` |

A jornada mostra que o sistema é essencialmente **dois endpoints com alta disponibilidade** — o que reforça a decisão de manter a solução enxuta.

---

## 4. Requisitos Não-Funcionais

| Requisito | Implicação Arquitetural |
|---|---|
| O serviço de lançamentos **não pode cair** se o consolidado cair | Desacoplamento via mensageria assíncrona — os dois serviços não se chamam diretamente |
| Consolidado suporta **50 req/s** em picos | Rate limiting no próprio serviço com middleware nativo do .NET 8 |
| No máximo **5% de perda** de requisições | Retry + Circuit Breaker + Dead Letter Queue no consumidor de mensagens |

Esses três requisitos definem a arquitetura inteira e eliminam qualquer abordagem onde o consolidado é calculado de forma síncrona dentro do serviço de lançamentos.

---

## 5. Decisões Arquiteturais

### 5.1. CQRS (Command Query Responsibility Segregation)

O enunciado pede literalmente dois serviços com responsabilidades distintas:

- **Command side** → API de Lançamentos (escrita)
- **Query side** → API de Consolidado (leitura)

O modelo de leitura (saldo consolidado) é uma **projeção** derivada dos eventos de escrita.

**Trade-off**: consistência eventual entre lançamento e saldo — aceitável para este tipo de consulta financeira (não é uma transferência bancária em tempo real).

### 5.2. Mensageria Assíncrona com RabbitMQ + MassTransit

O requisito de que o serviço de lançamentos não pode cair junto com o consolidado exige **desacoplamento temporal**. A API de Lançamentos publica um evento `LancamentoRegistrado` no broker. O Worker de Consolidação consome e atualiza a projeção.

- Se o Worker cair, as mensagens ficam na fila.
- Quando volta, processa o backlog e se atualiza.
- O serviço de lançamentos nunca sabe se o consolidado está de pé.

**Por que RabbitMQ (e não Azure Service Bus)?**

O desafio exige execução local com Docker. RabbitMQ roda em container com um comando e inclui UI de gerenciamento. Usando **MassTransit** como abstração, a troca para Azure Service Bus em produção é uma mudança de configuração (`cfg.UsingAzureServiceBus(...)` em vez de `cfg.UsingRabbitMq(...)`) — sem alterar uma linha de código de negócio.

### 5.3. Datastores

Uma instância PostgreSQL em Docker com **dois databases separados** (`FluxoCaixa_Lancamentos` e `FluxoCaixa_Consolidado`).

- Isolamento lógico: cada serviço acessa apenas seu database.
- Em produção, pode-se separar por instância se necessário.
- Localmente, um único container simplifica o Docker Compose.

### 5.4. Resiliência com Polly

Políticas aplicadas no Worker de Consolidação:

- **Retry com exponential backoff** — para falhas transitórias.
- **Circuit Breaker** — se o banco do consolidado estiver fora, o Worker para de tentar temporariamente.
- **Dead Letter Queue** — mensagens com falha persistente vão para DLQ para análise, não são descartadas.

### 5.5. Rate Limiting com Middleware Nativo

O .NET 8 oferece `Microsoft.AspNetCore.RateLimiting` com políticas de fixed window, sliding window e token bucket. Aplicado diretamente na API de Consolidado, protege contra picos acima de 50 req/s sem necessidade de API Gateway externo.

---

## 6. Diagramas C4

### 6.1. Level 1 — Contexto

```
┌─────────────┐         HTTPS / JSON         ┌───────────────────────────┐
│   Carlos    │ ──────────────────────────── │ Sistema de Fluxo de Caixa │
│ Comerciante │  Registra lançamentos e      │                           │
│             │  consulta saldo consolidado  │  Controle de lançamentos  │
└─────────────┘  (via Swagger ou frontend    │  e saldo diário           │
                  futuro)                    └───────────────────────────┘
```

### 6.2. Level 2 — Containers

```
                            ┌─────────────────┐
                            │   Comerciante   │
                            │    (Carlos)     │
                            └────────┬────────┘
                                     │ HTTPS/JSON (Swagger UI)
                    ┌────────────────┴───────────────┐
                    ▼                                ▼
          ┌─────────────────┐               ┌─────────────────┐
          │ API Lançamentos │               │ API Consolidado │
          │  .NET 8 / C#    │               │  .NET 8 / C#    │
          │  (Write Side)   │               │  Rate Limiting  │
          └───┬─────────┬───┘               └────────┬────────┘
              │         │                            │
              ▼         ▼                            ▼
   ┌─────────────┐  ┌──────────────┐       ┌────────────────┐
   │ DB Lanç.    │  │  RabbitMQ    │       │ DB Consolidado │
   │ PostgreSQL  │  │  (Broker)    │       │  PostgreSQL    │
   │ (database 1)│  └──────┬───────┘       │  (database 2)  │
   └─────────────┘         │               └────────▲───────┘
                           ▼                        │
                    ┌──────────────┐                │
                    │   Worker de  │────────────────┘
                    │ Consolidação │  Atualiza projeção
                    │ .NET 8       │
                    └──────────────┘
```

**O que este diagrama comunica:**

- Dois serviços independentes sem chamada direta entre eles
- Comunicação assíncrona via RabbitMQ (se o Worker cair, mensagens ficam na fila)
- Databases separados logicamente (mesmo container PostgreSQL)
- Rate limiting na própria API de Consolidado (sem API Gateway externo)
- Worker separado da API de consulta (escaláveis independentemente)

### 6.3. Level 3 — Componentes da API de Lançamentos

```
┌─────────────────────────────────────────────────────────────┐
│                  API de Lançamentos (.NET 8)                │
│                                                             │
│  ┌──────────────────────┐                                   │
│  │ Endpoints            │                                   │
│  │ (Minimal API)        │                                   │
│  └─────────┬────────────┘                                   │
│            │                                                │
│            ▼                                                │
│  ┌──────────────────────┐                                   │
│  │ LancamentoService    │                                   │
│  │ (Application Layer)  │                                   │
│  └──┬──────────────┬────┘                                   │
│     │              │                                        │
│     ▼              ▼                                        │
│  ┌──────────────┐ ┌──────────────────┐                      │
│  │ Domain Model │ │ IEventPublisher  │                      │
│  │ (Entidades)  │ │ (MassTransit)    │                      │
│  └──────────────┘ └──────┬───────────┘                      │
│     │                    │                                  │
│     ▼                    │                                  │
│  ┌──────────────────┐    │                                  │
│  │ DbContext (EF)   │    │                                  │
│  │ + Repository     │    │                                  │
│  └────────┬─────────┘    │                                  │
└───────────┼──────────────┼──────────────────────────────────┘
            ▼              ▼
     ┌────────────┐  ┌──────────────┐
     │ PostgreSQL │  │  RabbitMQ    │
     │ (Lanç.)    │  │              │
     └────────────┘  └──────────────┘
```

**Fluxo**: Endpoint recebe request → Service valida e aplica regras → persiste via EF Core → publica evento `LancamentoRegistrado` via MassTransit **após SaveChanges** (evita mensagens fantasma).

### 6.4. Level 3 — Componentes do Worker de Consolidação

```
     ┌──────────────┐
     │  RabbitMQ    │
     └──────┬───────┘
            │ Consome mensagens
            ▼
┌──────────────────────────────────────────────────┐
│        Worker de Consolidação (.NET 8)           │
│                                                  │
│  ┌──────────────────┐  ┌──────────────────┐      │
│  │ Consumer         │──│ Polly Policies   │      │
│  │ (MassTransit)    │  │ Retry + CB + DLQ │      │
│  └─────────┬────────┘  └──────────────────┘      │
│            │                                     │
│            ▼                                     │
│  ┌─────────────────────┐                         │
│  │ ConsolidacaoService │                         │
│  │ Calcula delta no    │                         │
│  │ saldo diário        │                         │
│  └──┬──────────────────┘                         │
│     │                                            │
│     ▼                                            │
│  ┌──────────────────┐                            │
│  │ DbContext (EF)   │                            │
│  │ SaldoDiario      │                            │
│  └────────┬─────────┘                            │
└───────────┼──────────────────────────────────────┘
            ▼
     ┌────────────────┐
     │   PostgreSQL   │
     │ (Consolidado)  │
     └────────────────┘
```

**Entidade SaldoDiario:**

- `Data` (date, PK)
- `TotalCreditos` (decimal)
- `TotalDebitos` (decimal)
- `Saldo` (decimal, calculado)

---

## 7. Stack Tecnológica

| Camada | Tecnologia | Justificativa |
|---|---|---|
| Runtime | .NET 8 / C# | Requisito obrigatório do desafio |
| API Framework | ASP.NET Minimal APIs | Menor overhead, ideal para APIs focadas |
| ORM | Entity Framework Core | Produtividade + migrations + abstração |
| Banco de Dados | PostgreSQL (Docker) | Imagem Docker nativa ARM64 (roda em qualquer Mac/Windows), leve, suportado pelo EF Core via Npgsql |
| Mensageria | RabbitMQ + MassTransit | RabbitMQ roda local em Docker; MassTransit abstrai o broker (troca para Service Bus sem mudar código) |
| Rate Limiting | `Microsoft.AspNetCore.RateLimiting` | Nativo do .NET 8, sem dependência externa |
| Resiliência | Polly | Retry, Circuit Breaker — padrão de mercado para .NET |
| Testes | xUnit + Moq + FluentAssertions | Stack padrão para testes em .NET |
| Containerização | Docker + Docker Compose | Execução local conforme pedido no desafio |
| Logs | Serilog (console sink) | Logs estruturados sem dependência de cloud |

---

## 8. Padrões Aplicados

| Padrão | Onde | Por quê |
|---|---|---|
| **CQRS** | Arquitetura geral | Separação de escrita e leitura, conforme requisitos |
| **Event-Driven** | Comunicação entre serviços | Desacoplamento temporal — lançamento não depende do consolidado |
| **Repository** | Camada de dados | Abstração de persistência, testabilidade |
| **Domain Model** | Entidades | Regras de negócio encapsuladas (ex: lançamento não pode ter valor negativo) |
| **Circuit Breaker** | Worker de Consolidação | Proteção contra falhas em cascata |
| **Retry + Backoff** | Consumo de mensagens | Tolerância a falhas transitórias |

---

## 9. Estratégia de Resiliência

### Cenário: Worker de Consolidação indisponível

1. API de Lançamentos continua operando normalmente.
2. Eventos `LancamentoRegistrado` se acumulam na fila do RabbitMQ.
3. Quando o Worker volta, processa o backlog na ordem (FIFO).
4. Saldo consolidado se atualiza automaticamente.
5. Nenhum dado é perdido.

### Cenário: Pico de 50 req/s no consolidado

1. Rate limiting middleware aplica política de fixed window na API de Consolidado.
2. Requisições dentro do limite são atendidas normalmente (consulta simples ao banco de projeção).
3. Requisições acima do limite recebem HTTP 429 (Too Many Requests) com header `Retry-After`.
4. Perda controlada e previsível, dentro do teto de 5%.

### Cenário: Falha no banco do consolidado

1. Polly detecta falhas consecutivas e abre o Circuit Breaker.
2. Worker para de consumir temporariamente (mensagens seguras na fila).
3. Após o tempo configurado, Polly faz half-open e testa novamente.
4. Se o banco voltou, retoma o consumo. Se não, mantém o circuito aberto.

---

## 10. Estrutura de Projeto

```
/src
  /FluxoCaixa.Lancamentos.API        → API de Lançamentos (Minimal API + Domain + Infra como pastas)
  /FluxoCaixa.Consolidado.API        → API de Consolidado (Minimal API + Domain + Infra como pastas)
  /FluxoCaixa.Consolidado.Worker     → Worker Service (consumidor de eventos via MassTransit)
  /FluxoCaixa.Shared                 → Contratos de eventos (DTOs compartilhados entre serviços)

/tests
  /FluxoCaixa.Lancamentos.Tests      → Testes unitários e de integração
  /FluxoCaixa.Consolidado.Tests      → Testes unitários e de integração

/docs
  /adrs                              → Architecture Decision Records
  /diagrams                          → Diagramas C4

docker-compose.yml                   → Orquestração local (APIs + Worker + PostgreSQL + RabbitMQ)
README.md                            → Instruções de execução
```

**4 projetos de produção + 2 de testes + 1 shared.** Separação por pastas internas (Domain/, Application/, Infrastructure/) dentro de cada API demonstra a mesma organização sem a cerimônia de 11 projetos para um domínio com 2 entidades.

---

## 11. Rastreabilidade: RNF → Decisões

| Requisito Não-Funcional | Descrição | ADRs |
|---|---|---|
| **RNF-01** | Serviço de lançamentos não pode ficar indisponível se o consolidado cair | ADR-001 (CQRS), ADR-002 (mensageria), ADR-003 (DBs separados), ADR-004 (Worker isolado) |
| **RNF-02** | Em picos, consolidado recebe 50 req/s com no máximo 5% de perda | ADR-005 (Polly), ADR-006 (rate limiting) |
| **Integridade** | Eventos publicados devem corresponder a dados persistidos | ADR-007 (publish after save) |
| **Executabilidade** | Rodar localmente com instruções claras | ADR-008 (Docker Compose) |

---

## 12. Resumo das Decisões (ADR Index)

Cada ADR com trade-offs detalhados, alternativas descartadas e configurações concretas está documentada em [`docs/adrs/adrs.md`](docs/adrs/adrs.md).

| # | Decisão | Justificativa | RNF |
|---|---|---|---|
| ADR-001 | CQRS para separação de lançamentos e consolidado | Requisito do enunciado: dois serviços independentes | RNF-01 |
| ADR-002 | RabbitMQ + MassTransit para mensageria | Roda local em Docker; MassTransit abstrai o broker | RNF-01 |
| ADR-003 | PostgreSQL com dois databases separados | ARM64 nativo, leve, isolamento lógico | RNF-01 |
| ADR-004 | Worker Service independente para consumir eventos | Desacoplamento entre consumo e consulta | RNF-01 |
| ADR-005 | Polly para resiliência (Retry + Circuit Breaker) | Tolerância a falhas transitórias, DLQ para falhas persistentes | RNF-02 |
| ADR-006 | Rate limiting nativo do .NET 8 | Protege contra picos de 50 req/s sem dependência externa | RNF-02 |
| ADR-007 | Publicação de evento após SaveChanges | Evita mensagens fantasma | Integridade |
| ADR-008 | Docker Compose para execução local | Requisito explícito do desafio | Executabilidade |

---

## 13. Evoluções Futuras

O desafio menciona explicitamente que evoluções futuras são bem-vindas. Estas são as evoluções naturais da solução, priorizadas por valor:

**Curto prazo (próximas sprints):**

- **Frontend Web (SPA)** — interface mobile-first para o comerciante Carlos, baseada na persona e jornada documentadas nas seções 2 e 3. Blazor WASM mantém o stack 100% C#.
- **Idempotência no consumidor** — garantir que reprocessamento de mensagens (at-least-once) não cause duplicação no saldo.
- **Cache em memória (IMemoryCache)** — na API de Consolidado, para reduzir pressão no banco em picos.

**Médio prazo (estabilização):**

- **Migração para Azure Service Bus** — em produção, trocar RabbitMQ por Service Bus via configuração do MassTransit, ganhando DLQ gerenciada e SLA de 99.95%.
- **API Gateway (Azure APIM)** — quando houver múltiplos consumers (frontend, mobile, integrações), centralizar autenticação, rate limiting e roteamento.
- **Observabilidade (Application Insights + OpenTelemetry)** — distributed tracing, dashboards, alertas em produção.
- **API Versioning** — versionamento de endpoints para evolução sem breaking changes.

**Longo prazo (escala):**

- **Event Sourcing** — armazenar cada lançamento como evento imutável, permitindo reconstruir o estado a qualquer momento e facilitando auditoria financeira.
- **Cache distribuído (Redis)** — substituir IMemoryCache quando houver múltiplas instâncias da API.
- **Autoscaling** — escalar horizontalmente APIs e Worker com base em métricas de fila (queue depth) e CPU.
- **Multi-tenancy** — suportar múltiplos comerciantes com isolamento de dados.

