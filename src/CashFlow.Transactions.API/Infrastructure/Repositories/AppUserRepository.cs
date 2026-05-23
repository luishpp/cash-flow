using CashFlow.Transactions.API.Domain.Entities;
using CashFlow.Transactions.API.Infrastructure.Persistence;
using Dapper;

namespace CashFlow.Transactions.API.Infrastructure.Repositories;

/// <summary>
/// Repository Dapper para <c>transactions.app_users</c> (ADR-010, ADR-016, ADR-021, ADR-023).
/// SQL sempre parameterizado — sem risco de SQL injection no login.
/// </summary>
public sealed class AppUserRepository(IUnitOfWork uow) : IAppUserRepository
{
    private const string ColumnsList =
        "id, username, password_hash, role, is_active, created_at, last_login_at, failed_login_attempts, locked_until";

    private static readonly string SelectByUsernameSql = $@"
        SELECT {ColumnsList}
          FROM transactions.app_users
         WHERE username = @Username
         LIMIT 1;";

    private static readonly string SelectByIdSql = $@"
        SELECT {ColumnsList}
          FROM transactions.app_users
         WHERE id = @Id
         LIMIT 1;";

    private const string InsertSql = @"
        INSERT INTO transactions.app_users
            (id, username, password_hash, role, is_active, created_at, last_login_at,
             failed_login_attempts, locked_until)
        VALUES
            (@Id, @Username, @PasswordHash, @Role, @IsActive, @CreatedAt, @LastLoginAt,
             @FailedLoginAttempts, @LockedUntil);";

    private const string UpdateAuthStateSql = @"
        UPDATE transactions.app_users
           SET last_login_at         = @LastLoginAt,
               failed_login_attempts = @FailedLoginAttempts,
               locked_until          = @LockedUntil
         WHERE id = @Id;";

    public async Task<AppUser?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        var row = await uow.Connection.QuerySingleOrDefaultAsync<AppUserRow>(
            new CommandDefinition(SelectByUsernameSql,
                new { Username = username.Trim().ToLowerInvariant() },
                transaction: uow.Transaction, cancellationToken: ct));
        return row?.ToEntity();
    }

    public async Task<AppUser?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var row = await uow.Connection.QuerySingleOrDefaultAsync<AppUserRow>(
            new CommandDefinition(SelectByIdSql, new { Id = id },
                transaction: uow.Transaction, cancellationToken: ct));
        return row?.ToEntity();
    }

    public Task InsertAsync(AppUser user, CancellationToken ct = default) =>
        uow.Connection.ExecuteAsync(new CommandDefinition(InsertSql,
            new
            {
                user.Id,
                user.Username,
                user.PasswordHash,
                user.Role,
                user.IsActive,
                user.CreatedAt,
                user.LastLoginAt,
                user.FailedLoginAttempts,
                user.LockedUntil
            },
            transaction: uow.Transaction, cancellationToken: ct));

    public Task UpdateAuthStateAsync(AppUser user, CancellationToken ct = default) =>
        uow.Connection.ExecuteAsync(new CommandDefinition(UpdateAuthStateSql,
            new
            {
                user.Id,
                user.LastLoginAt,
                user.FailedLoginAttempts,
                user.LockedUntil
            },
            transaction: uow.Transaction, cancellationToken: ct));

    // Npgsql (10.x) materializa TIMESTAMPTZ como DateTime UTC; convertemos p/ DateTimeOffset
    // ao reidratar a entidade — o domínio fala DateTimeOffset.
    private sealed record AppUserRow(
        Guid Id, string Username, string Password_Hash, string Role,
        bool Is_Active, DateTime Created_At, DateTime? Last_Login_At,
        int Failed_Login_Attempts, DateTime? Locked_Until)
    {
        public AppUser ToEntity() => AppUser.Rehydrate(
            Id, Username, Password_Hash, Role, Is_Active,
            ToOffset(Created_At)!.Value,
            ToOffset(Last_Login_At),
            Failed_Login_Attempts,
            ToOffset(Locked_Until));

        private static DateTimeOffset? ToOffset(DateTime? value) =>
            value is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }
}
