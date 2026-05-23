using CashFlow.Transactions.API.Domain.Exceptions;
using CashFlow.Transactions.API.Domain.ValueObjects;
using FluentAssertions;

namespace CashFlow.UnitTests.Transactions.Domain;

public class MoneyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100.50)]
    public void From_WithAmountLessThanOrEqualZero_ThrowsDomainException(decimal amount)
    {
        var act = () => Money.From(amount);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void From_WithMoreThanTwoDecimalPlaces_ThrowsDomainException()
    {
        var act = () => Money.From(10.123m);
        act.Should().Throw<DomainException>().WithMessage("*2 casas decimais*");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10.5)]
    [InlineData(100.99)]
    [InlineData(0.01)]
    public void From_WithValidAmount_ReturnsInstance(decimal amount)
    {
        var m = Money.From(amount);
        m.Amount.Should().Be(amount);
    }

    [Fact]
    public void ImplicitConversion_ToDecimal_ReturnsAmount()
    {
        Money m = Money.From(42.50m);
        decimal v = m;
        v.Should().Be(42.50m);
    }
}
