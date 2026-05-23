# ADR-022: BDD E2E com WebApplicationFactory + Testcontainers PostgreSQL

**Status:** Aceita

## Contexto

[ADR-017](adr-017-bdd-reqnroll.md) introduziu BDD com Reqnroll cobrindo o **domínio** (`DailyBalance.ApplyCredit` etc.) — 6 cenários, sem infraestrutura. Cobertura boa do **comportamento de invariantes**, mas zero cobertura do **fluxo HTTP real**: middleware de autenticação, deserialização JSON, pipeline ASP.NET Core, persistência Dapper, hash Argon2id.

Para um sistema com JWT + Authorization Policies + Argon2id ([ADR-016](adr-016-jwt-authentication.md), [ADR-021](adr-021-argon2id-password-hashing.md)), o teste mais valioso é **"o login funciona ponta-a-ponta?"** — coisa que unit test não responde.

Três níveis de E2E foram avaliados:

| Nível | Setup | Cobertura | Custo |
|---|---|---|---|
| **HTTP isolado (escolhido)** | WebApplicationFactory + Testcontainer Postgres; sem RabbitMQ | Auth + DB + middleware | ~10s startup, ~1s/cenário |
| HTTP cross-API | 2 WebApplicationFactory + Testcontainer Postgres + Testcontainer RabbitMQ + polling do consumer | Auth + DB + EDA completa | ~25s startup, ~3s/cenário |
| docker-compose externo | Stack levantada por script + tests fazendo HTTP raw | Tudo + redes reais | ~30-60s, fricção pré-execução |

Nível 1 oferece o melhor custo-benefício para validar **JWT + AuthZ + Argon2id**. Cross-API (RabbitMQ) é evolução natural — documentada na Seção "Evolução".

## Decisão

Adicionar **5 cenários E2E** ao projeto `CashFlow.Bdd.Tests` existente, usando:

| Componente | Versão | Propósito |
|---|---|---|
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.0 | `WebApplicationFactory<TransactionsApiAssembly>` — hosta a API em-memória, sem porta TCP |
| `Testcontainers.PostgreSql` | 4.7.0 | Postgres real (image `postgres:16-alpine`) num container efêmero |
| Reqnroll.xUnit | 2.4.1 | mesmo runner dos cenários de domínio já existentes |

### Setup compartilhado entre cenários

`CashFlow.Bdd.Tests/Setup/CashFlowApiFixture.cs` — classe `[Binding]` com hooks Reqnroll:

- `[BeforeTestRun]` (assíncrono):
  1. Sobe `PostgreSqlContainer` (~3-8s primeiro start, depois cache de imagem).
  2. Seta env vars (`ConnectionStrings__Postgres`, `Authentication__Jwt__*`, `ASPNETCORE_ENVIRONMENT=Testing`).
  3. Cria `WebApplicationFactory<TransactionsApiAssembly>` — `TransactionsApiAssembly` é uma marker class para evitar ambiguidade entre `Program` de Transactions e Balance.
  4. Roda `MigrationRunner.EnsureUpToDate` + `DemoUserSeeder.EnsureSeededAsync` manualmente (no env "Testing" o Program.cs pula esse passo).

- `[AfterTestRun]` — dispose ordenado de client, factory e container.

### Adaptações no Program.cs (Transactions.API)

Flag `isTesting = builder.Environment.IsEnvironment("Testing")` controla dois pontos:

1. **MassTransit/RabbitMQ**: não registrado quando `isTesting` → evita tentar conectar broker inexistente. Substituído por `NoOpEventPublisher` (`Infrastructure/Messaging/NoOpEventPublisher.cs`) — registra publicações como no-op.
2. **Migrations + Seed**: pulados no startup → o fixture controla, garantindo que cada teste comece com banco previsível.

### Disambiguação de `Program` (problema sutil)

Tanto `Transactions.API` quanto `Balance.API` declaram `public partial class Program {}` no namespace global (top-level statements). Quando o test project referencia ambos, `WebApplicationFactory<Program>` é ambíguo.

**Solução adotada:** marker class `CashFlow.Transactions.API.TransactionsApiAssembly` — namespaced, sem ambiguidade. `WebApplicationFactory<TEntryPoint>` usa `TEntryPoint` apenas para localizar o assembly, então o marker funciona perfeitamente.

### Cenários cobertos

`Features/AutenticacaoE2E.feature`:

1. **Login com credenciais válidas emite JWT Bearer** — POST `/api/v1/auth/login` → 200 + token + role.
2. **Login com senha errada retorna 401** — invariante de segurança crítica.
3. **Login com usuário inexistente retorna 401** — sem distinguir do anterior (anti user-enumeration, [ADR-021](adr-021-argon2id-password-hashing.md)).
4. **POST de transação sem token retorna 401** — `[Authorize(Policy=RequireMerchant)]` está realmente plugado.
5. **POST de transação com token válido retorna 201** — fluxo completo: login → bearer → persistência.

Total: **5 cenários verdes**. Tempo: ~2s após o startup do testcontainer (~5-8s primeira vez).

### Por que Cucumber Expressions ao invés de Regex

Reqnroll default é Cucumber Expression. Steps como `[When("faço POST em {string} com credenciais demo válidas")]` casam diretamente com o texto `Quando faço POST em "/api/v1/auth/login" com credenciais demo válidas`. Tentativa inicial com regex (`""/api/v1/auth/login""` literal) falhou — Reqnroll interpretou as aspas como `{string}` placeholder. Cucumber Expression é mais legível para steps com URLs/literais.

## Trade-offs

| Ganha | Perde |
|---|---|
| Auth flow validado ponta-a-ponta — não confiamos em "deve funcionar" | +~10s no teste suite (testcontainer startup) |
| Testcontainers = banco real, sem mocks; query Dapper e migração SQL exercitados | Requer Docker rodando — CI Linux já tem; dev local também |
| `WebApplicationFactory` em-memória = sem porta TCP, sem fricção de port-clash | `Program` precisou de marker class para disambiguar (efeito colateral de ter 2 APIs com Program em global) |
| Mesmo projeto `CashFlow.Bdd.Tests` hospeda BDD de domínio + E2E — descoberta de testes única | Test runner mistura testes rápidos (~50ms domain) com lentos (~1s E2E) — aceitável p/ 11 testes total |
| Env "Testing" no Program.cs deixa explícito o que é overridable | Adiciona um caminho condicional em Program.cs — código de produção sabe que existe um modo de teste |

## Verificação

```bash
# Pré-requisito: Docker Desktop rodando
dotnet test tests/CashFlow.Bdd.Tests
# 11/11 verdes (6 domain + 5 E2E)
```

Roda também no CI ([ADR-018](adr-018-github-actions-ci.md)) — `ubuntu-latest` tem Docker pré-instalado.

## Evolução natural

1. **Cenário cross-API completo** — adicionar Testcontainer RabbitMQ + WebApplicationFactory<BalanceApiAssembly>; cenário "login → POST transaction → aguarda consumer → GET balance reflete". ~+15s ao suite, mas valida EDA real.
2. **Cenário negativo de RNF-01** — derrubar Balance API no meio do teste e provar que Transactions continua atendendo.
3. **Snapshot de migration** — verificar que `transactions.app_users` e `transactions.transactions` foram criadas com colunas/índices esperados.
4. **Performance assertion** — falhar se p95 do login excede X ms (substitutivo lite de NBomber para CI).
5. **Replace RabbitMQ por MassTransit InMemory Test Harness** — permitiria validar publicação de evento sem subir broker.

## ADRs relacionadas

- [ADR-016](adr-016-jwt-authentication.md) — JWT Bearer cuja validade end-to-end esta ADR comprova.
- [ADR-017](adr-017-bdd-reqnroll.md) — Reqnroll/pt-BR; mesma infra, agora estendida para HTTP.
- [ADR-021](adr-021-argon2id-password-hashing.md) — hash real exercitado pelo `DemoUserSeeder` em cada test run.
- [ADR-018](adr-018-github-actions-ci.md) — CI roda esses testes no Linux com Docker.
