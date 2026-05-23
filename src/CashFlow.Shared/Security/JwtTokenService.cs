using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CashFlow.Shared.Security;

/// <summary>
/// Implementação <see cref="ITokenService"/> baseada em HMAC-SHA256 simétrico.
/// Em produção a chave deve vir de Key Vault — ver <c>docs/adrs/adr-015-jwt-authentication.md</c>.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private readonly JwtSettings _settings;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(JwtSettings settings)
    {
        _settings = settings;

        var keyBytes = Encoding.UTF8.GetBytes(settings.SecretKey);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "Authentication:Jwt:SecretKey deve ter no mínimo 32 bytes (256 bits) " +
                "para HMAC-SHA256.");
        }

        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256);
    }

    public (string Token, DateTimeOffset ExpiresAtUtc) IssueToken(
        string subject,
        string role,
        IEnumerable<Claim>? additionalClaims = null)
    {
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject é obrigatório.", nameof(subject));
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Role é obrigatório.", nameof(role));

        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_settings.TokenExpirationMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.Name, subject),
            new(ClaimTypes.Role, role),
        };

        if (additionalClaims is not null)
            claims.AddRange(additionalClaims);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _signingCredentials);

        var serialized = new JwtSecurityTokenHandler().WriteToken(token);
        return (serialized, new DateTimeOffset(expires, TimeSpan.Zero));
    }
}
