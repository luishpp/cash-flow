using FluentAssertions;
using NetArchTest.Rules;

namespace CashFlow.Architecture.Tests;

public class NamingConventionTests
{
    private static readonly System.Reflection.Assembly TransactionsAssembly =
        typeof(CashFlow.Transactions.API.Domain.Entities.Transaction).Assembly;

    // Após ADR-026: BC Balance está em 3 deploy units:
    //   - Balance.Core: Domain (DailyBalance) + Persistence + BalanceRepository (Shared Kernel intra-BC)
    //   - Balance.API:  read-only HTTP — sem repositórios próprios
    //   - Balance.Worker: ProcessedEventsRepository + ConsolidationService + Consumer
    private static readonly System.Reflection.Assembly BalanceCoreAssembly =
        typeof(CashFlow.Balance.Core.Domain.Entities.DailyBalance).Assembly;

    private static readonly System.Reflection.Assembly BalanceWorkerAssembly =
        typeof(CashFlow.Balance.Worker.Consumers.TransactionConsumer).Assembly;

    private static readonly System.Reflection.Assembly IdentityAssembly =
        typeof(CashFlow.Identity.API.Domain.Entities.AppUser).Assembly;

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
        // BalanceRepository vive em Core; ProcessedEventsRepository vive em Worker (ADR-026).
        var coreResult = Types.InAssembly(BalanceCoreAssembly)
            .That().ResideInNamespace("CashFlow.Balance.Core.Infrastructure.Repositories")
            .And().AreClasses()
            .And().ArePublic()
            .And().AreNotNested()
            .And().DoNotHaveNameStartingWith("<>")
            .Should().HaveNameEndingWith("Repository")
            .GetResult();

        var workerResult = Types.InAssembly(BalanceWorkerAssembly)
            .That().ResideInNamespace("CashFlow.Balance.Worker.Infrastructure.Repositories")
            .And().AreClasses()
            .And().ArePublic()
            .And().AreNotNested()
            .And().DoNotHaveNameStartingWith("<>")
            .Should().HaveNameEndingWith("Repository")
            .GetResult();

        coreResult.IsSuccessful.Should().BeTrue(
            "Public classes in Balance.Core.Infrastructure.Repositories must end with 'Repository': {0}",
            string.Join(", ", coreResult.FailingTypeNames ?? Enumerable.Empty<string>()));
        workerResult.IsSuccessful.Should().BeTrue(
            "Public classes in Balance.Worker.Infrastructure.Repositories must end with 'Repository': {0}",
            string.Join(", ", workerResult.FailingTypeNames ?? Enumerable.Empty<string>()));
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

    [Fact]
    public void Identity_Repositories_MustEndWith_Repository()
    {
        var result = Types.InAssembly(IdentityAssembly)
            .That().ResideInNamespace("CashFlow.Identity.API.Infrastructure.Repositories")
            .And().AreClasses()
            .And().ArePublic()
            .And().AreNotNested()
            .And().DoNotHaveNameStartingWith("<>")
            .Should().HaveNameEndingWith("Repository")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Public classes in Identity.Infrastructure.Repositories must end with 'Repository': {0}",
            string.Join(", ", result.FailingTypeNames ?? Enumerable.Empty<string>()));
    }

    [Fact]
    public void Identity_RepositoryInterfaces_MustStartWith_I()
    {
        var result = Types.InAssembly(IdentityAssembly)
            .That().ResideInNamespace("CashFlow.Identity.API.Infrastructure.Repositories")
            .And().AreInterfaces()
            .Should().HaveNameStartingWith("I")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
