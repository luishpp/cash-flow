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

    private static readonly System.Reflection.Assembly BalanceAssembly =
        typeof(CashFlow.Balance.API.Domain.Entities.DailyBalance).Assembly;

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
        var result = Types.InAssembly(BalanceAssembly)
            .That().ResideInNamespace("CashFlow.Balance.API.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(
                "CashFlow.Balance.API.Application",
                "CashFlow.Balance.API.Infrastructure",
                "CashFlow.Balance.API.Controllers",
                "CashFlow.Balance.API.Consumers",
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
}
