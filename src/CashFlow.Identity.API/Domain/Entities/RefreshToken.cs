using CashFlow.Identity.API.Domain.Exceptions;

namespace CashFlow.Identity.API.Domain.Entities;

/// <summary>
/// Refresh token persistido para rotação (ADR-024).
/// O hash (SHA-256 do raw token) é armazenado — o raw nunca toca disco.
/// Rotação: cada uso revoga o token atual e emite um novo, encadeando via <c>ReplacedByTokenHash</c>
/// (permite auditoria de cadeia de rotação e — em evolução — detecção de reuso).
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    private RefreshToken() { }

    public static RefreshToken Issue(Guid userId, string tokenHash, TimeSpan lifetime)
    {
        if (userId == Guid.Empty)
            throw new DomainException("UserId é obrigatório.");
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new DomainException("TokenHash é obrigatório.");
        if (lifetime <= TimeSpan.Zero)
            throw new DomainException("Lifetime deve ser positivo.");

        var now = DateTimeOffset.UtcNow;
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
            RevokedAt = null,
            ReplacedByTokenHash = null
        };
    }

    /// <summary>Reconstitui entidade vinda do banco — sem validações de criação.</summary>
    public static RefreshToken Rehydrate(
        Guid id, Guid userId, string tokenHash,
        DateTimeOffset createdAt, DateTimeOffset expiresAt,
        DateTimeOffset? revokedAt, string? replacedByTokenHash) => new()
    {
        Id = id,
        UserId = userId,
        TokenHash = tokenHash,
        CreatedAt = createdAt,
        ExpiresAt = expiresAt,
        RevokedAt = revokedAt,
        ReplacedByTokenHash = replacedByTokenHash
    };

    public bool IsActive =>
        RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;

    /// <summary>Revoga o token. Se for parte de uma rotação, registra o sucessor.</summary>
    public void Revoke(string? replacedByTokenHash = null)
    {
        if (RevokedAt is not null) return; // idempotente
        RevokedAt = DateTimeOffset.UtcNow;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
