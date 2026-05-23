using CashFlow.Transactions.API.Domain.Entities;
using CashFlow.Transactions.API.Infrastructure.Persistence;
using Dapper;

namespace CashFlow.Transactions.API.Infrastructure.Repositories;

/// <summary>
/// Repository Dapper para <c>transactions.refresh_tokens</c> (ADR-010, ADR-024).
/// </summary>
public sealed class RefreshTokenRepository(IUnitOfWork uow) : IRefreshTokenRepository
{
    private const string ColumnsList =
        "id, user_id, token_hash, created_at, expires_at, revoked_at, replaced_by_token_hash";

    private static readonly string SelectByHashSql = $@"
        SELECT {ColumnsList}
          FROM transactions.refresh_tokens
         WHERE token_hash = @TokenHash
         LIMIT 1;";

    private const string InsertSql = @"
        INSERT INTO transactions.refresh_tokens
            (id, user_id, token_hash, created_at, expires_at, revoked_at, replaced_by_token_hash)
        VALUES
            (@Id, @UserId, @TokenHash, @CreatedAt, @ExpiresAt, @RevokedAt, @ReplacedByTokenHash);";

    private const string UpdateRevocationSql = @"
        UPDATE transactions.refresh_tokens
           SET revoked_at             = @RevokedAt,
               replaced_by_token_hash = @ReplacedByTokenHash
         WHERE id = @Id;";

    public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var row = await uow.Connection.QuerySingleOrDefaultAsync<RefreshTokenRow>(
            new CommandDefinition(SelectByHashSql, new { TokenHash = tokenHash },
                transaction: uow.Transaction, cancellationToken: ct));
        return row?.ToEntity();
    }

    public Task InsertAsync(RefreshToken token, CancellationToken ct = default) =>
        uow.Connection.ExecuteAsync(new CommandDefinition(InsertSql,
            new
            {
                token.Id,
                token.UserId,
                token.TokenHash,
                token.CreatedAt,
                token.ExpiresAt,
                token.RevokedAt,
                token.ReplacedByTokenHash
            },
            transaction: uow.Transaction, cancellationToken: ct));

    public Task UpdateRevocationAsync(RefreshToken token, CancellationToken ct = default) =>
        uow.Connection.ExecuteAsync(new CommandDefinition(UpdateRevocationSql,
            new
            {
                token.Id,
                token.RevokedAt,
                token.ReplacedByTokenHash
            },
            transaction: uow.Transaction, cancellationToken: ct));

    private sealed record RefreshTokenRow(
        Guid Id, Guid User_Id, string Token_Hash,
        DateTime Created_At, DateTime Expires_At,
        DateTime? Revoked_At, string? Replaced_By_Token_Hash)
    {
        public RefreshToken ToEntity() => RefreshToken.Rehydrate(
            Id, User_Id, Token_Hash,
            ToOffset(Created_At)!.Value,
            ToOffset(Expires_At)!.Value,
            ToOffset(Revoked_At),
            Replaced_By_Token_Hash);

        private static DateTimeOffset? ToOffset(DateTime? value) =>
            value is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }
}
