using CashFlow.Balance.Core.Domain.Entities;
using CashFlow.Balance.Core.Infrastructure.Persistence;
using CashFlow.Balance.Core.Infrastructure.Repositories;
using CashFlow.Balance.Worker.Infrastructure.Repositories;
using CashFlow.Shared.Events;

namespace CashFlow.Balance.Worker.Application.Services;

/// <summary>
/// Aplica o delta de um TransactionRegistered na projeção DailyBalance.
/// Idempotente: chamadas repetidas com o mesmo EventId são no-ops (ADR-011).
/// Atomicidade: upsert do saldo + marcação do evento ocorrem na mesma transação.
/// </summary>
public sealed class ConsolidationService(
    IUnitOfWork uow,
    IBalanceRepository balanceRepo,
    IProcessedEventsRepository eventsRepo,
    ILogger<ConsolidationService> logger) : IConsolidationService
{
    public async Task ApplyAsync(
        TransactionRegistered evt, string consumerName, CancellationToken ct = default)
    {
        await uow.BeginAsync(ct);
        try
        {
            if (await eventsRepo.ExistsAsync(evt.EventId, consumerName, ct))
            {
                logger.LogInformation(
                    "Evento {EventId} já processado por {Consumer} — ignorando (idempotência).",
                    evt.EventId, consumerName);
                await uow.CommitAsync(ct);
                return;
            }

            var existing = await balanceRepo.GetByDateAsync(evt.MovementDate, ct);
            var balance = existing ?? DailyBalance.New(evt.MovementDate);

            switch (evt.Type.ToLowerInvariant())
            {
                case "credit": balance.ApplyCredit(evt.Amount); break;
                case "debit":  balance.ApplyDebit(evt.Amount);  break;
                default:
                    throw new InvalidOperationException(
                        $"Tipo de transação desconhecido no evento: '{evt.Type}'.");
            }

            await balanceRepo.UpsertAsync(balance, ct);
            await eventsRepo.RegisterAsync(evt.EventId, consumerName, ct);
            await uow.CommitAsync(ct);

            logger.LogInformation(
                "Saldo de {Date} atualizado por evento {EventId} (Transaction={TransactionId}, Type={Type}, Amount={Amount:F2}).",
                evt.MovementDate, evt.EventId, evt.TransactionId, evt.Type, evt.Amount);
        }
        catch
        {
            await uow.RollbackAsync(ct);
            throw;
        }
    }
}
