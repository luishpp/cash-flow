using System.Data;

namespace CashFlow.Balance.Core.Infrastructure.Persistence;

public interface IDbConnectionFactory
{
    Task<IDbConnection> CreateOpenConnectionAsync(CancellationToken ct = default);
}
