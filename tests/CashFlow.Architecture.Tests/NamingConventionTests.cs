using FluentAssertions;
using NetArchTest.Rules;

namespace CashFlow.Architecture.Tests;

public class NamingConventionTests
{
    private static readonly System.Reflection.Assembly TransactionsAssembly =
        typeof(CashFlow.Transactions.API.Domain.Entities.Transaction).Assembly;

    private static readonly System.Reflection.Assembly BalanceAssembly =
        typeof(CashFlow.Balance.API.Domain.Entities.DailyBalance).Assembly;

    [Fact]
    public void Transactions_Repositories_MustEndWith_Repository()
    {
        var result = Types.InAssembly(TransactionsAssembly)
            .That().ResideInNamespace("CashFlow.Transactions.API.Infrastructure.Repositories")
            .And().AreClasses()
            .And().ArePublic()
            .And().AreNotNested()
            .And().DoNotHaveNameStartingWith("<>")
            .Should().HaveNameEndingWith("Repository")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Public classes in Transactions.Infrastructure.Repositories must end with 'Repository': {0}",
            string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void Balance_Repositories_MustEndWith_Repository()
    {
        var result = Types.InAssembly(BalanceAssembly)
            .That().ResideInNamespace("CashFlow.Balance.API.Infrastructure.Repositories")
            .And().AreClasses()
            .And().ArePublic()
            .And().AreNotNested()
            .And().DoNotHaveNameStartingWith("<>")
            .Should().HaveNameEndingWith("Repository")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Public classes in Balance.Infrastructure.Repositories must end with 'Repository': {0}",
            string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void Transactions_RepositoryInterfaces_MustStartWith_I()
    {
        var result = Types.InAssembly(TransactionsAssembly)
            .That().ResideInNamespace("CashFlow.Transactions.API.Infrastructure.Repositories")
            .And().AreInterfaces()
            .Should().HaveNameStartingWith("I")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
