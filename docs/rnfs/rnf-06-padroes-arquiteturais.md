# RNF-06 — Padrões Arquiteturais

**Origem:** Derivado *(seção "Objetivo do Desafio" do PDF)*.

## Declaração

> "Padrões Arquiteturais: Escolha padrões adequados, como microsserviços, monolitos, SOA ou serverless. Considere trade-offs entre simplicidade e flexibilidade."

## Decisões arquiteturais que atendem

| ADR | Como contribui |
|---|---|
| [ADR-001 — CQRS](../adrs/adr-001-cqrs.md) | Separação write/read na arquitetura geral. |
| [ADR-009 — Rich Domain Model](../adrs/adr-009-rich-domain-model.md) | DDD tático (entidades, value objects, invariantes, factory methods). |

## Padrões aplicados no MVP

| Padrão | Onde | Por quê |
|---|---|---|
| **CQRS** | Arquitetura geral (Transactions vs Balance) | RNF-01: independência write/read |
| **Event-Driven Architecture (EDA)** | Comunicação entre serviços via `TransactionRegistered` | Desacoplamento temporal |
| **Clean Architecture** | Dentro de cada API (Domain → Application → Infrastructure → Controllers) | Testabilidade + manutenibilidade |
| **Rich Domain Model (DDD tático)** | `Transaction`, `DailyBalance`, Value Objects | Invariantes encapsuladas |
| **Repository + Unit of Work** | `Infrastructure/Repositories/`, `IUnitOfWork` | Abstração de persistência sobre Dapper |
| **Idempotent Consumer** | `TransactionConsumer` + `processed_events` | Reentregas at-least-once sem corromper saldo |
| **Retry com Exponential Backoff** | Consumer via Polly v8 | Tolerância a falhas transitórias |
| **Fitness Functions** | `CashFlow.Architecture.Tests` | Regras arquiteturais como testes executáveis |
| **API Versioning (prefixo)** | `/api/v1/...` em todos endpoints | Evolução sem breaking changes |

## Por que essa combinação (e não microsserviços puros / monolito anêmico)

- **Monolito** não cabe — viola o RNF-01 (lançamento e consolidado precisam falhar independentemente).
- **Microsserviços completos** com database-per-service em containers separados seria over-engineering para 2 entidades. CQRS + 2 bounded contexts em 2 APIs entrega o desacoplamento sem o overhead de service mesh, distributed tracing complexo, etc.
- **Serverless** não cabe na execução local com Docker exigida pelo desafio.

## Trade-off aceito

Mais componentes para operar (2 APIs + broker + banco) do que um monolito simples — em troca de independência de deploy/disponibilidade que o RNF-01 exige.

## Verificação

- Diagramas C4 em [`../diagrams/`](../diagrams/) refletem os padrões aplicados.
- Testes de arquitetura em `CashFlow.Architecture.Tests` validam Clean Architecture (Dependency Rule).
- ADRs documentam cada escolha com trade-offs explícitos.

## Evolução

- **Event Sourcing**: armazenar cada transação como evento imutável (event store), facilitando auditoria financeira e time-travel queries.
- **Saga Pattern**: orquestrar operações cross-service quando o domínio crescer.
- **Hexagonal Architecture** explícita com Ports & Adapters formalizados (já implícito na separação Domain/Infrastructure).
