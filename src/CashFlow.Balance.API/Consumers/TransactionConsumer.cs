using CashFlow.Balance.API.Application.Services;
using CashFlow.Shared.Events;
using MassTransit;
using Polly.Registry;

namespace CashFlow.Balance.API.Consumers;

/// <summary>
/// BackgroundService consumer (ADR-004) hospedado dentro da API de Balance.
/// Polly Retry com exponential backoff envolve a aplicação do evento (ADR-005).
/// Idempotência garantida em ConsolidationService via tabela processed_events (ADR-011).
/// </summary>
public sealed class TransactionConsumer(
    IConsolidationService consolidationService,
    ResiliencePipelineProvider<string> pipelineProvider,
    ILogger<TransactionConsumer> logger) : IConsumer<TransactionRegistered>
{
    public const string Name = nameof(TransactionConsumer);

    public async Task Consume(ConsumeContext<TransactionRegistered> context)
    {
        var pipeline = pipelineProvider.GetPipeline("consumer-pipeline");
        var evt = context.Message;

        await pipeline.ExecuteAsync(async ct =>
        {
            logger.LogDebug("Consumindo evento {EventId} (Transaction={TransactionId}).",
                evt.EventId, evt.TransactionId);

            await consolidationService.ApplyAsync(evt, Name, ct);
        }, context.CancellationToken);
    }
}
