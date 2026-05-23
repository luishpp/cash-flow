# RNF-07 — Integração

**Origem:** Derivado *(seção "Objetivo do Desafio" do PDF)*.

## Declaração

> "Integração: Defina como os componentes se comunicarão. Avalie protocolos, formatos de mensagem e ferramentas de integração."

## Decisões arquiteturais que atendem

| ADR | Como contribui |
|---|---|
| [ADR-002 — RabbitMQ + MassTransit](../adrs/adr-002-rabbitmq-masstransit.md) | Protocolo AMQP para integração assíncrona entre bounded contexts; MassTransit como abstração que permite trocar broker (RabbitMQ → Azure Service Bus) sem mudar código de negócio. |

## Protocolos e formatos no MVP

| Camada | Protocolo | Formato | Onde |
|---|---|---|---|
| Cliente ↔ APIs | **HTTPS** (TLS) | **JSON** | Swagger UI, curl, frontend futuro |
| Transactions API ↔ Balance API | **AMQP** | **JSON** (via MassTransit) | Evento `CashFlow.Shared.Events.TransactionRegistered` |
| APIs ↔ PostgreSQL | **TCP** + Postgres wire protocol | Binário | Via Npgsql/Dapper |
| Healthchecks externos | **HTTP** | JSON | `/health/live`, `/health/ready` |

## Contratos

- **REST**: documentado via Swagger/OpenAPI auto-gerado em `/swagger`.
- **Eventos AMQP**: contrato definido em código no projeto `CashFlow.Shared/Events/TransactionRegistered.cs` (referenciado por ambos os serviços para garantir compatibilidade).

## Cobertura no MVP

**Total.** Os três protocolos relevantes (REST/JSON síncrono, AMQP assíncrono, Postgres wire) estão implementados.

## Trade-off aceito

- REST/JSON em vez de gRPC entre APIs: REST/JSON é mais legível em debugging e tem suporte nativo no Swagger. gRPC só se justifica em comunicação interna de alta vazão entre microsserviços (evolução).
- MassTransit adiciona uma camada de abstração — em troca de portabilidade de broker.

## Verificação

- Swagger UI em http://localhost:5001/swagger e http://localhost:5002/swagger expõe todos os endpoints REST.
- RabbitMQ Management em http://localhost:15672 mostra as filas e mensagens (login: guest/guest).
- Contrato do evento é um record imutável compartilhado por ambos os projetos via `ProjectReference`.

## Evolução

- **gRPC** para comunicação interna de alta vazão entre microsserviços (ex: Balance API consultando outro serviço de regras).
- **Webhooks de saída** para notificar sistemas externos (parceiros, BI).
- **API Gateway (Apigee / Azure APIM)** para padronização de contratos, transformação de payload, rate limit por consumidor.
- **AsyncAPI** spec para documentar contratos de eventos como Swagger/OpenAPI faz para REST.
- **CloudEvents** como envelope padronizado para eventos cross-system.
