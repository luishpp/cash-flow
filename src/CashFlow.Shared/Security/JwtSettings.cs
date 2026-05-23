namespace CashFlow.Shared.Security;

/// <summary>
/// Configuração de emissão e validação de tokens JWT.
/// Lida da seção <c>Authentication:Jwt</c> de appsettings.
/// </summary>
public sealed class JwtSettings
{
    public const string SectionName = "Authentication:Jwt";

    public required string Issuer { get; init; }
    public required string Audience { get; init; }

    /// <summary>
    /// Chave HMAC-SHA256 — mínimo 32 bytes (256 bits).
    /// Em produção: ler de Key Vault / secret manager, NUNCA de appsettings.
    /// </summary>
    public required string SecretKey { get; init; }

    public bool ValidateIssuer { get; init; } = true;
    public bool ValidateAudience { get; init; } = true;
    public bool ValidateLifetime { get; init; } = true;
    public bool ValidateIssuerSigningKey { get; init; } = true;

    public TimeSpan ClockSkew { get; init; } = TimeSpan.FromMinutes(2);

    public int TokenExpirationMinutes { get; init; } = 60;
}
