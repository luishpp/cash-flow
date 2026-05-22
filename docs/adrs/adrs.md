# Architecture Decision Records (ADRs)

## Rastreabilidade: RNF → Decisões

Cada decisão neste documento existe para atender a um requisito não-funcional específico do desafio. Nenhuma decisão foi tomada por preferência tecnológica — todas são respostas diretas a restrições do enunciado.

| Requisito Não-Funcional | Descrição | ADRs que atendem |
|---|---|---|
| **RNF-01** | O serviço de lançamentos não pode ficar indisponível se o consolidado cair | ADR-001, ADR-002, ADR-003, ADR-004 |
| **RNF-02** | Em picos, o consolidado recebe 50 req/s com no máximo 5% de perda | ADR-005, ADR-006 |
| **Integridade** | Garantir que eventos publicados correspondam a dados persistidos | ADR-007 |
| **Executabilidade** | Rodar localmente com instruções claras | ADR-008 |

---

## ADR-001: Adotar CQRS para separar lançamentos e consolidado

**Status:** Aceita

**Contexto:**
O desafio pede dois serviços — um de controle de lançamentos e outro de consolidado diário. O RNF-01 exige que a queda do consolidado não afete o serviço de lançamentos. Isso implica que os dois não podem compartilhar o mesmo processo, a mesma transação ou a mesma dependência crítica.

**Decisão:**
Adotar CQRS (Command Query Responsibility Segregation), onde o serviço de lançamentos é o command side (escrita) e o consolidado é o query side (leitura). O saldo consolidado é uma projeção derivada dos eventos de lançamento, não uma consulta direta ao mesmo banco.

**Trade-offs:**

| Ganha | Perde |
|---|---|
| Independência total de deploy e ciclo de vida entre os serviços | Consistência eventual — o saldo pode estar alguns segundos defasado |
| Modelo de leitura otimizado para consulta (desnormalizado, rápido) | Duplicação de dados entre write store e read store |
| Escala independente — pode ter N instâncias do consolidado sem afetar lançamentos | Complexidade de sincronização via eventos |
| Atende RNF-01 diretamente | Mais componentes para operar (2 APIs + 1 Worker vs. 1 API monolítica) |

**Por que a consistência eventual é aceitável aqui:**
O consolidado é uma consulta de saldo — não é uma transferência bancária entre contas. Um atraso de 2-3 segundos entre o registro do lançamento e a atualização do saldo consolidado não causa impacto operacional para o comerciante. Se fosse um sistema de pagamentos em tempo real, essa decisão seria diferente.

**Alternativa descartada:**
Serviço único com consolidação síncrona — simples de implementar, mas viola RNF-01: se a lógica de consolidação falhar, o lançamento falha junto.

---

## ADR-002: Usar RabbitMQ com MassTransit para mensageria

**Status:** Aceita

**Contexto:**
Com CQRS adotado (ADR-001), é necessário um mecanismo para comunicar eventos do write side para o read side. O RNF-01 exige que essa comunicação seja assíncrona — se o consumidor estiver fora, o produtor não pode ser afetado. O desafio também exige execução local via Docker.

**Decisão:**
Usar RabbitMQ como message broker com MassTransit como camada de abstração no código .NET.

**Por que RabbitMQ (e não Azure Service Bus):**

| Critério | RabbitMQ | Azure Service Bus |
|---|---|---|
| Execução local em Docker | Imagem oficial, sobe em segundos, ARM64 nativo | Emulador experimental, instável, sem ARM64 |
| UI de gerenciamento | Management plugin incluso (porta 15672) | Sem UI local |
| Tamanho da imagem | ~180MB | N/A (serviço cloud) |
| Maturidade do ecossistema .NET | MassTransit, EasyNetQ, client oficial | MassTransit, Azure.Messaging |

**Por que MassTransit como abstração:**
O MassTransit abstrai o broker. O código de negócio publica e consome mensagens via interfaces (`IPublishEndpoint`, `IConsumer<T>`) que não referenciam RabbitMQ diretamente. A troca para Azure Service Bus em produção é uma mudança de uma linha no `Program.cs`:

```csharp
// Local (RabbitMQ)
cfg.UsingRabbitMq((ctx, cfg) => { cfg.Host("localhost"); });

// Produção (Azure Service Bus)
cfg.UsingAzureServiceBus((ctx, cfg) => { cfg.Host(connectionString); });
```

**Trade-offs:**

| Ganha | Perde |
|---|---|
| Desacoplamento temporal — mensagens sobrevivem a quedas do consumidor | Complexidade operacional — mais um componente para monitorar |
| Garantia de entrega at-least-once | Necessidade de tratar idempotência no consumidor (evolução futura) |
| Portabilidade via MassTransit — troca de broker sem refatoração | Abstração adicional — curva de aprendizado do MassTransit |
| Atende RNF-01 diretamente | Consistência eventual (consequência aceita na ADR-001) |

**Alternativa descartada:**
Chamada HTTP síncrona entre os serviços — viola RNF-01 (se o consolidado cair, o POST de lançamento retornaria erro ou timeout). Retry com HTTP resolve parte do problema, mas o acoplamento de disponibilidade permanece.

---

## ADR-003: PostgreSQL com dois databases separados

**Status:** Aceita

**Contexto:**
O RNF-01 exige que a indisponibilidade do consolidado não afete lançamentos. Isso precisa se estender até a camada de dados — se os dois serviços usarem o mesmo database e ele travar numa query pesada de consolidação, o serviço de lançamentos seria afetado. O desafio também exige execução local (Docker) em qualquer plataforma (Windows, macOS Intel e Apple Silicon).

**Decisão:**
Usar PostgreSQL com dois databases separados (`fluxocaixa_lancamentos` e `fluxocaixa_consolidado`) na mesma instância Docker.

**Por que PostgreSQL (e não SQL Server):**

| Critério | PostgreSQL | SQL Server |
|---|---|---|
| Docker ARM64 (Apple Silicon) | Imagem nativa, funciona perfeitamente | Apenas AMD64, requer Rosetta 2, instável |
| Tamanho da imagem | ~80MB | ~1.5GB |
| Startup time | ~2 segundos | ~15-30 segundos |
| Licença | Open source, gratuito | Licença Microsoft (Developer Edition gratuita para dev) |
| EF Core | Suportado via Npgsql, sem diferença de código | Suportado nativamente |
| Azure (produção) | Azure Database for PostgreSQL (gerenciado) | Azure SQL Database (gerenciado) |

O fator decisivo é pragmático: se o avaliador tiver um MacBook com Apple Silicon, o SQL Server em Docker simplesmente pode não subir. PostgreSQL elimina esse risco.

**Por que dois databases (e não dois containers):**

| Abordagem | Vantagem | Desvantagem |
|---|---|---|
| Um container, dois databases | Simples no Docker Compose, menos recursos | Compartilha CPU/memória do container |
| Dois containers separados | Isolamento total de recursos | Dobra a complexidade do Compose, usa mais memória |

Para execução local, um container com dois databases é suficiente. Em produção (Azure), seriam duas instâncias gerenciadas separadas. A separação lógica demonstra a intenção arquitetural sem over-engineering para o ambiente de avaliação.

**Trade-offs:**

| Ganha | Perde |
|---|---|
| Isolamento lógico — cada serviço acessa apenas seu database | Em cenário extremo local, compartilham I/O do mesmo container |
| Roda em qualquer plataforma sem emulação | Menor familiaridade no ecossistema .NET que SQL Server (percepção, não realidade) |
| Imagem leve, startup rápido | Precisa do pacote Npgsql (troca trivial no EF Core) |
| Atende RNF-01 na camada de dados | — |

**Alternativa descartada:**
Database único com schemas separados — funciona, mas não demonstra a intenção de isolamento. Com dois databases, a separação é visível no `docker compose logs` e na connection string de cada serviço.

---

## ADR-004: Worker Service independente para consumo de eventos

**Status:** Aceita

**Contexto:**
O Worker de Consolidação consome eventos `LancamentoRegistrado` e atualiza a projeção de saldo. Ele poderia ser um background service dentro da API de Consolidado (mesmo processo) ou um processo independente.

**Decisão:**
Implementar o consumidor como um `Worker Service` (.NET 8) em processo separado da API de Consolidado.

**Trade-offs:**

| Ganha | Perde |
|---|---|
| Falha no Worker não derruba a API de consulta (e vice-versa) | Mais um container no Docker Compose |
| Escala independente — posso ter 3 Workers e 1 API, ou 1 Worker e 3 APIs | Mais um projeto no solution |
| Responsabilidade única — Worker consome, API consulta | Precisa garantir que ambos apontem para o mesmo database |
| Deploy independente — posso atualizar o Worker sem afetar consultas | — |

**Cenário que justifica a separação:**
Se o Worker estiver processando um backlog grande (centenas de mensagens acumuladas), ele vai consumir CPU e I/O de escrita no banco. Se estivesse no mesmo processo da API, a latência das consultas (`GET /consolidado/{data}`) seria degradada. Com processos separados, a API de consulta continua respondendo rápido enquanto o Worker digere o backlog.

**Alternativa descartada:**
`IHostedService` dentro da API de Consolidado — menor complexidade operacional, mas acopla o ciclo de vida do consumidor à API. Um crash no consumo de mensagens poderia reiniciar o processo inteiro, derrubando a API de consulta junto.

---

## ADR-005: Polly para políticas de resiliência

**Status:** Aceita

**Contexto:**
O RNF-02 define no máximo 5% de perda de requisições no consolidado. No Worker, falhas transitórias (banco temporariamente indisponível, timeout de rede) podem causar perda de mensagens se não forem tratadas. É necessário um mecanismo de retry inteligente e proteção contra falhas em cascata.

**Decisão:**
Usar Polly para implementar três políticas compostas no Worker de Consolidação:

1. **Retry com exponential backoff** — tenta 3 vezes com intervalos crescentes (1s, 2s, 4s) antes de desistir.
2. **Circuit Breaker** — após 5 falhas consecutivas, abre o circuito por 30 segundos. Evita que o Worker fique martelando um banco que está fora.
3. **Dead Letter Queue (DLQ)** — mensagens que falharam em todas as retentativas são movidas para a DLQ do RabbitMQ, não descartadas.

**Por que essas políticas atendem o RNF-02:**

| Tipo de falha | Sem Polly | Com Polly |
|---|---|---|
| Timeout transitório no banco | Mensagem perdida → saldo inconsistente | Retry resolve em 1-4 segundos |
| Banco indisponível por minutos | Worker em loop de erro, CPU 100% | Circuit breaker pausa, mensagens seguras na fila |
| Mensagem malformada / bug | Loop infinito de retry | Vai para DLQ após 3 tentativas, não bloqueia a fila |

**Trade-offs:**

| Ganha | Perde |
|---|---|
| Tolerância a falhas transitórias sem perder mensagens | Latência adicional nos cenários de retry (1-4s por tentativa) |
| Proteção contra cascata (circuit breaker) | Complexidade no diagnóstico — precisa monitorar estado do circuito |
| Mensagens com falha persistente ficam na DLQ para análise | DLQ precisa ser monitorada (não é auto-resolvida) |
| Atende RNF-02 diretamente | — |

**Configuração concreta:**

```csharp
// Retry
.AddRetry(new RetryStrategyOptions
{
    MaxRetryAttempts = 3,
    Delay = TimeSpan.FromSeconds(1),
    BackoffType = DelayBackoffType.Exponential
})

// Circuit Breaker
.AddCircuitBreaker(new CircuitBreakerStrategyOptions
{
    FailureRatio = 0.5,
    MinimumThroughput = 5,
    SamplingDuration = TimeSpan.FromSeconds(30),
    BreakDuration = TimeSpan.FromSeconds(30)
})
```

**Alternativa descartada:**
Retry manual com `try/catch` e `Thread.Sleep` — funciona para casos simples, mas não oferece circuit breaker, não compõe políticas, e é propenso a erros (esquecimento de jitter, retry infinito acidental).

---

## ADR-006: Rate limiting nativo do .NET 8

**Status:** Aceita

**Contexto:**
O RNF-02 define 50 req/s como pico no serviço de consolidado. É necessário proteger o serviço contra sobrecarga, mantendo a perda controlada abaixo de 5%. Uma opção seria usar um API Gateway externo (Azure API Management), mas isso adiciona um componente que não roda localmente em Docker.

**Decisão:**
Usar o middleware nativo `Microsoft.AspNetCore.RateLimiting` do .NET 8 com política de fixed window na API de Consolidado.

**Configuração concreta:**

```csharp
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("consolidado", opt =>
    {
        opt.Window = TimeSpan.FromSeconds(1);
        opt.PermitLimit = 50;
        opt.QueueLimit = 5;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
```

**Como isso atende o RNF-02:**

| Cenário | Comportamento |
|---|---|
| ≤50 req/s | Todas atendidas normalmente |
| 51-55 req/s | 5 entram na fila, processadas no próximo segundo |
| >55 req/s | Excedentes recebem HTTP 429 com `Retry-After` header |

Com a fila de 5 (QueueLimit), picos curtos de até 55 req/s são absorvidos. Acima disso, a rejeição é explícita e controlada — o cliente sabe que precisa esperar, e a perda nunca excede o que ultrapassa 55/s, bem dentro do teto de 5%.

**Trade-offs:**

| Ganha | Perde |
|---|---|
| Zero dependência externa — roda em qualquer ambiente | Rate limiting per-instance (não distribuído entre réplicas) |
| Configuração simples, testável | Em produção com múltiplas instâncias, precisaria de rate limiting distribuído (Redis + sliding window) |
| Atende RNF-02 para o cenário do desafio (instância única) | Não substitui um API Gateway completo (sem roteamento, sem auth centralizada) |
| Resposta padrão HTTP 429 com `Retry-After` | — |

**Alternativa descartada:**
Azure API Management — resolve rate limiting, auth e roteamento, mas é um serviço gerenciado que custa ~US$300/mês e não roda em Docker. Para um desafio que precisa funcionar com `docker compose up`, é over-engineering. Documentado como evolução futura.

---

## ADR-007: Publicação de evento após SaveChanges

**Status:** Aceita

**Contexto:**
Ao registrar um lançamento, a API persiste no banco e publica um evento no RabbitMQ. A ordem dessas duas operações importa — se publicar antes de salvar, pode gerar mensagens fantasma (evento sem dados). Se salvar antes de publicar e a publicação falhar, o lançamento existe mas o consolidado nunca é atualizado.

**Decisão:**
Publicar o evento `LancamentoRegistrado` após o `SaveChangesAsync()` do EF Core retornar com sucesso.

**Cenários de falha e comportamento:**

| Cenário | O que acontece | Impacto |
|---|---|---|
| SaveChanges OK, Publish OK | Fluxo feliz | Nenhum |
| SaveChanges FALHA | Nenhum evento publicado | Correto — não há lançamento para consolidar |
| SaveChanges OK, Publish FALHA | Lançamento salvo, evento perdido | Saldo consolidado fica defasado |

**Trade-offs:**

| Ganha | Perde |
|---|---|
| Nunca gera mensagem fantasma (evento sem dados) | Em caso raro de falha na publicação, mensagem se perde |
| Implementação simples e compreensível | Não garante exactly-once entre banco e broker |
| Suficiente para o escopo do desafio | — |

**O cenário de falha na publicação é raro?**
Sim. O RabbitMQ está na mesma rede Docker que a API. A latência de publicação é sub-milissegundo. Falha nesse ponto significa que o RabbitMQ caiu — nesse caso, o retry do MassTransit tenta novamente. Se o RabbitMQ estiver genuinamente fora, a mensagem é perdida e o saldo consolida sem esse lançamento até que uma reconciliação manual ou reprocessamento corrija.

**Evolução futura — Outbox Pattern:**
Para eliminar completamente esse cenário, o Outbox Pattern salva a mensagem na mesma transação do banco e um processo em background despacha para o broker. O MassTransit tem suporte nativo (`UseEntityFrameworkOutbox`). Documentado como evolução futura — para o escopo do desafio, publish-after-save é suficiente.

---

## ADR-008: Docker Compose para execução local

**Status:** Aceita

**Contexto:**
O desafio exige explicitamente: "Readme com instruções claras de como rodar localmente." O avaliador precisa clonar o repositório, rodar um comando, e ter tudo funcionando — APIs, banco, broker, worker.

**Decisão:**
Usar Docker Compose para orquestrar todos os componentes:

| Serviço | Imagem | Porta |
|---|---|---|
| API Lançamentos | Build local (Dockerfile) | 5001 |
| API Consolidado | Build local (Dockerfile) | 5002 |
| Worker Consolidação | Build local (Dockerfile) | — |
| PostgreSQL | `postgres:16-alpine` | 5432 |
| RabbitMQ | `rabbitmq:3-management-alpine` | 5672, 15672 |

**Trade-offs:**

| Ganha | Perde |
|---|---|
| Um comando para subir tudo (`docker compose up --build`) | Requer Docker Desktop instalado |
| Ambiente idêntico para qualquer avaliador | Primeiro build mais lento (restore + compile das imagens .NET) |
| Dependências isoladas — nada instalado na máquina do avaliador | Consome mais memória que rodar direto (~500MB total para todos os containers) |
| Imagens Alpine para minimizar tamanho | — |

**Health checks e dependências:**
O Docker Compose define `depends_on` com `condition: service_healthy` para que as APIs só subam após o PostgreSQL aceitar conexões e o Worker só suba após o RabbitMQ estar pronto. Isso evita erros de "connection refused" nos primeiros segundos.

```yaml
services:
  postgres:
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      retries: 5

  rabbitmq:
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "check_port_connectivity"]
      interval: 5s
      retries: 5

  api-lancamentos:
    depends_on:
      postgres:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy
```
