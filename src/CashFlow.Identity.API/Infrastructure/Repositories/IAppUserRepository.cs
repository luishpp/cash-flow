using CashFlow.Identity.API.Domain.Entities;

namespace CashFlow.Identity.API.Infrastructure.Repositories;

public interface IAppUserRepository
{
    Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task InsertAsync(AppUser user, CancellationToken ct = default);

    /// <summary>
    /// Persiste mudanças no estado de auth: <c>last_login_at</c>, <c>failed_login_attempts</c>, <c>locked_until</c>.
    /// Usado tanto no sucesso (reset + last_login) quanto na falha (incremento + eventual lockout) — ADR-023.
    /// </summary>
    Task UpdateAuthStateAsync(AppUser user, CancellationToken ct = default);
}
