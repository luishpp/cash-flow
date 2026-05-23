namespace CashFlow.Balance.API.Infrastructure.Repositories;

public interface IProcessedEventsRepository
{
    Task<bool> ExistsAsync(Guid eventId, string consumerName, CancellationToken ct = default);
    Task RegisterAsync(Guid eventId, string consumerName, CancellationToken ct = default);
}
