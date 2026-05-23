using CashFlow.Balance.API.Application.DTOs;

namespace CashFlow.Balance.API.Application.Services;

public interface IBalanceQueryService
{
    Task<BalanceResponse?> GetByDateAsync(DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<BalanceResponse>> ListByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
