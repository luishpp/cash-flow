using RabbitMQ.Client;

namespace CashFlow.Balance.API.Application.Admin;

/// <summary>
/// Move mensagens da DLQ (<c>balance.transaction-registered_error</c>, criada pela MassTransit
/// quando o retry esgota) de volta para a fila principal para reprocessamento manual.
///
/// Uso esperado: após investigar o motivo do failure (logs, banco, etc.) e ter certeza
/// de que o retry agora vai passar. A idempotência do consumer (tabela <c>processed_events</c>)
/// já protege contra duplicação caso a mensagem original tenha sido parcialmente processada.
/// </summary>
public sealed class ErrorQueueRedeliveryService(IConfiguration configuration, ILogger<ErrorQueueRedeliveryService> logger)
{
    public const string ErrorQueueName = "balance.transaction-registered_error";
    public const string TargetQueueName = "balance.transaction-registered";

    public async Task<int> RedeliverAllAsync(int? maxMessages, CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = configuration.GetValue<string>("RabbitMq:Host") ?? "rabbitmq",
            UserName = configuration.GetValue<string>("RabbitMq:User") ?? "guest",
            Password = configuration.GetValue<string>("RabbitMq:Password") ?? "guest"
        };

        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        int moved = 0;
        while (!ct.IsCancellationRequested && (maxMessages is null || moved < maxMessages))
        {
            var result = await channel.BasicGetAsync(ErrorQueueName, autoAck: false, ct);
            if (result is null) break;

            // Republica mantendo headers/properties — preserva message-id, content-type, etc.
            var props = new BasicProperties(result.BasicProperties);

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: TargetQueueName,
                mandatory: false,
                basicProperties: props,
                body: result.Body,
                cancellationToken: ct);

            await channel.BasicAckAsync(result.DeliveryTag, multiple: false, ct);
            moved++;
        }

        logger.LogInformation(
            "Redelivery: {Moved} mensagem(ns) movida(s) de '{Error}' para '{Target}'.",
            moved, ErrorQueueName, TargetQueueName);
        return moved;
    }

    public async Task<uint> PeekErrorCountAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = configuration.GetValue<string>("RabbitMq:Host") ?? "rabbitmq",
            UserName = configuration.GetValue<string>("RabbitMq:User") ?? "guest",
            Password = configuration.GetValue<string>("RabbitMq:Password") ?? "guest"
        };

        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        // QueueDeclarePassive falha se a queue não existir — capturamos para retornar 0.
        try
        {
            var ok = await channel.QueueDeclarePassiveAsync(ErrorQueueName, ct);
            return ok.MessageCount;
        }
        catch
        {
            return 0;
        }
    }
}
