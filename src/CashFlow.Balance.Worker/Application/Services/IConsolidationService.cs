using CashFlow.Shared.Events;

namespace CashFlow.Balance.Worker.Application.Services;

public interface IConsolidationService
{
    Task ApplyAsync(TransactionRegistered evt, string consumerName, CancellationToken ct = default);
}
