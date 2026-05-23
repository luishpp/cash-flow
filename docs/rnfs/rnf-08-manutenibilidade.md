# RNF-08 — Manutenibilidade

**Origem:** Derivado *(combinação de "Objetivo do Desafio" e cobranças de "boas práticas" e "documentação" do PDF)*.

## Declaração

> "Documentação: Registre decisões arquiteturais, diagramas e fluxos de dados. Isso facilita a comunicação e a manutenção." + "Boas práticas são bem vindas (Design Patterns, Padrões de Arquitetura, SOLID e etc)."

Manutenibilidade aqui significa: **a arquitetura deve continuar coerente conforme o time evolui o código**.

## Decisões arquiteturais que atendem

| ADR | Como contribui |
|---|---|
| [ADR-009 — Rich Domain Model](../adrs/adr-009-rich-domain-model.md) | Encapsulamento garante invariantes — código que viola é difícil de escrever sem perceber. |
| [ADR-012 — Testes de Arquitetura](../adrs/adr-012-architecture-tests.md) | Fitness functions validam a Dependency Rule em CI — degradação detectada no PR. |
| [ADR-015 — Application Services (sem MediatR)](../adrs/adr-015-application-services-no-mediatr.md) | Stack-trace honesto sem reflection; onboarding em 5 min. |
| [ADR-017 — BDD com Reqnroll](../adrs/adr-017-bdd-reqnroll.md) | Feature files pt-BR = documentação executável legível por não-devs. |
| [ADR-018 — CI GitHub Actions](../adrs/adr-018-github-actions-ci.md) | Pipeline roda as 3 suítes a cada PR — degradação detectada antes do merge. |
| [ADR-020 — Stryker mutation testing](../adrs/adr-020-stryker-mutation-testing.md) | Mede **qualidade das assertions**, não só cobertura. Break threshold = 70%. |
| [ADR-022 — BDD E2E via WebApplicationFactory](../adrs/adr-022-bdd-e2e-webapplicationfactory.md) | Cenários BDD que tocam HTTP/DB reais — pegam regressões que unit + mutation não pegam. |

## Cobertura no MVP

**Total.** O que está em vigor:

- ✅ **ADRs documentadas** ([../adrs/](../adrs/)) — 24 decisões com contexto, trade-offs, alternativas descartadas.
- ✅ **RNFs documentados** ([./](./)) — 9 RNFs com origem, decisões que atendem, verificação.
- ✅ **Diagramas C4** ([../diagrams/](../diagrams/)) — Contexto, Containers, Componentes em Mermaid.
- ✅ **Testes de arquitetura** (`CashFlow.Architecture.Tests`) — 8 fitness functions validando Clean Architecture.
- ✅ **Testes unitários** (`CashFlow.UnitTests`) — 85 testes cobrindo Rich Domain (entidades + value objects + AppUser + RefreshToken).
- ✅ **Testes BDD de domínio** (`CashFlow.Bdd.Tests/Features/SaldoConsolidado.feature`) — 6 cenários Reqnroll pt-BR.
- ✅ **Testes BDD E2E** (`CashFlow.Bdd.Tests/Features/AutenticacaoE2E.feature`) — 9 cenários via WebApplicationFactory + Testcontainers Postgres (login/auth + lockout + refresh rotation + logout).
- ✅ **Testes de mutação** (Stryker.NET) — mutation score **91.09% (Transactions)** e **100% (Balance)** sobre o Domain; break threshold de 70%.
- ✅ **CI** (GitHub Actions) — build + 3 suítes a cada PR; cache NuGet; artifacts de teste.
- ✅ **Convenções de nomenclatura** — repositórios terminam com `Repository`, interfaces começam com `I`, métodos async terminam com `Async` (validado em testes).

## Trade-off aceito

Tempo upfront em ADRs e testes de arquitetura — em troca de evitar retrabalho quando o código degrada silenciosamente.

## Verificação

```bash
# Testes de arquitetura
dotnet test ./tests/CashFlow.Architecture.Tests
# Deve passar 8/8.

# Testes unitários
dotnet test ./tests/CashFlow.UnitTests
# Deve passar 85/85.

# Testes BDD (domínio + E2E via WebApplicationFactory/Testcontainers)
dotnet test ./tests/CashFlow.Bdd.Tests
# Deve passar 15/15 (6 domínio + 9 E2E). Requer Docker.

# Mutação (Stryker) — local; também disponível em workflow_dispatch
dotnet tool restore
cd tests/CashFlow.UnitTests
dotnet stryker --project CashFlow.Transactions.API.csproj    # ≥85% (atual: 91.09%)
dotnet stryker --project CashFlow.Balance.API.csproj         # ≥85% (atual: 100%)

# CI (GitHub Actions): build + 3 suítes a cada PR; falha bloqueia merge.
```

## Evolução

- **Cron noturno do Stryker** (`schedule:` no workflow) — mutation score sem clique manual.
- **`stryker --since main`** — mutar só arquivos alterados no PR; cabe em CI sem o custo total.
- **Roslyn analyzers** customizados — para regras que NetArchTest não consegue expressar (ex: detectar uso de `Console.WriteLine` em produção).
- **CodeQL + Dependabot** (gratuitos no GitHub para repos públicos) — análise estática e atualização de vulnerabilidades.
- **SonarCloud** com quality gates: cobertura mínima, complexidade ciclomática, code smells.
- **Fitness functions adicionais**: acoplamento entre módulos, complexidade ciclomática, dependência circular.
- **Architecture Decision Logs** (ADLs) para registrar decisões revisadas (ex: ADR-003 e ADR-004 já foram revisadas neste projeto).
