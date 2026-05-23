# ADR-020: Testes de mutação com Stryker.NET sobre o Domain

**Status:** Aceita

## Contexto

A vaga ([`../references/vaga-verx.md`](../references/vaga-verx.md)) lista *"testes de mutação"* entre os critérios — junto com unitário, integração e carga. Testes unitários **passando** dizem que o código funciona para os casos escritos; **mutação** mede o oposto: se os testes capturariam regressões reais.

A solução já tem 24 testes unitários de domínio (`Transaction`, `Money`, `DailyBalance`, etc.). A pergunta é: **se alguém trocar `>` por `>=` em uma invariante, os testes pegariam?** Sem mutação, não há resposta empírica.

Duas ferramentas viáveis:

| Ferramenta | Por que considerada | Trade-off |
|---|---|---|
| **Stryker.NET** | Padrão de mercado para .NET; ativa, MIT, integração CLI/CI fluida | — (escolhida) |
| NCrunch (mutation mode) | Pago, IDE-integrated | Licenciamento e dependência de IDE |

## Decisão

Adotar **Stryker.NET 4.14** como **dotnet local tool** (`.config/dotnet-tools.json`), com config dedicada em `tests/CashFlow.UnitTests/stryker-config.json`. Rodado **manualmente** via `workflow_dispatch` (não bloqueia PRs).

### Escopo da mutação

| Camada | Mutado? | Por quê |
|---|---|---|
| `**/Domain/**/*.cs` | ✅ | Onde mora invariante de negócio (factory methods, `ApplyCredit`, validações) |
| `**/Domain/Exceptions/*.cs` | ❌ | Tipos sem lógica — mutações geram só ruído |
| `Infrastructure/`, `Controllers/`, `Application/` | ❌ | Mutar SQL/HTTP gera falsos positivos; o que queremos é mutar **regra**, não **encanamento** |

### Métodos ignorados

`ToString`, `GetHashCode`, `Equals` — mutações neles raramente refletem regressão real de domínio.

### Thresholds

| Métrica | Valor |
|---|---|
| `high` (verde no report) | 85% |
| `low` (amarelo) | 70% |
| `break` (build falha) | 70% |

Domain pequeno, com testes diretos contra factory methods e métodos de negócio — 85% é meta razoável. Abaixo de 70% indica que a suíte unitária deixa passar mutações triviais.

### Execução

**Local (durante desenvolvimento):**

```bash
dotnet tool restore                           # primeira vez
cd tests/CashFlow.UnitTests
dotnet stryker --project CashFlow.Transactions.API.csproj
dotnet stryker --project CashFlow.Balance.API.csproj
```

Report HTML em `StrykerOutput/<timestamp>/reports/mutation-report.html` — abre no browser, navegação por arquivo, mostra cada mutante (killed/survived/timeout/no-coverage) e o snippet exato.

**CI manual (`workflow_dispatch`):** botão "Run workflow" no GitHub Actions, com input `target` (Transactions / Balance / Both). Reports vão para artifact de 30 dias.

## Por que NÃO está no CI automático (PR/push)

Mutação é cara: para cada mutante, Stryker **recompila** e **roda os testes**. Mesmo com domínio pequeno (~10 arquivos), gera ~100-200 mutantes → ~3-5 minutos por API. Adicionar isso ao `ci.yml` significa:

- ~6-10 minutos extras por PR (somando os 2 APIs).
- Fila de PRs sofre.
- Resultado raramente muda PR a PR — mutação é sinal de **drift de qualidade da suíte**, não de regressão por commit.

Padrão escolhido: **dispatch manual + cron noturno futuro**. Adequado ao perfil da informação que produz.

## Trade-offs

| Ganha | Perde |
|---|---|
| **Cobertura ≠ qualidade da assertion** vira número auditável | Mutação é lenta — não cabe em PR feedback loop |
| Mutantes sobreviventes apontam casos não cobertos por design (não por descuido) | Falsos positivos existem (timeout, equivalent mutants) — exige curadoria periódica |
| Stryker como `local tool` = mesma versão em qualquer máquina; `dotnet tool restore` é a única dependência extra | Stryker 4.x ainda evolui APIs entre minor releases — pinning é importante |
| Report HTML é navegável — review compartilhado entre time | HTML não cabe inline em PR; depende de artifact download |
| Workflow `workflow_dispatch` separado = zero impacto na pipeline principal | Quem nunca clicar no botão nunca vê a métrica — requer cultura ou cron noturno |

## Validação

```bash
dotnet tool restore                                           # garante Stryker disponível
cd tests/CashFlow.UnitTests
dotnet stryker --project CashFlow.Transactions.API.csproj    # mutation score esperado ≥ 70%
```

Saída relevante:

```text
Mutation score: XX,XX%
Killed:    NN
Survived:  MM
Timeout:   K
```

Se `Mutation score < break (70%)`, o tool retorna exit code ≠ 0 e o job CI falha.

## Caminho de evolução

- **Cron noturno** (`schedule:` no workflow) — relatório diário sem clique manual.
- **`--since` no PR** — mutar só arquivos alterados desde `main` (Stryker suporta) → cabe em CI sem o custo total.
- **Dashboard Stryker** (`reporters: ["dashboard"]`) — histórico de mutation score na nuvem grátis para repos públicos.
- **Expansão de escopo** para `Application/` quando ela tiver lógica relevante (hoje só orquestra).

## ADRs relacionadas

- [ADR-009](adr-009-rich-domain-model.md) — Rich Domain Model concentra lógica no Domain; mutação rende mais quando código tem invariantes claras.
- [ADR-012](adr-012-architecture-tests.md) — fitness functions garantem estrutura; mutação garante **comportamento**.
- [ADR-018](adr-018-github-actions-ci.md) — explica por que CI principal não inclui mutação.
