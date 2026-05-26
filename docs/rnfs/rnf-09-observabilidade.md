# RNF-09 — Observabilidade

**Origem:** Derivado *(implícito em "Manutenibilidade" + "Resiliência" do PDF; cobrado também na descrição da vaga)*.

## Declaração

Capacidade de **entender o estado interno do sistema a partir dos seus outputs** (logs, métricas, traces). Pilar fundamental para operação de sistemas distribuídos.

## Decisões arquiteturais que atendem

| ADR | Como contribui |
|---|---|
| [ADR-013 — Versionamento, healthchecks, validação, CORS, HTTPS](../adrs/adr-013-security-observability.md) | Logs estruturados com Serilog + healthchecks live/ready. |

## Cobertura no MVP

**Mínimo honesto.** O que está em vigor:

- ✅ **Logs estruturados** via Serilog em ambas as APIs; sink console com nível configurável por ambiente.
- ✅ **Enricher de `Service`** — distingue logs entre `CashFlow.Transactions.API` e `CashFlow.Balance.API`.
- ✅ **`UseSerilogRequestLogging()`** — log de cada requisição HTTP com path, status, duração.
- ✅ **Healthchecks** dedicados:
  - `/health/live` — apenas valida que o processo respira (sem dependências).
  - `/health/ready` — valida dependências (Postgres). Não inclui consumer (ver [ADR-004](../adrs/adr-004-consumer-hostedservice.md)).
- ⚠️ **Distributed tracing**: ausente no MVP — adicionado quando OpenTelemetry virar evolução.
- ⚠️ **Métricas de negócio**: ausentes no MVP — instrumentação futura.

## Trade-off aceito

Sem stack completa de observabilidade (OpenTelemetry + Application Insights + dashboards), apenas logs estruturados em console. Em produção, isso seria insuficiente — mas para o escopo de avaliação local, console + healthchecks permitem inspeção via `docker compose logs`.

## Verificação

```bash
# Logs estruturados em tempo real
docker compose logs -f api-transactions
docker compose logs -f api-balance

# Healthchecks
curl http://localhost:5001/health/ready
curl http://localhost:5002/health/ready
# Esperado: 200 OK com status "Healthy" se Postgres OK; 503 caso contrário.
```

## Evolução

- **OpenTelemetry** completo (traces + métricas + logs):
  - Instrumentação automática de ASP.NET Core, HttpClient, Npgsql.
  - Propagação manual de `W3C TraceContext` em headers AMQP entre Transactions → broker → Balance API.
  - Exporters para Azure Monitor / Application Insights / Jaeger / Grafana Tempo.
- **Métricas customizadas** via `System.Diagnostics.Metrics`:
  - Contadores: transações registradas, eventos processados, retentativas.
  - Histogramas: latência por endpoint (p50/p95/p99).
  - Métricas de negócio: total de crédito/débito por dia.
- **Dashboards** em Azure Monitor Workbooks ou Grafana — RED metrics (Rate, Errors, Duration) por serviço.
- **Alertas** em Azure Monitor Alert Rules com KQL — taxa de erro, latência alta, queue depth crescente.
- **Correlation ID** propagado por header `X-Correlation-Id` entre client → API → broker → consumer.
