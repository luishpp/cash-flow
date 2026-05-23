using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace CashFlow.Transactions.API.Application.Auth;

/// <summary>
/// Hashing Argon2id — vencedor da Password Hashing Competition (2015) e recomendado pelo OWASP.
/// Parâmetros default seguem <c>OWASP Password Storage Cheat Sheet</c>:
///   m=65536 (64 MiB), t=3 iterations, p=1 thread, salt=16 bytes, hash=32 bytes.
/// Formato encodado é auto-descritivo — permite tunar parâmetros sem migração de schema.
/// </summary>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    // Parâmetros OWASP — ver https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html
    private const int MemorySizeKb = 65536; // 64 MiB
    private const int Iterations = 3;
    private const int DegreeOfParallelism = 1;
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int ArgonVersion = 19; // Argon2 v1.3

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = ComputeHash(password, salt);
        return Encode(salt, hash);
    }

    public bool Verify(string password, string encodedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(encodedHash))
            return false;

        if (!TryDecode(encodedHash, out var salt, out var expectedHash))
            return false;

        var actualHash = ComputeHash(password, salt);
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    private static byte[] ComputeHash(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = DegreeOfParallelism,
            Iterations = Iterations,
            MemorySize = MemorySizeKb,
        };
        return argon2.GetBytes(HashSizeBytes);
    }

    private static string Encode(byte[] salt, byte[] hash)
    {
        // Formato PHC: $argon2id$v=19$m=65536,t=3,p=1$<salt-b64-no-padding>$<hash-b64-no-padding>
        var saltB64 = Convert.ToBase64String(salt).TrimEnd('=');
        var hashB64 = Convert.ToBase64String(hash).TrimEnd('=');
        return $"$argon2id$v={ArgonVersion}$m={MemorySizeKb},t={Iterations},p={DegreeOfParallelism}${saltB64}${hashB64}";
    }

    private static bool TryDecode(string encoded, out byte[] salt, out byte[] hash)
    {
        salt = Array.Empty<byte>();
        hash = Array.Empty<byte>();

        var parts = encoded.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5) return false;
        if (parts[0] != "argon2id") return false;

        try
        {
            salt = DecodeBase64NoPadding(parts[3]);
            hash = DecodeBase64NoPadding(parts[4]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] DecodeBase64NoPadding(string value)
    {
        var padded = value + new string('=', (4 - value.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
