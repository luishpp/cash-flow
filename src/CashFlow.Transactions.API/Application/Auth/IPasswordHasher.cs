namespace CashFlow.Transactions.API.Application.Auth;

/// <summary>
/// Abstração de hashing de senha. A implementação concreta (Argon2id) está em
/// <see cref="Argon2idPasswordHasher"/>. Outras estratégias (PBKDF2, scrypt) podem coexistir
/// caso seja necessário migrar legacy hashes — basta o repositório identificar o algoritmo
/// pelo prefixo do hash armazenado.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Gera hash codificado contendo algoritmo, parâmetros (memory, iterations, parallelism), salt e hash.
    /// Formato: <c>$argon2id$v=19$m=65536,t=3,p=1$&lt;salt-b64&gt;$&lt;hash-b64&gt;</c>.
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Verifica em tempo constante (proteção timing-attack) se a senha confere com o hash armazenado.
    /// </summary>
    bool Verify(string password, string encodedHash);
}
