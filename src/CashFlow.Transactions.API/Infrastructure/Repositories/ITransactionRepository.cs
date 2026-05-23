using CashFlow.Transactions.API.Domain.Entities;

namespace CashFlow.Transactions.API.Infrastructure.Repositories;

public interface ITransactionRepository
{
    Task InsertAsync(Transaction transaction, CancellationToken ct = default);
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> ListByDateAsync(DateOnly date, CancellationToken ct = default);
}
