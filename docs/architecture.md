# Arquitetura da Solução — CashFlow

> **Propósito deste documento:** dar uma visão arquitetural **unificada** da solução em ~15 minutos de leitura — estilo arquitetural adotado, estrutura de módulos, regras de dependência, e fluxos principais. Para o **porquê** de cada decisão (com trade-offs e alternativas descartadas), veja [`adrs/`](adrs/). Para os **requisitos** que motivaram cada decisão, veja [`rnfs/`](rnfs/). Para a **visualização**, veja [`diagrams/`](diagrams/).

---

## 1. Visão geral

CashFlow é um sistema de controle de fluxo de caixa diário, composto por **três bounded contexts** com **deploy units distintas dentro do BC Balance** (ADR-026, ADR-027, ADR-028):

- **Identity** *(auth)* — emite JWT, gerencia AppUser + RefreshToken + lockout. ([ADR-027](adrs/adr-027-identity-service-extraction.md))
- **Transactions** *(write side)* — registra lançamentos individuais (débitos/créditos), publica eventos via Outbox.
- **Balance** *(read side)* — expõe o saldo diário consolidado, mantido por uma projeção atualizada por eventos.
  - **Balance.API** — read side puro (`GET /balance`).
  - **Balance.Worker** — consumer + dono das migrations do schema balance. ([ADR-026](adrs/adr-026-balance-worker-extraction.md))
  - **Balance.Core** — shared kernel intra-BC (Domain + Persistence + BalanceRepository).
- **Admin** *(ops)* — endpoints administrativos da DLQ. Sem Postgres. ([ADR-028](adrs/adr-028-admin-api-extraction.md))

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
│   ├── CashFlow.Identity.API/                ← Auth BC — login, refresh, logout (ADR-027)
│   │   ├── Controllers/                      ← AuthController
│   │   ├── Domain/                           ← AppUser, RefreshToken, DomainException
│   │   ├── Application/Auth/                 ← AuthenticationService, Argon2idPasswordHasher,
│   │   │                                        Sha256RefreshTokenFactory, LockoutSettings,
│   │   │                                        RefreshTokenSettings, AuthDtos
│   │   ├── Infrastructure/
│   │   │   ├── Auth/                         ← DemoUserSeeder
│   │   │   ├── Persistence/                  ← Dapper UoW próprio (intra-BC)
│   │   │   ├── Repositories/                 ← AppUserRepository, RefreshTokenRepository
│   │   │   └── Migrations/Scripts/           ← 001-003 (schema identity)
│   │   ├── Program.cs / appsettings.json / Dockerfile
│   │
│   ├── CashFlow.Transactions.API/            ← Write side + Outbox dispatcher
│   │   ├── Controllers/                      ← TransactionsController
│   │   ├── Domain/                           ← Transaction, Money, TransactionType, MovementDate
│   │   ├── Application/                      ← DTOs, Services, Validators
│   │   ├── Infrastructure/
│   │   │   ├── Persistence/                  ← Dapper UoW próprio (intra-BC)
│   │   │   ├── Repositories/                 ← TransactionRepository
│   │   │   ├── Messaging/                    ← IEventPublisher, MassTransitEventPublisher
│   │   │   ├── Outbox/                       ← OutboxRepository, OutboxDispatcher
│   │   │   └── Migrations/Scripts/           ← 002 (transactions) + 006 (outbox_events)
│   │   ├── Program.cs / TransactionsApiAssembly.cs / Dockerfile
│   │
│   ├── CashFlow.Balance.API/                 ← Read side puro (GET /balance)
│   │   ├── Controllers/                      ← BalanceController
│   │   ├── Application/                      ← DTOs, BalanceQueryService
│   │   ├── Infrastructure/Configuration/     ← RateLimitSettings
│   │   ├── Program.cs / Dockerfile
│   │   └── ← stack mínima: 5 packages (era 13 antes do refator)
│   │
│   ├── CashFlow.Balance.Worker/              ← Consumer + dono das migrations (ADR-026)
│   │   ├── Consumers/                        ← TransactionConsumer
│   │   ├── Application/Services/             ← IConsolidationService, ConsolidationService
│   │   ├── Infrastructure/
│   │   │   ├── Repositories/                 ← ProcessedEventsRepository (idempotência)
│   │   │   └── Migrations/Scripts/           ← 002 (daily_balance) + 003 (processed_events)
│   │   ├── Program.cs (Host.CreateApplicationBuilder, sem ASP.NET)
│   │   ├── appsettings.json / Dockerfile (runtime base, não aspnet)
│   │
│   ├── CashFlow.Balance.Core/                ← Shared kernel intra-BC (ADR-026)
│   │   ├── Domain/                           ← DailyBalance, DomainException
│   │   └── Infrastructure/
│   │       ├── Persistence/                  ← IDbConnectionFactory, NpgsqlConnectionFactory,
│   │       │                                    IUnitOfWork, DapperUnitOfWork, DateOnlyTypeHandler
│   │       └── Repositories/                 ← IBalanceRepository, BalanceRepository
│   │   └── ← Class library: só Dapper + Npgsql (sem ASP.NET)
│   │
│   ├── CashFlow.Admin.API/                   ← DLQ ops (ADR-028)
│   │   ├── Controllers/                      ← AdminController
│   │   ├── Application/Admin/                ← ErrorQueueRedeliveryService
│   │   ├── Program.cs / appsettings.json / Dockerfile
│   │   └── ← Stack mínima: RabbitMQ.Client + Shared (JWT) — sem Postgres
│   │
│   └── CashFlow.Shared/                      ← Shared Kernel cross-BC mínimo
│       ├── Events/TransactionRegistered.cs   ← Contrato de evento (Transactions → Balance)
│       └── Security/                         ← JwtTokenService, JwtSettings,
│                                                AuthorizationPolicies, CashFlowRoles,
│                                                SecurityServiceCollectionExtensions
│                                                (Identity emite; outros validam)
│
├── tests/
│   ├── CashFlow.UnitTests/
│   │   ├── Transactions/Domain/              ← TransactionTests, MoneyTests, etc.
│   │   ├── Balance/                          ← DailyBalanceTests, BalanceQueryServiceTests
│   │   └── Identity/Domain/                  ← AppUserTests, RefreshTokenTests
│   │
│   ├── CashFlow.Architecture.Tests/          ← 18 fitness functions (8 → 18 com ADR-026/027/028)
│   │   ├── LayerDependencyTests.cs           ← Dependency Rule (+ Identity)
│   │   ├── ImmutabilityTests.cs              ← Rich Domain (+ Identity entities)
│   │   ├── NamingConventionTests.cs          ← Repositories/Interfaces (+ Identity)
│   │   └── BoundedContextIsolationTests.cs   ← NOVO: 5 testes cross-BC isolation
│   │
│   ├── CashFlow.Bdd.Tests/                   ← 15 cenários Reqnroll pt-BR
│   │   └── ← TODO refator E2E pós-extração de Identity (testes boot só Transactions hoje)
│   │
│   └── CashFlow.LoadTests/                   ← NBomber (não é IsTestProject)
│
├── infra/
│   ├── postgres/init.sql                     ← 3 users + 3 schemas + GRANTs cruzados bloqueados
│   └── rabbitmq/Dockerfile                   ← + plugin rabbitmq_delayed_message_exchange
│
├── .github/workflows/                        ← ci.yml + mutation.yml
├── .config/dotnet-tools.json                 ← Stryker como local tool
├── docs/                                     ← architecture.md, analysis/, adrs/ (28), rnfs/ (9),
│                                                diagrams/, challenge/, references/
├── docker-compose.yml                        ← Postgres + RabbitMQ + 5 serviços CashFlow
└── README.md
```

**Totais:** **7 projetos** de produção + 4 de teste/carga = 11 projetos no `.sln`. **127 testes** automatizados (91 unit + 18 architecture + 15 BDD), todos verdes para 99/99 que rodam sem Docker.

### 3.2. Grafo de dependências entre projetos

```text
                        ┌─────────────────────────┐
                        │    CashFlow.Shared      │  (eventos + JWT primitives)
                        └────────────┬────────────┘
                                     │
            ┌─────────────┬──────────┼──────────┬──────────────┐
            │             │          │          │              │
            ▼             ▼          ▼          ▼              ▼
   ┌──────────────┐ ┌────────────┐ ┌─────────┐ ┌──────────┐ ┌─────────┐
   │ Identity.API │ │Transactions│ │ Balance │ │ Balance  │ │  Admin  │
   │              │ │   .API     │ │  .API   │ │ .Worker  │ │  .API   │
   └──────┬───────┘ └─────┬──────┘ └────┬────┘ └────┬─────┘ └────┬────┘
          │               │             │            │           │
          │ (auth puro)   │ (write +    │ (read      │ (consumer │ (DLQ ops
          │               │   outbox)   │ ←─────┐    │  + writer │  raw broker)
          │               │             │       │    │  schema   │
          │               │             │       │    │  balance) │
          │               │             │       ▼    ▼           │
          │               │             │  ┌──────────────────┐  │
          │               │             │  │CashFlow.Balance. │  │
          │               │             │  │  Core (class lib)│  │
          │               │             │  │ ← Domain + UoW + │  │
          │               │             │  │   BalanceRepo    │  │
          │               │             │  └──────────────────┘  │
          ▼               ▼             ▼                        ▼
                  ┌────────────────────────────────────┐
                  │ tests/ (UnitTests, Architecture,   │
                  │         BDD) — referenciam tudo    │
                  └────────────────────────────────────┘
```

**Regras (verificadas por fitness functions — [`BoundedContextIsolationTests`](../tests/CashFlow.Architecture.Tests/BoundedContextIsolationTests.cs)):**

- **Cross-BC isolation:** Identity, Transactions, Balance.* e Admin **não se referenciam diretamente** — toda comunicação é via `Shared.Events` no broker (ou via JWT validação em `Shared.Security`).
- **Intra-BC isolation:** Balance.API e Balance.Worker referenciam `Balance.Core` (shared kernel intra-BC), mas **não se referenciam entre si** — são deploy units distintas dentro do mesmo BC.
- **Balance.Core não depende de ninguém** — nem de `Shared` (não tem evento de integração). É shared kernel **puro**.
- **Shared** depende só de `Microsoft.IdentityModel.JsonWebTokens`.
- Testes referenciam tudo — para validar tipos e arquitetura.

### 3.3. Por que 7 projetos de produção (e não 3, nem 11)

A versão pré-refator deste projeto tinha **3 projetos**, defendidos como "Clean Architecture sem cerimônia". A versão pós-refator tem **7**, defendidos pelos **4 limites arquiteturais** das ADRs [026](adrs/adr-026-balance-worker-extraction.md), [027](adrs/adr-027-identity-service-extraction.md), [028](adrs/adr-028-admin-api-extraction.md). A evolução foi explícita, motivada por feedback de entrevista:

| # | Projeto | Por que projeto separado |
|---|---|---|
| 1 | `CashFlow.Identity.API` | BC próprio: linguagem ubíqua + ciclo regulatório próprios (ADR-027) |
| 2 | `CashFlow.Transactions.API` | BC write side: linguagem ubíqua de lançamentos |
| 3 | `CashFlow.Balance.API` | Deploy unit read-only do BC Balance: latency-bound (ADR-026) |
| 4 | `CashFlow.Balance.Worker` | Deploy unit consumer do BC Balance: throughput-bound (ADR-026) |
| 5 | `CashFlow.Balance.Core` | Shared kernel intra-BC: Domain + Persistence + BalanceRepository |
| 6 | `CashFlow.Admin.API` | Operações administrativas: escala ínfima + falha isolada (ADR-028) |
| 7 | `CashFlow.Shared` | Shared kernel cross-BC mínimo: eventos + JWT primitives |

**Cada agrupamento é defendido pelos 4 limites:**

- **Limite de escala** — perfis de carga distintos?
- **Limite de falha** — bug em A pode degradar B?
- **Limite de deploy/versionamento** — evoluem em ritmo independente?
- **Limite de domínio** — linguagem ubíqua + ciclo de negócio próprios?

Esse esqueleto é o **antídoto explícito** ao gap identificado em entrevista (justificar decisões pela implementação em vez de pelos critérios arquiteturais). Veja [ADR-026 § "Análise pelos 4 limites"](adrs/adr-026-balance-worker-extraction.md) como gabarito.

**Por que NÃO 11 projetos** (1 csproj por camada × N BCs): para Identity ou Transactions sozinhos, separar Domain/Application/Infrastructure em 3 projetos cada não traz benefício real — ninguém compartilha o Domain de Identity com outro serviço. Camadas internas validadas por fitness functions (`Domain` não conhece `Infrastructure`) cumprem o mesmo papel sem multiplicar artefatos. **Cerimônia se justifica por necessidade, não por princípio.**

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

`CashFlow.Architecture.Tests` valida no CI **18 invariantes** (era 8 antes de ADR-026/027/028):

**Clean Architecture intra-BC** ([`LayerDependencyTests`](../tests/CashFlow.Architecture.Tests/LayerDependencyTests.cs)):

| Teste | Garantia |
| --- | --- |
| `Transactions_Domain_MustNotDependOn_Infrastructure_Application_orAspNetCore` | Domain isolado |
| `Balance_Domain_MustNotDependOn_Infrastructure_Application_orAspNetCore` | Domain isolado |
| `Identity_Domain_MustNotDependOn_Infrastructure_Application_orAspNetCore` | Domain isolado (inclui blacklist de Konscious.Argon2) |
| `Transactions_Application_MustNotDependOn_Controllers` | Application não conhece HTTP |
| `Identity_Application_MustNotDependOn_Controllers` | Application não conhece HTTP |

**Rich Domain Model** ([`ImmutabilityTests`](../tests/CashFlow.Architecture.Tests/ImmutabilityTests.cs)):

| Teste | Garantia |
| --- | --- |
| `Transactions_Entities_MustNotHavePublicSetters` | Encapsulamento — Transaction |
| `Balance_Entities_MustNotHavePublicSetters` | Encapsulamento — DailyBalance |
| `Identity_Entities_MustNotHavePublicSetters` | Encapsulamento — AppUser, RefreshToken |

**Convenções de nomenclatura** ([`NamingConventionTests`](../tests/CashFlow.Architecture.Tests/NamingConventionTests.cs)):

| Teste | Garantia |
| --- | --- |
| `Transactions_Repositories_MustEndWith_Repository` | Sufixo Repository |
| `Balance_Repositories_MustEndWith_Repository` | Sufixo Repository (Core + Worker) |
| `Identity_Repositories_MustEndWith_Repository` | Sufixo Repository |
| `Transactions_RepositoryInterfaces_MustStartWith_I` | Prefixo I em interface |
| `Identity_RepositoryInterfaces_MustStartWith_I` | Prefixo I em interface |

**Isolamento entre Bounded Contexts** ([`BoundedContextIsolationTests`](../tests/CashFlow.Architecture.Tests/BoundedContextIsolationTests.cs)) — **diferencial pós-ADR-026/027/028**:

| Teste | Garantia |
| --- | --- |
| `Identity_MustNotDependOn_Transactions_Balance_or_Admin` | Identity é BC autossuficiente |
| `Transactions_MustNotDependOn_Identity_Balance_or_Admin` | Transactions é BC autossuficiente |
| `BalanceApi_MustNotDependOn_Identity_Transactions_or_Admin` | Balance.API não conhece outros BCs |
| `BalanceWorker_MustNotDependOn_Identity_Transactions_BalanceApi_or_Admin` | Worker é deploy unit independente da API |
| `BalanceCore_MustNotDependOn_AnyOtherAssembly` | Core é shared kernel intra-BC **puro** (nem Shared/eventos) |

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

### 5.2. Fluxo de consumo: `TransactionConsumer` (em Balance.Worker — ADR-026)

![Fluxo de consumo — Balance Worker](diagrams/fluxo-consumo.svg)

> Fonte editável: [`diagrams/fluxo-consumo.mmd`](diagrams/fluxo-consumo.mmd)
>
> **Pós-ADR-026:** este fluxo agora vive em `CashFlow.Balance.Worker` (deploy unit independente da Balance.API). Falha no consumer não derruba a read API; deploy de consumer não força redeploy da API. Diagrama editável reflete o nome anterior; refator pendente.

**ADRs envolvidas:** [ADR-002](adrs/adr-002-rabbitmq-masstransit.md) (broker), [ADR-005](adrs/adr-005-polly-retry.md) (Polly), [ADR-011](adrs/adr-011-idempotency.md) (idempotência), [ADR-026](adrs/adr-026-balance-worker-extraction.md) (extração do Worker).

### 5.x. Fluxo de autenticação: `POST /api/v1/auth/login` (em Identity.API — ADR-027)

Esquerda do mapa de fluxos: login emite **par** (access JWT + refresh opaco), validados em Transactions, Balance e Admin via `CashFlow.Shared.Security`. Detalhes em [ADR-027](adrs/adr-027-identity-service-extraction.md) e nas ADRs [016](adrs/adr-016-jwt-authentication.md), [021](adrs/adr-021-argon2id-password-hashing.md), [023](adrs/adr-023-account-lockout.md), [024](adrs/adr-024-refresh-tokens-rotation.md).

### 5.3. Fluxo de leitura: `GET /api/v1/balance/{date}`

![Fluxo de leitura — saldo consolidado](diagrams/fluxo-leitura.svg)

> Fonte editável: [`diagrams/fluxo-leitura.mmd`](diagrams/fluxo-leitura.mmd)

**ADRs envolvidas:** [ADR-006](adrs/adr-006-rate-limiting.md) (rate limit), [ADR-001](adrs/adr-001-cqrs.md) (CQRS: leitura O(1) é consequência de termos a projeção pronta).

---

## 6. Bounded Contexts e Shared Kernels

### 6.1. Os três contextos (pós-ADR-027)

| Contexto | Deploy units | Linguagem ubíqua | Aggregate Roots |
| --- | --- | --- | --- |
| **Identity** | `CashFlow.Identity.API` | "credential", "lockout", "refresh rotation", "session" | `AppUser`, `RefreshToken` |
| **Transactions** | `CashFlow.Transactions.API` | "lançamento", "registrar", "débito/crédito" | `Transaction` |
| **Balance** | `CashFlow.Balance.API` (read) + `CashFlow.Balance.Worker` (writer) | "saldo diário", "consolidado", "aplicar lançamento ao saldo" | `DailyBalance` |
| *(ops, não é BC de negócio)* | `CashFlow.Admin.API` | "DLQ", "redelivery", "poison message" | — |

Os modelos são **propositalmente diferentes** — cada BC encapsula o que precisa, sem refletir o modelo dos outros:

- `Transaction` tem `Money`, `TransactionType`, `MovementDate` — preocupações do write side de negócio.
- `DailyBalance` tem `TotalCredits`, `TotalDebits`, `Balance` (derivado) — preocupações de agregação para leitura.
- `AppUser` tem `PasswordHash`, `FailedLoginAttempts`, `LockedUntil` — preocupações de Identity, alheias ao negócio.

Não há classe `Transaction` compartilhada — a única coisa cruzada é o **evento de integração** (`TransactionRegistered` em `Shared`).

### 6.2. Shared Kernels (intra-BC + cross-BC)

CashFlow tem **dois shared kernels distintos**, cada um com escopo bem definido:

**Cross-BC: `CashFlow.Shared`** (mínimo absoluto)

```text
CashFlow.Shared/
├── Events/
│   └── TransactionRegistered.cs       ← Contrato de evento entre BCs
└── Security/                          ← JWT primitives
    ├── JwtTokenService.cs             ← Identity ISSUE; outros VALIDAM
    ├── JwtSettings.cs
    ├── AuthorizationPolicies.cs
    ├── CashFlowRoles.cs
    └── SecurityServiceCollectionExtensions.cs
```

Princípio: **deliberadamente mínimo** — só contratos cross-BC (eventos + JWT). Nada de entidades de negócio, nada de utilitários cross-cutting, nada de DTOs HTTP. Cada BC evolui seu modelo interno livremente.

**Intra-BC: `CashFlow.Balance.Core`** (compartilhado SÓ entre Balance.API + Balance.Worker)

```text
CashFlow.Balance.Core/
├── Domain/
│   ├── Entities/DailyBalance.cs        ← Aggregate root
│   └── Exceptions/DomainException.cs
└── Infrastructure/
    ├── Persistence/                    ← Dapper UoW + connection factory
    └── Repositories/
        ├── IBalanceRepository.cs
        └── BalanceRepository.cs        ← Worker escreve; API lê
```

Princípio: **shared kernel intra-BC ≠ shared kernel cross-BC**. Balance.Core é compartilhado entre deploy units do **mesmo** bounded context (Balance.API + Balance.Worker) — preserva linguagem ubíqua sem duplicação. Por ser intra-BC, não tem evento de integração (não precisa conhecer `Shared`). **Fitness function** `BalanceCore_MustNotDependOn_AnyOtherAssembly` defende essa pureza.

> **Frase pra entrevista:** *"Distingo 2 tipos de shared kernel: o cross-BC, que é mínimo absoluto (só contratos), e o intra-BC, que pode ser mais rico porque opera dentro do mesmo bounded context. Misturar os dois conceitos vira coupling implícito."*

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
