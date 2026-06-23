using CashFlow.Balance.Core.Domain.Exceptions;

namespace CashFlow.Balance.Core.Domain.Entities;

/// <summary>
/// Projeção de leitura do bounded context Balance.
/// Mantém totais agregados por data; saldo é derivado (credits - debits).
/// Rich Domain Model (ADR-009): mutações via método de negócio.
/// </summary>
public sealed class DailyBalance
{
    public DateOnly Date { get; private set; }
    public decimal TotalCredits { get; private set; }
    public decimal TotalDebits { get; private set; }
    public decimal Balance => TotalCredits - TotalDebits;
    public DateTimeOffset UpdatedAt { get; private set; }

    private DailyBalance() { }

    public static DailyBalance New(DateOnly date) => new()
    {
        Date = date,
        TotalCredits = 0m,
        TotalDebits = 0m,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public void ApplyCredit(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("Valor de crédito deve ser maior que zero.");
        TotalCredits += amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyDebit(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("Valor de débito deve ser maior que zero.");
        TotalDebits += amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
