# Análise do Desafio — Arquiteto de Software

> Este documento é a **análise do desafio**: contexto, persona, jornada, RNFs e decisões arquiteturais com rastreabilidade. Para uma **visão arquitetural unificada** (estilo adotado, estrutura de módulos, fluxos), veja [`../architecture.md`](../architecture.md).

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
| Abertura do caixa | 7h | Registra troco inicial como crédito | `POST /api/v1/transactions` |
| Lançamentos | Ao longo do dia | Registra cada venda (crédito) ou despesa (débito) | `POST /api/v1/transactions` |
| Olhada rápida | Intervalos | Quer ver como está o dia | `GET /api/v1/balance/{date}` |
| Fechamento | 19h | Confere saldo consolidado, compara com caixa físico | `GET /api/v1/balance/{date}` |
| Histórico | Eventual | Compara dias anteriores, prepara dados p/ contador | `GET /api/v1/balance?from={date}&to={date}` |

> **Mapeamento de termos pt-br → en-us no código:** *Lançamento* → `Transaction`; *Consolidado* → `Balance` (bounded context); *Saldo diário* → `DailyBalance` (entidade). Glossário completo no [`README.md`](../../README.md#glossário-de-termos).

A jornada mostra que o sistema é essencialmente **dois endpoints com alta disponibilidade** — o que reforça a decisão de manter a solução enxuta.

---

## 4. Requisitos Não-Funcionais

O enunciado declara dois RNFs **explícitos** (disponibilidade e carga) e — na seção *Objetivo do Desafio* — sete dimensões adicionais que tratamos como RNFs derivados (Escalabilidade, Resiliência, Segurança, Padrões Arquiteturais, Integração, Manutenibilidade, Observabilidade).

Cada RNF é documentado em arquivo próprio em [`../rnfs/`](../rnfs/), com **declaração**, **decisões que o atendem**, **distinção MVP vs Evolução**, e **forma de verificação**. Abaixo, apenas o sumário com os links:

### 4.1. RNFs Explícitos (obrigatórios pelo enunciado)

| ID | Dimensão | Resumo | Cobertura MVP |
|---|---|---|---|
| [**RNF-01**](../rnfs/rnf-01-disponibilidade.md) | Disponibilidade | Transactions API não cai se Balance API cair | **Total** |
| [**RNF-02**](../rnfs/rnf-02-carga.md) | Carga | 50 req/s no Balance, máx. 5% perda | **Total** |

### 4.2. RNFs Derivados (objetivos do desafio)

| ID | Dimensão | Resumo | Cobertura MVP |
|---|---|---|---|
| [**RNF-03**](../rnfs/rnf-03-escalabilidade.md) | Escalabilidade | Stateless + projeção pré-calculada + fila absorve picos | Parcial |
| [**RNF-04**](../rnfs/rnf-04-resiliencia.md) | Resiliência | Retry + healthchecks + idempotência + mensagens persistentes | **Total para o escopo** |
| [**RNF-05**](../rnfs/rnf-05-seguranca.md) | Segurança | Validação, parameterização SQL, CORS, HTTPS, rate limit | Mínimo honesto |
| [**RNF-06**](../rnfs/rnf-06-padroes-arquiteturais.md) | Padrões Arquiteturais | CQRS, EDA, Clean Architecture, Rich Domain | **Total** |
| [**RNF-07**](../rnfs/rnf-07-integracao.md) | Integração | REST/JSON, AMQP, MassTransit como abstração de broker | **Total** |
| [**RNF-08**](../rnfs/rnf-08-manutenibilidade.md) | Manutenibilidade | ArchTests, ADRs, RNFs, diagramas C4, convenções verificadas | **Total** |
| [**RNF-09**](../rnfs/rnf-09-observabilidade.md) | Observabilidade | Logs estruturados, healthchecks live/ready | Mínimo |

### 4.3. Como esta tabela orienta as decisões

Cada decisão arquitetural (Seção 5) e cada ADR (Seção 12) **rastreia-se de volta** para pelo menos um RNF acima. Isso evita o anti-pattern de "decisão por preferência" — toda escolha técnica responde a uma restrição documentada do enunciado ou a uma dimensão explicitamente cobrada no *Objetivo do Desafio*.

**Distinção MVP vs Evolução:** o escopo do desafio é construir uma solução executável localmente. Tudo marcado como "Evolução" nos arquivos RNF/ADR é citado como caminho natural em produção (Azure), demonstrando consciência arquitetural sem inflar o escopo entregável.

---

## 5. Decisões Arquiteturais

### 5.1. CQRS (Command Query Responsibility Segregation)

O enunciado pede literalmente dois serviços com responsabilidades distintas:

- **Command side** → `CashFlow.Transactions.API` (escrita: registra lançamentos)
- **Query side** → `CashFlow.Balance.API` (leitura: expõe saldo consolidado)

O modelo de leitura (`DailyBalance`) é uma **projeção** derivada dos eventos de escrita.

**Trade-off**: consistência eventual entre lançamento e saldo — aceitável para este tipo de consulta financeira (não é uma transferência bancária em tempo real). Detalhes em [ADR-001](../adrs/adr-001-cqrs.md).

### 5.2. Mensageria Assíncrona com RabbitMQ + MassTransit

O requisito de que o Transactions não pode cair junto com o Balance exige **desacoplamento temporal**. A Transactions API publica um evento `TransactionRegistered` no broker. Um consumidor (`TransactionConsumer`, rodando como `BackgroundService` dentro da Balance API — ver § 5.4) processa e atualiza a projeção.

- Se o consumidor cair, as mensagens ficam na fila.
- Quando volta, processa o backlog e se atualiza.
- A Transactions API nunca sabe se a Balance API está de pé.

**Por que RabbitMQ (e não Azure Service Bus)?**

O desafio exige execução local com Docker. RabbitMQ roda em container com um comando e inclui UI de gerenciamento. Usando **MassTransit** como abstração, a troca para Azure Service Bus em produção é uma mudança de configuração (`cfg.UsingAzureServiceBus(...)` em vez de `cfg.UsingRabbitMq(...)`) — sem alterar uma linha de código de negócio. Detalhes em [ADR-002](../adrs/adr-002-rabbitmq-masstransit.md).

### 5.3. Datastores — PostgreSQL com schemas separados

Uma instância PostgreSQL em Docker com **um database (`cashflow`) e dois schemas** (`transactions` e `balance`). Cada serviço usa um usuário (`app_transactions` / `app_balance`) com `GRANT` restrito ao seu schema — isolamento real, não cosmético.

- Em produção (Azure), evolução natural: separar em instâncias dedicadas por bounded context.
- Localmente, um container com schemas isolados via GRANTs é suficiente e demonstra a intenção arquitetural.
- A versão anterior desta decisão previa dois databases — substituída após revisão honesta. Detalhes em [ADR-003](../adrs/adr-003-postgres-schemas.md).

### 5.4. Consumidor como HostedService (com Polly Retry)

Para o MVP, o `TransactionConsumer` roda como `BackgroundService` **dentro do processo da Balance API** (não em projeto separado). Decisão pragmática para 50 req/s — menos um container, menos um Dockerfile, menos um ponto de falha no `docker compose up`. A migração para Worker Service dedicado (`CashFlow.Balance.Worker`) é refator mecânico documentado como evolução. Detalhes em [ADR-004](../adrs/adr-004-consumer-hostedservice.md).

Política de resiliência aplicada no consumer:

- **Retry com exponential backoff (Polly v8)** — para falhas transitórias (jitter de rede, contenção no banco). Ver [ADR-005](../adrs/adr-005-polly-retry.md).
- **Idempotência** ([ADR-011](../adrs/adr-011-idempotency.md)) via tabela `balance.processed_events` — pré-requisito para Retry seguro com entrega at-least-once do RabbitMQ.
- **Circuit Breaker + DLQ** — citados como evolução natural (ADR-005); não cabem no MVP porque não há cenário local que justifique demonstração.

### 5.5. Rate Limiting com Middleware Nativo

O ASP.NET Core 10 oferece `Microsoft.AspNetCore.RateLimiting` com políticas de fixed window, sliding window e token bucket (disponível desde .NET 8). Aplicado diretamente no `BalanceController` (via `[EnableRateLimiting("balance")]`), protege contra picos acima de 50 req/s sem necessidade de API Gateway externo. Detalhes em [ADR-006](../adrs/adr-006-rate-limiting.md).

### 5.6. Rich Domain Model (DDD tático)

Entidades em `Domain/Entities/` (`Transaction`, `DailyBalance`) são modeladas com construtor privado + factory method estático (`Transaction.Register(...)`, `DailyBalance.New(...)`), propriedades com `private set`, e métodos de negócio que expressam intenção (`balance.ApplyCredit(amount)`, `balance.ApplyDebit(amount)`). Value Objects (`Money`, `TransactionType`, `MovementDate`) encapsulam invariantes. `DomainException` é lançada em violação — entidade sempre-válida. Detalhes em [ADR-009](../adrs/adr-009-rich-domain-model.md).

### 5.7. Persistência com Dapper

Em vez de Entity Framework Core, a camada de Infrastructure usa **Dapper** com `IDbConnectionFactory` injetado e SQL parameterizado nos `*Repository`. Migrations versionadas em SQL puro via **DbUp**, executadas no startup das APIs (`MigrationRunner.EnsureUpToDate`). Trade-offs aceitos (sem migrations auto-geradas, sem change tracking) em troca de controle total do SQL e zero atrito com Rich Domain Model. Detalhes em [ADR-010](../adrs/adr-010-dapper.md).

### 5.8. Idempotência no Consumer

Tabela `balance.processed_events` com chave primária composta `(event_id, consumer_name)`. Antes de aplicar o delta no saldo, o `ConsolidationService` verifica via `IProcessedEventsRepository.ExistsAsync` se aquele `EventId` já foi processado — se sim, ignora; se não, aplica e marca como processado **na mesma transação Dapper**. Isso garante que retentativas (causadas por Polly Retry ou crash + redelivery do RabbitMQ) não dupliquem o saldo. Detalhes em [ADR-011](../adrs/adr-011-idempotency.md).

### 5.9. Testes de Arquitetura (NetArchTest)

Projeto `CashFlow.Architecture.Tests` valida automaticamente em CI:

- `Domain` não referencia `Infrastructure`, `Application`, `Microsoft.AspNetCore`, `Dapper`, `Npgsql`, `MassTransit`.
- Entidades não têm setters públicos.
- Repositórios terminam com sufixo `Repository`.
- Interfaces de repositório começam com `I`.

Fitness functions transformam regras arquiteturais em invariantes verificadas. Detalhes em [ADR-012](../adrs/adr-012-architecture-tests.md).

### 5.10. Segurança e Observabilidade básicas

Para o MVP: prefixo `/api/v1/` em todos endpoints, healthchecks `/health/live` e `/health/ready`, FluentValidation no input (`RegisterTransactionValidator`), CORS configurável, HTTPS no compose, logs estruturados com Serilog. OAuth/JWT, API Gateway (APIM/Apigee) e OpenTelemetry completo ficam como evolução natural. Detalhes em [ADR-013](../adrs/adr-013-security-observability.md).

---

## 6. Diagramas C4

### 6.1. Level 1 — Contexto

```text
┌─────────────┐         HTTPS / JSON         ┌──────────────────────────┐
│   Carlos    │ ──────────────────────────── │ Sistema CashFlow         │
│ Comerciante │  Registra lançamentos e      │                          │
│             │  consulta saldo consolidado  │  Transactions + Balance  │
└─────────────┘  (via Swagger ou frontend    │  (write + read sides)    │
                  futuro)                    └──────────────────────────┘
```

### 6.2. Level 2 — Containers

```text
                            ┌─────────────────┐
                            │   Comerciante   │
                            │    (Carlos)     │
                            └────────┬────────┘
                                     │ HTTPS/JSON (Swagger UI)
                    ┌────────────────┴───────────────┐
                    ▼                                ▼
          ┌─────────────────┐               ┌───────────────────────┐
          │ Transactions    │               │      Balance API      │
          │   API .NET 10   │               │       .NET 10         │
          │  Write Side     │               │  Read Side            │
          │  + Rich Domain  │               │  + Rate Limiting      │
          │                 │               │  + BackgroundService  │
          │                 │               │  (TransactionConsumer)│
          └───┬─────────┬───┘               └────┬────────────┬─────┘
              │         │                        │            │
              │         ▼                        ▼            │
              │  ┌──────────────┐  ┌───────────────────┐      │
              │  │   RabbitMQ   │  │   PostgreSQL      │      │
              │  │   (Broker)   │  │   cashflow        │      │
              │  │  port 5672   │  │ ┌──────────────┐  │      │
              │  │  port 15672  │  │ │schema:       │  │      │
              │  └──────┬───────┘  │ │ transactions │  │      │
              │         │          │ └──────────────┘  │      │
              │         │          │ ┌──────────────┐  │      │
              │         └──────────┼─│schema:       │◀┘      │
              │                    │ │   balance    │         │
              │                    │ └──────────────┘ │       │
              └────────────────────│ user grants      │◀──── (consultas)
                                   │ por schema       │
                                   └──────────────────┘
```

**O que este diagrama comunica:**

- Dois serviços independentes — não há chamada síncrona entre eles.
- Comunicação assíncrona via RabbitMQ — se o consumidor cair, mensagens ficam na fila.
- Um database PostgreSQL com dois schemas isolados via GRANTs por usuário.
- Rate limiting na própria Balance API (sem API Gateway externo no MVP).
- O consumer é um `BackgroundService` dentro da Balance API (ver [ADR-004](../adrs/adr-004-consumer-hostedservice.md)) — separação em processo dedicado é evolução documentada.

**Diagrama completo em Mermaid (renderizado pelo GitHub):** veja [`../diagrams/c4-containers.md`](../diagrams/c4-containers.md).

### 6.3. Level 3 — Componentes da Transactions API

```text
┌─────────────────────────────────────────────────────────────┐
│              CashFlow.Transactions.API (.NET 10)            │
│                                                             │
│  ┌──────────────────────┐                                   │
│  │ Controllers v1       │                                   │
│  │ (TransactionsCtrl)   │                                   │
│  │ + FluentValidation   │                                   │
│  └─────────┬────────────┘                                   │
│            │                                                │
│            ▼                                                │
│  ┌──────────────────────┐                                   │
│  │ TransactionService   │                                   │
│  │ (Application Layer)  │                                   │
│  │ + IUnitOfWork        │                                   │
│  └──┬──────────────┬────┘                                   │
│     │              │                                        │
│     ▼              ▼                                        │
│  ┌──────────────┐ ┌──────────────────┐                      │
│  │ Rich Domain  │ │ IEventPublisher  │                      │
│  │ (Transaction,│ │ (MassTransit)    │                      │
│  │  Money, VOs) │ └──────┬───────────┘                      │
│  └──────┬───────┘        │                                  │
│         │                │                                  │
│         ▼                │                                  │
│  ┌──────────────────────┐│                                  │
│  │ TransactionRepository││                                  │
│  │ (Dapper)             ││                                  │
│  └────────┬─────────────┘│                                  │
└───────────┼──────────────┼──────────────────────────────────┘
            ▼              ▼
     ┌────────────┐  ┌──────────────┐
     │ PostgreSQL │  │  RabbitMQ    │
     │ schema:    │  │              │
     │transactions│  │              │
     └────────────┘  └──────────────┘
```

**Fluxo**: Endpoint recebe request → FluentValidation valida DTO → Service abre transação Dapper → `Transaction.Register()` valida invariantes do domínio → Repository persiste → Service comita transação → publica evento `TransactionRegistered` via MassTransit **após commit** (evita mensagens fantasma — [ADR-007](../adrs/adr-007-publish-after-commit.md)).

### 6.4. Level 3 — Componentes do Consumer (dentro da Balance API)

```text
     ┌──────────────┐
     │  RabbitMQ    │
     └──────┬───────┘
            │ Consome TransactionRegistered
            ▼
┌─────────────────────────────────────────────────────────┐
│  CashFlow.Balance.API (.NET 10)                         │
│                                                         │
│  ┌──────────────────────┐  ┌─────────────────────┐      │
│  │ Controllers v1       │  │ TransactionConsumer │      │
│  │ (BalanceController)  │  │ (BackgroundService) │      │
│  │ + Rate Limiting      │  │ + Polly Retry       │      │
│  └─────────┬────────────┘  └─────────┬───────────┘      │
│            │                         │                  │
│            ▼                         ▼                  │
│  ┌─────────────────────┐  ┌────────────────────────┐    │
│  │ BalanceQueryService │  │ ConsolidationService   │    │
│  │ (queries)           │  │ (aplica delta no saldo)│    │
│  └─────────┬───────────┘  └──┬─────────────────┬───┘    │
│            │                 │                 │        │
│            ▼                 ▼                 ▼        │
│  ┌─────────────────┐ ┌──────────────────┐ ┌──────────┐  │
│  │BalanceRepository│ │ BalanceRepository│ │ Processed│  │
│  │ (Dapper SELECT) │ │ (Dapper UPSERT)  │ │EventsRepo│  │
│  └────────┬────────┘ └────────┬─────────┘ └──────┬───┘  │
└───────────┼───────────────────┼──────────────────┼──────┘
            │                   │                  │
            └─────────┬─────────┴──────────────────┘
                      ▼
              ┌─────────────────┐
              │   PostgreSQL    │
              │ schema:         │
              │   balance       │
              │  - daily_balance│
              │  - processed_   │
              │    events       │
              └─────────────────┘
```

**Fluxo do consumer**: mensagem chega → consumer abre transação Dapper → verifica `IProcessedEventsRepository.ExistsAsync(EventId)` (idempotência — [ADR-011](../adrs/adr-011-idempotency.md)) → se novo, `ConsolidationService.ApplyAsync(...)` atualiza `daily_balance` e marca `processed_events` na mesma transação → commit → `Ack` para RabbitMQ.

**Entidade DailyBalance:**

- `Date` (date, PK)
- `TotalCredits` (decimal)
- `TotalDebits` (decimal)
- `Balance` (decimal, derivado)
- `UpdatedAt` (timestamptz)

**Diagramas completos em Mermaid:** veja [`../diagrams/`](../diagrams/) (Contexto, Containers, Componentes para cada API).

---

## 7. Stack Tecnológica

| Camada | Tecnologia | Justificativa |
|---|---|---|
| Runtime | .NET 10 / C# (LTS) | Requisito obrigatório do desafio; LTS atual com runway até Nov/2028 ([ADR-014](../adrs/adr-014-dotnet-10.md)) |
| API Framework | ASP.NET Core (Controllers) | Padrão maduro; suporte natural a versionamento e atributos `[Authorize]`/`[RateLimit]` |
| Persistência | **Dapper** + Npgsql | Controle total do SQL, performance previsível, zero atrito com Rich Domain (ADR-010) |
| Migrations | **DbUp** | SQL puro versionado, rodado no startup das APIs |
| Banco de Dados | PostgreSQL (Docker) | ARM64 nativo, leve, ~2s de startup, compatível com qualquer plataforma do avaliador (ADR-003) |
| Mensageria | RabbitMQ + MassTransit | RabbitMQ local em Docker; MassTransit abstrai broker (RabbitMQ→Service Bus em produção, sem mudar código) (ADR-002) |
| Rate Limiting | `Microsoft.AspNetCore.RateLimiting` | Nativo do ASP.NET Core (desde .NET 8), sem dependência externa (ADR-006) |
| Resiliência | Polly v8 (`Microsoft.Extensions.Resilience`) | Retry com backoff exponencial + jitter no consumer (ADR-005) |
| Validação | FluentValidation | Validação de input antes do handler, mensagens estruturadas |
| Domínio | Rich Domain Model + Value Objects | Invariantes encapsuladas, entidades sempre-válidas (ADR-009) |
| Testes Unitários | xUnit + FluentAssertions + Moq | Stack padrão para .NET |
| Testes de Arquitetura | NetArchTest.Rules | Fitness functions validam Clean Architecture em CI (ADR-012) |
| Containerização | Docker + Docker Compose | Execução local conforme pedido no desafio (ADR-008) |
| Logs | Serilog (console sink) + correlation ID | Logs estruturados sem dependência de cloud (ADR-013) |
| Health Checks | `Microsoft.Extensions.Diagnostics.HealthChecks` | `/health/live` e `/health/ready` para readiness probes (ADR-013) |

---

## 8. Padrões Aplicados

| Padrão | Onde | Por quê |
|---|---|---|
| **CQRS** | Arquitetura geral | Separação write/read (ADR-001) |
| **Event-Driven Architecture (EDA)** | Comunicação entre serviços | Desacoplamento temporal — lançamento não depende de consolidado |
| **Clean Architecture** | Dentro de cada API (Domain → Application → Infrastructure → API) | Testabilidade, manutenibilidade, validada por NetArchTest (ADR-012) |
| **Rich Domain Model (DDD tático)** | `Domain/Entities/`, `Domain/ValueObjects/` | Invariantes encapsuladas, sempre-válido (ADR-009) |
| **Repository + Unit of Work** | `Infrastructure/Repositories/` | Abstração de persistência sobre Dapper, transações explícitas (ADR-010) |
| **Idempotent Consumer** | Consumer de eventos | Tabela `eventos_processados` evita duplicação em retentativas (ADR-011) |
| **Retry com Exponential Backoff** | Consumer | Tolerância a falhas transitórias (ADR-005) |
| **Fitness Functions** | `Architecture.Tests` | Regras arquiteturais como testes executáveis (ADR-012) |
| **API Versioning** | Prefixo `/api/v1/` em todos endpoints | Evita breaking changes ao evoluir (ADR-013) |

---

## 9. Estratégia de Resiliência

### Cenário: Balance API / consumidor indisponível

1. Transactions API continua operando normalmente.
2. Eventos `TransactionRegistered` se acumulam na fila do RabbitMQ (persistentes).
3. Quando a Balance API volta, o `BackgroundService` (`TransactionConsumer`) processa o backlog na ordem.
4. Cada evento é checado contra `processed_events` antes de aplicar — reentrega não duplica saldo.
5. Saldo consolidado se atualiza automaticamente. Nenhum dado é perdido.

### Cenário: Pico de 50 req/s no Balance

1. Rate limiting middleware aplica política de fixed window (50 req/s + queue 5).
2. Requisições dentro do limite atendidas normalmente (leitura O(1) na projeção `daily_balance`).
3. Requisições acima do limite recebem HTTP 429 (Too Many Requests) com `Retry-After`.
4. Perda controlada e previsível, dentro do teto de 5%.

### Cenário: Falha transitória no banco durante consumo

1. Polly Retry tenta 3 vezes com backoff exponencial (1s, 2s, 4s) + jitter.
2. Se a falha persistir, exception sobe; MassTransit faz `Nack` e a mensagem volta para a fila.
3. Próxima retentativa do RabbitMQ encontra o evento via `IProcessedEventsRepository.ExistsAsync` se algum efeito parcial foi commitado — idempotência garante consistência.
4. Cenário extremo (banco fora por minutos): Circuit Breaker é a evolução documentada ([ADR-005](../adrs/adr-005-polly-retry.md)).

### Cenário: Crash do consumer no meio do processamento

1. Transação Dapper aberta no consumer é abortada automaticamente → nenhum efeito parcial no banco.
2. RabbitMQ não recebeu `Ack` → mensagem volta para a fila.
3. Nova instância (ou reinício) processa a mensagem novamente — idempotência garante que aplicar duas vezes é seguro.

---

## 10. Estrutura de Projeto

```text
/src
  /CashFlow.Transactions.API   → Write Side
                                  (pastas internas: Domain/, Application/,
                                   Infrastructure/, Controllers/,
                                   Infrastructure/Migrations/)

  /CashFlow.Balance.API        → Read Side + BackgroundService consumer
                                  (pastas internas: Domain/, Application/,
                                   Infrastructure/, Controllers/, Consumers/,
                                   Infrastructure/Migrations/)

  /CashFlow.Shared             → Contratos de eventos (TransactionRegistered)

/tests
  /CashFlow.UnitTests             → Testes de domínio (Transaction, Money,
                                     DailyBalance, etc.)
  /CashFlow.Architecture.Tests    → NetArchTest — fitness functions

/infra
  /postgres/init.sql              → Criação de users + schemas + GRANTs

/docs
  /challenge   → PDF original do desafio
  /analysis    → Este documento (análise + decisões)
  /adrs        → 13 ADRs em arquivos individuais
  /rnfs        → 9 RNFs em arquivos individuais
  /diagrams    → Diagramas C4 em Mermaid
  /references  → Material de estudo, vocabulário, plano de preparação

docker-compose.yml   → PostgreSQL + RabbitMQ + 2 APIs com healthchecks
CashFlow.sln         → Solution file
README.md            → Instruções de execução
```

**3 projetos de produção + 2 de testes.** Separação por pastas internas (Domain/, Application/, Infrastructure/) dentro de cada API demonstra Clean Architecture sem a cerimônia de 11 projetos para um domínio com 2 entidades.

---

## 11. Rastreabilidade: RNF → Decisões

| RNF | Descrição | ADRs |
|---|---|---|
| [**RNF-01 — Disponibilidade**](../rnfs/rnf-01-disponibilidade.md) | Transactions não cai se Balance cair | [001](../adrs/adr-001-cqrs.md), [002](../adrs/adr-002-rabbitmq-masstransit.md), [003](../adrs/adr-003-postgres-schemas.md), [004](../adrs/adr-004-consumer-hostedservice.md) |
| [**RNF-02 — Carga**](../rnfs/rnf-02-carga.md) | 50 req/s no Balance, máx. 5% perda | [005](../adrs/adr-005-polly-retry.md), [006](../adrs/adr-006-rate-limiting.md) |
| [**RNF-03 — Escalabilidade**](../rnfs/rnf-03-escalabilidade.md) | Stateless, projeção pré-calculada, fila absorve picos | [001](../adrs/adr-001-cqrs.md), [004](../adrs/adr-004-consumer-hostedservice.md), [006](../adrs/adr-006-rate-limiting.md) |
| [**RNF-04 — Resiliência**](../rnfs/rnf-04-resiliencia.md) | Retry, healthchecks, mensagens persistentes, idempotência | [002](../adrs/adr-002-rabbitmq-masstransit.md), [005](../adrs/adr-005-polly-retry.md), [007](../adrs/adr-007-publish-after-commit.md), [011](../adrs/adr-011-idempotency.md) |
| [**RNF-05 — Segurança**](../rnfs/rnf-05-seguranca.md) | Validação, parameterização SQL, CORS, HTTPS, rate limit | [006](../adrs/adr-006-rate-limiting.md), [010](../adrs/adr-010-dapper.md), [013](../adrs/adr-013-security-observability.md) |
| [**RNF-06 — Padrões Arquiteturais**](../rnfs/rnf-06-padroes-arquiteturais.md) | CQRS, EDA, Clean Architecture, Rich Domain | [001](../adrs/adr-001-cqrs.md), [009](../adrs/adr-009-rich-domain-model.md) |
| [**RNF-07 — Integração**](../rnfs/rnf-07-integracao.md) | REST/JSON, AMQP, MassTransit como abstração | [002](../adrs/adr-002-rabbitmq-masstransit.md) |
| [**RNF-08 — Manutenibilidade**](../rnfs/rnf-08-manutenibilidade.md) | ArchTests, ADRs, convenções verificadas | [009](../adrs/adr-009-rich-domain-model.md), [012](../adrs/adr-012-architecture-tests.md) |
| [**RNF-09 — Observabilidade**](../rnfs/rnf-09-observabilidade.md) | Logs estruturados, healthchecks, correlation ID | [013](../adrs/adr-013-security-observability.md) |
| **Integridade** | Eventos publicados correspondem a dados persistidos; idempotência | [007](../adrs/adr-007-publish-after-commit.md), [011](../adrs/adr-011-idempotency.md) |
| **Executabilidade** | `docker compose up --build` | [008](../adrs/adr-008-docker-compose.md) |

---

## 12. Resumo das Decisões (ADR Index)

Cada ADR é um arquivo separado em [`../adrs/`](../adrs/) com trade-offs detalhados, alternativas descartadas e configurações concretas.

| # | Decisão | Justificativa | RNF |
|---|---|---|---|
| [ADR-001](../adrs/adr-001-cqrs.md) | CQRS para separação write/read | Requisito do enunciado: dois serviços independentes | RNF-01, RNF-03, RNF-06 |
| [ADR-002](../adrs/adr-002-rabbitmq-masstransit.md) | RabbitMQ + MassTransit | Roda local em Docker; MassTransit abstrai broker | RNF-01, RNF-04, RNF-07 |
| [ADR-003](../adrs/adr-003-postgres-schemas.md) | PostgreSQL com 1 DB + 2 schemas | Isolamento real via GRANTs; ARM64; menos cerimônia | RNF-01 |
| [ADR-004](../adrs/adr-004-consumer-hostedservice.md) | Consumer como HostedService no MVP | Menos containers; processo dedicado documentado como evolução | RNF-01, RNF-03 |
| [ADR-005](../adrs/adr-005-polly-retry.md) | Polly Retry no consumer (CB+DLQ como evolução) | Simplicidade pragmática para 50 req/s | RNF-02, RNF-04 |
| [ADR-006](../adrs/adr-006-rate-limiting.md) | Rate limiting nativo do ASP.NET Core | Atende picos sem API Gateway externo | RNF-02, RNF-05 |
| [ADR-007](../adrs/adr-007-publish-after-commit.md) | Publicação de evento após commit | Evita mensagens fantasma | Integridade |
| [ADR-008](../adrs/adr-008-docker-compose.md) | Docker Compose para execução local | Requisito explícito do desafio | Executabilidade |
| [ADR-009](../adrs/adr-009-rich-domain-model.md) | Rich Domain Model (DDD tático) | Invariantes encapsuladas; demonstra padrão | RNF-06, RNF-08 |
| [ADR-010](../adrs/adr-010-dapper.md) | Dapper em vez de EF Core | Controle do SQL + zero atrito com Rich Domain | RNF-05, RNF-08 |
| [ADR-011](../adrs/adr-011-idempotency.md) | Idempotência via tabela `processed_events` | Pré-requisito de Retry seguro com at-least-once | RNF-04, Integridade |
| [ADR-012](../adrs/adr-012-architecture-tests.md) | Testes de Arquitetura com NetArchTest | Fitness functions — Clean Architecture verificada | RNF-08 |
| [ADR-013](../adrs/adr-013-security-observability.md) | Versionamento, healthchecks, validação, CORS, HTTPS | Segurança e observabilidade mínimas honestas | RNF-05, RNF-09 |
| [ADR-014](../adrs/adr-014-dotnet-10.md) | Runtime .NET 10 (LTS) | LTS atual com runway até Nov/2028; .NET 9 descartado por ser STS; .NET 8 perto do EOL | Manutenibilidade |

---

## 13. Evoluções Futuras

O desafio menciona explicitamente que evoluções futuras são bem-vindas. Estas são as evoluções naturais da solução, priorizadas por valor:

**Curto prazo (próximas sprints):**

- **Frontend Web (SPA)** — interface mobile-first para o comerciante Carlos, baseada na persona e jornada documentadas nas seções 2 e 3. Blazor WASM mantém o stack 100% C#.
- **Consumer em processo dedicado** — extrair o `BackgroundService` (`TransactionConsumer`) da Balance API para `CashFlow.Balance.Worker` (refator mecânico) quando volume justificar ([ADR-004](../adrs/adr-004-consumer-hostedservice.md)).
- **Cache em memória (IMemoryCache)** — na Balance API, para reduzir pressão no banco em picos sustentados.
- **Stryker.NET (testes de mutação)** — complementa testes unitários medindo qualidade real das assertions, alinhado ao plano de estudo Verx.

**Médio prazo (estabilização):**

- **Circuit Breaker + DLQ** ([ADR-005](../adrs/adr-005-polly-retry.md)) — quando houver telemetria de produção mostrando cenários de falha sustentada ou mensagens venenosas que justifiquem.
- **Outbox Pattern** via MassTransit `UseEntityFrameworkOutbox` — eliminar a janela de inconsistência da [ADR-007](../adrs/adr-007-publish-after-commit.md). Requer migrar para EF Core no write side (revisa [ADR-010](../adrs/adr-010-dapper.md)).
- **Migração para Azure Service Bus** — em produção, trocar RabbitMQ por Service Bus via configuração do MassTransit (`UsingAzureServiceBus(...)`), ganhando DLQ gerenciada e SLA de 99.95%.
- **API Gateway (Azure APIM / Apigee)** — centraliza autenticação, rate limiting distribuído, transformação de payload e developer portal. Tópico relevante para a vaga Verx (cliente provavelmente do setor financeiro/regulado).
- **OAuth 2.0 / OIDC com Microsoft Entra ID** — quando houver identidade definida (frontend, mobile, integrações B2B).
- **OpenTelemetry completo** (traces + métricas + logs) com Application Insights — distributed tracing propagado pelo broker (W3C TraceContext em headers AMQP).

**Longo prazo (escala):**

- **Event Sourcing** — armazenar cada lançamento como evento imutável (event store em PostgreSQL ou Cosmos DB), permitindo reconstruir o estado a qualquer momento e facilitando auditoria financeira.
- **Cache distribuído (Redis)** — substituir IMemoryCache quando houver múltiplas instâncias da Balance API.
- **Autoscaling com KEDA** — escalar horizontalmente APIs e consumer com base em métricas de fila (queue depth) e CPU; viável em AKS/Container Apps.
- **Multi-tenancy** — suportar múltiplos comerciantes com isolamento por tenant (column-based ou schema-based, dependendo da escala).
- **Separação física dos schemas em instâncias dedicadas** — evolução natural do [ADR-003](../adrs/adr-003-postgres-schemas.md) quando o volume justificar isolamento de I/O real (não só lógico).

