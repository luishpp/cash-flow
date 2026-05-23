using System.Text.Json;
using CashFlow.Shared.Events;
using CashFlow.Transactions.API.Application.DTOs;
using CashFlow.Transactions.API.Domain.Entities;
using CashFlow.Transactions.API.Domain.ValueObjects;
using CashFlow.Transactions.API.Infrastructure.Outbox;
using CashFlow.Transactions.API.Infrastructure.Persistence;
using CashFlow.Transactions.API.Infrastructure.Repositories;

namespace CashFlow.Transactions.API.Application.Services;

public sealed class TransactionService(
    IUnitOfWork uow,
    ITransactionRepository repo,
    IOutboxRepository outbox,
    ILogger<TransactionService> logger) : ITransactionService
{
    public async Task<IReadOnlyList<TransactionResponse>> RegisterManyAsync(
        IReadOnlyList<RegisterTransactionRequest> requests, CancellationToken ct = default)
    {
        var transactions = new List<Transaction>(requests.Count);
        foreach (var req in requests)
        {
            var type = TransactionTypeExtensions.Parse(req.Type);
            transactions.Add(Transaction.Register(req.Amount, type, req.Description, req.MovementDate));
        }

        await uow.BeginAsync(ct);
        try
        {
            foreach (var tx in transactions)
            {
                await repo.InsertAsync(tx, ct);

                var evt = new TransactionRegistered(
                    EventId: Guid.NewGuid(),
                    TransactionId: tx.Id,
                    Amount: tx.Amount.Amount,
                    Type: tx.Type.ToSnakeCase(),
                    Description: tx.Description,
                    MovementDate: tx.MovementDate.Value,
                    RegisteredAt: DateTimeOffset.UtcNow);

                // INSERT + outbox na mesma tx — atomicidade entre escrita e intenção de publicar
                // fecha a janela do ADR-007 (Balance nunca fica defasada por publish perdido).
                await outbox.EnqueueAsync(
                    evt.EventId,
                    nameof(TransactionRegistered),
                    JsonSerializer.Serialize(evt),
                    ct);
            }
            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(ct);
            throw;
        }

        logger.LogInformation(
            "{Count} transação(ões) persistida(s) — eventos enfileirados no outbox para dispatch assíncrono.",
            transactions.Count);

        return transactions.Select(ToResponse).ToList();
    }

    public async Task<TransactionResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await uow.BeginAsync(ct);
        var transaction = await repo.GetByIdAsync(id, ct);
        return transaction is null ? null : ToResponse(transaction);
    }

    public async Task<IReadOnlyList<TransactionResponse>> ListByDateAsync(
        DateOnly date, CancellationToken ct = default)
    {
        await uow.BeginAsync(ct);
        var transactions = await repo.ListByDateAsync(date, ct);
        return transactions.Select(ToResponse).ToList();
    }

    private static TransactionResponse ToResponse(Transaction t) => new(
        t.Id, t.Type.ToSnakeCase(), t.Amount.Amount, t.Description,
        t.MovementDate.Value, t.CreatedAt);
}
