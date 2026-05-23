namespace CashFlow.Balance.API.Application.DTOs;

public sealed record BalanceResponse(
    DateOnly Date,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal Balance,
    DateTimeOffset UpdatedAt
);
