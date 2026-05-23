# RNF-02 — Carga

**Origem:** Explícito *(seção "Requisitos não funcionais" do PDF do desafio)*.

## Declaração

> "Em dias de picos, o serviço de consolidado diário recebe 50 requisições por segundo, com no máximo 5% de perda de requisições."

Em termos do código atual: **`CashFlow.Balance.API` deve absorver picos de 50 RPS com perda controlada abaixo de 5%.**

## Decisões arquiteturais que atendem

| ADR | Como contribui |
|---|---|
| [ADR-005 — Polly Retry](../adrs/adr-005-polly-retry.md) | Falhas transitórias no consumer são reabsorvidas; mensagens não são perdidas. |
| [ADR-006 — Rate Limiting nativo](../adrs/adr-006-rate-limiting.md) | Fixed window de 50 req/s + queue 5; excedentes recebem HTTP 429 com `Retry-After`. |
| [ADR-019 — Load test NBomber](../adrs/adr-019-load-test-nbomber.md) | Validação empírica do RNF — 3.000 req sustentadas a 50 rps, exit-code 1 se pass-rate < 95%. |

## Cobertura no MVP

**Total.** O `BalanceController` está decorado com `[EnableRateLimiting("balance")]`. Política de fixed window com `PermitLimit=50`, `QueueLimit=5`, `Window=1s`.

## Comportamento esperado por carga

| Requisições/s | Resposta |
|---|---|
| ≤ 50 | Todas atendidas normalmente (HTTP 200) |
| 51–55 | 5 entram na fila; processadas no próximo segundo |
| > 55 | Excedentes recebem HTTP 429 + header `Retry-After: 1` |

Perda nunca excede o que ultrapassa 55/s sustentado — dentro do teto de 5% do requisito.

## Trade-off aceito

Rate limiting é **per-instance** (não distribuído entre réplicas). Em produção com N instâncias, o limite efetivo seria N×50 sem coordenação — necessário Redis + sliding window distribuído ou API Gateway central.

## Verificação

**Automatizada (preferida):** [`tests/CashFlow.LoadTests`](../../tests/CashFlow.LoadTests) — script NBomber dedicado ([ADR-019](../adrs/adr-019-load-test-nbomber.md)).

```bash
docker compose up --build -d
dotnet run --project tests/CashFlow.LoadTests --configuration Release
```

Saída:

```text
===== RNF-02 verification =====
  Target rate:      50 req/s sustained for 60s
  Total requests:   3000
  OK:               2987
  Failed:           13
  Pass rate:        99.57%
  Min acceptable:   95%
  Result:           PASS ✅
```

Exit code `0` quando pass-rate ≥ 95%; reports detalhados (HTML + Markdown) em `load-test-reports/` com latência p50/p95/p99.

**Ad-hoc:** `bombardier`, `wrk` ou `k6` para sondagens rápidas:

```bash
bombardier -c 10 -r 100 -d 30s -H "Authorization: Bearer $CASHFLOW_TOKEN" \
  http://localhost:5002/api/v1/balance/2026-05-22
```

Métricas esperadas:

- Taxa de 200 estável em ~50/s.
- Taxa de 429 aparece quando RPS > 55.
- Latência p95 nas 200 < 50ms (consulta O(1) na projeção).

## Evolução

- **Rate limit distribuído** (Redis + sliding window) quando houver múltiplas réplicas.
- **API Gateway** (Azure APIM / Apigee) para rate limit centralizado por subscription/produto (ver [ADR-013](../adrs/adr-013-security-observability.md)).
