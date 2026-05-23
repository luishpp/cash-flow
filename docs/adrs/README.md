# Architecture Decision Records (ADRs)

Cada decisão técnica deste projeto é registrada como uma ADR (Architecture Decision Record) — formato leve com **contexto**, **decisão**, **trade-offs**, **alternativas descartadas** e, quando aplicável, **configuração concreta**.

> **Princípio de governança:** nenhuma decisão foi tomada por preferência tecnológica. Toda escolha responde a um **RNF explícito do enunciado** ou a uma das **dimensões cobradas no *Objetivo do Desafio*** (Escalabilidade, Resiliência, Segurança, Padrões Arquiteturais, Integração). Detalhamento completo dos RNFs em [`../analysis/analise-desafio-arquiteto.md` § 4](../analysis/analise-desafio-arquiteto.md).

## Índice das ADRs

| # | Decisão | Justificativa em uma linha | RNFs |
|---|---|---|---|
| [ADR-001](adr-001-cqrs.md) | CQRS — separação write/read | Requisito do enunciado: dois serviços independentes | RNF-01, RNF-03, RNF-06 |
| [ADR-002](adr-002-rabbitmq-masstransit.md) | RabbitMQ + MassTransit | Roda local em Docker; MassTransit abstrai broker | RNF-01, RNF-04, RNF-07 |
| [ADR-003](adr-003-postgres-schemas.md) | PostgreSQL com 1 DB + 2 schemas | Isolamento real via GRANTs; ARM64; menos cerimônia | RNF-01 |
| [ADR-004](adr-004-consumer-hostedservice.md) | Consumer como HostedService no MVP | Menos containers; processo dedicado documentado como evolução | RNF-01, RNF-03 |
| [ADR-005](adr-005-polly-retry.md) | Polly Retry (CB+DLQ como evolução) | Simplicidade pragmática para 50 req/s | RNF-02, RNF-04 |
| [ADR-006](adr-006-rate-limiting.md) | Rate limiting nativo do ASP.NET Core | Atende picos sem API Gateway externo | RNF-02, RNF-05 |
| [ADR-007](adr-007-publish-after-commit.md) | Publicação de evento após commit | Evita mensagens fantasma | Integridade |
| [ADR-008](adr-008-docker-compose.md) | Docker Compose para execução local | Requisito explícito do desafio | Executabilidade |
| [ADR-009](adr-009-rich-domain-model.md) | Rich Domain Model (DDD tático) | Invariantes encapsuladas; demonstra padrão | RNF-06, RNF-08 |
| [ADR-010](adr-010-dapper.md) | Dapper em vez de EF Core | Controle do SQL + zero atrito com Rich Domain | RNF-05, RNF-08 |
| [ADR-011](adr-011-idempotency.md) | Idempotência via tabela `processed_events` | Pré-requisito de Retry seguro com at-least-once | RNF-04, Integridade |
| [ADR-012](adr-012-architecture-tests.md) | Testes de Arquitetura com NetArchTest | Fitness functions — Clean Architecture verificada | RNF-08 |
| [ADR-013](adr-013-security-observability.md) | Versionamento, healthchecks, validação, CORS, HTTPS | Segurança e observabilidade mínimas honestas | RNF-05, RNF-09 |
| [ADR-014](adr-014-dotnet-10.md) | Runtime .NET 10 (LTS) | LTS atual com runway até Nov/2028; descarte de STS .NET 9 e LTS .NET 8 próxima do EOL | Manutenibilidade |
| [ADR-015](adr-015-application-services-no-mediatr.md) | Application Services como dispatcher (sem MediatR) | MediatR v12+ é pago; DI nativa basta para 2 use cases | RNF-06, RNF-08 |
| [ADR-016](adr-016-jwt-authentication.md) | JWT Bearer + Policy-based Authorization | Cobre a maior lacuna do MVP (Segurança) com stack nativa do .NET | RNF-05 |
| [ADR-017](adr-017-bdd-reqnroll.md) | BDD com Reqnroll (pt-BR) | Cobre sigla BDD da vaga; feature files como doc executável | RNF-08 |
| [ADR-018](adr-018-github-actions-ci.md) | CI via GitHub Actions (build + 3 suítes) | Vaga cobra CI/CD; evidência empírica de que tudo compila e testes passam | Manutenibilidade |
| [ADR-019](adr-019-load-test-nbomber.md) | Teste de carga com NBomber | RNF-02 deixa de ser "atendido por design" e vira "atendido por evidência" | RNF-02 |
| [ADR-020](adr-020-stryker-mutation-testing.md) | Testes de mutação com Stryker.NET | Cobertura ≠ qualidade da assertion; sigla "mutação" da vaga; thresholds break=70% | RNF-08 |
| [ADR-021](adr-021-argon2id-password-hashing.md) | Hashing Argon2id substituindo DemoUserStore | Senha nunca em claro; OWASP defaults; user enumeration mitigado | RNF-05 |
| [ADR-022](adr-022-bdd-e2e-webapplicationfactory.md) | BDD E2E via WebApplicationFactory + Testcontainers | Valida JWT/AuthZ/Argon2id ponta-a-ponta — não confia em "deve funcionar" | RNF-05, RNF-08 |
| [ADR-023](adr-023-account-lockout.md) | Account lockout (5 tentativas / 15 min) | 2ª camada contra brute-force, complementa Argon2id; counter + locked_until em app_users | RNF-05 |
| [ADR-024](adr-024-refresh-tokens-rotation.md) | Refresh tokens opacos com rotação a cada uso | Access JWT cai para 15min; logout funciona; rotação detecta reuso | RNF-05 |
| [ADR-025](adr-025-outbox-and-dlq.md) | Outbox transacional + Delayed Redelivery + DLQ admin | Fecha a janela do ADR-007 sem EF; absorve outage de minutos; DLQ visível e reprocessável | RNF-04, Integridade |

## Rastreabilidade: RNF → ADRs

| RNF | Descrição | ADRs que atendem |
|---|---|---|
| **RNF-01 — Disponibilidade** *(explícito)* | Transactions não cai se Balance cair | [001](adr-001-cqrs.md), [002](adr-002-rabbitmq-masstransit.md), [003](adr-003-postgres-schemas.md), [004](adr-004-consumer-hostedservice.md) |
| **RNF-02 — Carga** *(explícito)* | 50 req/s no Balance, máx. 5% perda | [005](adr-005-polly-retry.md), [006](adr-006-rate-limiting.md), [019](adr-019-load-test-nbomber.md) |
| **RNF-03 — Escalabilidade** | Stateless, projeção pré-calculada, fila absorve picos | [001](adr-001-cqrs.md), [004](adr-004-consumer-hostedservice.md), [006](adr-006-rate-limiting.md) |
| **RNF-04 — Resiliência** | Retry (2 níveis), healthchecks, mensagens persistentes, idempotência, outbox transacional, DLQ visível | [002](adr-002-rabbitmq-masstransit.md), [005](adr-005-polly-retry.md), [007](adr-007-publish-after-commit.md), [011](adr-011-idempotency.md), [025](adr-025-outbox-and-dlq.md) |
| **RNF-05 — Segurança** | JWT Bearer + Authorization Policies + Argon2id hash + lockout + refresh tokens com rotação + E2E coverage, validação, parameterização SQL, CORS, HTTPS, rate limit | [006](adr-006-rate-limiting.md), [010](adr-010-dapper.md), [013](adr-013-security-observability.md), [016](adr-016-jwt-authentication.md), [021](adr-021-argon2id-password-hashing.md), [022](adr-022-bdd-e2e-webapplicationfactory.md), [023](adr-023-account-lockout.md), [024](adr-024-refresh-tokens-rotation.md) |
| **RNF-06 — Padrões Arquiteturais** | CQRS, EDA, Clean Architecture, Rich Domain, Application Services como dispatcher | [001](adr-001-cqrs.md), [009](adr-009-rich-domain-model.md), [015](adr-015-application-services-no-mediatr.md) |
| **RNF-07 — Integração** | REST/JSON, AMQP, MassTransit como abstração | [002](adr-002-rabbitmq-masstransit.md) |
| **RNF-08 — Manutenibilidade** | ArchTests, BDD (doc executável + E2E), mutação (Stryker), ADRs, convenções verificadas | [009](adr-009-rich-domain-model.md), [012](adr-012-architecture-tests.md), [015](adr-015-application-services-no-mediatr.md), [017](adr-017-bdd-reqnroll.md), [020](adr-020-stryker-mutation-testing.md), [022](adr-022-bdd-e2e-webapplicationfactory.md) |
| **RNF-09 — Observabilidade** | Logs estruturados, healthchecks, correlation ID | [013](adr-013-security-observability.md) |
| **Integridade** | Eventos publicados correspondem a dados persistidos; idempotência; outbox transacional fecha janela do publish-after-commit | [007](adr-007-publish-after-commit.md), [011](adr-011-idempotency.md), [025](adr-025-outbox-and-dlq.md) |
| **Executabilidade** | `docker compose up --build` | [008](adr-008-docker-compose.md) |

## Como ler

- Comece pelos **ADRs explicitamente cobrados pelo enunciado**: [001](adr-001-cqrs.md) (CQRS), [002](adr-002-rabbitmq-masstransit.md) (mensageria), [005](adr-005-polly-retry.md) (resiliência), [006](adr-006-rate-limiting.md) (rate limit).
- Em seguida, os **ADRs de padrões internos**: [009](adr-009-rich-domain-model.md) (Rich Domain), [010](adr-010-dapper.md) (Dapper), [011](adr-011-idempotency.md) (idempotência), [012](adr-012-architecture-tests.md) (fitness functions).
- Por fim, os **ADRs de execução e qualidade**: [008](adr-008-docker-compose.md) (Docker), [013](adr-013-security-observability.md) (segurança/observabilidade básicas).
