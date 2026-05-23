namespace CashFlow.Transactions.API.Infrastructure.Messaging;

/// <summary>
/// <see cref="IEventPublisher"/> no-op para o ambiente <c>Testing</c> (ADR-022).
/// Permite que <c>WebApplicationFactory</c> suba o pipeline sem precisar de broker.
/// Testes que precisam observar publicação devem registrar sua própria implementação
/// via <c>ConfigureTestServices</c>.
/// </summary>
public sealed class NoOpEventPublisher : IEventPublisher
{
    public Task PublishAsync<T>(T evt, CancellationToken ct = default) where T : class =>
        Task.CompletedTask;
}
