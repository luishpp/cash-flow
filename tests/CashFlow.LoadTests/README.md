# CashFlow.LoadTests

Validação empírica do **RNF-02** (`docs/rnfs/rnf-02-carga.md`):

> "Em dias de picos, o serviço de consolidado diário recebe 50 requisições por segundo, com no máximo 5% de perda de requisições."

Decisão arquitetural: [ADR-019](../../docs/adrs/adr-019-load-test-nbomber.md).

## Pré-requisitos

- Docker Desktop rodando.
- Portas `5001`, `5002`, `5432`, `5672`, `15672` livres.
- .NET 10 SDK instalado (para `dotnet run`).

## Como rodar

```bash
# 1. Subir a stack
docker compose up --build -d

# 2. Aguardar healthchecks (~10s)
docker compose ps   # confirmar que api-transactions e api-balance estão Up

# 3. Rodar o teste de carga
dotnet run --project tests/CashFlow.LoadTests --configuration Release
```

O programa tenta **auto-login** com as credenciais demo (`carlos` / `S3cret!ChangeMe`). Se precisar de outro usuário, exporte o token:

```bash
# bash / zsh
export CASHFLOW_TOKEN="eyJhbGciOi..."

# PowerShell
$env:CASHFLOW_TOKEN = "eyJhbGciOi..."
```

## O que mede

- **Cenário:** `GET /api/v1/balance/{date}` (data do dia).
- **Ramp-up:** 0 → 50 req/s em 10s.
- **Sustained:** 50 req/s por 60s (3.000 requisições no total).
- **Critério de aprovação:** ≥ 95% das requisições retornam `200 OK` (perda ≤ 5%, conforme RNF-02).

## Saída

Console:

```
===== RNF-02 verification =====
  Target rate:      50 req/s sustained for 60s
  Total requests:   3000
  OK:               2987
  Failed:           13
  Pass rate:        99.57%
  Min acceptable:   95%
  Result:           PASS ✅
```

Reports detalhados (HTML / Markdown / TXT) em `load-test-reports/` — inclui latência (p50, p95, p99), distribuição de status codes e gráficos.

Exit code: `0` se passou, `1` se falhou — adequado para CI manual (`workflow_dispatch`).

## Por que NÃO está no CI automático

Adicionar este teste ao `.github/workflows/ci.yml` exigiria service containers (Postgres + RabbitMQ) com healthcheck-com-retry e ~3min de runtime por PR — custo/benefício ruim para um teste técnico. Roda **localmente** ou via `workflow_dispatch` manual quando se quer regredir performance. Trade-off detalhado em [ADR-019](../../docs/adrs/adr-019-load-test-nbomber.md).
