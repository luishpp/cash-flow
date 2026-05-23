using CashFlow.Transactions.API.Domain.Exceptions;
using CashFlow.Transactions.API.Domain.ValueObjects;
using FluentAssertions;

namespace CashFlow.UnitTests.Transactions.Domain;

public class MovementDateTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public void From_Today_Succeeds()
    {
        var d = MovementDate.From(Today);
        d.Value.Should().Be(Today);
    }

    [Fact]
    public void From_PastDate_Succeeds()
    {
        var past = Today.AddDays(-30);
        var d = MovementDate.From(past);
        d.Value.Should().Be(past);
    }

    [Fact]
    public void From_FarPastDate_Succeeds()
    {
        var ancient = new DateOnly(1970, 1, 1);
        var d = MovementDate.From(ancient);
        d.Value.Should().Be(ancient);
    }

    [Fact]
    public void From_Tomorrow_ThrowsDomainException()
    {
        var tomorrow = Today.AddDays(1);
        var act = () => MovementDate.From(tomorrow);
        act.Should().Throw<DomainException>()
            .WithMessage($"*{tomorrow:yyyy-MM-dd}*futura*");
    }

    [Fact]
    public void From_FarFutureDate_ThrowsDomainException()
    {
        var future = Today.AddYears(1);
        var act = () => MovementDate.From(future);
        act.Should().Throw<DomainException>().WithMessage("*futura*");
    }

    [Fact]
    public void ImplicitConversion_ToDateOnly_ReturnsValue()
    {
        var movement = MovementDate.From(new DateOnly(2026, 1, 15));
        DateOnly date = movement;
        date.Should().Be(new DateOnly(2026, 1, 15));
    }
}
