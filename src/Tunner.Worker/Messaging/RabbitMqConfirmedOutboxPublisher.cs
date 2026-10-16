using System.Globalization;
using System.Text;
using RabbitMQ.Client;
using Tunner.Database.Outbox;

namespace Tunner.Worker.Messaging;

/// <summary>RabbitMQ.Client publisher that awaits tracked publisher confirmations before returning.</summary>
public sealed class RabbitMqConfirmedOutboxPublisher : IConfirmedOutboxPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim publishLock = new(1, 1);
    private readonly ConnectionFactory factory;
    private readonly string exchangeName;
    private IConnection? connection;
    private IChannel? channel;
    private bool disposed;

    public RabbitMqConfirmedOutboxPublisher(Uri endpoint, string exchangeName)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Scheme is not "amqp" and not "amqps")
        {
            throw new ArgumentException("RabbitMQ endpoint must use amqp or amqps.", nameof(endpoint));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(exchangeName);
        this.exchangeName = exchangeName;
        factory = new ConnectionFactory
        {
            Uri = endpoint,
            ClientProvidedName = "tunner-worker:outbox-dispatcher"
        };
    }

    public async Task PublishConfirmedAsync(PendingOutboxMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await publishLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var activeChannel = await GetChannelAsync(cancellationToken).ConfigureAwait(false);
            var properties = new BasicProperties
            {
                MessageId = message.EventId.ToString("D"),
                Type = message.EventType,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Headers = CreateEnvelopeHeaders(message)
            };

            await activeChannel.BasicPublishAsync(
                exchangeName,
                message.EventType,
                mandatory: true,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(message.Payload),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            publishLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await publishLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (channel is not null)
            {
                await channel.DisposeAsync().ConfigureAwait(false);
                channel = null;
            }

            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                connection = null;
            }
        }
        finally
        {
            publishLock.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private static Dictionary<string, object?> CreateEnvelopeHeaders(PendingOutboxMessage message)
    {
        var headers = new Dictionary<string, object?>
        {
            ["envelope_version"] = message.EnvelopeVersion,
            ["event_schema_version"] = message.EventSchemaVersion,
            ["occurred_at_utc"] = message.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["source"] = message.Source,
            ["subject"] = message.Subject,
            ["data_classification"] = message.DataClassification
        };

        AddOptionalHeader(headers, "resource_version", message.ResourceVersion);
        AddOptionalHeader(headers, "operation_id", message.OperationId);
        AddOptionalHeader(headers, "correlation_id", message.CorrelationId);
        AddOptionalHeader(headers, "causation_event_id", message.CausationEventId);
        return headers;
    }

    private static void AddOptionalHeader(Dictionary<string, object?> headers, string name, string? value)
    {
        if (value is not null)
        {
            headers.Add(name, value);
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (channel is not null && channel.IsOpen)
        {
            return channel;
        }

        if (channel is not null)
        {
            await channel.DisposeAsync().ConfigureAwait(false);
            channel = null;
        }

        if (connection is null || !connection.IsOpen)
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken).ConfigureAwait(false);
        await channel.ExchangeDeclareAsync(exchangeName, ExchangeType.Topic, durable: true, autoDelete: false, arguments: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        return channel;
    }
}