# C4 Level 2 — Diagrama de Containers

**Pergunta que responde:** Quais aplicações/serviços/databases compõem o sistema CashFlow, e como se comunicam?

![Diagrama de Containers](c4-containers.svg)

> 📊 Fonte editável em [`c4-containers.mmd`](c4-containers.mmd). Após editar, re-gere o SVG: `mmdc -i c4-containers.mmd -o c4-containers.svg`.

## Decisões refletidas neste diagrama

| Elemento | Decisão | ADR |
| --- | --- | --- |
| Duas APIs sem chamada direta | **CQRS** — write side (Transactions) e read side (Balance) totalmente independentes | [ADR-001](../adrs/adr-001-cqrs.md) |
| RabbitMQ entre elas | **EDA via MassTransit** — desacoplamento temporal; troca para Service Bus em produção sem mudar código | [ADR-002](../adrs/adr-002-rabbitmq-masstransit.md) |
| Schema `balance` no mesmo cluster | **1 DB + 2 schemas com GRANTs** — isolamento lógico real via permissões; mesma instância em Docker para reduzir cerimônia | [ADR-003](../adrs/adr-003-postgres-schemas.md) |
| Consumer dentro da Balance API | **HostedService no MVP** — menos containers; processo dedicado é evolução documentada | [ADR-004](../adrs/adr-004-consumer-hostedservice.md) |
| Tabela `processed_events` | **Idempotência** — RabbitMQ garante at-least-once; precisamos tratar reentregas | [ADR-011](../adrs/adr-011-idempotency.md) |

## Garantias de disponibilidade (RNF-01)

- Se **Balance API** cair: lançamentos continuam sendo registrados; eventos se acumulam na fila do RabbitMQ.
- Se **RabbitMQ** cair: lançamentos continuam sendo persistidos no banco e os eventos ficam pendentes em `transactions.outbox_events`; quando o broker volta, o `OutboxDispatcher` (BackgroundService) drena e publica em ordem. A janela do [ADR-007](../adrs/adr-007-publish-after-commit.md) foi fechada pelo Outbox Pattern em Dapper ([ADR-025](../adrs/adr-025-outbox-and-dlq.md)).
- Se **Transactions API** cair: consultas ao saldo continuam funcionando normalmente.

## Próximos níveis

- **Nível 3 — Transactions:** [c4-componentes-transactions.md](c4-componentes-transactions.md)
- **Nível 3 — Balance:** [c4-componentes-balance.md](c4-componentes-balance.md)
