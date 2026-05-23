# C4 Level 2 — Diagrama de Containers

**Pergunta que responde:** Quais aplicações/serviços/databases compõem o sistema CashFlow, e como se comunicam?

```mermaid
graph TB
    Comerciante["👤 <b>Comerciante (Carlos)</b>"]

    subgraph Sistema["🏦 Sistema CashFlow"]
        TransactionsAPI["📦 <b>Transactions API</b><br/>[ASP.NET Core 10 / C#]<br/><i>Write side — registra<br/>débitos e créditos.<br/>Publica eventos.</i>"]

        BalanceAPI["📊 <b>Balance API</b><br/>[ASP.NET Core 10 / C#]<br/><i>Read side — expõe saldo<br/>diário. Hospeda<br/>BackgroundService consumer.</i>"]

        RabbitMQ["📮 <b>RabbitMQ</b><br/>[Broker AMQP]<br/><i>Persiste eventos<br/>TransactionRegistered.</i>"]

        DBTransactions["🗄️ <b>PostgreSQL — schema 'transactions'</b><br/>[PostgreSQL 16]<br/><i>Persiste transactions.<br/>user: app_transactions<br/>(GRANT restrito)</i>"]

        DBBalance["🗄️ <b>PostgreSQL — schema 'balance'</b><br/>[PostgreSQL 16]<br/><i>Persiste daily_balance<br/>+ processed_events.<br/>user: app_balance<br/>(GRANT restrito)</i>"]
    end

    Comerciante -->|"POST /api/v1/transactions<br/>GET /api/v1/transactions/{id}<br/>(HTTPS/JSON)"| TransactionsAPI
    Comerciante -->|"GET /api/v1/balance/{date}<br/>GET /api/v1/balance?from=&to=<br/>(HTTPS/JSON)"| BalanceAPI

    TransactionsAPI -->|"INSERT transaction<br/>(Dapper, transação)"| DBTransactions
    TransactionsAPI -->|"Publica<br/>TransactionRegistered<br/>(AMQP)"| RabbitMQ

    RabbitMQ -->|"Consume<br/>TransactionRegistered<br/>(AMQP, at-least-once,<br/>queue: balance.transaction-registered)"| BalanceAPI
    BalanceAPI -->|"UPSERT daily_balance<br/>INSERT processed_events<br/>(Dapper, transação atômica)"| DBBalance
    BalanceAPI -->|"SELECT daily_balance<br/>(Dapper, O(1) por date)"| DBBalance

    classDef person fill:#08427b,stroke:#052e56,color:#fff
    classDef writeContainer fill:#1168bd,stroke:#0b4884,color:#fff
    classDef readContainer fill:#2e7d32,stroke:#1b5e20,color:#fff
    classDef infraContainer fill:#f57c00,stroke:#bf5f00,color:#fff
    classDef dataContainer fill:#6a1b9a,stroke:#4a148c,color:#fff

    class Comerciante person
    class TransactionsAPI writeContainer
    class BalanceAPI readContainer
    class RabbitMQ infraContainer
    class DBTransactions,DBBalance dataContainer
```

## Decisões refletidas neste diagrama

| Elemento | Decisão | ADR |
|---|---|---|
| Duas APIs sem chamada direta | **CQRS** — write side (Transactions) e read side (Balance) totalmente independentes | [ADR-001](../adrs/adr-001-cqrs.md) |
| RabbitMQ entre elas | **EDA via MassTransit** — desacoplamento temporal; troca para Service Bus em produção sem mudar código | [ADR-002](../adrs/adr-002-rabbitmq-masstransit.md) |
| Schema `balance` no mesmo cluster | **1 DB + 2 schemas com GRANTs** — isolamento lógico real via permissões; mesma instância em Docker para reduzir cerimônia | [ADR-003](../adrs/adr-003-postgres-schemas.md) |
| Consumer dentro da Balance API | **HostedService no MVP** — menos containers; processo dedicado é evolução documentada | [ADR-004](../adrs/adr-004-consumer-hostedservice.md) |
| Tabela `processed_events` | **Idempotência** — RabbitMQ garante at-least-once; precisamos tratar reentregas | [ADR-011](../adrs/adr-011-idempotency.md) |

## Garantias de disponibilidade (RNF-01)

- Se **Balance API** cair: lançamentos continuam sendo registrados; eventos se acumulam na fila do RabbitMQ.
- Se **RabbitMQ** cair: lançamentos continuam sendo persistidos no banco; quando o broker volta, o `IPublishEndpoint` retoma a publicação (MassTransit gerencia a fila local). Janela de inconsistência tratada na [ADR-007](../adrs/adr-007-publish-after-commit.md); eliminada via Outbox Pattern (evolução).
- Se **Transactions API** cair: consultas ao saldo continuam funcionando normalmente.

## Próximos níveis

- **Nível 3 — Transactions:** [c4-componentes-transactions.md](c4-componentes-transactions.md)
- **Nível 3 — Balance:** [c4-componentes-balance.md](c4-componentes-balance.md)
