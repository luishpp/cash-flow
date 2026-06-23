using CashFlow.Identity.API.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CashFlow.Identity.API.Controllers;

/// <summary>
/// Emissão e ciclo de vida de tokens (login → refresh → logout).
/// Credenciais validadas via <see cref="IAuthenticationService"/> (Argon2id, lockout, rotação de refresh).
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[AllowAnonymous]
public sealed class AuthController(IAuthenticationService authService) : ControllerBase
{
    /// <summary>
    /// Login username/password — emite Bearer access token + refresh token opaco.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var session = await authService.AuthenticateAsync(request.Username, request.Password, ct);
        if (session is null)
            return Unauthorized(new { error = "Credenciais inválidas." });

        return Ok(ToResponse(session));
    }

    /// <summary>
    /// Rotaciona o refresh token: revoga o token atual e emite par novo (access + refresh).
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var session = await authService.RefreshAsync(request.RefreshToken, ct);
        if (session is null)
            return Unauthorized(new { error = "Refresh token inválido ou expirado." });

        return Ok(ToResponse(session));
    }

    /// <summary>Revoga o refresh token. Idempotente.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken ct)
    {
        await authService.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    private static TokenResponse ToResponse(AuthenticatedSession s) => new(
        AccessToken: s.AccessToken,
        TokenType: "Bearer",
        ExpiresAtUtc: s.AccessTokenExpiresAtUtc,
        Role: s.Role,
        RefreshToken: s.RefreshTokenRaw,
        RefreshTokenExpiresAtUtc: s.RefreshTokenExpiresAtUtc);
}
