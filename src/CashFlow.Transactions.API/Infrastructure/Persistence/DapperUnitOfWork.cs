using System.Data;
using Npgsql;

namespace CashFlow.Transactions.API.Infrastructure.Persistence;

public sealed class DapperUnitOfWork(IDbConnectionFactory factory) : IUnitOfWork
{
    private IDbConnection? _connection;
    private IDbTransaction? _transaction;

    public IDbConnection Connection =>
        _connection ?? throw new InvalidOperationException("UnitOfWork não iniciada. Chame BeginAsync primeiro.");

    public IDbTransaction Transaction =>
        _transaction ?? throw new InvalidOperationException("UnitOfWork não iniciada. Chame BeginAsync primeiro.");

    public async Task BeginAsync(CancellationToken ct = default)
    {
        if (_connection is not null)
            throw new InvalidOperationException("UnitOfWork já iniciada.");

        _connection = await factory.CreateOpenConnectionAsync(ct);
        _transaction = _connection.BeginTransaction();
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null) throw new InvalidOperationException("Sem transação ativa.");
        _transaction.Commit();
        await ResetAsync();
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null) throw new InvalidOperationException("Sem transação ativa.");
        _transaction.Rollback();
        await ResetAsync();
    }

    public ValueTask DisposeAsync() => ResetAsync();

    private async ValueTask ResetAsync()
    {
        _transaction?.Dispose();
        if (_connection is NpgsqlConnection npg)
            await npg.DisposeAsync();
        else
            _connection?.Dispose();
        _transaction = null;
        _connection = null;
    }
}
