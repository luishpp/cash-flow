namespace CashFlow.Transactions.API.Domain.Entities;

public abstract class AuditableEntity
{
    public DateTimeOffset CreatedAt { get; protected set; }
}
