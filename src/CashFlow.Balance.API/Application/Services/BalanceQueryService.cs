using CashFlow.Balance.API.Application.DTOs;
using CashFlow.Balance.API.Domain.Entities;
using CashFlow.Balance.API.Infrastructure.Persistence;
using CashFlow.Balance.API.Infrastructure.Repositories;

namespace CashFlow.Balance.API.Application.Services;

public sealed class BalanceQueryService(IUnitOfWork uow, IBalanceRepository repo) : IBalanceQueryService
{
    public async Task<BalanceResponse?> GetByDateAsync(DateOnly date, CancellationToken ct = default)
    {
        await uow.BeginAsync(ct);
        var balance = await repo.GetByDateAsync(date, ct);
        return balance is null ? null : ToResponse(balance);
    }

    public async Task<IReadOnlyList<BalanceResponse>> ListByPeriodAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
            throw new ArgumentException("Parâmetro 'to' deve ser maior ou igual a 'from'.");

        await uow.BeginAsync(ct);
        var balances = await repo.ListByPeriodAsync(from, to, ct);
        return balances.Select(ToResponse).ToList();
    }

    private static BalanceResponse ToResponse(DailyBalance b) =>
        new(b.Date, b.TotalCredits, b.TotalDebits, b.Balance, b.UpdatedAt);
}
