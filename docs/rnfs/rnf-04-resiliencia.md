# RNF-04 — Resiliência

**Origem:** Derivado *(seção "Objetivo do Desafio" do PDF)*.

## Declaração

> "Resiliência: Projete para a recuperação de falhas. Isso inclui redundância, failover, monitoramento proativo e estratégias de recuperação."

## Decisões arquiteturais que atendem

| ADR | Como contribui |
|---|---|
| [ADR-002 — RabbitMQ + MassTransit](../adrs/adr-002-rabbitmq-masstransit.md) | Mensagens persistentes sobrevivem a crash do consumer e do próprio broker (com volume persistido). |
| [ADR-005 — Polly Retry](../adrs/adr-005-polly-retry.md) | Falhas transitórias (jitter de rede, contenção de banco) reabsorvidas com exponential backoff + jitter. |
| [ADR-007 — Publish após commit](../adrs/adr-007-publish-after-commit.md) | Evita mensagens fantasma; commit do banco é a fronteira de verdade. |
| [ADR-011 — Idempotência](../adrs/adr-011-idempotency.md) | Reentregas at-least-once não corrompem o saldo. |

## Cobertura no MVP

**Total para o escopo.** Garantias entregues:

- ✅ Crash do consumer no meio do processamento: transação Dapper aborta + RabbitMQ não recebe `Ack` → mensagem volta para a fila → idempotência garante reprocessamento seguro.
- ✅ Crash do RabbitMQ: lançamentos continuam sendo persistidos no banco; quando broker volta, `IPublishEndpoint` retoma a publicação (MassTransit gerencia fila local em memória — perda apenas no cenário extremo de crash do broker + crash da API simultâneo, documentado em ADR-007).
- ✅ Falha transitória no banco: Polly Retry 3x com backoff 1s/2s/4s + jitter.
- ✅ Healthchecks `/health/live` e `/health/ready` para liveness/readiness probes em K8s/Compose.

Não cobertos no MVP (evolução): Circuit Breaker, Dead Letter Queue explícita, Outbox Pattern.

## Trade-off aceito

Janela teórica de inconsistência entre commit do banco e publish do evento ([ADR-007](../adrs/adr-007-publish-after-commit.md)). É raro e mitigado pelo retry interno do MassTransit; eliminação completa exigiria Outbox Pattern (evolução).

## Verificação

1. **Crash do consumer:** `docker kill cashflow-balance` no meio de uma rajada → reiniciar → verificar que saldos batem (idempotência funcionou).
2. **Crash do broker:** `docker stop cashflow-rabbitmq` → fazer POSTs no Transactions (deve continuar OK) → restart broker → eventos drenam.
3. **Healthchecks:** `curl http://localhost:5001/health/ready` deve retornar 200 com Postgres OK e 503 se Postgres parar.

## Evolução

- **Circuit Breaker (Polly)** quando telemetria justificar — ver [ADR-005](../adrs/adr-005-polly-retry.md).
- **Dead Letter Queue** explícita no MassTransit + rotina de inspeção/reprocessamento.
- **Outbox Pattern** via MassTransit `UseEntityFrameworkOutbox` — elimina a janela de [ADR-007](../adrs/adr-007-publish-after-commit.md).
- **Chaos engineering** (Azure Chaos Studio) — injetar falhas controladas em produção.
- **Multi-região com failover** para indisponibilidades de zona inteira.
