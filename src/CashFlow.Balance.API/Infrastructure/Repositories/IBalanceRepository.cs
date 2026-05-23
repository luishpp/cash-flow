using CashFlow.Balance.API.Domain.Entities;

namespace CashFlow.Balance.API.Infrastructure.Repositories;

public interface IBalanceRepository
{
    Task<DailyBalance?> GetByDateAsync(DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<DailyBalance>> ListByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task UpsertAsync(DailyBalance balance, CancellationToken ct = default);
}
