using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace CashFlow.Identity.API.Application.Auth;

/// <summary>
/// Refresh token random (Base64Url) + hash SHA-256 (Base64) para persistência.
/// Argon2id (ADR-021) é overkill aqui — tokens já têm 256 bits de entropia, não há ataque
/// de dicionário viável; o hash serve apenas para evitar que um dump do banco vaze tokens válidos.
/// </summary>
public sealed class Sha256RefreshTokenFactory : IRefreshTokenFactory
{
    private readonly int _tokenBytes;

    public Sha256RefreshTokenFactory(IOptions<RefreshTokenSettings> options)
    {
        _tokenBytes = options.Value.TokenBytes;
        if (_tokenBytes < 16)
            throw new InvalidOperationException("RefreshToken:TokenBytes mínimo é 16 (128 bits).");
    }

    public (string RawToken, string TokenHash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(_tokenBytes);
        var raw = Base64UrlEncode(bytes);
        return (raw, Hash(raw));
    }

    public string Hash(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            throw new ArgumentException("Raw token é obrigatório.", nameof(rawToken));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToBase64String(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
