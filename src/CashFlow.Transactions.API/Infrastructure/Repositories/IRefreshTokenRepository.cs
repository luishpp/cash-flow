using CashFlow.Transactions.API.Domain.Entities;

namespace CashFlow.Transactions.API.Infrastructure.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);
    Task InsertAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>Persiste mudanças no estado de revogação (revoked_at, replaced_by_token_hash).</summary>
    Task UpdateRevocationAsync(RefreshToken token, CancellationToken ct = default);
}
