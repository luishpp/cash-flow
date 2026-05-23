# ADR-012: Testes de Arquitetura com NetArchTest

**Status:** Aceita

## Contexto

O desafio cobra *"boas práticas (SOLID, padrões arquiteturais)"* e o **RNF-08 (Manutenibilidade)** exige que a Clean Architecture interna não degrade ao longo do tempo. Testes manuais de arquitetura (code review) falham silenciosamente — um PR pode adicionar `using Npgsql;` no Domain e ninguém percebe. Fitness functions resolvem isso automatizando a validação no CI.

## Decisão

Adotar **NetArchTest.Rules** no projeto `CashFlow.Architecture.Tests`, com bateria mínima de testes que falham o build se alguma regra for violada.

## Por que NetArchTest (e não ArchUnitNET)

| Critério | NetArchTest.Rules | ArchUnitNET |
|---|---|---|
| Maturidade no ecossistema | Alta (referência da Microsoft Architecture Guides) | Crescente |
| API fluente | Excelente — `Types.InAssembly(...).That()...ShouldNot()...` | Mais verbosa |
| Curva de aprendizado | Baixa | Média |
| Performance | Rápida (reflection direta) | Mais lenta em assemblies grandes |

NetArchTest é a opção mais estabelecida e idiomática para .NET; cobre 100% das regras do escopo do desafio.

## Bateria de testes do MVP

```csharp
// LayerDependencyTests
[Fact]
public void Transactions_Domain_MustNotDependOn_Infrastructure_Application_orAspNetCore()
{
    var result = Types.InAssembly(typeof(Transaction).Assembly)
        .That().ResideInNamespace("CashFlow.Transactions.API.Domain")
        .ShouldNot()
        .HaveDependencyOnAny(
            "CashFlow.Transactions.API.Application",
            "CashFlow.Transactions.API.Infrastructure",
            "Microsoft.AspNetCore",
            "Dapper",
            "Npgsql",
            "MassTransit")
        .GetResult();

    result.IsSuccessful.Should().BeTrue();
}

// ImmutabilityTests (via reflection — NetArchTest 1.3.2 não tem helper específico)
[Fact]
public void Transactions_Entities_MustNotHavePublicSetters() { /* ... */ }

// NamingConventionTests
[Fact]
public void Transactions_Repositories_MustEndWith_Repository()
{
    var result = Types.InAssembly(typeof(TransactionRepository).Assembly)
        .That().ResideInNamespace("CashFlow.Transactions.API.Infrastructure.Repositories")
        .And().AreClasses().And().ArePublic().And().AreNotNested()
        .Should().HaveNameEndingWith("Repository")
        .GetResult();
    result.IsSuccessful.Should().BeTrue();
}
```

Total: **8 testes de arquitetura** cobrindo dependency rule, immutability e naming conventions.

## Trade-offs

| Ganha | Perde |
|---|---|
| Arquitetura validada em CI — degradação detectada no PR, não em produção | Testes precisam ser mantidos quando a arquitetura evolui legitimamente |
| Documentação executável das regras | False positives ocasionais (ex: nested DTOs precisam ser excluídos por `AreNotNested()`) |
| Onboarding: novo dev sente o feedback imediato | Setup inicial requer disciplina na criação dos testes |
| Demonstra fitness functions — vocabulário arquitetural avançado | — |

## Por que isso atende o RNF-08 (Manutenibilidade) explicitamente

A regra de Clean Architecture *"Domain não depende de Infrastructure"* é ineficaz se for apenas convenção. Com NetArchTest, ela vira **invariante verificada** — equivalente a um teste unitário, mas para a estrutura do código. Inversão de dependência deixa de ser "esperança no code review".

## Alternativa descartada

**Validação apenas em code review** — não escala, falha silenciosamente, depende da atenção do revisor. Manter regra arquitetural em PR template (markdown) tem o mesmo problema.

## ADRs relacionadas

- [ADR-009](adr-009-rich-domain-model.md) — fitness functions verificam o encapsulamento prometido
- [ADR-010](adr-010-dapper.md) — convenção de parameterização pode ser fitness function adicional
