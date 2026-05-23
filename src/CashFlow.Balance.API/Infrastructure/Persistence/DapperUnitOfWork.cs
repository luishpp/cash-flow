using System.Data;
using Npgsql;

namespace CashFlow.Balance.API.Infrastructure.Persistence;

public sealed class DapperUnitOfWork(IDbConnectionFactory factory) : IUnitOfWork
{
    private IDbConnection? _connection;
    private IDbTransaction? _transaction;

    public IDbConnection Connection =>
        _connection ?? throw new InvalidOperationException("UnitOfWork não iniciada.");
    public IDbTransaction Transaction =>
        _transaction ?? throw new InvalidOperationException("UnitOfWork não iniciada.");

    public async Task BeginAsync(CancellationToken ct = default)
    {
        if (_connection is not null)
            throw new InvalidOperationException("UnitOfWork já iniciada.");
        _connection = await factory.CreateOpenConnectionAsync(ct);
        _transaction = _connection.BeginTransaction();
    }

    public Task CommitAsync(CancellationToken ct = default)
    {
        _transaction?.Commit();
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken ct = default)
    {
        _transaction?.Rollback();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _transaction?.Dispose();
        if (_connection is NpgsqlConnection npg)
            await npg.DisposeAsync();
        else
            _connection?.Dispose();
    }
}
