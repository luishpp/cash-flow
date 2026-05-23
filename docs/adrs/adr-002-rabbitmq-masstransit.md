# ADR-002: Usar RabbitMQ com MassTransit para mensageria

**Status:** Aceita

## Contexto

Com CQRS adotado ([ADR-001](adr-001-cqrs.md)), é necessário um mecanismo para comunicar eventos do write side (`CashFlow.Transactions.API`) para o read side (`CashFlow.Balance.API`). O **RNF-01** exige que essa comunicação seja assíncrona — se o consumidor estiver fora, o produtor não pode ser afetado. O desafio também exige execução local via Docker.

## Decisão

Usar **RabbitMQ** como message broker com **MassTransit** como camada de abstração no código .NET. O evento publicado é `CashFlow.Shared.Events.TransactionRegistered`, definido no projeto compartilhado para evitar acoplamento de tipos entre serviços.

## Por que RabbitMQ (e não Azure Service Bus)

| Critério | RabbitMQ | Azure Service Bus |
|---|---|---|
| Execução local em Docker | Imagem oficial, sobe em segundos, ARM64 nativo | Emulador experimental, instável, sem ARM64 |
| UI de gerenciamento | Management plugin incluso (porta 15672) | Sem UI local |
| Tamanho da imagem | ~180MB | N/A (serviço cloud) |
| Maturidade do ecossistema .NET | MassTransit, EasyNetQ, client oficial | MassTransit, Azure.Messaging |

## Por que MassTransit como abstração

O MassTransit abstrai o broker. O código de negócio publica e consome mensagens via interfaces (`IPublishEndpoint`, `IConsumer<T>`) que não referenciam RabbitMQ diretamente. A troca para Azure Service Bus em produção é uma mudança de uma linha no `Program.cs`:

```csharp
// Local (RabbitMQ)
cfg.UsingRabbitMq((ctx, cfg) => { cfg.Host("localhost"); });

// Produção (Azure Service Bus)
cfg.UsingAzureServiceBus((ctx, cfg) => { cfg.Host(connectionString); });
```

## Trade-offs

| Ganha | Perde |
|---|---|
| Desacoplamento temporal — mensagens sobrevivem a quedas do consumidor | Complexidade operacional — mais um componente para monitorar |
| Garantia de entrega at-least-once | Necessidade de tratar idempotência no consumidor (ver [ADR-011](adr-011-idempotency.md)) |
| Portabilidade via MassTransit — troca de broker sem refatoração | Abstração adicional — curva de aprendizado do MassTransit |
| Atende RNF-01 diretamente | Consistência eventual (consequência aceita em [ADR-001](adr-001-cqrs.md)) |

## Alternativa descartada

**Chamada HTTP síncrona entre os serviços** — viola RNF-01 (se o Balance cair, o POST de transação retornaria erro ou timeout). Retry com HTTP resolve parte do problema, mas o acoplamento de disponibilidade permanece.

## ADRs relacionadas

- [ADR-001](adr-001-cqrs.md) — porque mensageria existe (CQRS)
- [ADR-005](adr-005-polly-retry.md) — políticas de resiliência no consumer
- [ADR-007](adr-007-publish-after-commit.md) — quando publicar o evento
- [ADR-011](adr-011-idempotency.md) — idempotência no consumer (at-least-once)
