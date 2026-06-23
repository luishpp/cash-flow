using CashFlow.Balance.Core.Domain.Entities;
using CashFlow.Balance.Core.Domain.Exceptions;
using FluentAssertions;

namespace CashFlow.UnitTests.Balance.Domain;

public class DailyBalanceTests
{
    [Fact]
    public void New_CreatesZeroedBalanceForDate()
    {
        var date = new DateOnly(2026, 5, 22);
        var b = DailyBalance.New(date);

        b.Date.Should().Be(date);
        b.TotalCredits.Should().Be(0m);
        b.TotalDebits.Should().Be(0m);
        b.Balance.Should().Be(0m);
    }

    [Fact]
    public void ApplyCredit_AccumulatesTotalCredits()
    {
        var b = DailyBalance.New(new DateOnly(2026, 5, 22));
        b.ApplyCredit(100m);
        b.ApplyCredit(50.25m);

        b.TotalCredits.Should().Be(150.25m);
        b.Balance.Should().Be(150.25m);
    }

    [Fact]
    public void ApplyDebit_AccumulatesTotalDebits()
    {
        var b = DailyBalance.New(new DateOnly(2026, 5, 22));
        b.ApplyDebit(30m);
        b.ApplyDebit(20m);

        b.TotalDebits.Should().Be(50m);
        b.Balance.Should().Be(-50m);
    }

    [Fact]
    public void Balance_IsDerivedFromCreditsMinusDebits()
    {
        var b = DailyBalance.New(new DateOnly(2026, 5, 22));
        b.ApplyCredit(200m);
        b.ApplyDebit(80m);

        b.Balance.Should().Be(120m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void ApplyCredit_WithAmountLessThanOrEqualZero_ThrowsDomainException(decimal amount)
    {
        var b = DailyBalance.New(new DateOnly(2026, 5, 22));
        var act = () => b.ApplyCredit(amount);
        act.Should().Throw<DomainException>().WithMessage("*crédito*maior que zero*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void ApplyDebit_WithAmountLessThanOrEqualZero_ThrowsDomainException(decimal amount)
    {
        var b = DailyBalance.New(new DateOnly(2026, 5, 22));
        var act = () => b.ApplyDebit(amount);
        act.Should().Throw<DomainException>().WithMessage("*débito*maior que zero*");
    }
}
