namespace CashFlow.Identity.API.Application.Auth;

/// <summary>
/// Orquestra os fluxos de autenticação: login, refresh, logout (ADR-016, ADR-021, ADR-023, ADR-024).
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Valida credenciais e emite par (access token JWT + refresh token opaco).
    /// Retorna <c>null</c> em qualquer cenário de falha (user inexistente, senha inválida,
    /// usuário inativo, conta travada) — sem distinguir para evitar enumeration attacks.
    /// Lockout (ADR-023): incrementa contador a cada falha; trava após N tentativas.
    /// </summary>
    Task<AuthenticatedSession?> AuthenticateAsync(string username, string password, CancellationToken ct);

    /// <summary>
    /// Troca um refresh token válido por um novo par (access + refresh) — rotação.
    /// O refresh token antigo é revogado atomicamente (ADR-024).
    /// </summary>
    Task<AuthenticatedSession?> RefreshAsync(string refreshTokenRaw, CancellationToken ct);

    /// <summary>Revoga o refresh token. Idempotente — token já revogado é tratado como sucesso.</summary>
    Task LogoutAsync(string refreshTokenRaw, CancellationToken ct);
}

/// <summary>Resultado de um login/refresh — usado pelo controller para montar <see cref="TokenResponse"/>.</summary>
public sealed record AuthenticatedSession(
    string Username,
    string Role,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshTokenRaw,
    DateTimeOffset RefreshTokenExpiresAtUtc);
