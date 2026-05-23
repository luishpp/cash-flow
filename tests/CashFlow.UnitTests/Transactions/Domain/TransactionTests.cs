using CashFlow.Transactions.API.Domain.Entities;
using CashFlow.Transactions.API.Domain.Exceptions;
using CashFlow.Transactions.API.Domain.ValueObjects;
using FluentAssertions;

namespace CashFlow.UnitTests.Transactions.Domain;

public class TransactionTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public void Register_WithValidData_ReturnsValidInstance()
    {
        var t = Transaction.Register(100m, TransactionType.Credit, "Venda do dia", Today);

        t.Should().NotBeNull();
        t.Id.Should().NotBe(Guid.Empty);
        t.Amount.Amount.Should().Be(100m);
        t.Type.Should().Be(TransactionType.Credit);
        t.Description.Should().Be("Venda do dia");
        t.MovementDate.Value.Should().Be(Today);
        t.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Register_WithZeroAmount_ThrowsDomainException()
    {
        var act = () => Transaction.Register(0m, TransactionType.Credit, "Test", Today);
        act.Should().Throw<DomainException>().WithMessage("*maior que zero*");
    }

    [Fact]
    public void Register_WithNegativeAmount_ThrowsDomainException()
    {
        var act = () => Transaction.Register(-10m, TransactionType.Debit, "Test", Today);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Register_WithEmptyDescription_ThrowsDomainException()
    {
        var act = () => Transaction.Register(50m, TransactionType.Credit, "", Today);
        act.Should().Throw<DomainException>().WithMessage("*Descrição*obrigatória*");
    }

    [Fact]
    public void Register_WithTooLongDescription_ThrowsDomainException()
    {
        var longDescription = new string('x', 201);
        var act = () => Transaction.Register(50m, TransactionType.Credit, longDescription, Today);
        act.Should().Throw<DomainException>().WithMessage("*200 caracteres*");
    }

    [Fact]
    public void Register_WithFutureDate_ThrowsDomainException()
    {
        var tomorrow = Today.AddDays(1);
        var act = () => Transaction.Register(50m, TransactionType.Credit, "Test", tomorrow);
        act.Should().Throw<DomainException>().WithMessage("*não pode ser futura*");
    }

    [Fact]
    public void Register_TrimsDescriptionWhitespace()
    {
        var t = Transaction.Register(50m, TransactionType.Credit, "  Venda  ", Today);
        t.Description.Should().Be("Venda");
    }
}
