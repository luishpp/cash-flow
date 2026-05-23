using CashFlow.Transactions.API.Infrastructure.Persistence;
using Dapper;

namespace CashFlow.Transactions.API.Infrastructure.Outbox;

/// <summary>
/// Implementação Dapper do outbox. <see cref="EnqueueAsync"/> usa a UoW corrente (mesma tx do
/// INSERT da transação); os outros métodos abrem conexão própria pois rodam fora da request.
/// </summary>
public sealed class OutboxRepository(IUnitOfWork uow, IDbConnectionFactory factory) : IOutboxRepository
{
    private const string EnqueueSql = @"
        INSERT INTO transactions.outbox_events (id, event_type, payload)
        VALUES (@Id, @EventType, CAST(@Payload AS JSONB));";

    private const string PeekSql = @"
        SELECT id, event_type, payload::text, created_at, attempts
          FROM transactions.outbox_events
         WHERE published_at IS NULL
         ORDER BY seq ASC
         LIMIT @BatchSize;";

    private const string MarkPublishedSql = @"
        UPDATE transactions.outbox_events
           SET published_at = NOW(), last_error = NULL
         WHERE id = @Id;";

    private const string RecordFailureSql = @"
        UPDATE transactions.outbox_events
           SET attempts = attempts + 1, last_error = @Error
         WHERE id = @Id;";

    public Task EnqueueAsync(Guid id, string eventType, string payload, CancellationToken ct = default) =>
        uow.Connection.ExecuteAsync(new CommandDefinition(
            EnqueueSql,
            new { Id = id, EventType = eventType, Payload = payload },
            transaction: uow.Transaction,
            cancellationToken: ct));

    public async Task<IReadOnlyList<OutboxEntry>> PeekPendingAsync(int batchSize, CancellationToken ct = default)
    {
        await using var conn = (Npgsql.NpgsqlConnection)await factory.CreateOpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<OutboxRow>(new CommandDefinition(
            PeekSql, new { BatchSize = batchSize }, cancellationToken: ct));
        return rows.Select(r => new OutboxEntry(
            r.Id, r.Event_Type, r.Payload,
            new DateTimeOffset(DateTime.SpecifyKind(r.Created_At, DateTimeKind.Utc)),
            r.Attempts)).ToList();
    }

    public async Task MarkPublishedAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = (Npgsql.NpgsqlConnection)await factory.CreateOpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            MarkPublishedSql, new { Id = id }, cancellationToken: ct));
    }

    public async Task RecordFailureAsync(Guid id, string error, CancellationToken ct = default)
    {
        await using var conn = (Npgsql.NpgsqlConnection)await factory.CreateOpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            RecordFailureSql, new { Id = id, Error = error }, cancellationToken: ct));
    }

    private sealed record OutboxRow(
        Guid Id, string Event_Type, string Payload, DateTime Created_At, int Attempts);
}
