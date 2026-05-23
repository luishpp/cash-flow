using System.Text.Json;
using CashFlow.Shared.Events;
using CashFlow.Transactions.API.Infrastructure.Messaging;
using Npgsql;

namespace CashFlow.Transactions.API.Infrastructure.Outbox;

/// <summary>
/// BackgroundService que drena <c>transactions.outbox_events</c> e publica no broker.
/// Garante at-least-once ponta-a-ponta: o INSERT da transação e o registro no outbox são
/// na MESMA tx; o publish acontece depois, com retentativas independentes do request HTTP.
/// Idempotência do consumer (tabela <c>processed_events</c>) protege contra duplicações
/// caso o publish suceda mas a marcação de "publicado" falhe.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxDispatcher iniciado (batch={BatchSize}).", BatchSize);

        // Evita poluir o log com a mesma stack a cada 5s quando dependência (DB/broker)
        // está fora; loga uma vez no início do outage e uma vez na recuperação.
        bool inOutage = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchBatchAsync(stoppingToken);
                if (inOutage)
                {
                    logger.LogInformation("OutboxDispatcher recuperou conexão com a dependência.");
                    inOutage = false;
                }
                if (dispatched == 0)
                    await Task.Delay(IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                if (!inOutage)
                {
                    logger.LogWarning(
                        "OutboxDispatcher: dependência indisponível ({Type}: {Message}). " +
                        "Continua tentando a cada {Backoff}s — próximos erros suprimidos até recuperar.",
                        ex.GetType().Name, ex.Message, ErrorBackoff.TotalSeconds);
                    inOutage = true;
                }
                await Task.Delay(ErrorBackoff, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro inesperado no loop do OutboxDispatcher — backoff.");
                await Task.Delay(ErrorBackoff, stoppingToken);
            }
        }

        logger.LogInformation("OutboxDispatcher parado.");
    }

    private static bool IsTransient(Exception ex) =>
        ex is NpgsqlException                                              // DB inacessível
        || ex is System.Net.Sockets.SocketException                        // DNS/socket
        || ex.InnerException is System.Net.Sockets.SocketException
        || ex is TimeoutException;

    private async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        // AsyncScope obrigatório: DapperUnitOfWork implementa só IAsyncDisposable.
        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        var pending = await repo.PeekPendingAsync(BatchSize, ct);
        if (pending.Count == 0) return 0;

        int published = 0;
        foreach (var entry in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await PublishByTypeAsync(entry.EventType, entry.Payload, publisher, ct);
                await repo.MarkPublishedAsync(entry.Id, ct);
                published++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Falha ao publicar outbox entry {Id} (attempts={Attempts}). Vai retentar no próximo ciclo.",
                    entry.Id, entry.Attempts);
                await repo.RecordFailureAsync(entry.Id, ex.Message, ct);
                // Quebra o batch — preserva ordem FIFO (não pula evento que falhou).
                break;
            }
        }

        if (published > 0)
            logger.LogInformation("OutboxDispatcher publicou {Count} evento(s).", published);

        return published;
    }

    // Switch por tipo concreto preserva o T de IPublishEndpoint, indispensável p/ MassTransit
    // rotear no exchange correto (resolvido por T, não pelo conteúdo do payload).
    private static Task PublishByTypeAsync(string eventType, string payload, IEventPublisher publisher, CancellationToken ct)
        => eventType switch
        {
            nameof(TransactionRegistered) => publisher.PublishAsync(
                JsonSerializer.Deserialize<TransactionRegistered>(payload)
                    ?? throw new InvalidOperationException($"Payload nulo p/ {eventType}."),
                ct),
            _ => throw new InvalidOperationException($"Tipo desconhecido no outbox: '{eventType}'.")
        };
}
