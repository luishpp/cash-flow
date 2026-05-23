using System.Security.Claims;

namespace CashFlow.Shared.Security;

/// <summary>
/// Emissão de JWT. A validação fica a cargo do middleware AspNetCore (JwtBearer).
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Gera um JWT HMAC-SHA256 contendo <c>sub</c>, <c>name</c>, <c>role</c> e claims extras.
    /// </summary>
    /// <param name="subject">Identificador do principal (geralmente o username).</param>
    /// <param name="role">Role da aplicação (ex.: <see cref="CashFlowRoles.Merchant"/>).</param>
    /// <param name="additionalClaims">Claims adicionais a serem incluídas no token.</param>
    /// <returns>Token JWT serializado + sua data de expiração UTC.</returns>
    (string Token, DateTimeOffset ExpiresAtUtc) IssueToken(
        string subject,
        string role,
        IEnumerable<Claim>? additionalClaims = null);
}
