# ADR-018: CI via GitHub Actions (build + 3 suítes de teste)

**Status:** Aceita

## Contexto

A vaga ([`../references/vaga-verx.md`](../references/vaga-verx.md)) cobra explicitamente *"Saber utilizar alguma ferramenta/processo de Integração e Entrega Contínua (preferencialmente GitLab, Jenkins ou AzureDevops) há pelo menos 2 anos"*. Sem pipeline visível, o avaliador não tem evidência de que o candidato sabe operar CI — só consciência de que existe.

O repositório está no GitHub. As três opções viáveis para CI eram:

| Opção | Por que considerada | Por que descartada |
|---|---|---|
| **GitHub Actions** | Nativo do GitHub, gratuito para repos públicos, YAML simples | — (escolhida) |
| Azure DevOps Pipelines | Citada na vaga | Requer outro tenant; complexidade desnecessária para um repo público no GitHub |
| Jenkins | Citado na vaga | Auto-hospedado; inviável para um teste técnico |
| GitLab CI | Citado na vaga | Repo está no GitHub |

## Decisão

Adotar **GitHub Actions** com um único workflow `.github/workflows/ci.yml` que dispara em `push: main`, `pull_request: main` e `workflow_dispatch` (manual). Pipeline:

1. **Checkout** do repositório.
2. **Setup .NET 10** (SDK declarado em [ADR-014](adr-014-dotnet-10.md)).
3. **Cache NuGet** chaveado por hash dos `*.csproj` — acelera runs subsequentes.
4. **Restore + Build** em configuração `Release`.
5. **Test em 3 etapas separadas** (para que falhas sejam atribuídas à suíte certa):
   - `CashFlow.UnitTests` (24 testes, ~70ms)
   - `CashFlow.Architecture.Tests` (8 fitness functions, ~130ms)
   - `CashFlow.Bdd.Tests` (6 cenários Reqnroll pt-BR, ~60ms)
6. **Upload de TRX + cobertura** como artifact (retenção 7 dias) — permite inspecionar resultados de runs antigos.

### O que NÃO está no CI

- **Testes de carga (NBomber)** — exigem stack completa via `docker compose up` (Postgres + RabbitMQ + 2 APIs). Documentado para execução local em [ADR-019](adr-019-load-test-nbomber.md). Adicionar ao CI exigiria service containers, healthchecks com retry, e ~3min de runtime — não vale o custo p/ um teste técnico.
- **Build de imagens Docker** — fora do escopo do MVP; evolução natural com `docker/build-push-action`.
- **Análise estática (SonarCloud, CodeQL)** — evolução natural; cobertura cresce sem custo se ligados depois.

## Trade-offs

| Ganha | Perde |
|---|---|
| Badge "CI passing" visível no README — evidência empírica de que tudo compila e os testes passam | Build em PR roda apenas o que está no workflow — não substitui validação local antes do push |
| 3 jobs separados por suíte = mensagem de erro nomeada (`Test (BDD)` falhou) em vez de "algum teste falhou" | Cada suíte recompila — mitigado por `--no-build` após o passo de Build |
| Cache de NuGet por hash de csproj — primeiro run ~2min, runs seguintes ~40s | Sem matrix de OS — só Linux. Adicionar Windows é trivial mas duplica custo. |
| TRX como artifact — permite triagem assíncrona de flaky tests | TRX cru é difícil de ler — evolução: publicar via `dorny/test-reporter` |
| Permissões mínimas (`contents: read`, `checks: write`, `pull-requests: write`) — princípio do menor privilégio | — |

## Verificação

- **Localmente:** simular o pipeline com `dotnet build CashFlow.sln -c Release && dotnet test CashFlow.sln -c Release`.
- **No GitHub:** badge "CI" no topo do README muda de cor a cada commit.

## Evolução

- **Matrix `ubuntu` + `windows`** quando relevante (provável, dado que o ambiente do avaliador é Windows).
- **`dorny/test-reporter`** para inline annotations de teste no PR.
- **CodeQL** + **dependabot** (gratuitos no GitHub para repos públicos).
- **`docker/build-push-action`** publicando imagens em GHCR a cada tag.
- **Stryker.NET** rodando em job separado (mais lento) — `workflow_dispatch` ou cron noturno.

## ADRs relacionadas

- [ADR-014](adr-014-dotnet-10.md) — SDK alvo do CI.
- [ADR-012](adr-012-architecture-tests.md) — fitness functions rodam aqui.
- [ADR-017](adr-017-bdd-reqnroll.md) — cenários BDD rodam aqui.
- [ADR-019](adr-019-load-test-nbomber.md) — explica por que load test fica fora do CI.
