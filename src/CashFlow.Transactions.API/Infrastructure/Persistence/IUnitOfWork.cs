using System.Data;

namespace CashFlow.Transactions.API.Infrastructure.Persistence;

/// <summary>
/// Unit of Work manual sobre Dapper (ADR-010).
/// Begin abre conexão + transação; Commit/Rollback finalizam atomicamente.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    IDbConnection Connection { get; }
    IDbTransaction Transaction { get; }

    Task BeginAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
