namespace CashFlow.Transactions.API.Application.Auth;

/// <summary>
/// Política de refresh tokens (ADR-024). Seção <c>Authentication:RefreshToken</c>.
/// </summary>
public sealed class RefreshTokenSettings
{
    public const string SectionName = "Authentication:RefreshToken";

    /// <summary>Validade do refresh token. Default 7 dias (alinhado a UX típica de SaaS).</summary>
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Tamanho do token raw (em bytes) — convertido para Base64Url (URL-safe).</summary>
    public int TokenBytes { get; init; } = 32; // 256 bits de entropia
}
