using CashFlow.Identity.API.Domain.Entities;
using CashFlow.Identity.API.Infrastructure.Persistence;
using CashFlow.Identity.API.Infrastructure.Repositories;
using CashFlow.Shared.Security;
using Microsoft.Extensions.Options;

namespace CashFlow.Identity.API.Application.Auth;

/// <summary>
/// Orquestra:
///   - login: busca user → check lockout → verify hash Argon2id → emite JWT + refresh token
///   - refresh: hash do refresh → busca → valida (não revogado, não expirado) → revoga + emite par novo
///   - logout: hash do refresh → revoga (idempotente)
/// Mensagens de erro são genéricas (mesma resposta para "user not found" e "wrong password")
/// para prevenir user enumeration (ADR-021).
/// </summary>
public sealed class AuthenticationService(
    IAppUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    IRefreshTokenFactory refreshFactory,
    ITokenService tokenService,
    IUnitOfWork uow,
    IOptions<LockoutSettings> lockoutSettings,
    IOptions<RefreshTokenSettings> refreshSettings,
    ILogger<AuthenticationService> logger) : IAuthenticationService
{
    private readonly LockoutSettings _lockout = lockoutSettings.Value;
    private readonly RefreshTokenSettings _refresh = refreshSettings.Value;

    public async Task<AuthenticatedSession?> AuthenticateAsync(string username, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return null;

        await uow.BeginAsync(ct);
        var user = await users.GetByUsernameAsync(username, ct);

        if (user is null || !user.IsActive)
        {
            logger.LogWarning("Login falhou: usuário '{Username}' não encontrado ou inativo.", username);
            return null;
        }

        if (user.IsLockedOut)
        {
            logger.LogWarning("Login falhou: conta '{Username}' travada até {LockedUntil}.",
                user.Username, user.LockedUntil);
            return null;
        }

        if (!hasher.Verify(password, user.PasswordHash))
        {
            user.RegisterFailedLogin(_lockout.MaxFailedAttempts, _lockout.LockoutDuration);
            await users.UpdateAuthStateAsync(user, ct);
            await uow.CommitAsync(ct);

            logger.LogWarning(
                "Login falhou: senha inválida para '{Username}' (tentativas: {Attempts}/{Max}{Locked}).",
                user.Username, user.FailedLoginAttempts, _lockout.MaxFailedAttempts,
                user.IsLockedOut ? " — LOCKED" : "");
            return null;
        }

        user.RecordSuccessfulLogin();
        await users.UpdateAuthStateAsync(user, ct);

        var session = await IssueSessionAsync(user, ct);
        await uow.CommitAsync(ct);

        logger.LogInformation("Login bem-sucedido para '{Username}'.", user.Username);
        return session;
    }

    public async Task<AuthenticatedSession?> RefreshAsync(string refreshTokenRaw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenRaw))
            return null;

        await uow.BeginAsync(ct);

        var incomingHash = refreshFactory.Hash(refreshTokenRaw);
        var existing = await refreshTokens.GetByHashAsync(incomingHash, ct);

        if (existing is null || !existing.IsActive)
        {
            logger.LogWarning("Refresh falhou: token inválido, expirado ou já revogado.");
            return null;
        }

        var user = await users.GetByIdAsync(existing.UserId, ct);
        if (user is null || !user.IsActive || user.IsLockedOut)
        {
            logger.LogWarning("Refresh falhou: user {UserId} indisponível.", existing.UserId);
            return null;
        }

        // Emite o novo refresh antes de revogar — assim conseguimos encadear (replaced_by_token_hash).
        var (newRaw, newHash) = refreshFactory.Generate();
        var newToken = RefreshToken.Issue(user.Id, newHash, _refresh.Lifetime);
        await refreshTokens.InsertAsync(newToken, ct);

        existing.Revoke(replacedByTokenHash: newHash);
        await refreshTokens.UpdateRevocationAsync(existing, ct);

        var (accessToken, accessExp) = tokenService.IssueToken(user.Username, user.Role);
        await uow.CommitAsync(ct);

        logger.LogInformation("Refresh OK para '{Username}'.", user.Username);
        return new AuthenticatedSession(
            user.Username, user.Role,
            accessToken, accessExp,
            newRaw, newToken.ExpiresAt);
    }

    public async Task LogoutAsync(string refreshTokenRaw, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenRaw))
            return;

        await uow.BeginAsync(ct);
        var hash = refreshFactory.Hash(refreshTokenRaw);
        var existing = await refreshTokens.GetByHashAsync(hash, ct);

        if (existing is null || existing.RevokedAt is not null)
        {
            // Idempotente — não vazamos informação se o token existe.
            await uow.CommitAsync(ct);
            return;
        }

        existing.Revoke();
        await refreshTokens.UpdateRevocationAsync(existing, ct);
        await uow.CommitAsync(ct);

        logger.LogInformation("Logout: refresh token revogado.");
    }

    private async Task<AuthenticatedSession> IssueSessionAsync(AppUser user, CancellationToken ct)
    {
        var (rawRefresh, refreshHash) = refreshFactory.Generate();
        var refreshToken = RefreshToken.Issue(user.Id, refreshHash, _refresh.Lifetime);
        await refreshTokens.InsertAsync(refreshToken, ct);

        var (accessToken, accessExp) = tokenService.IssueToken(user.Username, user.Role);

        return new AuthenticatedSession(
            user.Username, user.Role,
            accessToken, accessExp,
            rawRefresh, refreshToken.ExpiresAt);
    }
}
