# ADR-026: Extração do consumer para deploy unit dedicado (CashFlow.Balance.Worker)

**Status:** Aceita — supersede [ADR-004](adr-004-consumer-hostedservice.md).

**Data:** 2026-06.

## Contexto

A [ADR-004](adr-004-consumer-hostedservice.md) hospedava o `TransactionConsumer` como `BackgroundService` **dentro** da `CashFlow.Balance.API`, justificando a escolha por pragmatismo de MVP (menos containers, boot mais rápido).

Em entrevista técnica, o trade-off foi questionado: *"Por que acoplar o worker no serviço de saldo?"*. A resposta dada na ocasião enfatizou implementação ("já estava tudo num projeto"), não fundamento arquitetural. **Esse foi um dos pontos que evidenciaram gap na defesa de decisões distribuídas.**

Esta ADR registra a refatoração: extrair o consumer para um **deploy unit dedicado** (`CashFlow.Balance.Worker`) e **documentar a defesa pelos 4 limites arquiteturais** — escala, falha, deploy/versionamento e domínio.

## Decisão

Extrair `TransactionConsumer` + `ConsolidationService` + `ProcessedEventsRepository` + migrations de schema `balance` para um novo projeto **`CashFlow.Balance.Worker`** (Worker Service SDK, `Microsoft.NET.Sdk.Worker`). Domain (`DailyBalance`) e infraestrutura compartilhada (`BalanceRepository`, `UnitOfWork`, `DbConnectionFactory`) ficam em **`CashFlow.Balance.Core`** — shared kernel **intra-BC** referenciado tanto por Balance.API quanto por Balance.Worker.

Topologia resultante do BC Balance:

```text
Balance.Core (class lib)   ← Domain + Persistence + BalanceRepository
       ▲
       │
   ┌───┴────┐
   │        │
Balance.API   Balance.Worker
(read HTTP)   (consumer + dono das migrations)
```

## Análise pelos 4 limites

### 1. Limite de escala

**Antes (acoplado):** consumer e API HTTP escalam juntos. Worker é **throughput-bound** (drenar fila o mais rápido possível); read API é **latency-bound** (P95 < 200ms). Forçar a mesma topologia desperdiça recursos:

- Em pico de eventos: precisaria escalar **N réplicas da API** só pra ter N workers.
- Em pico de leitura: escala workers junto, embora eles não tenham trabalho a fazer.

**Depois (separado):** Balance.Worker escala horizontalmente em função de **queue depth**; Balance.API escala em função de **request rate**. Cada um com sua métrica própria (KEDA, HPA com custom metric).

### 2. Limite de falha

**Antes:** crash, leak de memória ou loop no consumer **derruba o processo da API** — viola o RNF-01 "read API não pode cair se houver problema no consumer". A ADR-004 mitigava com `try/catch` no consumer + `BackgroundService` exception isolation, mas isso é **defesa em profundidade frágil**: qualquer bug no Polly pipeline, no MassTransit ou no Dapper UoW que não capture exception derruba o host.

**Depois:** falha no Balance.Worker é isolada. Read API continua respondendo `GET /balance/{date}` mesmo se Worker estiver em crash loop. Métrica RED da API não fica contaminada por métricas USE do consumer.

### 3. Limite de deploy / versionamento

**Antes:** hotfix em Polly retry policy do consumer força redeploy da read API. Mudança em lógica de consolidação força redeploy. Cada deploy é **mais arriscado** porque touched two concerns.

**Depois:** Worker tem ciclo de deploy próprio. Pode atualizar política de retry sem tocar na API. Pode rollback do consumer sem afetar queries. **Blast radius de cada deploy é menor.**

### 4. Limite de domínio

Esse é o **único limite que NÃO justifica separação em BC**. Worker e API compartilham:

- Mesma linguagem ubíqua (`DailyBalance`, `ApplyCredit`, `ApplyDebit`)
- Mesma persistência (schema `balance`)
- Mesmo Aggregate Root (`DailyBalance` é mantido pelo Worker, lido pela API)

→ Não são bounded contexts distintos. **São deploy units distintas dentro do mesmo BC.** Por isso o shared kernel é **intra-BC** (`Balance.Core`), não um terceiro BC.

## Trade-offs

| Ganha | Paga |
|---|---|
| Escala independente (consumer ≠ API) | +1 container no docker-compose |
| Falha isolada (RNF-01 robusto de verdade) | +1 Dockerfile + Program.cs |
| Deploy independente (blast radius menor) | +1 projeto na .sln (Balance.Core + Balance.Worker) |
| Observabilidade separada (RED da API ≠ USE do worker) | Coordenação de ordem de boot (Balance.API depende do Worker ter rodado migrations) |
| Worker pode evoluir sem ASP.NET (runtime image base, sem aspnet) | Worker precisa de healthcheck próprio (não tem HTTP — usamos `service_started` no compose) |
| Defesa arquitetural documentada (este doc) | — |

## Por que **Worker** é dono das migrations

Worker é o **writer** do schema `balance` (cria/atualiza `daily_balance` + `processed_events`). API só **lê**. Princípio: **quem escreve, é dono do schema**. Em produção real, migrations seriam um job separado (não rodariam no Worker no startup), mas para o demo isso é over-engineering. Citado como evolução.

## Configuração do Worker

```csharp
// Program.cs do Balance.Worker
var builder = Host.CreateApplicationBuilder(args);   // IHost genérico, sem ASP.NET

builder.Services.AddMassTransit(cfg =>
{
    cfg.AddConsumer<TransactionConsumer>();
    cfg.AddDelayedMessageScheduler();
    cfg.UsingRabbitMq((ctx, rmq) =>
    {
        rmq.UseDelayedMessageScheduler();
        rmq.ReceiveEndpoint("balance.transaction-registered", ep =>
        {
            ep.SingleActiveConsumer = true;            // FIFO entre réplicas
            ep.ConcurrentMessageLimit = 1;
            ep.PrefetchCount = 1;
            ep.UseDelayedRedelivery(r => r.Intervals(  // 2º nível de retry (ADR-025)
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(15)));
            ep.ConfigureConsumer<TransactionConsumer>(ctx);
        });
    });
});
```

**Imagem base:** `mcr.microsoft.com/dotnet/aspnet:10.0`.

> **Trade-off honesto descoberto na verificação empírica (load test):** Worker NÃO expõe HTTP em código, então tentamos primeiro a imagem `runtime:10.0` (menor, ~80MB vs ~110MB). Falhou com exit 150 porque `Microsoft.Extensions.Hosting` 10.0.0 declara `FrameworkReference Microsoft.AspNetCore.App` transitivamente — comportamento novo no .NET 10. Voltamos para `aspnet:10.0`. O ganho de 30MB de imagem não compensa o custo de descobrir/suprimir o FrameworkReference. **Em produção real, valeria investigar o `EnableDefaultMicrosoftAspNetCoreApp` MSBuild property; para o demo, a imagem aspnet é pragmática.**

## Topologia no docker-compose

```yaml
balance-worker:
  depends_on:
    postgres:  { condition: service_healthy }
    rabbitmq:  { condition: service_healthy }

api-balance:
  depends_on:
    postgres:        { condition: service_healthy }
    balance-worker:  { condition: service_started }    # API espera Worker iniciar
```

`service_started` (não `service_healthy`) porque Worker é BackgroundService sem HTTP healthcheck. Trade-off explícito: pequeno risco de race em primeira boot (Worker sobe mas migrations ainda não correram quando API faz a primeira query) — aceitável no demo, mitigado em produção com migration job dedicado.

## Alternativas descartadas

**A. Manter como BackgroundService dentro da API (status quo da ADR-004)**
Defendido na entrevista por pragmatismo de MVP. **Rejeitado** porque o feedback técnico expôs que o pragmatismo virou justificativa para evitar o esforço de articular trade-offs reais. Em vaga de Arquiteto, o esforço de articulação **é o produto**.

**B. Worker como sidecar de Balance.API (mesmo container, processos separados)**
Compartilharia espaço de memória, mas anularia o ganho de **deploy independente**. Adicionaria complexidade de PID management sem benefício real.

**C. Worker referenciando Balance.API como projeto (em vez de Balance.Core)**
Worker carregaria ASP.NET, Swashbuckle, RateLimiter — peso de framework que não usa. Acoplaria Worker à camada HTTP. **Rejeitado** explicitamente em favor de Balance.Core (class library limpa, só Dapper + Npgsql).

## RNFs atendidos

| RNF | Como atende |
|---|---|
| **RNF-01 — Disponibilidade** | Falha no consumer não derruba read API; deploy independente reduz blast radius |
| **RNF-03 — Escalabilidade** | Worker e API escalam horizontalmente em métricas distintas |
| **RNF-08 — Manutenibilidade** | Fitness functions ([ADR-012]) validam isolamento (Balance.Worker não depende de Balance.API) |

## Fitness functions que defendem essa decisão

Adicionadas em `tests/CashFlow.Architecture.Tests/BoundedContextIsolationTests.cs`:

- `BalanceWorker_MustNotDependOn_Identity_Transactions_BalanceApi_or_Admin` — garante que Worker continua sendo deploy unit independente; ninguém pode "voltar a acoplar" via PR.
- `BalanceCore_MustNotDependOn_AnyOtherAssembly` — Core é shared kernel **puro** (nem evento de integração); intra-BC.
