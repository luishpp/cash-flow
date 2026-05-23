namespace CashFlow.Shared.Events;

/// <summary>
/// Evento publicado pela API de Transactions após persistir uma transação com sucesso.
/// Consumido pela API de Balance para atualizar a projeção DailyBalance.
/// EventId é a chave de idempotência (ADR-011).
/// </summary>
public sealed record TransactionRegistered(
    Guid EventId,
    Guid TransactionId,
    decimal Amount,
    string Type,
    string Description,
    DateOnly MovementDate,
    DateTimeOffset RegisteredAt
);
