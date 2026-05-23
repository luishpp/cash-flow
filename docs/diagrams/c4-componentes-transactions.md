# C4 Level 3 — Componentes da Transactions API

**Pergunta que responde:** Quais componentes internos formam a `CashFlow.Transactions.API`, e como o fluxo de registro de uma transação atravessa as camadas?

```mermaid
graph TB
    Cliente["👤 <b>Cliente HTTP</b><br/>(Swagger UI / curl / frontend)"]

    subgraph TransactionsAPI["📦 Transactions API [ASP.NET Core 10]"]

        subgraph LayerAPI["Camada API"]
            Controller["<b>TransactionsController</b><br/><i>POST /api/v1/transactions<br/>GET /api/v1/transactions/{id}</i>"]
            Validator["<b>RegisterTransactionValidator</b><br/>[FluentValidation]<br/><i>Valida DTO antes do handler</i>"]
            HealthCheck["<b>HealthChecks</b><br/>/health/live · /health/ready"]
        end

        subgraph LayerApp["Camada Application"]
            AppService["<b>TransactionService</b><br/><i>Orquestra: abre UoW,<br/>chama Domain, persiste,<br/>publica evento após commit</i>"]
            UoW["<b>IUnitOfWork</b><br/><i>Transação Dapper<br/>(IDbTransaction)</i>"]
        end

        subgraph LayerDomain["Camada Domain (Rich Domain Model)"]
            Transaction["<b>Transaction</b> (Entity)<br/><i>Transaction.Register(...)<br/>— factory method<br/>com invariantes</i>"]
            VOs["<b>Value Objects</b><br/>Money · TransactionType<br/>MovementDate"]
            DomainException["<b>DomainException</b>"]
        end

        subgraph LayerInfra["Camada Infrastructure"]
            Repo["<b>TransactionRepository</b><br/>[Dapper]<br/><i>INSERT transactions<br/>(SQL parameterizado)</i>"]
            EventPublisher["<b>IEventPublisher</b><br/>[MassTransit IPublishEndpoint]"]
            ConnFactory["<b>NpgsqlConnectionFactory</b><br/><i>Cria IDbConnection<br/>com user app_transactions</i>"]
            DbUp["<b>DbUp Migrator</b><br/><i>Roda no startup —<br/>aplica scripts SQL<br/>incrementais</i>"]
        end
    end

    Postgres["🗄️ <b>PostgreSQL</b><br/>schema: transactions"]
    Rabbit["📮 <b>RabbitMQ</b>"]

    Cliente -->|"HTTPS/JSON"| Controller
    Controller -->|"valida"| Validator
    Controller -->|"chama"| AppService
    AppService -->|"abre tx"| UoW
    AppService -->|"Transaction.Register(...)"| Transaction
    Transaction -->|"usa"| VOs
    Transaction -.->|"lança em violação"| DomainException
    AppService -->|"persiste"| Repo
    Repo -->|"INSERT (parameterizado)"| ConnFactory
    ConnFactory --> Postgres
    AppService -->|"após commit"| EventPublisher
    EventPublisher -->|"AMQP / TransactionRegistered"| Rabbit
    DbUp -.->|"startup"| Postgres
    HealthCheck -.->|"verifica"| Postgres

    classDef api fill:#42a5f5,stroke:#1565c0,color:#fff
    classDef app fill:#7e57c2,stroke:#4527a0,color:#fff
    classDef domain fill:#ef6c00,stroke:#b53d00,color:#fff
    classDef infra fill:#26a69a,stroke:#00695c,color:#fff
    classDef external fill:#9e9e9e,stroke:#616161,color:#fff

    class Controller,Validator,HealthCheck api
    class AppService,UoW app
    class Transaction,VOs,DomainException domain
    class Repo,EventPublisher,ConnFactory,DbUp infra
    class Cliente,Postgres,Rabbit external
```

## Fluxo de "Registrar Transaction" (golden path)

1. `Cliente` envia `POST /api/v1/transactions` com `{ type, amount, description, movementDate }`.
2. `TransactionsController` recebe DTO; `FluentValidation` valida formato/limites — se falha, retorna **HTTP 400** com detalhes.
3. Controller delega para `TransactionService.RegisterAsync(request)`.
4. `TransactionService` abre `IUnitOfWork` (begin transaction).
5. `Transaction.Register(...)` é chamada — factory method valida invariantes de domínio. Se violação, lança `DomainException` (middleware traduz para **HTTP 422**).
6. `TransactionRepository.InsertAsync(transaction, ct)` persiste via Dapper com SQL parameterizado.
7. `IUnitOfWork.CommitAsync()` confirma a transação no PostgreSQL.
8. **Após commit**, `IEventPublisher.PublishAsync(new TransactionRegistered(...))` envia evento para RabbitMQ ([ADR-007](../adrs/adr-007-publish-after-commit.md) — evita mensagens fantasma).
9. Controller retorna **HTTP 201 Created** com `Location` header.

## Regras arquiteturais validadas por NetArchTest ([ADR-012](../adrs/adr-012-architecture-tests.md))

- `Domain` (`Transaction`, VOs) **não referencia** `Infrastructure`, `Microsoft.AspNetCore.*`, `Dapper`, `Npgsql`, `MassTransit`.
- Entidades em `Domain/Entities/` **não têm setters públicos**.
- Repositórios em `Infrastructure/Repositories/` **terminam com sufixo `Repository`**.
- Interfaces de repositório **começam com `I`**.

## Estrutura de pastas correspondente

```text
CashFlow.Transactions.API/
├── Controllers/
│   └── TransactionsController.cs
├── Domain/
│   ├── Entities/{AuditableEntity, Transaction}.cs
│   ├── ValueObjects/{Money, TransactionType, MovementDate}.cs
│   └── Exceptions/DomainException.cs
├── Application/
│   ├── Services/{ITransactionService, TransactionService}.cs
│   ├── DTOs/TransactionDtos.cs
│   └── Validators/RegisterTransactionValidator.cs
├── Infrastructure/
│   ├── Persistence/{NpgsqlConnectionFactory, DapperUnitOfWork}.cs
│   ├── Repositories/{ITransactionRepository, TransactionRepository}.cs
│   ├── Messaging/{IEventPublisher, MassTransitEventPublisher}.cs
│   └── Migrations/
│       ├── MigrationRunner.cs
│       └── Scripts/{001_create_schema, 002_create_transactions_table}.sql
└── Program.cs
```
