namespace CashFlow.Transactions.API.Application.Auth;

/// <summary>
/// Geração e hashing de refresh tokens (ADR-024).
/// Tokens são opacos (alta entropia random), não JWT — não há claims, só lookup por hash.
/// </summary>
public interface IRefreshTokenFactory
{
    /// <summary>
    /// Gera um par <c>(rawToken, tokenHash)</c>: o raw vai para o cliente, o hash vai para o banco.
    /// SHA-256 é suficiente — tokens random de 256 bits têm entropia que dispensa hashing lento.
    /// </summary>
    (string RawToken, string TokenHash) Generate();

    /// <summary>Computa hash do raw token (idempotente — mesma raw → mesmo hash).</summary>
    string Hash(string rawToken);
}
