namespace CashFlow.Transactions.API.Infrastructure.Messaging;

/// <summary>
/// Abstração sobre o broker — desacopla Application Layer de MassTransit (ADR-002).
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<T>(T evt, CancellationToken ct = default) where T : class;
}
