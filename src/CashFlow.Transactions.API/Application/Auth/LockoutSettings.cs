namespace CashFlow.Transactions.API.Application.Auth;

/// <summary>
/// Política de account lockout (ADR-023). Seção <c>Authentication:Lockout</c>.
/// Defaults pragmáticos: 5 tentativas, 15 minutos.
/// </summary>
public sealed class LockoutSettings
{
    public const string SectionName = "Authentication:Lockout";

    /// <summary>Tentativas falhas consecutivas antes de travar a conta.</summary>
    public int MaxFailedAttempts { get; init; } = 5;

    /// <summary>Duração da trava após atingir <see cref="MaxFailedAttempts"/>.</summary>
    public TimeSpan LockoutDuration { get; init; } = TimeSpan.FromMinutes(15);
}
