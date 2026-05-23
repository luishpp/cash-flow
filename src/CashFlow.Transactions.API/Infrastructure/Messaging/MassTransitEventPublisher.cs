using MassTransit;

namespace CashFlow.Transactions.API.Infrastructure.Messaging;

public sealed class MassTransitEventPublisher(IPublishEndpoint publishEndpoint) : IEventPublisher
{
    public Task PublishAsync<T>(T evt, CancellationToken ct = default) where T : class =>
        publishEndpoint.Publish(evt, ct);
}
