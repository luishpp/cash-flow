# ADR-014: Runtime .NET 10 (LTS)

**Status:** Aceita *(revisada — versão inicial usava .NET 8)*

## Contexto

O desafio exige .NET / C# como requisito obrigatório, mas **não especifica versão**. No momento da revisão desta decisão (maio/2026), três versões eram tecnicamente viáveis:

| Versão | Tipo | Lançada | Fim de suporte | Observação |
|---|---|---|---|---|
| **.NET 8** | **LTS** (3 anos) | Nov/2023 | **Nov/2026** | Apenas ~6 meses de runway restantes |
| **.NET 9** | **STS** (18 meses) | Nov/2024 | **Mai/2026** | Sai de suporte neste mês |
| **.NET 10** | **LTS** (3 anos) | Nov/2025 | **Nov/2028** | LTS atual; ~2,5 anos de runway |

> **Política de release Microsoft:** versões **pares** (8, 10, 12) são **LTS** com 3 anos de suporte; versões **ímpares** (7, 9, 11) são **STS** com 18 meses. STS é orientada a quem quer testar novidades — não para sistemas em produção com ciclos de manutenção longos.

A versão original deste projeto adotou .NET 8 — defensável no momento, porém com janela de suporte que expira em ~6 meses. A revisão se justifica pela proximidade do EOL e pela necessidade de minimizar dívida técnica logo após entrega.

## Decisão

Adotar **.NET 10 (LTS)** como runtime para todos os projetos:

- `CashFlow.Transactions.API` — `<TargetFramework>net10.0</TargetFramework>`
- `CashFlow.Balance.API` — `<TargetFramework>net10.0</TargetFramework>`
- `CashFlow.Shared` — `<TargetFramework>net10.0</TargetFramework>`
- `CashFlow.UnitTests` — `<TargetFramework>net10.0</TargetFramework>`
- `CashFlow.Architecture.Tests` — `<TargetFramework>net10.0</TargetFramework>`

Dockerfiles atualizados para `mcr.microsoft.com/dotnet/sdk:10.0` (build) e `mcr.microsoft.com/dotnet/aspnet:10.0` (runtime).

## Por que .NET 10 (e não 8 ou 9)

| Critério | .NET 8 | .NET 9 | .NET 10 |
|---|---|---|---|
| Tipo de suporte | LTS | **STS** ❌ | LTS ✅ |
| Tempo restante (mai/2026) | ~6 meses ⚠️ | EOL agora ❌ | ~2,5 anos ✅ |
| Adequado a ambiente regulado | Sim (mas migrar logo) | Não — política comum bloqueia STS | **Sim** ✅ |
| Maturidade do ecossistema | Muito alta (2+ anos) | Alta | Alta (~6 meses) |
| Features modernas | Boas | Mais | **Mais ainda** (LINQ, primary ctors, perf) |

A combinação **LTS + janela longa + ecossistema maduro o suficiente** torna .NET 10 a escolha mais defensível em mai/2026.

## Por que NÃO .NET 9 (descarte direto)

- **STS de 18 meses** — incompatível com ciclos de change management em ambientes financeiros/regulados (onde a vaga Verx provavelmente está alocada).
- A Microsoft posiciona STS como **preview da próxima LTS** — para quem quer testar features que aparecerão no .NET 10 (par seguinte).
- Comitês de arquitetura tipicamente **bloqueiam STS por política** — citá-lo demonstra entendimento de governance corporativa.

## Por que NÃO permanecer em .NET 8

- **EOL em Nov/2026** — ~6 meses de runway no momento da decisão; obrigaria migração logo após entrega.
- Não há feature de .NET 8 que torne a migração arriscada — toda a stack (Dapper, MassTransit, Polly, Npgsql, Serilog) tem pacotes estáveis e testados em .NET 10.
- A migração é **mecanicamente trivial**: trocar `TargetFramework` em 5 csproj + atualizar base images em 2 Dockerfiles. Custo total: minutos.

## Trade-offs

| Ganha | Perde |
|---|---|
| Suporte estendido até Nov/2028 — runway compatível com ciclo de vida típico do sistema | Dependências externas precisam ter builds .NET 10 (todas tinham — verificado) |
| Acesso a melhorias de performance e LINQ do .NET 10 | ~6 meses de exposição a bugs ainda não cobertos por SP1/SP2 (mitigado pela cobertura de testes existente: 32 testes) |
| Discurso de arquitetura mais defensível em sabatina (LTS mais recente) | — |
| Demonstra disciplina de gestão de runtime (não ficar em LTS antigo "por inércia") | — |

## Validação

A migração foi validada por:

1. **Build limpo**: `dotnet build` → 0 warnings, 0 errors em todos os 5 projetos.
2. **Testes**: `dotnet test` → 24 unitários + 8 de arquitetura, 32/32 passando.
3. **Pacotes**: nenhum precisou ser atualizado — todos já tinham builds compatíveis com .NET 10.

## Estratégia de upgrade futura

Estabelecemos o padrão de migrar para a **próxima LTS** ao final do suporte da atual:

- **Nov/2028** (EOL .NET 10) → migrar para **.NET 12 LTS** (lançada Nov/2027).
- Janela de overlap de ~12 meses entre lançamento da nova LTS e EOL da anterior permite migração planejada.
- Versões STS intermediárias (.NET 11) **não são adotadas** — coerente com política conservadora para ambiente regulado.

## ADRs relacionadas

- [ADR-008](adr-008-docker-compose.md) — base images Docker foram atualizadas em conjunto.
- Todas as outras ADRs herdam essa escolha de runtime; nenhuma muda de mérito.
