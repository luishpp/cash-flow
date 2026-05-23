using CashFlow.Shared.Events;
using CashFlow.Transactions.API.Application.DTOs;
using CashFlow.Transactions.API.Domain.Entities;
using CashFlow.Transactions.API.Domain.ValueObjects;
using CashFlow.Transactions.API.Infrastructure.Messaging;
using CashFlow.Transactions.API.Infrastructure.Persistence;
using CashFlow.Transactions.API.Infrastructure.Repositories;

namespace CashFlow.Transactions.API.Application.Services;

public sealed class TransactionService(
    IUnitOfWork uow,
    ITransactionRepository repo,
    IEventPublisher publisher,
    ILogger<TransactionService> logger) : ITransactionService
{
    public async Task<TransactionResponse> RegisterAsync(
        RegisterTransactionRequest request, CancellationToken ct = default)
    {
        var type = TransactionTypeExtensions.Parse(request.Type);
        var transaction = Transaction.Register(request.Amount, type, request.Description, request.MovementDate);

        await uow.BeginAsync(ct);
        try
        {
            await repo.InsertAsync(transaction, ct);
            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(ct);
            throw;
        }

        // Publica APÓS commit (ADR-007). Falha aqui é cenário documentado;
        // Outbox Pattern é evolução natural (ADR-011 documenta o trade-off).
        var evt = new TransactionRegistered(
            EventId: Guid.NewGuid(),
            TransactionId: transaction.Id,
            Amount: transaction.Amount.Amount,
            Type: transaction.Type.ToSnakeCase(),
            Description: transaction.Description,
            MovementDate: transaction.MovementDate.Value,
            RegisteredAt: DateTimeOffset.UtcNow);

        try
        {
            await publisher.PublishAsync(evt, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Falha ao publicar TransactionRegistered (EventId={EventId}, TransactionId={TransactionId}). " +
                "Transação foi persistida — Balance ficará defasado até reconciliação.",
                evt.EventId, transaction.Id);
        }

        return ToResponse(transaction);
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
