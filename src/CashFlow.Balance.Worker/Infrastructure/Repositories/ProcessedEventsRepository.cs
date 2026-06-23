using CashFlow.Balance.Core.Infrastructure.Persistence;
using Dapper;

namespace CashFlow.Balance.Worker.Infrastructure.Repositories;

/// <summary>
/// Tabela de idempotência (ADR-011) — chave de proteção contra reentregas at-least-once.
/// </summary>
public sealed class ProcessedEventsRepository(IUnitOfWork uow) : IProcessedEventsRepository
{
    private const string ExistsSql = @"
        SELECT EXISTS(
            SELECT 1 FROM balance.processed_events
             WHERE event_id = @EventId AND consumer_name = @ConsumerName
        );";

    private const string InsertSql = @"
        INSERT INTO balance.processed_events (event_id, consumer_name)
        VALUES (@EventId, @ConsumerName);";

    public Task<bool> ExistsAsync(Guid eventId, string consumerName, CancellationToken ct = default) =>
        uow.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            ExistsSql, new { EventId = eventId, ConsumerName = consumerName },
            transaction: uow.Transaction, cancellationToken: ct));

    public Task RegisterAsync(Guid eventId, string consumerName, CancellationToken ct = default) =>
        uow.Connection.ExecuteAsync(new CommandDefinition(
            InsertSql, new { EventId = eventId, ConsumerName = consumerName },
            transaction: uow.Transaction, cancellationToken: ct));
}
