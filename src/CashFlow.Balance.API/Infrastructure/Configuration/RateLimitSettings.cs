namespace CashFlow.Balance.API.Infrastructure.Configuration;

/// <summary>
/// Configuração do rate-limiter da Balance API.
/// Lida de <c>RateLimiting:Balance</c> em appsettings; defaults batem nos valores históricos
/// (RNF-02: 50 req/s sustentado). Seção opcional — sem ela, os defaults aplicam.
/// </summary>
public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimiting:Balance";

    /// <summary>Tamanho da janela fixa em segundos. Default: 1.</summary>
    public int WindowSeconds { get; set; } = 1;

    /// <summary>Máximo de requests aceitos por janela. Default: 50 (target do RNF-02).</summary>
    public int PermitLimit { get; set; } = 50;

    /// <summary>Requests enfileirados além do permit antes de retornar 429. Default: 5.</summary>
    public int QueueLimit { get; set; } = 5;

    /// <summary>Valor do header Retry-After devolvido em 429. Default: "1".</summary>
    public string RetryAfterSeconds { get; set; } = "1";
}
