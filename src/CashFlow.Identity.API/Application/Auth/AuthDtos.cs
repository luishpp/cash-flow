namespace CashFlow.Identity.API.Application.Auth;

public sealed record LoginRequest(string Username, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAtUtc,
    string Role,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);
