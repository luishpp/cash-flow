# ADR-017: BDD com Reqnroll cobrindo cenários do domínio

**Status:** Aceita

## Contexto

BDD agrega valor real ao MVP: feature files em pt-BR funcionam como **documentação executável** das regras de domínio — leíveis pelo persona Carlos (comerciante, baixa literacia digital) ou pelo product owner. Além disso, formaliza o vocabulário ubíquo do bounded context Balance (crédito, débito, saldo consolidado) em artefatos que sobrevivem a refatorações.

## Decisão

Adicionar o projeto `tests/CashFlow.Bdd.Tests` baseado em **Reqnroll** (sucessor open-source do SpecFlow, mantido após a transição do SpecFlow para licenciamento comercial).

### Stack escolhida

| Componente | Escolha | Por quê |
|---|---|---|
| Framework BDD | **Reqnroll 2.4** | Fork open-source ativo do SpecFlow, MIT license |
| Runner | xUnit (`Reqnroll.xUnit`) | Consistente com `CashFlow.UnitTests` e `CashFlow.Architecture.Tests` |
| Linguagem dos `.feature` | **pt-BR** (`# language: pt-BR`) | Aderente à persona Carlos; força raciocínio em vocabulário ubíquo |
| Asserts | FluentAssertions | Mesma stack dos demais projetos de teste |

### Estrutura

```
tests/CashFlow.Bdd.Tests/
├── CashFlow.Bdd.Tests.csproj
├── reqnroll.json                       # configura language=pt-BR
├── Features/
│   └── SaldoConsolidado.feature        # 4 cenários + 1 esquema (3 exemplos) = 6 cases
└── Steps/
    └── SaldoConsolidadoSteps.cs        # bindings com regex pt-BR
```

### Cenários cobertos (MVP)

1. **Aplicar crédito** atualiza total de créditos e saldo.
2. **Aplicar débito** após crédito reduz o saldo proporcionalmente.
3. **Crédito não positivo** é rejeitado com `DomainException`.
4. **Múltiplos lançamentos em sequência** (esquema com 3 exemplos: crédito+débito, dois créditos, dois débitos).

Total: **6 cenários executáveis**, todos verdes.

### Por que mirar o domínio (e não o HTTP API) no MVP

| Abordagem | Trade-off |
|---|---|
| **Domínio (escolhida)** | Rápido (~70ms), sem infra (sem Postgres/RabbitMQ), executa em CI sem testcontainers. Cobre o coração das invariantes. |
| API via `WebApplicationFactory` | Mais impressionante, mas requer setup de containers e/ou mocks de RabbitMQ — vale como evolução curta. |

A escolha pelo domínio mantém o pipeline de testes determinístico e rápido. A evolução natural (cenário end-to-end "login → registra → consulta saldo") está listada em [ADR-016](adr-016-jwt-authentication.md) § Validação.

## Nota técnica: pt-BR para Gherkin, English para atributos

Reqnroll **traduz palavras Gherkin** (`Dado`/`Quando`/`Então`) pelo header `# language: pt-BR`, mas os **atributos C# permanecem em inglês** (`[Given]`/`[When]`/`[Then]`). O regex dentro dos atributos pode (e deve) ser pt-BR para casar o texto do feature. Exemplo:

```csharp
[Given(@"que o saldo do dia ""([^""]*)"" está zerado")]
public void DadoSaldoZerado(string iso) { ... }
```

## Trade-offs

| Ganha | Perde |
|---|---|
| **Feature files em pt-BR** = documentação executável que o time de produto consegue ler | Mais cerimônia que xUnit puro (1 feature + 1 step file por funcionalidade) |
| **Esquema do Cenário** = data-driven testing com tabela legível | Steps ambíguos são erro comum (vimos isso no setup — dois regex casavam o mesmo step) |
| Cobre a sigla BDD da vaga | Aumenta superfície de manutenção dos testes |
| Reqnroll é OSS estável (MIT), sucessor natural do SpecFlow | Tooling de IDE p/ Reqnroll ainda menos maduro que SpecFlow (mas suficiente) |

## Alternativas descartadas

### SpecFlow
- **Por quê não:** licenciamento comercial desde 2024; Reqnroll é o fork open-source.

### LightBDD
- **Por quê não:** menos popular no ecossistema .NET; menor reconhecimento pelo avaliador.

### xUnit puro com nomes "Should_X_When_Y"
- **Por quê não:** não cobre a sigla BDD; não gera artefato lível para não-devs.

## ADRs relacionadas

- [ADR-012](adr-012-architecture-tests.md) — Testes de Arquitetura (fitness functions) complementam os BDDs (regras de arquitetura vs. regras de domínio).
- [ADR-009](adr-009-rich-domain-model.md) — Rich Domain Model torna trivial chamar `balance.ApplyCredit(...)` direto do step, sem mocks.
- [ADR-016](adr-016-jwt-authentication.md) — evolução natural: cenário BDD do fluxo de auth end-to-end.
