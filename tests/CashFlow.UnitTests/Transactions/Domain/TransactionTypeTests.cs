using CashFlow.Transactions.API.Domain.Exceptions;
using CashFlow.Transactions.API.Domain.ValueObjects;
using FluentAssertions;

namespace CashFlow.UnitTests.Transactions.Domain;

public class TransactionTypeTests
{
    [Theory]
    [InlineData("credit", TransactionType.Credit)]
    [InlineData("Credit", TransactionType.Credit)]
    [InlineData("CREDIT", TransactionType.Credit)]
    [InlineData("credito", TransactionType.Credit)]
    [InlineData("crédito", TransactionType.Credit)]
    [InlineData("  credit  ", TransactionType.Credit)]
    [InlineData("debit", TransactionType.Debit)]
    [InlineData("DEBIT", TransactionType.Debit)]
    [InlineData("debito", TransactionType.Debit)]
    [InlineData("débito", TransactionType.Debit)]
    public void Parse_WithValidValue_ReturnsExpectedType(string input, TransactionType expected)
    {
        TransactionTypeExtensions.Parse(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Parse_WithNullOrWhitespace_ThrowsDomainException(string? input)
    {
        var act = () => TransactionTypeExtensions.Parse(input!);
        act.Should().Throw<DomainException>().WithMessage("*Tipo*obrigatório*");
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("withdrawal")]
    [InlineData("c")]
    [InlineData("d")]
    [InlineData("credit_card")]
    public void Parse_WithInvalidValue_ThrowsDomainExceptionContainingValue(string input)
    {
        var act = () => TransactionTypeExtensions.Parse(input);
        act.Should().Throw<DomainException>()
            .WithMessage($"*'{input}'*credit*debit*");
    }

    [Fact]
    public void ToSnakeCase_Credit_ReturnsLowercaseCredit()
    {
        TransactionType.Credit.ToSnakeCase().Should().Be("credit");
    }

    [Fact]
    public void ToSnakeCase_Debit_ReturnsLowercaseDebit()
    {
        TransactionType.Debit.ToSnakeCase().Should().Be("debit");
    }

    [Fact]
    public void ToSnakeCase_UnknownEnumValue_ThrowsDomainException()
    {
        var unknown = (TransactionType)99;
        var act = () => unknown.ToSnakeCase();
        act.Should().Throw<DomainException>().WithMessage("*desconhecido*99*");
    }
}
