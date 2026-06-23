using FluentAssertions;
using NetArchTest.Rules;

namespace CashFlow.Architecture.Tests;

/// <summary>
/// Fitness functions de Clean Architecture (ADR-012).
/// Garantem que a regra de dependência (Domain → ... → Infrastructure) não seja violada.
/// </summary>
public class LayerDependencyTests
{
    private static readonly System.Reflection.Assembly TransactionsAssembly =
        typeof(CashFlow.Transactions.API.Domain.Entities.Transaction).Assembly;

    // Após ADR-026: Domain de Balance vive em CashFlow.Balance.Core.
    private static readonly System.Reflection.Assembly BalanceCoreAssembly =
        typeof(CashFlow.Balance.Core.Domain.Entities.DailyBalance).Assembly;

    // Após ADR-027: Identity é BC próprio com Domain.Entities (AppUser, RefreshToken).
    private static readonly System.Reflection.Assembly IdentityAssembly =
        typeof(CashFlow.Identity.API.Domain.Entities.AppUser).Assembly;

    [Fact]
    public void Transactions_Domain_MustNotDependOn_Infrastructure_Application_orAspNetCore()
    {
        var result = Types.InAssembly(TransactionsAssembly)
            .That().ResideInNamespace("CashFlow.Transactions.API.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(
                "CashFlow.Transactions.API.Application",
                "CashFlow.Transactions.API.Infrastructure",
                "CashFlow.Transactions.API.Controllers",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions.Hosting",
                "Microsoft.Extensions.DependencyInjection",
                "Dapper",
                "Npgsql",
                "MassTransit",
                "FluentValidation")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain de Transactions viola a dependency rule: {0}",
            string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void Balance_Domain_MustNotDependOn_Infrastructure_Application_orAspNetCore()
    {
        // Após ADR-026 (Balance.Core): Domain.DailyBalance vive em mesmo assembly
        // que Infrastructure.Persistence + BalanceRepository (Core = Shared Kernel intra-BC).
        // A regra de dependência se mantém via namespace: Domain não pode depender de
        // Infrastructure mesmo coabitando assembly. Validar via ResideInNamespace + HaveDependencyOn.
        var result = Types.InAssembly(BalanceCoreAssembly)
            .That().ResideInNamespace("CashFlow.Balance.Core.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(
                "CashFlow.Balance.Core.Infrastructure",
                "CashFlow.Balance.API",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions.Hosting",
                "Microsoft.Extensions.DependencyInjection",
                "Dapper",
                "Npgsql",
                "MassTransit",
                "FluentValidation")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain de Balance viola a dependency rule: {0}",
            string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void Transactions_Application_MustNotDependOn_Controllers()
    {
        var result = Types.InAssembly(TransactionsAssembly)
            .That().ResideInNamespace("CashFlow.Transactions.API.Application")
            .ShouldNot().HaveDependencyOn("CashFlow.Transactions.API.Controllers")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Identity_Domain_MustNotDependOn_Infrastructure_Application_orAspNetCore()
    {
        var result = Types.InAssembly(IdentityAssembly)
            .That().ResideInNamespace("CashFlow.Identity.API.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(
                "CashFlow.Identity.API.Application",
                "CashFlow.Identity.API.Infrastructure",
                "CashFlow.Identity.API.Controllers",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions.Hosting",
                "Microsoft.Extensions.DependencyInjection",
                "Dapper",
                "Npgsql",
                "Konscious.Security.Cryptography",
                "FluentValidation")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain de Identity viola a dependency rule: {0}",
            string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void Identity_Application_MustNotDependOn_Controllers()
    {
        var result = Types.InAssembly(IdentityAssembly)
            .That().ResideInNamespace("CashFlow.Identity.API.Application")
            .ShouldNot().HaveDependencyOn("CashFlow.Identity.API.Controllers")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
