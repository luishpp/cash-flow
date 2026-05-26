# ADR-011: Idempotência no consumer via tabela `processed_events`

**Status:** Aceita

## Contexto

RabbitMQ + MassTransit entregam mensagens com garantia **at-least-once**. Se o consumer processar uma mensagem `TransactionRegistered` e cair antes de fazer `Ack`, a mesma mensagem volta para a fila. Sem idempotência, o `DailyBalance` seria **incrementado duas vezes** — bug funcional, não apenas operacional. Esta ADR estava listada na versão anterior como "evolução futura"; após revisão, é **pré-requisito de correção** do MVP.

## Decisão

Implementar idempotência no `TransactionConsumer` via tabela `balance.processed_events`:

```sql
CREATE TABLE balance.processed_events (
    event_id        UUID         NOT NULL,
    consumer_name   VARCHAR(100) NOT NULL,
    processed_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    PRIMARY KEY (event_id, consumer_name)
);
```

O fluxo no `ConsolidationService.ApplyAsync` fica:

```csharp
await uow.BeginAsync(ct);
try
{
    if (await eventsRepo.ExistsAsync(evt.EventId, consumerName, ct))
    {
        await uow.CommitAsync(ct);
        return; // idempotente — ignora reentrega
    }

    var balance = (await balanceRepo.GetByDateAsync(evt.MovementDate, ct))
                  ?? DailyBalance.New(evt.MovementDate);
    if (evt.Type == "credit") balance.ApplyCredit(evt.Amount);
    else                      balance.ApplyDebit(evt.Amount);

    await balanceRepo.UpsertAsync(balance, ct);
    await eventsRepo.RegisterAsync(evt.EventId, consumerName, ct);
    await uow.CommitAsync(ct);
}
catch { await uow.RollbackAsync(ct); throw; }
```

## Por que essa abordagem (e não outras)

| Abordagem | Vantagem | Desvantagem |
|---|---|---|
| **Tabela `processed_events`** *(escolhida)* | Simples, rastreável, funciona com qualquer broker | Cresce indefinidamente — exige limpeza periódica (`DELETE WHERE processed_at < NOW() - INTERVAL '90 days'`) |
| Hash do payload como chave única em `daily_balance` | Sem tabela extra | Não distingue reentrega genuína de evento duplicado legítimo; engessa a projeção |
| Idempotência via MassTransit `UseInMemoryOutbox` | Padrão framework | In-memory — perde garantia em crash do processo |
| Outbox + Inbox Pattern (MassTransit `UseEntityFrameworkOutbox`) | Garantia exactly-once de ponta a ponta | Requer EF Core (conflita com [ADR-010](adr-010-dapper.md)) — evolução documentada |

## Por que MVP, não evolução

- O cenário "consumer cai depois de aplicar saldo, antes de Ack" é **provável em qualquer redeploy**.
- Sem idempotência, um redeploy do consumer = saldos incorretos.
- 20 linhas de código + 1 tabela = solução completa.

## Trade-offs

| Ganha | Perde |
|---|---|
| Reentrega de mensagens (causa: crash, redeploy, retry do Polly) não corrompe saldo | Tabela cresce — precisa rotina de housekeeping (cron job ou `pg_partman`) |
| Funciona com qualquer broker (RabbitMQ hoje, Service Bus amanhã) | Cada consumer precisa carregar o padrão (mitigado por classe base no futuro) |
| Combinada com transação Dapper: aplicar saldo + marcar processado é atômico | — |
| Pré-requisito para Polly Retry funcionar com segurança | — |

## Identificação do `EventId`

- Gerado no momento da publicação na Transactions API (`Guid.NewGuid()`) em `TransactionService.RegisterAsync`.
- Enviado no payload do evento `CashFlow.Shared.Events.TransactionRegistered`.
- Persistido também na tabela `processed_events` para correlação.

## Evolução documentada

- **Outbox publisher-side** — **implementado** em [ADR-025](adr-025-outbox-and-dlq.md) via tabela `transactions.outbox_events` + `OutboxDispatcher` (BackgroundService). Não foi usado o `UseEntityFrameworkOutbox` do MassTransit para preservar Dapper ([ADR-010](adr-010-dapper.md)). Combinado com esta ADR (inbox consumer-side via `processed_events`), o sistema entrega garantia próxima a exactly-once ponta-a-ponta.
- **Housekeeping automatizado** via `pg_cron` ou worker dedicado para purgar `processed_events` > 90 dias (e analogamente `outbox_events` já publicados).

## ADRs relacionadas

- [ADR-002](adr-002-rabbitmq-masstransit.md) — garantia at-least-once do broker
- [ADR-004](adr-004-consumer-hostedservice.md) — consumer onde a idempotência é aplicada
- [ADR-005](adr-005-polly-retry.md) — retry sem idempotência seria perigoso
- [ADR-007](adr-007-publish-after-commit.md) — janela de inconsistência (superada por [ADR-025](adr-025-outbox-and-dlq.md))
- [ADR-025](adr-025-outbox-and-dlq.md) — outbox publisher-side que casa com esta inbox consumer-side
