namespace CashFlow.Transactions.API.Infrastructure.Outbox;

public sealed record OutboxEntry(
    Guid Id,
    string EventType,
    string Payload,
    DateTimeOffset CreatedAt,
    int Attempts);

public interface IOutboxRepository
{
    /// <summary>Insere um evento no outbox dentro da UoW atual.</summary>
    Task EnqueueAsync(Guid id, string eventType, string payload, CancellationToken ct = default);

    /// <summary>Lê os próximos eventos pendentes em ordem FIFO. Roda sem UoW (conexão própria).</summary>
    Task<IReadOnlyList<OutboxEntry>> PeekPendingAsync(int batchSize, CancellationToken ct = default);

    /// <summary>Marca como publicado. Roda sem UoW (conexão própria).</summary>
    Task MarkPublishedAsync(Guid id, CancellationToken ct = default);

    /// <summary>Registra falha de publish (incrementa attempts, salva erro).</summary>
    Task RecordFailureAsync(Guid id, string error, CancellationToken ct = default);
}
