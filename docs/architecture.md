# Arquitetura da Solução — CashFlow

> **Propósito deste documento:** dar uma visão arquitetural **unificada** da solução em ~15 minutos de leitura — estilo arquitetural adotado, estrutura de módulos, regras de dependência, e fluxos principais. Para o **porquê** de cada decisão (com trade-offs e alternativas descartadas), veja [`adrs/`](adrs/). Para os **requisitos** que motivaram cada decisão, veja [`rnfs/`](rnfs/). Para a **visualização**, veja [`diagrams/`](diagrams/).

---

## 1. Visão geral

CashFlow é um sistema de controle de fluxo de caixa diário, composto por **dois bounded contexts** que se comunicam de forma **assíncrona** através de um broker:

- **Transactions** *(write side)* — registra lançamentos individuais (débitos/créditos).
- **Balance** *(read side)* — expõe o saldo diário consolidado, mantido por uma projeção atualizada por eventos.

O design responde a dois requisitos não-negociáveis do enunciado: o **write side não pode cair se o read side cair** ([RNF-01](rnfs/rnf-01-disponibilidade.md)) e o **read side precisa absorver 50 RPS com no máximo 5% de perda** ([RNF-02](rnfs/rnf-02-carga.md)). Toda decisão arquitetural se justifica nessas duas restrições ou nas sete dimensões adicionais cobradas pelo desafio (Escalabilidade, Resiliência, Segurança, Padrões, Integração, Manutenibilidade, Observabilidade).

![Visão geral — Containers](diagrams/c4-containers.svg)

> Fonte editável: [`diagrams/c4-containers.mmd`](diagrams/c4-containers.mmd) · Página C4 completa: [`diagrams/c4-containers.md`](diagrams/c4-containers.md).

---

## 2. Estilos arquiteturais adotados

A solução combina **quatro estilos**, cada um respondendo a uma preocupação distinta. Nenhum é "puro" — todos têm escopo bem delimitado.

### 2.1. CQRS (Command Query Responsibility Segregation)

**Escopo:** macro-arquitetura — separação write/read em dois serviços.

| Lado | Projeto | Responsabilidade |
| --- | --- | --- |
| Command (write) | `CashFlow.Transactions.API` | Registra `Transaction`, persiste, publica `TransactionRegistered` |
| Query (read) | `CashFlow.Balance.API` | Mantém projeção `DailyBalance` via consumer; expõe queries |

**Trade-off central:** consistência eventual entre escrita e leitura (segundos de defasagem). Aceitável aqui — não é transferência em tempo real. Detalhes: [ADR-001](adrs/adr-001-cqrs.md).

### 2.2. Event-Driven Architecture (EDA)

**Escopo:** comunicação entre os dois bounded contexts.

A Transactions API **não conhece** a Balance API. Publica `CashFlow.Shared.Events.TransactionRegistered` no RabbitMQ via `IPublishEndpoint` (MassTransit). A Balance API consome via `TransactionConsumer` (BackgroundService) — desacoplamento temporal completo.

**Garantia do broker:** at-least-once → consumer precisa ser idempotente ([ADR-011](adrs/adr-011-idempotency.md)). Detalhes: [ADR-002](adrs/adr-002-rabbitmq-masstransit.md).

### 2.3. Clean Architecture (interna a cada API)

**Escopo:** organização interna de cada projeto API — camadas com **regra de dependência** (Dependency Rule).

![Clean Architecture — regra de dependência](diagrams/clean-architecture.svg)

> Fonte editável: [`diagrams/clean-architecture.mmd`](diagrams/clean-architecture.mmd)

A regra é **verificada em CI** via NetArchTest (fitness functions). Detalhes: [ADR-012](adrs/adr-012-architecture-tests.md).

### 2.4. DDD tático (Rich Domain Model)

**Escopo:** modelagem das entidades de domínio.

- **Construtor privado + factory method**: `Transaction.Register(...)`, `DailyBalance.New(...)`.
- **Propriedades com `private set`**: mutação apenas via método de domínio.
- **Value Objects**: `Money`, `TransactionType`, `MovementDate` encapsulam invariantes.
- **Métodos de negócio expressam intenção**: `balance.ApplyCredit(amount)` em vez de `balance.TotalCredits += amount`.
- **`DomainException`** para violações de invariantes — nunca `null` como sentinela.

Detalhes: [ADR-009](adrs/adr-009-rich-domain-model.md).

---

## 3. Estrutura de módulos da solução

### 3.1. Visão de árvore

```text
CashFlow.sln                                  ← solution file
│
├── src/
│   ├── CashFlow.Transactions.API/            ← Write side + /auth + Outbox dispatcher
│   │   ├── Controllers/                      ← TransactionsController, AuthController
│   │   ├── Domain/                           ← Rich Domain Model
│   │   │   ├── Entities/                     ← AuditableEntity, Transaction, AppUser, RefreshToken
│   │   │   ├── ValueObjects/                 ← Money, TransactionType, MovementDate
│   │   │   └── Exceptions/                   ← DomainException
│   │   ├── Application/
│   │   │   ├── Auth/                         ← AuthenticationService, Argon2idPasswordHasher,
│   │   │   │                                    Sha256RefreshTokenFactory, LockoutSettings,
│   │   │   │                                    RefreshTokenSettings, AuthDtos
│   │   │   ├── DTOs/                         ← RegisterTransactionRequest, TransactionResponse
│   │   │   ├── Services/                     ← ITransactionService, TransactionService
│   │   │   └── Validators/                   ← RegisterTransactionValidator (FluentValidation)
│   │   ├── Infrastructure/
│   │   │   ├── Auth/                         ← DemoUserSeeder (Argon2id, primeiro startup)
│   │   │   ├── Persistence/                  ← (Conn factory, UoW Dapper, DateOnlyTypeHandler)
│   │   │   ├── Repositories/                 ← TransactionRepository, AppUserRepository,
│   │   │   │                                    RefreshTokenRepository
│   │   │   ├── Messaging/                    ← IEventPublisher, MassTransitEventPublisher,
│   │   │   │                                    NoOpEventPublisher (testing)
│   │   │   ├── Outbox/                       ← IOutboxRepository, OutboxRepository,
│   │   │   │                                    OutboxDispatcher (BackgroundService — ADR-025)
│   │   │   └── Migrations/
│   │   │       ├── MigrationRunner.cs        ← DbUp (journal em transactions.schemaversions)
│   │   │       └── Scripts/                  ← 002_create_transactions_table,
│   │   │                                        003_create_app_users_table,
│   │   │                                        004_alter_app_users_lockout,
│   │   │                                        005_create_refresh_tokens_table,
│   │   │                                        006_create_outbox_events
│   │   ├── Program.cs                        ← Composition root
│   │   ├── TransactionsApiAssembly.cs        ← Marker p/ WebApplicationFactory dos BDD
│   │   ├── appsettings.json
│   │   └── Dockerfile
│   │
│   ├── CashFlow.Balance.API/                 ← Read side + Consumer + DLQ admin
│   │   ├── Controllers/                      ← BalanceController, AdminController (DLQ)
│   │   ├── Consumers/                        ← TransactionConsumer (BackgroundService)
│   │   ├── Domain/
│   │   │   ├── Entities/                     ← DailyBalance
│   │   │   └── Exceptions/                   ← DomainException
│   │   ├── Application/
│   │   │   ├── Admin/                        ← ErrorQueueRedeliveryService (move da DLQ)
│   │   │   ├── DTOs/                         ← BalanceResponse
│   │   │   └── Services/                     ← BalanceQueryService, ConsolidationService
│   │   ├── Infrastructure/
│   │   │   ├── Persistence/                  ← (mesmo padrão de Transactions)
│   │   │   ├── Repositories/                 ← BalanceRepository, ProcessedEventsRepository
│   │   │   └── Migrations/
│   │   │       └── Scripts/                  ← 002_create_daily_balance,
│   │   │                                        003_create_processed_events
│   │   ├── Program.cs                        ← + UseDelayedRedelivery (ADR-025) + SAC + RL
│   │   ├── appsettings.json
│   │   └── Dockerfile
│   │
│   └── CashFlow.Shared/                      ← Shared Kernel mínimo
│       ├── Events/
│       │   └── TransactionRegistered.cs      ← Contrato do evento entre os dois contextos
│       └── Security/                         ← JwtTokenService, JwtSettings,
│                                                AuthorizationPolicies, CashFlowRoles,
│                                                SecurityServiceCollectionExtensions
│
├── tests/                                    ← 4 projetos de teste
│   ├── CashFlow.UnitTests/                   ← 95 testes — Domain de ambos contextos
│   │   ├── Transactions/Domain/              ← TransactionTests, MoneyTests, MovementDateTests,
│   │   │                                        TransactionTypeTests, AppUserTests, RefreshTokenTests
│   │   ├── Balance/Domain/                   ← DailyBalanceTests
│   │   ├── stryker-config.json               ← Stryker.NET (ADR-020) — threshold ≥ 70%
│   │   └── StrykerOutput/                    ← Reports HTML (gitignored)
│   │
│   ├── CashFlow.Architecture.Tests/          ← 12 fitness functions (NetArchTest — ADR-012)
│   │   ├── LayerDependencyTests.cs           ← Dependency Rule (Clean Architecture)
│   │   ├── ImmutabilityTests.cs              ← Rich Domain (sem setters públicos)
│   │   └── NamingConventionTests.cs          ← Repositories/Interfaces
│   │
│   ├── CashFlow.Bdd.Tests/                   ← 15 cenários Reqnroll pt-BR (ADR-017, ADR-022)
│   │   ├── Features/                         ← AutenticacaoE2E.feature, SaldoConsolidado.feature
│   │   ├── Steps/                            ← AutenticacaoE2ESteps (E2E via WebApplicationFactory
│   │   │                                        + Testcontainers Postgres) + SaldoConsolidadoSteps
│   │   │                                        (BDD de domínio puro)
│   │   ├── Setup/                            ← CashFlowApiFixture (boot da API "in-process")
│   │   └── reqnroll.json
│   │
│   └── CashFlow.LoadTests/                   ← NBomber (ADR-019) — não é IsTestProject;
│                                                roda via `dotnet run --project ... -c Release`
│
├── infra/
│   ├── postgres/init.sql                     ← Users + schemas + GRANTs (executa no 1º start)
│   └── rabbitmq/Dockerfile                   ← + plugin rabbitmq_delayed_message_exchange
│                                                (necessário p/ UseDelayedRedelivery — ADR-025)
│
├── .github/
│   └── workflows/
│       ├── ci.yml                            ← build + 3 suítes (push/PR)
│       └── mutation.yml                      ← Stryker (workflow_dispatch manual)
│
├── .config/
│   └── dotnet-tools.json                     ← Stryker como local tool
│
├── docs/                                     ← Documentação arquitetural
│   ├── architecture.md                       ← Este documento
│   ├── analysis/analise-desafio-arquiteto.md ← Análise do desafio
│   ├── adrs/                                 ← 25 ADRs individuais + README índice
│   ├── rnfs/                                 ← 9 RNFs individuais + README índice
│   ├── diagrams/                             ← Diagramas C4 + fluxos (SVG embedado + fonte .mmd)
│   ├── challenge/                            ← PDF original do desafio
│   └── references/                           ← Material de estudo
│
├── docker-compose.yml                        ← Postgres + RabbitMQ (custom) + 2 APIs
└── README.md                                 ← Entry point + glossário + instruções
```

**Totais:** 3 projetos de produção + 4 de teste/carga = 7 projetos no `.sln`. **122 testes** automatizados (95 unit + 12 architecture + 15 BDD).

### 3.2. Grafo de dependências entre projetos

```text
                ┌────────────────────────┐
                │   CashFlow.Shared      │   (apenas eventos)
                └──────────┬─────────────┘
                           │ depende
                ┌──────────┴──────────────┐
                │                         │
                ▼                         ▼
   ┌───────────────────────────┐  ┌─────────────────────────┐
   │ CashFlow.Transactions.API │  │   CashFlow.Balance.API  │
   └─────────────┬─────────────┘  └────────────┬────────────┘
                 │                             │
                 │   referenciados por         │
                 ▼                             ▼
        ┌────────────────────────────────────────────────┐
        │           CashFlow.UnitTests                   │
        │           CashFlow.Architecture.Tests          │
        └────────────────────────────────────────────────┘
```

**Regras:**

- `Transactions.API` e `Balance.API` **não se referenciam** — toda comunicação é via `Shared.Events` no broker.
- `Shared` **não depende de ninguém** (puro POCO record).
- Testes referenciam tudo — para validar tipos e arquitetura.

### 3.3. Por que 3 projetos de produção (e não 11)

Uma leitura comum de Clean Architecture sugere 4 projetos por bounded context: `Domain`, `Application`, `Infrastructure`, `API`. Para 2 contextos = **8 projetos** só de produção, mais `Shared`. Para um domínio com **2 entidades** isso é over-engineering: ninguém vai trocar a UI sem trocar o resto, ninguém vai compartilhar `Application` entre dois consumidores diferentes.

A decisão pragmática:

- **Camadas como pastas internas** (`Domain/`, `Application/`, `Infrastructure/`) — demonstra a separação sem multiplicar projetos.
- **Regra de dependência verificada por testes de arquitetura** ([ADR-012](adrs/adr-012-architecture-tests.md)) — o equivalente em comportamento ao isolamento por projeto, sem a cerimônia.
- **Quando faz sentido separar**: se houver real reuso (ex: `Domain` compartilhado por API + Worker dedicado), separar em projetos. Citado como evolução em [ADR-004](adrs/adr-004-consumer-hostedservice.md).

---

## 4. Estrutura interna de cada API (Clean Architecture)

Cada API segue o mesmo padrão de quatro camadas. As pastas refletem a regra de dependência.

### 4.1. Camadas e responsabilidades

| Camada | Pasta | Responsabilidade | O que NÃO pode |
| --- | --- | --- | --- |
| **Domain** | `Domain/` | Entidades, Value Objects, regras de negócio e invariantes | Referenciar Application, Infrastructure, Controllers, ASP.NET, Dapper, MassTransit, FluentValidation |
| **Application** | `Application/` | Orquestração de casos de uso (Services), DTOs, validação | Referenciar Controllers, ASP.NET-specifics |
| **Infrastructure** | `Infrastructure/` | Persistência (Dapper), mensageria (MassTransit), migrations (DbUp) | Conhecer detalhes de HTTP |
| **API (Controllers)** | `Controllers/`, `Program.cs` | Endpoints HTTP, composition root (DI), middlewares | Lógica de negócio (deve delegar para Application) |

### 4.2. Regra de dependência (visualizada)

![Regra de dependência — versão detalhada](diagrams/regra-dependencia.svg)

> Fonte editável: [`diagrams/regra-dependencia.mmd`](diagrams/regra-dependencia.mmd)
>
> **Observação:** neste MVP, as **interfaces de repositório** (`ITransactionRepository`, `IBalanceRepository`) vivem em `Infrastructure/Repositories/` (não em `Domain/`). Trade-off pragmático: evitar uma pasta extra `Domain/Abstractions/` para 2 entidades. Em projetos maiores, mover para `Domain/` deixa a regra de dependência mais óbvia. Os testes de arquitetura validam o ponto que importa: `Domain` não conhece `Infrastructure`.

### 4.3. Validação automática (fitness functions)

`CashFlow.Architecture.Tests` valida no CI:

| Teste | Garantia |
| --- | --- |
| `Transactions_Domain_MustNotDependOn_Infrastructure_Application_orAspNetCore` | Domain isolado |
| `Balance_Domain_MustNotDependOn_Infrastructure_Application_orAspNetCore` | Domain isolado |
| `Transactions_Application_MustNotDependOn_Controllers` | Application não conhece HTTP |
| `Transactions_Entities_MustNotHavePublicSetters` | Rich Domain — encapsulamento |
| `Balance_Entities_MustNotHavePublicSetters` | Rich Domain — encapsulamento |
| `Transactions_Repositories_MustEndWith_Repository` | Convenção de nomenclatura |
| `Balance_Repositories_MustEndWith_Repository` | Convenção de nomenclatura |
| `Transactions_RepositoryInterfaces_MustStartWith_I` | Convenção de nomenclatura |

---

## 5. Fluxos arquiteturais principais

### 5.1. Fluxo de escrita: `POST /api/v1/transactions`

**Caminho síncrono (request HTTP):**

![Fluxo de escrita — sync](diagrams/fluxo-escrita.svg)

> Fonte editável: [`diagrams/fluxo-escrita.mmd`](diagrams/fluxo-escrita.mmd)

**Caminho assíncrono (publish — fora do request):**

![Fluxo de outbox dispatch — async](diagrams/fluxo-outbox-dispatch.svg)

> Fonte editável: [`diagrams/fluxo-outbox-dispatch.mmd`](diagrams/fluxo-outbox-dispatch.mmd)

**Por que outbox + dispatcher:** o `INSERT transação` e o `INSERT outbox` rodam na **mesma UoW** — se o broker estiver fora, a transação ainda é persistida e o evento fica `WHERE published_at IS NULL` até o dispatcher conseguir publicar. Fecha a janela que o [ADR-007](adrs/adr-007-publish-after-commit.md) reconhecia (publish-after-commit perdido). Detalhes da reliability completa em [ADR-025](adrs/adr-025-outbox-and-dlq.md).

**ADRs envolvidas:** [ADR-009](adrs/adr-009-rich-domain-model.md) (Rich Domain), [ADR-010](adrs/adr-010-dapper.md) (Dapper+UoW), [ADR-015](adrs/adr-015-application-services-no-mediatr.md) (Application Service dispatcher), [ADR-025](adrs/adr-025-outbox-and-dlq.md) (Outbox + Delayed Redelivery + DLQ), e a [ADR-007](adrs/adr-007-publish-after-commit.md) (superada).

### 5.2. Fluxo de consumo: `TransactionConsumer`

![Fluxo de consumo — Balance API](diagrams/fluxo-consumo.svg)

> Fonte editável: [`diagrams/fluxo-consumo.mmd`](diagrams/fluxo-consumo.mmd)

**ADRs envolvidas:** [ADR-002](adrs/adr-002-rabbitmq-masstransit.md) (broker), [ADR-005](adrs/adr-005-polly-retry.md) (Polly), [ADR-011](adrs/adr-011-idempotency.md) (idempotência).

### 5.3. Fluxo de leitura: `GET /api/v1/balance/{date}`

![Fluxo de leitura — saldo consolidado](diagrams/fluxo-leitura.svg)

> Fonte editável: [`diagrams/fluxo-leitura.mmd`](diagrams/fluxo-leitura.mmd)

**ADRs envolvidas:** [ADR-006](adrs/adr-006-rate-limiting.md) (rate limit), [ADR-001](adrs/adr-001-cqrs.md) (CQRS: leitura O(1) é consequência de termos a projeção pronta).

---

## 6. Bounded Contexts e Shared Kernel

### 6.1. Os dois contextos

| Contexto | Projeto | Linguagem ubíqua | Aggregate Root |
| --- | --- | --- | --- |
| **Transactions** | `CashFlow.Transactions.API` | "Lançamento", "registrar", "débito/crédito" | `Transaction` |
| **Balance** | `CashFlow.Balance.API` | "Saldo diário", "consolidado", "aplicar lançamento ao saldo" | `DailyBalance` |

Os modelos são **propositalmente diferentes**:

- `Transaction` tem `Money`, `TransactionType`, `MovementDate` — preocupações do write side.
- `DailyBalance` tem `TotalCredits`, `TotalDebits`, `Balance` (derivado) — preocupações de agregação para leitura.

Não há classe `Transaction` compartilhada — cada contexto modela o que precisa. A única coisa cruzada é o **evento de integração**.

### 6.2. Shared Kernel mínimo: `CashFlow.Shared`

```text
CashFlow.Shared/
└── Events/
    └── TransactionRegistered.cs   ← record imutável com 7 propriedades
```

**Princípio:** o Shared Kernel é deliberadamente **mínimo** — só o contrato do evento de integração. Nada de entidades compartilhadas, nada de utilitários cross-cutting, nada de DTOs HTTP. Cada bounded context é livre para evoluir seu modelo interno.

**Por quê:** Shared Kernels que crescem viram acoplamento implícito disfarçado. Quanto menor, mais fácil garantir que os contextos podem evoluir independentemente.

### 6.3. Comunicação entre contextos

![Comunicação entre bounded contexts](diagrams/comunicacao-contextos.svg)

> Fonte editável: [`diagrams/comunicacao-contextos.mmd`](diagrams/comunicacao-contextos.mmd)

- **Sem chamada síncrona** entre os contextos — desacoplamento total.
- **Sem banco compartilhado** — schemas isolados com GRANTs distintos.
- **Sem código compartilhado de domínio** — só o evento.

---

## 7. Onde ler mais

| Quero entender... | Comece por |
| --- | --- |
| **Por que** cada decisão foi tomada (com trade-offs) | [`adrs/`](adrs/) — 25 ADRs individuais |
| **Quais requisitos** motivam cada decisão | [`rnfs/`](rnfs/) — 9 RNFs individuais |
| **Como** os componentes se relacionam visualmente | [`diagrams/`](diagrams/) — diagramas C4 + fluxos + estruturais (SVG embedado + fonte `.mmd` editável) |
| **Quem é o usuário** e qual a jornada | [`analysis/analise-desafio-arquiteto.md` § 2-3](analysis/analise-desafio-arquiteto.md) |
| **Como rodar** localmente | [`../README.md`](../README.md) |
| **O que cada termo significa** (pt-br ↔ en-us) | [`../README.md` — Glossário](../README.md#glossário-de-termos) |
| **O desafio original** | [`challenge/desafio-arquiteto-software.pdf`](challenge/desafio-arquiteto-software.pdf) |
