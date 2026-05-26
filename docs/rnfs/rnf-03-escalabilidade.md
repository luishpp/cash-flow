# RNF-03 — Escalabilidade

**Origem:** Derivado *(seção "Objetivo do Desafio" do PDF)*.

## Declaração

> "Escalabilidade: Garanta que a arquitetura possa lidar com o aumento da carga de trabalho sem degradação significativa do desempenho. Considere dimensionamento horizontal, balanceamento de carga e estratégias de cache."

## Decisões arquiteturais que atendem

| ADR | Como contribui |
|---|---|
| [ADR-001 — CQRS](../adrs/adr-001-cqrs.md) | Read side escala independente do write side (cargas têm perfis diferentes). |
| [ADR-004 — Consumer como HostedService](../adrs/adr-004-consumer-hostedservice.md) | Migração documentada para Worker dedicado permite escalar consumer ortogonalmente. |
| [ADR-006 — Rate Limiting](../adrs/adr-006-rate-limiting.md) | Protege o downstream contra estouro; fila absorve picos curtos. |

## Cobertura no MVP

**Parcial.** O design suporta horizontal scaling, mas não está provisionado:

- ✅ APIs são **stateless** — nenhum estado em memória além de pools de conexão.
- ✅ Projeção `daily_balance` é **pré-calculada** — consulta O(1) por data; não há cálculo síncrono pesado.
- ✅ Fila RabbitMQ **absorve picos** de escrita; consumer processa em ritmo controlado.
- ⚠️ Rate limiting é **per-instance** (não distribuído) — limita escala horizontal sem coordenação.
- ⚠️ Sem cache (`IMemoryCache` ou Redis) entre API e banco — leitura sempre toca o storage.

## Trade-off aceito

No volume do desafio (50 req/s), uma única instância de cada API é suficiente. Provisionar autoscaling e cache distribuído seria over-engineering para o cenário de avaliação local.

## Verificação

- **Hoje:** carga sustentada a 50 RPS por 60s validada empiricamente em [`tests/CashFlow.LoadTests`](../../tests/CashFlow.LoadTests/) com **NBomber** ([ADR-019](../adrs/adr-019-load-test-nbomber.md)) — cobre o teto do RNF-02. Rate-limiter parametrizável em `RateLimiting:Balance` permite ajustar `PermitLimit`/`QueueLimit` sem rebuild para sondar o efeito de pequenas variações.
- **Evolução natural:** ramp progressivo (50 → 100 → 200 RPS) reaproveitando a mesma infra NBomber, com `Simulation.RampingInject` — confirmaria degradação proporcional vs. catastrófica. Não está no MVP porque RNF-03 é derivado, não explícito.
- Observar a queue depth no RabbitMQ durante picos de escrita — deve drenar quando a carga cai.

## Evolução

- **Autoscaling com KEDA** baseado em queue depth (consumer) e CPU/RPS (APIs).
- **Azure Container Apps** ou **AKS** para horizontal scaling automático.
- **Cache distribuído (Redis)** entre Balance API e banco.
- **Read replicas** do Postgres para queries de range longas.
- **Sharding por data** ou tenant em volumes muito altos.
