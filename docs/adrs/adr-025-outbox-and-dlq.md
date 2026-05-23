# ADR-025: Outbox transacional + DLQ visível com redelivery delayed

**Status:** Aceita

## Contexto

A [ADR-007](adr-007-publish-after-commit.md) aceitou conscientemente uma janela de inconsistência: `INSERT da transação` ➜ `PublishAsync(TransactionRegistered)` rodam sequencialmente, e se o broker estiver fora **depois** do commit, a mensagem se perde. A própria ADR-007 listou o **Outbox Pattern** como evolução futura. A [ADR-005](adr-005-polly-retry.md) cobre transientes rápidos via Polly (3x, exp backoff até ~3s) — bom para um GC pause do banco, ruim para outage de dependência que dura minutos. E a [ADR-011](adr-011-idempotency.md) garante que reentregas não corrompem saldo, mas só atua se a mensagem chegar — não resolve mensagens que ficam perdidas.

Para um cenário de **fechamento contábil**, o saldo defasar silenciosamente porque um evento se perdeu é inaceitável. Falta:

1. Garantir que **todo INSERT persistido gera evento publicado** (mesmo com broker fora no instante do commit).
2. Aguentar outage de dependência da ordem de **minutos a uma hora** sem operação manual.
3. Quando tudo der errado, ter **DLQ visível** com reprocessamento sob comando.

## Decisão

Implementar três camadas complementares — uma para cada janela de falha:

### 1. Outbox no publisher (Transactions API)

Tabela `transactions.outbox_events`:

```sql
CREATE TABLE transactions.outbox_events (
    seq          BIGSERIAL    NOT NULL,
    id           UUID         PRIMARY KEY,
    event_type   VARCHAR(200) NOT NULL,
    payload      JSONB        NOT NULL,
    created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    published_at TIMESTAMPTZ  NULL,
    attempts     INTEGER      NOT NULL DEFAULT 0,
    last_error   TEXT         NULL
);

CREATE INDEX idx_outbox_events_pending
    ON transactions.outbox_events (seq)
    WHERE published_at IS NULL;
```

`TransactionService.RegisterManyAsync` insere a transação **e** o registro do outbox **na mesma UoW** — ambos commitam ou ambos rollback. Um `OutboxDispatcher` (`BackgroundService`) drena `WHERE published_at IS NULL ORDER BY seq ASC` a cada 1s e publica via `IEventPublisher` (MassTransit). Em falha de publish: `attempts++` + `last_error`, **quebra o batch** (não pula o evento) — preserva FIFO.

**Por que `seq BIGSERIAL` e não `created_at`:** Postgres atribui o mesmo `NOW()` a todos os INSERTs de uma única statement/tx, então `ORDER BY created_at` não preserva ordem dentro de um batch — `seq` é monotônico e único por linha.

### 2. Delayed Redelivery no consumer (Balance API)

Dois níveis de retry combinados:

| Nível | Mecanismo | Intervalos | Cobre |
|---|---|---|---|
| 1 (in-process) | Polly — [ADR-005](adr-005-polly-retry.md) | ~1s + 2s + 4s (exp backoff + jitter) | GC pause, deadlock momentâneo |
| 2 (broker) | `UseDelayedRedelivery` (MassTransit) | 1min ➜ 5min ➜ 15min | Outage de DB/dependência, deploy do consumer, manutenção |

`UseDelayedRedelivery` republica a mensagem com header de delay — exige o plugin `rabbitmq_delayed_message_exchange`. Imagem custom em [`infra/rabbitmq/Dockerfile`](../../infra/rabbitmq/Dockerfile) habilita o plugin via `rabbitmq-plugins enable --offline`.

### 3. DLQ visível + reprocessamento via endpoint

Após esgotar todos os retries, MassTransit move a mensagem para `balance.transaction-registered_error` (queue criada automaticamente). Para evitar que vire "buraco preto", `AdminController` na Balance API expõe:

| Rota | Ação |
|---|---|
| `GET /api/v1/admin/errors/count` | `QueueDeclarePassive` para ler `MessageCount` sem consumir |
| `POST /api/v1/admin/errors/redeliver?max=N` | `BasicGet` da `_error` + `BasicPublish` na queue principal + `BasicAck` |

Implementação usa `RabbitMQ.Client` 7.x diretamente (não MassTransit) — operação cirúrgica de mover bytes preservando `BasicProperties`. A idempotência da [ADR-011](adr-011-idempotency.md) protege contra duplicação caso a mensagem original já tenha sido parcialmente processada antes de ir para o `_error`.

## Cenários de falha cobertos

| Cenário | Comportamento antes (ADR-007) | Comportamento agora |
|---|---|---|
| Broker fora **no momento do commit** | Saldo defasa silenciosamente | Evento fica em outbox; dispatcher publica quando broker volta |
| Banco da Balance fora 30s | Polly esgota; vai para `_error` imediatamente | Polly esgota; reagendado para +1min — banco volta antes |
| Banco da Balance fora 10min | Idem (mensagem em `_error`) | Reagendado +1min, +5min, +15min — recupera transparente |
| Bug funcional no consumer (payload inválido) | Vai para `_error` sem visibilidade | Vai para `_error` → `GET /admin/errors/count` denuncia → fix + `POST /redeliver` |
| Crash do `OutboxDispatcher` | — | Próximo restart resume do `WHERE published_at IS NULL` (estado durável) |
| Crash do `OutboxDispatcher` **entre** publish e mark-published | — | Idempotência da [ADR-011](adr-011-idempotency.md) absorve a duplicata |

## Trade-offs

| Ganha | Perde |
|---|---|
| At-least-once **ponta-a-ponta** — fecha a janela do ADR-007 sem precisar de EF Core | Latência adicional de até 1s no caminho POST → evento no broker (tick do dispatcher) |
| Outage de minutos é absorvido sem operação manual | Memória da `outbox_events` cresce indefinidamente se houver outage prolongado — exige housekeeping (`DELETE WHERE published_at < NOW() - INTERVAL '30 days'`) |
| DLQ deixa de ser invisível — operador vê e age | Plugin `rabbitmq_delayed_message_exchange` é community-maintained — versão pinada no Dockerfile (`PLUGIN_VERSION=3.13.0`) |
| `OutboxDispatcher` quebra batch em falha → preserva FIFO | Throughput do dispatcher cai se um evento "envenenado" travar a fila — alívio: `attempts++` permite alarmar e tratar via SQL direto |
| Endpoint admin é compatível com qualquer cenário (corrupção, indisponibilidade, bug) | Endpoint hoje exige role `Merchant` — em produção, separar role `Admin` própria |

## Alternativas descartadas

| Alternativa | Por que não |
|---|---|
| MassTransit `UseEntityFrameworkOutbox` | Requer EF Core — conflita com [ADR-010](adr-010-dapper.md). Adotar EF só pelo outbox = grande inversão arquitetural |
| `UseInMemoryOutbox` da MassTransit | In-memory — perde garantia em crash. Mesmo problema que o `UseInMemoryMessageScheduler` |
| `UseScheduledRedelivery` + `AddInMemoryMessageScheduler` | Scheduler in-process, lost on restart — promessa de redelivery silenciosamente quebra |
| `UseMessageRetry(r => r.Interval(N, ...))` apenas | Retries imediatos — não cobre outage de minutos sem ocupar thread do consumer |
| Não fazer DLQ, apenas Polly | MVP suficiente para "transientes rápidos" mas perde mensagens em outage real — exatamente o que ADR-007 já apontava |
| Reprocessamento via `rabbitmqctl shovel` / RabbitMQ Management UI | Funciona mas obriga o operador a entrar no broker — endpoint dedicado é discoverable, autoabilável e auditável |

## Logs em outage prolongado

O `OutboxDispatcher` distingue **exceções transientes** (`NpgsqlException`, `SocketException`, `TimeoutException`) de bugs reais: na primeira falha, emite **uma** `WRN` resumida + entra em "modo outage" (suprime erros idênticos); na recuperação, emite uma `INF` informando. Erros não-transientes (parse de payload, etc.) continuam como `ERR` com stack — esses são bugs, queremos vê-los.

## Evoluções futuras

- **Housekeeping** automatizado (`pg_cron` ou worker dedicado) para purgar `outbox_events.published_at < NOW() - INTERVAL '30 days'` e `processed_events` análogo.
- **Notify/Listen** no Postgres para o dispatcher acordar imediatamente em vez de polar a cada 1s — latência POST → broker cai pra ms.
- **Métricas** (`outbox_pending_count`, `outbox_oldest_pending_age_seconds`, `dlq_message_count`) expostas via `/metrics` para alarmar no monitoramento.
- **Role `Admin` separada** para os endpoints de DLQ (hoje compartilha `RequireMerchant`).
- **Cenário BDD cross-API** que simula outage do broker, verifica acúmulo no outbox, restaura e verifica drenagem — validação automatizada do contrato deste ADR.

## ADRs relacionadas

- [ADR-002](adr-002-rabbitmq-masstransit.md) — broker e cliente que suportam `UseDelayedRedelivery`
- [ADR-005](adr-005-polly-retry.md) — primeiro nível de retry (in-process) que esta ADR estende
- [ADR-007](adr-007-publish-after-commit.md) — janela de inconsistência que o outbox fecha
- [ADR-010](adr-010-dapper.md) — escolha de Dapper exigiu outbox custom (não `UseEntityFrameworkOutbox`)
- [ADR-011](adr-011-idempotency.md) — pré-requisito que torna redelivery segura
