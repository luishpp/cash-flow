using CashFlow.Balance.API.Domain.Entities;
using CashFlow.Balance.API.Infrastructure.Persistence;
using Dapper;

namespace CashFlow.Balance.API.Infrastructure.Repositories;

public sealed class BalanceRepository(IUnitOfWork uow) : IBalanceRepository
{
    private const string SelectByDateSql = @"
        SELECT date, total_credits AS TotalCredits, total_debits AS TotalDebits, updated_at AS UpdatedAt
          FROM balance.daily_balance
         WHERE date = @Date;";

    private const string SelectByPeriodSql = @"
        SELECT date, total_credits AS TotalCredits, total_debits AS TotalDebits, updated_at AS UpdatedAt
          FROM balance.daily_balance
         WHERE date BETWEEN @From AND @To
         ORDER BY date ASC;";

    private const string UpsertSql = @"
        INSERT INTO balance.daily_balance
            (date, total_credits, total_debits, updated_at)
        VALUES
            (@Date, @TotalCredits, @TotalDebits, @UpdatedAt)
        ON CONFLICT (date) DO UPDATE SET
            total_credits = EXCLUDED.total_credits,
            total_debits  = EXCLUDED.total_debits,
            updated_at    = EXCLUDED.updated_at;";

    public async Task<DailyBalance?> GetByDateAsync(DateOnly date, CancellationToken ct = default)
    {
        var row = await uow.Connection.QuerySingleOrDefaultAsync<BalanceRow>(
            new CommandDefinition(SelectByDateSql, new { Date = date },
                transaction: uow.Transaction, cancellationToken: ct));
        return row?.ToEntity();
    }

    public async Task<IReadOnlyList<DailyBalance>> ListByPeriodAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await uow.Connection.QueryAsync<BalanceRow>(
            new CommandDefinition(SelectByPeriodSql, new { From = from, To = to },
                transaction: uow.Transaction, cancellationToken: ct));
        return rows.Select(r => r.ToEntity()).ToList();
    }

    public Task UpsertAsync(DailyBalance balance, CancellationToken ct = default) =>
        uow.Connection.ExecuteAsync(new CommandDefinition(
            UpsertSql,
            new
            {
                Date = balance.Date,
                TotalCredits = balance.TotalCredits,
                TotalDebits = balance.TotalDebits,
                UpdatedAt = balance.UpdatedAt
            },
            transaction: uow.Transaction,
            cancellationToken: ct));

    private sealed record BalanceRow(
        DateOnly Date, decimal TotalCredits, decimal TotalDebits, DateTimeOffset UpdatedAt)
    {
        public DailyBalance ToEntity()
        {
            var b = DailyBalance.New(Date);
            if (TotalCredits > 0) b.ApplyCredit(TotalCredits);
            if (TotalDebits > 0) b.ApplyDebit(TotalDebits);
            return b;
        }
    }
}
