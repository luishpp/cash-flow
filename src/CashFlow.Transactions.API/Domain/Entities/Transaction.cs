using CashFlow.Transactions.API.Domain.Exceptions;
using CashFlow.Transactions.API.Domain.ValueObjects;

namespace CashFlow.Transactions.API.Domain.Entities;

/// <summary>
/// Agregado raiz do bounded context Transactions.
/// Rich Domain Model (ADR-009): construtor privado + factory method,
/// invariantes garantidas na criação.
/// </summary>
public sealed class Transaction : AuditableEntity
{
    public Guid Id { get; private set; }
    public Money Amount { get; private set; }
    public TransactionType Type { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public MovementDate MovementDate { get; private set; }

    private Transaction() { }

    public static Transaction Register(
        decimal amount,
        TransactionType type,
        string description,
        DateOnly movementDate)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainException("Descrição da transação é obrigatória.");

        var cleanDescription = description.Trim();
        if (cleanDescription.Length > 200)
            throw new DomainException("Descrição não pode exceder 200 caracteres.");

        return new Transaction
        {
            Id = Guid.NewGuid(),
            Amount = Money.From(amount),
            Type = type,
            Description = cleanDescription,
            MovementDate = MovementDate.From(movementDate),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
