using CashFlow.Transactions.API.Domain.Entities;
using CashFlow.Transactions.API.Domain.ValueObjects;
using CashFlow.Transactions.API.Infrastructure.Persistence;
using Dapper;

namespace CashFlow.Transactions.API.Infrastructure.Repositories;

/// <summary>
/// Repository Dapper com SQL parameterizado (ADR-010, ADR-013).
/// Sempre via @parameters — proteção contra SQL injection.
/// </summary>
public sealed class TransactionRepository(IUnitOfWork uow) : ITransactionRepository
{
    private const string InsertSql = @"
        INSERT INTO transactions.transactions
            (id, amount, type, description, movement_date, created_at)
        VALUES
            (@Id, @Amount, @Type, @Description, @MovementDate, @CreatedAt);";

    private const string SelectByIdSql = @"
        SELECT id, amount, type, description, movement_date, created_at
          FROM transactions.transactions
         WHERE id = @Id;";

    private const string SelectByDateSql = @"
        SELECT id, amount, type, description, movement_date, created_at
          FROM transactions.transactions
         WHERE movement_date = @Date
         ORDER BY created_at ASC;";

    public Task InsertAsync(Transaction transaction, CancellationToken ct = default) =>
        uow.Connection.ExecuteAsync(new CommandDefinition(
            InsertSql,
            new
            {
                Id = transaction.Id,
                Amount = transaction.Amount.Amount,
                Type = transaction.Type.ToSnakeCase(),
                Description = transaction.Description,
                MovementDate = transaction.MovementDate.Value,
                CreatedAt = transaction.CreatedAt
            },
            transaction: uow.Transaction,
            cancellationToken: ct));

    public async Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await uow.Connection.QuerySingleOrDefaultAsync<TransactionRow>(
            new CommandDefinition(SelectByIdSql, new { Id = id },
                transaction: uow.Transaction, cancellationToken: ct));
        return row?.ToEntity();
    }

    public async Task<IReadOnlyList<Transaction>> ListByDateAsync(DateOnly date, CancellationToken ct = default)
    {
        var rows = await uow.Connection.QueryAsync<TransactionRow>(
            new CommandDefinition(SelectByDateSql, new { Date = date },
                transaction: uow.Transaction, cancellationToken: ct));
        return rows.Select(r => r.ToEntity()).ToList();
    }

    private sealed record TransactionRow(
        Guid Id, decimal Amount, string Type, string Description,
        DateOnly Movement_Date, DateTimeOffset Created_At)
    {
        public Transaction ToEntity() =>
            Transaction.Register(
                Amount,
                TransactionTypeExtensions.Parse(Type),
                Description,
                Movement_Date);
    }
}
