namespace CashFlow.Balance.API.Application.DTOs;

public sealed record BalanceResponse(
    DateOnly Date,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal Balance,
    DateTimeOffset UpdatedAt
);

/// <summary>
/// Saldo consolidado de um intervalo [From, To]:
/// totais agregados no topo (mesmo shape de <see cref="BalanceResponse"/>) + lista diária em <see cref="Days"/>.
/// </summary>
public sealed record BalancePeriodResponse(
    DateOnly From,
    DateOnly To,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal Balance,
    IReadOnlyList<BalanceResponse> Days
);
