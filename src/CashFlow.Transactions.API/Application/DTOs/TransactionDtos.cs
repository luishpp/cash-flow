namespace CashFlow.Transactions.API.Application.DTOs;

public sealed record RegisterTransactionRequest(
    string Type,
    decimal Amount,
    string Description,
    DateOnly MovementDate
);

public sealed record TransactionResponse(
    Guid Id,
    string Type,
    decimal Amount,
    string Description,
    DateOnly MovementDate,
    DateTimeOffset CreatedAt
);
