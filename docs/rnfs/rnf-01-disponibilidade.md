# RNF-01 — Disponibilidade

**Origem:** Explícito *(seção "Requisitos não funcionais" do PDF do desafio)*.

## Declaração

> "O serviço de controle de lançamento não deve ficar indisponível se o sistema de consolidado diário cair."

Em termos do código atual: **`CashFlow.Transactions.API` não pode parar de aceitar requisições se `CashFlow.Balance.API` (ou seu consumer) estiver fora.**

## Decisões arquiteturais que atendem

| ADR | Como contribui |
|---|---|
| [ADR-001 — CQRS](../adrs/adr-001-cqrs.md) | Separa write side (Transactions) e read side (Balance) em serviços independentes. |
| [ADR-002 — RabbitMQ + MassTransit](../adrs/adr-002-rabbitmq-masstransit.md) | Comunicação assíncrona — Transactions não conhece a saúde do Balance. |
| [ADR-003 — 1 DB + 2 schemas](../adrs/adr-003-postgres-schemas.md) | Isolamento lógico via GRANTs por usuário; em produção, evolução para instâncias dedicadas. |
| [ADR-004 — Consumer como HostedService](../adrs/adr-004-consumer-hostedservice.md) | Crash do consumer não derruba a API de Transactions. |

## Cobertura no MVP

**Total.** Cenário verificável: `docker stop cashflow-balance` → POST `/api/v1/transactions` em `cashflow-transactions` continua respondendo HTTP 201 e os eventos se acumulam no RabbitMQ (verificável em http://localhost:15672).

## Trade-off aceito

Consistência eventual entre o write side e o read side — saldo consolidado pode ficar alguns segundos defasado. Aceitável para o contexto financeiro do desafio (não é transferência em tempo real).

## Verificação

1. Subir o ambiente: `docker compose up --build -d`.
2. Parar a Balance API: `docker stop cashflow-balance`.
3. Fazer 5x POST `/api/v1/transactions` no Transactions API → deve retornar HTTP 201.
4. Verificar mensagens na fila do RabbitMQ Management UI.
5. Subir Balance novamente: `docker start cashflow-balance`.
6. Backlog processa automaticamente; GET `/api/v1/balance/{date}` reflete os novos saldos.

## Evolução

- Failover multi-região (Azure Front Door + read replicas).
- ~~Outbox Pattern para eliminar a janela documentada em ADR-007~~ — **já implementado** em [ADR-025](../adrs/adr-025-outbox-and-dlq.md) (outbox transacional em Dapper + 2 níveis de retry + DLQ visível com endpoint admin).
