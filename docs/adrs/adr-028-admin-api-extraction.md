# ADR-028: Extração de endpoints administrativos para serviço dedicado (CashFlow.Admin.API)

**Status:** Aceita.

**Data:** 2026-06.

## Contexto

A versão original hospedava `/api/v1/admin/errors/count` e `/api/v1/admin/errors/redeliver` (operações de Dead Letter Queue — [ADR-025](adr-025-outbox-and-dlq.md)) **dentro** de Balance.API. Justificativa original: AdminController é HTTP, Balance.API já tem o pipeline HTTP; reuso.

Ao refatorar Worker ([ADR-026](adr-026-balance-worker-extraction.md)) e Identity ([ADR-027](adr-027-identity-service-extraction.md)), revisitar Admin pelos mesmos **4 limites** revelou: Admin merece deploy unit próprio também — pelos mesmos motivos que o Worker, com perfil ainda mais radical.

## Decisão

Extrair `AdminController` + `ErrorQueueRedeliveryService` para projeto novo **`CashFlow.Admin.API`**. Stack mínima: ASP.NET Controllers + RabbitMQ.Client + JWT validation. **Sem Postgres** — Admin só fala com RabbitMQ direto.

Balance.API resulta enxuto: removidos `RabbitMQ.Client` e `AspNetCore.HealthChecks.Rabbitmq` (não eram usados pela read API; só pelo Admin). Stack final de Balance.API: 5 packages (era 13 antes do refator).

## Análise pelos 4 limites

### 1. Limite de escala

Diferença de escala **mais extrema** do refator:

- **Balance.API:** 50 RPS sustentado (RNF-02), com picos absorvíveis por rate limiting.
- **Admin.API:** ~1 request por hora em uso normal. Ops humano ou job de cron interrogando DLQ count.

Acoplar admin ao Balance:
- API de leitura otimizada por throughput coabita com endpoint usado **uma vez por hora**.
- Tuning de threadpool/connection pool da API é o que importa; admin não recebe atenção.
- Eventual spike administrativo (reprocessamento em massa após incident response) **disputa recursos da API de leitura**.

Separado: Admin pode rodar em **1 réplica única**, instância pequena. Read API escala 5-10 réplicas.

### 2. Limite de falha

- Admin opera operações **destrutivas** sobre a fila (move mensagens da DLQ). Bug em redeliver pode causar reprocessamento descontrolado.
- Acoplado em Balance.API: bug no Admin **arrisca derrubar o read side** que está atendendo 50 RPS.
- Separado: blast radius de bug no Admin é **só ele mesmo**.

### 3. Limite de deploy / versionamento

- Mudança em política de DLQ ops (filtros, paginação, autorização mais restrita) não deve forçar redeploy da read API.
- Admin pode ser **deploy noturno** (zero downtime fácil) enquanto a Balance.API tem janela de manutenção restrita.

### 4. Limite de domínio + **limite de segurança** (5º limite específico desse caso)

Admin manipula recursos da infraestrutura (mensagens em filas), não dados de negócio. **Linguagem completamente distinta:**

- Balance: "saldo diário", "consolidado", "movimentação"
- Admin: "DLQ", "redelivery", "queue depth", "poison message"

Adicionalmente: **autorização administrativa** é fundamentalmente diferente da autorização de leitura. Read API aceita qualquer comerciante autenticado; Admin deveria exigir **role Admin** (não `RequireMerchant` que o código atual usa por simplificação — citado como evolução). Em produção: Admin atrás de **VPN interna** ou **gateway de operações**, nunca exposto na internet.

## Trade-offs

| Ganha | Paga |
|---|---|
| Read API isolada de operações administrativas | +1 container no docker-compose |
| Admin pode evoluir com autorização mais restrita (RequireAdmin) | +1 Dockerfile + Program.cs |
| Bug em Admin não derruba leitura | Stack adicional pra manter |
| Tuning específico para perfil de carga ínfimo | Endpoints administrativos ficam separados do "olho" do dev que está na Balance.API |
| Possibilidade futura de expor Admin só atrás de VPN/gateway interno | — |

## Por que **sem Postgres** em Admin

Admin **não persiste estado** — todas as operações são contra o broker direto via `RabbitMQ.Client`. Não tem `IUnitOfWork`, não tem `DapperConnectionFactory`. Stack: 4 packages (RabbitMQ.Client, Serilog, Swashbuckle, Shared para JWT validation).

Healthcheck do Admin **só liveness**, sem readiness em RabbitMQ. Decisão consciente: *"ops convivem com 502 transitório se broker estiver fora; a DLQ não vai a lugar nenhum"*. Bot de monitoramento alerta sobre DLQ via outro caminho (não polling no Admin).

## Configuração

```csharp
// Admin.API Program.cs — minimal
builder.Services.AddControllers();
builder.Services.AddCashFlowAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddCashFlowAuthorization();
builder.Services.AddScoped<ErrorQueueRedeliveryService>();
builder.Services.AddHealthChecks(); // só liveness — sem readiness em broker
```

## Por que NÃO juntar com Identity ou outro serviço

Pode parecer que Identity + Admin = "API de ops" — ambos são serviços de baixa carga. **Rejeitado** porque:

- Identity manipula dados de domínio (credentials, sessions). Admin manipula infraestrutura (broker).
- Identity tem stack de segurança pesada (Argon2id, lockout). Admin é leve, só JWT validation.
- Audit logs de Identity vs Admin têm propósitos distintos.
- Failure semantics são diferentes: Identity DOWN = ninguém loga. Admin DOWN = ops espera.

→ Misturar viola o princípio do refator inteiro (1 BC ou 1 deploy unit = 1 responsabilidade clara).

## Alternativas descartadas

**A. Manter dentro de Balance.API**
Pragmatismo do MVP. **Rejeitado** depois do refator de Worker + Identity — manter Admin coabitando seria inconsistência no raciocínio arquitetural.

**B. Mover para Balance.Worker** (que já fala com broker)
Worker é **background service**, não expõe HTTP. Adicionar HTTP minimal só pra admin ops mistura propriedades fundamentais (compute-bound vs operator-facing). **Rejeitado** como anti-pattern.

**C. Não criar Admin.API — operadores usam Management Plugin do RabbitMQ direto**
Funciona, mas perde:
- Audit trail customizado
- Autorização integrada (RBAC com role Admin)
- Endpoint REST que pode ser scriptado por job/CI

Citado como simplificação aceitável em ambientes pequenos.

## RNFs atendidos

| RNF | Como atende |
|---|---|
| **RNF-01 — Disponibilidade** | Read API isolada de operações administrativas; falha em Admin não derruba leitura |
| **RNF-05 — Segurança** | Admin pode ser exposto só atrás de gateway interno; autorização mais restrita evolui sem afetar APIs públicas |
| **RNF-04 — Resiliência** | Reprocessamento de DLQ desacoplado do read side |

## Fitness functions que defendem essa decisão

Adicionadas em `tests/CashFlow.Architecture.Tests/BoundedContextIsolationTests.cs`:

- `BalanceApi_MustNotDependOn_Identity_Transactions_or_Admin` — garante que Balance.API permanece read-only puro; ninguém pode adicionar dependência em Admin via PR.

## Decisões em aberto (evoluções)

- **Trocar `RequireMerchant` por `RequireAdmin`** no AdminController. Hoje qualquer comerciante autenticado pode chamar `/admin/errors/redeliver` — anti-padrão. Trivial de corrigir, mas exige adicionar role `Admin` em [ADR-016](adr-016-jwt-authentication.md).
- **Expor Admin só atrás de VPN interna** ou colocar `/admin` em path no API Gateway com whitelist de IPs internos.
- **Endpoint de redelivery seletiva** (filtrar por payload, message-id). Hoje move todas as N mensagens em ordem FIFO.
