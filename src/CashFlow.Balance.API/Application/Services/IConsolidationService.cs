using CashFlow.Shared.Events;

namespace CashFlow.Balance.API.Application.Services;

public interface IConsolidationService
{
    Task ApplyAsync(TransactionRegistered evt, string consumerName, CancellationToken ct = default);
}
