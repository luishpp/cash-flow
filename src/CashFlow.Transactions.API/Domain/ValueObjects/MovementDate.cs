using CashFlow.Transactions.API.Domain.Exceptions;

namespace CashFlow.Transactions.API.Domain.ValueObjects;

/// <summary>
/// Value Object para a data do movimento financeiro.
/// Invariante: não pode ser data futura (transações referem-se a fatos consumados).
/// </summary>
public readonly record struct MovementDate
{
    public DateOnly Value { get; }

    private MovementDate(DateOnly value) => Value = value;

    public static MovementDate From(DateOnly value)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (value > today)
            throw new DomainException($"Data do movimento ({value:yyyy-MM-dd}) não pode ser futura.");

        return new MovementDate(value);
    }

    public override string ToString() => Value.ToString("yyyy-MM-dd");

    public static implicit operator DateOnly(MovementDate d) => d.Value;
}
