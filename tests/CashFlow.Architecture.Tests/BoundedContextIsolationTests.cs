using FluentAssertions;
using NetArchTest.Rules;

namespace CashFlow.Architecture.Tests;

/// <summary>
/// Fitness functions de **isolamento entre Bounded Contexts** (ADR-026, ADR-027, ADR-028).
///
/// Regra fundamental: BCs comunicam-se SÓ via eventos no Shared Kernel.
/// Acoplamento direto entre BCs (using estranho) é violação que deve quebrar o build.
///
/// Cada BC pode depender de CashFlow.Shared (kernel mínimo: eventos + JWT).
/// Nenhum BC pode depender diretamente de outro BC.
///
/// Trade-off articulado: testes verificam **dependência ao nível de namespace**, não de assembly,
/// porque os tests project obviamente referencia todos os assemblies. NetArchTest valida que
/// nenhum tipo dentro de um BC importa/usa tipos de outro BC — equivalente ao que a regra de
/// dependência das ADRs garante por design.
/// </summary>
public class BoundedContextIsolationTests
{
    private static readonly System.Reflection.Assembly IdentityAssembly =
        typeof(CashFlow.Identity.API.Domain.Entities.AppUser).Assembly;

    private static readonly System.Reflection.Assembly TransactionsAssembly =
        typeof(CashFlow.Transactions.API.Domain.Entities.Transaction).Assembly;

    private static readonly System.Reflection.Assembly BalanceApiAssembly =
        typeof(CashFlow.Balance.API.Application.Services.BalanceQueryService).Assembly;

    private static readonly System.Reflection.Assembly BalanceWorkerAssembly =
        typeof(CashFlow.Balance.Worker.Consumers.TransactionConsumer).Assembly;

    private static readonly System.Reflection.Assembly BalanceCoreAssembly =
        typeof(CashFlow.Balance.Core.Domain.Entities.DailyBalance).Assembly;

    [Fact]
    public void Identity_MustNotDependOn_Transactions_Balance_or_Admin()
    {
        // Identity é BC próprio — não conhece nenhum outro contexto.
        var result = Types.InAssembly(IdentityAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "CashFlow.Transactions.API",
                "CashFlow.Balance.API",
                "CashFlow.Balance.Worker",
                "CashFlow.Balance.Core",
                "CashFlow.Admin.API")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Identity.API não pode depender de outros BCs (acoplamento cross-BC): {0}",
            string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void Transactions_MustNotDependOn_Identity_Balance_or_Admin()
    {
        // Transactions é write side puro — comunica com Balance só via evento no Shared Kernel.
        var result = Types.InAssembly(TransactionsAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "CashFlow.Identity.API",
                "CashFlow.Balance.API",
                "CashFlow.Balance.Worker",
                "CashFlow.Balance.Core",
                "CashFlow.Admin.API")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Transactions.API não pode depender de outros BCs: {0}",
            string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void BalanceApi_MustNotDependOn_Identity_Transactions_or_Admin()
    {
        // Balance.API depende SÓ de Balance.Core (shared kernel intra-BC) + Shared.
        var result = Types.InAssembly(BalanceApiAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "CashFlow.Identity.API",
                "CashFlow.Transactions.API",
                "CashFlow.Admin.API")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Balance.API não pode depender de outros BCs: {0}",
            string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void BalanceWorker_MustNotDependOn_Identity_Transactions_BalanceApi_or_Admin()
    {
        // Balance.Worker é deploy unit independente — depende SÓ de Balance.Core + Shared (eventos).
        var result = Types.InAssembly(BalanceWorkerAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "CashFlow.Identity.API",
                "CashFlow.Transactions.API",
                "CashFlow.Balance.API",
                "CashFlow.Admin.API")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Balance.Worker não pode depender de outros BCs ou da Balance.API: {0}",
            string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void BalanceCore_MustNotDependOn_AnyOtherAssembly()
    {
        // Balance.Core é o coração do BC — nada além de Dapper/Npgsql/BCL.
        // Não pode nem depender de Shared (eventos), pois Core é puro domain + persistence.
        var result = Types.InAssembly(BalanceCoreAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "CashFlow.Identity.API",
                "CashFlow.Transactions.API",
                "CashFlow.Balance.API",
                "CashFlow.Balance.Worker",
                "CashFlow.Admin.API",
                "CashFlow.Shared")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Balance.Core deve ser shared kernel intra-BC puro: {0}",
            string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }
}
