# ADR-019: Teste de carga com NBomber para validar RNF-02

**Status:** Aceita

## Contexto

O **RNF-02** ([`../rnfs/rnf-02-carga.md`](../rnfs/rnf-02-carga.md)) é um dos dois RNFs **explícitos** do enunciado:

> "Em dias de picos, o serviço de consolidado diário recebe 50 requisições por segundo, com no máximo 5% de perda de requisições."

Até este ponto o RNF estava **atendido por design** (rate limiter + projeção O(1) + Polly), mas **sem evidência empírica**. Para um teste de arquiteto sênior, "por design" é metade do trabalho: o avaliador espera saber que o autor pelo menos **mediu** o sistema sob a carga declarada.

Três ferramentas foram consideradas:

| Ferramenta | Por que considerada | Trade-off |
|---|---|---|
| **NBomber** | Nativo .NET, integra com C#, gera reports HTML | Maior — escolhida |
| k6 | Padrão de mercado para load test | Script em JS — fora do stack do projeto |
| JMeter | Tradicional, GUI rica | XML pesado, JVM, fricção de execução |
| Bombardier | CLI minimal, Go | Limitado em assertions; sem reports estruturados |

## Decisão

Adotar **NBomber 6.0** em um projeto console dedicado `tests/CashFlow.LoadTests/`, **fora do CI automático** (roda localmente sob `docker compose up`).

### Cenário implementado

- **Endpoint:** `GET /api/v1/balance/{date}` (a Balance API que o RNF-02 cobra)
- **Auth:** auto-login com credenciais demo (ou via env var `CASHFLOW_TOKEN`) — injeta `Authorization: Bearer ...` em cada request
- **Ramp-up:** 0 → 50 req/s em 10s (evita falso-positivo de cold start do .NET / Postgres)
- **Sustained:** 50 req/s por 60s = **3.000 requisições alvo**
- **Critério de aprovação:** `pass_rate >= 95%` (perda ≤ 5% conforme RNF-02)
- **Exit code:** `0` se passou, `1` se falhou — adequado para `workflow_dispatch` manual

### Saída

- Console com sumário (total, OK, fail, pass rate, PASS/FAIL).
- `load-test-reports/` com HTML, Markdown e TXT detalhados — latência p50/p95/p99, gráficos, distribuição de status codes.

## Trade-offs

| Ganha | Perde |
|---|---|
| **RNF-02 vira "atendido por evidência"**, não só "por design" | Roda em ~70s — caro demais para PR-time CI |
| Stack 100% .NET — sem necessidade de aprender DSL nova | Requer stack rodando (`docker compose up`) — fricção a mais para o avaliador rodar |
| Reports HTML/MD anexáveis ao PR ou ao README | Cenário medido em loopback (mesma máquina) — não substitui benchmark em ambiente target |
| Exit code permite uso em `workflow_dispatch` ou cron noturno futuro | NBomber 6.0 mudou APIs de report (versões anteriores tinham `WithReportFormats`) |
| Auto-login com credenciais demo — `dotnet run` é o único comando | Em produção, o JWT viria de IdP externo — script precisaria adaptar |

## Por que NÃO está no CI automático

Adicionar ao `.github/workflows/ci.yml` exigiria:

1. **Service containers** para Postgres + RabbitMQ + 2 APIs (5 containers).
2. **Healthchecks com retry** (~10-20s de espera após `up`).
3. **~70s de execução** do cenário NBomber.
4. **~3min totais por PR** — em fila de PRs grande, isso é dor real.

Custo/benefício ruim para um teste técnico. Roda **localmente** ou via `workflow_dispatch` (botão "Run workflow" no GitHub) quando se quer regredir performance. Decisão de CI documentada em [ADR-018](adr-018-github-actions-ci.md).

## Verificação

```bash
docker compose up --build -d
# aguardar healthchecks (~10s)
dotnet run --project tests/CashFlow.LoadTests --configuration Release
```

Saída esperada (números aproximados — depende da máquina):

```
===== RNF-02 verification =====
  Target rate:      50 req/s sustained for 60s
  Total requests:   3000
  OK:               ~2987
  Failed:           ~13
  Pass rate:        ~99.5%
  Min acceptable:   95%
  Result:           PASS ✅
```

## Evolução

- **Job `load-test` em `workflow_dispatch`** — botão manual no GitHub Actions executando NBomber com service containers; resultado vira artifact.
- **Cenário de write side** — bombardear `POST /api/v1/transactions` para medir backpressure da fila.
- **Cenário de degradação** — derrubar a Balance API durante o load e medir se a Transactions continua atendendo (validação empírica do RNF-01).
- **Ambiente target** — rodar em ACI / AKS para benchmark real, não em loopback localhost.
- **Threshold automático** — falhar build se p95 > 200ms (depois de baseline estabelecido).

## ADRs relacionadas

- [ADR-005](adr-005-polly-retry.md) — Polly retry no consumer; carga sustentada pode acumular dead-lettering.
- [ADR-006](adr-006-rate-limiting.md) — rate limiter é o componente que NBomber mais exercita.
- [ADR-016](adr-016-jwt-authentication.md) — script consome `/auth/login` para obter Bearer.
- [ADR-018](adr-018-github-actions-ci.md) — explica por que load test fica fora do CI automático.
