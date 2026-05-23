using CashFlow.Transactions.API.Domain.Exceptions;

namespace CashFlow.Transactions.API.Domain.ValueObjects;

/// <summary>
/// Value Object que encapsula um valor monetário positivo em BRL.
/// Invariante: amount &gt; 0 (zero ou negativos são transações inválidas no domínio).
/// </summary>
public readonly record struct Money
{
    public decimal Amount { get; }

    private Money(decimal amount) => Amount = amount;

    public static Money From(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("Valor da transação deve ser maior que zero.");
        if (decimal.Round(amount, 2) != amount)
            throw new DomainException("Valor da transação deve ter no máximo 2 casas decimais.");

        return new Money(amount);
    }

    public override string ToString() => Amount.ToString("F2");

    public static implicit operator decimal(Money m) => m.Amount;
}
