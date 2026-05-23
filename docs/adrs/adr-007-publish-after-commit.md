# ADR-007: Publicação de evento após commit

**Status:** Superada pelo [ADR-025](adr-025-outbox-and-dlq.md) (mantida para histórico — a janela aceita aqui foi fechada via outbox transacional)

## Contexto

Ao registrar uma transação na `CashFlow.Transactions.API`, o serviço persiste no banco e publica um evento `TransactionRegistered` no RabbitMQ. A ordem dessas duas operações importa — se publicar antes de salvar, pode gerar mensagens fantasma (evento sem dados). Se salvar antes de publicar e a publicação falhar, a transação existe mas o saldo nunca é atualizado.

## Decisão

Publicar o evento `TransactionRegistered` **após o `IUnitOfWork.CommitAsync()`** do Dapper retornar com sucesso. Implementado em `TransactionService.RegisterAsync(...)`.

## Cenários de falha e comportamento

| Cenário | O que acontece | Impacto |
|---|---|---|
| Commit OK, Publish OK | Fluxo feliz | Nenhum |
| Commit FALHA | Nenhum evento publicado | Correto — não há transação para consolidar |
| Commit OK, Publish FALHA | Transação salva, evento perdido | Saldo consolidado fica defasado |

## Trade-offs

| Ganha | Perde |
|---|---|
| Nunca gera mensagem fantasma (evento sem dados) | Em caso raro de falha na publicação, mensagem se perde |
| Implementação simples e compreensível | Não garante exactly-once entre banco e broker |
| Suficiente para o escopo do desafio | — |

## O cenário de falha na publicação é raro?

Sim. O RabbitMQ está na mesma rede Docker que a API. A latência de publicação é sub-milissegundo. Falha nesse ponto significa que o RabbitMQ caiu — nesse caso, o retry interno do MassTransit tenta novamente. Se o RabbitMQ estiver genuinamente fora, a mensagem é perdida e o saldo consolida sem essa transação até que uma reconciliação manual ou reprocessamento corrija.

## Evolução — Outbox Pattern (implementado em [ADR-025](adr-025-outbox-and-dlq.md))

Esta janela de inconsistência foi **fechada posteriormente** pelo [ADR-025](adr-025-outbox-and-dlq.md), que implementa um outbox custom em Dapper (tabela `transactions.outbox_events` + `OutboxDispatcher` BackgroundService). Não foi adotado o `UseEntityFrameworkOutbox` do MassTransit porque exigiria abandonar Dapper (conflitaria com [ADR-010](adr-010-dapper.md)).

## ADRs relacionadas

- [ADR-002](adr-002-rabbitmq-masstransit.md) — broker no qual publicamos
- [ADR-010](adr-010-dapper.md) — escolha de Dapper limita Outbox nativo do MassTransit
- [ADR-011](adr-011-idempotency.md) — consumer idempotente lida com qualquer redelivery
