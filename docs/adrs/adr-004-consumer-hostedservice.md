# ADR-004: Consumer de eventos como HostedService dentro da Balance API

**Status:** **Superseded by [ADR-026](adr-026-balance-worker-extraction.md)** *(2026-06)* — extração para deploy unit dedicado após feedback de entrevista que expôs gap de raciocínio sobre limites arquiteturais (escala/falha/deploy/domínio). A versão "embarcado no MVP" desta ADR continua válida como **registro histórico** do pragmatismo inicial; a defesa pelos 4 limites vive na ADR-026.

## Contexto

O consumidor de eventos `TransactionRegistered` precisa ler do RabbitMQ e atualizar a projeção `DailyBalance` no schema `balance`. Duas opções de hospedagem:

1. **Worker Service** (`Microsoft.Extensions.Hosting.WorkerService`) em processo/container separado
2. **HostedService** (`IHostedService`/`BackgroundService`) embarcado na Balance API

Para a carga do desafio (50 req/s no pico de consultas; volume de escrita igualmente modesto), a pergunta é: o benefício de processo separado justifica o custo operacional adicional?

## Decisão

Para o **MVP do desafio**, implementar o consumidor como `BackgroundService` (`TransactionConsumer`) dentro da `CashFlow.Balance.API` via `cfg.AddConsumer<TransactionConsumer>()` do MassTransit. A separação em processo independente fica documentada como **evolução natural** quando o volume justificar.

## Por que HostedService primeiro

| Critério | HostedService embarcado | Worker Service separado |
|---|---|---|
| Containers no docker-compose | 4 (Postgres, RabbitMQ, 2 APIs) | 5 (+1 worker) |
| Projetos no solution | 3 produção | 4 produção |
| Dockerfiles | 2 | 3 |
| Tempo de boot do compose | ~25s | ~35s |
| Risco de falha do `docker compose up` | Menor | Maior |
| Demonstra padrão Background Worker | Sim (mesmo padrão) | Sim |

A diferença prática para 50 req/s de leitura + escrita modesta é nula. A diferença operacional (menos uma imagem para construir, menos um container para gerenciar) é concreta.

## Por que documentar a separação como evolução

O argumento da versão anterior — "se o Worker processa backlog grande, degrada queries" — é **válido em volumes altos**, mas é otimização prematura para 50 req/s. A solução pragmática é:

1. Começar embarcado (MVP rápido, menos pontos de falha no `docker compose up`).
2. Separar em processo dedicado quando uma das condições se materializar:
   - Backlog típico > centenas de mensagens
   - Queries começam a sofrer latência > 100ms durante consumo
   - Necessidade de escalar consumer (N workers) sem escalar API
3. A migração é **mecânica** — mover a classe `TransactionConsumer` para um novo projeto `CashFlow.Balance.Worker` mantendo a mesma `DbConnection` Dapper e o mesmo `MassTransit` config.

## Trade-offs da decisão atual

| Ganha | Perde |
|---|---|
| Menor cerimônia de infraestrutura (3 projetos vs 4) | Crash do consumer derruba a API de consulta junto (mitigado por `try/catch` no consumer + supervisor do `BackgroundService`) |
| Boot mais rápido do ambiente local | Em backlog extremo, consumer compete por CPU com a API |
| Demonstra o mesmo padrão arquitetural (consumer assíncrono desacoplado por fila) | Não demonstra escalabilidade horizontal independente do consumer |
| Migração futura é refator mecânico, não redesign | — |

## Mitigação do risco de "crash derruba a API"

- Consumer envolto em `try/catch` que loga e faz `Nack` da mensagem (volta para fila); exception nunca borbulha para derrubar o host.
- `BackgroundService` no .NET (desde 8, mantido em 10) isola exceptions do worker do host por padrão (`BackgroundServiceExceptionBehavior.Ignore` é o default — mantido).
- Healthcheck `/health/ready` da Balance API **não inclui** o estado do consumer, evitando que falha no consumer marque a API como unhealthy.

## Alternativa descartada

**Worker Service como processo separado desde o MVP** — adiciona um container, uma imagem Docker e uma fonte de bug no boot do compose sem benefício mensurável no volume do desafio. Citado na Seção 13 (Evoluções Futuras) da análise como caminho de evolução.

## ADRs relacionadas

- [ADR-001](adr-001-cqrs.md) — separação write/read que demanda o consumer
- [ADR-002](adr-002-rabbitmq-masstransit.md) — broker que entrega os eventos
- [ADR-005](adr-005-polly-retry.md) — retry em torno da execução do consumer
- [ADR-011](adr-011-idempotency.md) — proteção contra reentregas
