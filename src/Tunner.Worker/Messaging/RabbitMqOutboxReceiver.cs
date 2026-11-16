using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Tunner.Worker.Messaging;

/// <summary>One finite sequential manual-ack batch. Durable deduplication and business effects belong to the scoped callback.</summary>
public sealed class RabbitMqOutboxReceiver : IAsyncDisposable
{
    private readonly RabbitMqOutboxReceiveOptions options;
    private readonly ConnectionFactory factory;
    private readonly IServiceScopeFactory scopes;
    private readonly OutboxMessageProcessor process;
    private readonly RabbitMqOutboxTopologyQualification qualify;
    private readonly SemaphoreSlim receiveLock = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;
    private bool disposed;

    public RabbitMqOutboxReceiver(RabbitMqOutboxReceiveOptions options, ConnectionFactory factory, IServiceScopeFactory scopes,
        OutboxMessageProcessor process, RabbitMqOutboxTopologyQualification qualify)
    {
        ArgumentNullException.ThrowIfNull(options); ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(scopes); ArgumentNullException.ThrowIfNull(process); ArgumentNullException.ThrowIfNull(qualify);
        options.Require();
        if (factory.AutomaticRecoveryEnabled)
            throw new ArgumentException("The caller must explicitly disable automatic recovery; each retry requalifies a new channel.", nameof(factory));
        this.options = options; this.factory = factory; this.scopes = scopes; this.process = process; this.qualify = qualify;
    }

    public async Task<OutboxReceiveBatchResult> ReceiveBatchAsync(CancellationToken cancellationToken = default)
    {
        await receiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var active = await GetChannelAsync(cancellationToken).ConfigureAwait(false);
            await qualify(active, options, cancellationToken).ConfigureAwait(false);
            var received = 0; var completed = 0; var rejected = 0;
            for (var index = 0; index < options.MaximumMessagesPerBatch; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var delivery = await active.BasicGetAsync(options.QueueName, autoAck: false, cancellationToken).ConfigureAwait(false);
                if (delivery is null) break;
                received++;
                var outcome = OutboxReceiveOutcome.Rejected;
                if (RabbitMqOutboxEnvelope.TryDecode(delivery.BasicProperties, delivery.Body, options.MaximumPayloadBytes, out var message))
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        outcome = await process(scope.ServiceProvider, message!, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
                    {
                        // Any uncertain callback result stays retryable. Never log or expose its exception/payload.
                        outcome = OutboxReceiveOutcome.Retry;
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (outcome == OutboxReceiveOutcome.Completed)
                {
                    await active.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken).ConfigureAwait(false);
                    completed++;
                }
                else if (outcome == OutboxReceiveOutcome.Rejected)
                {
                    // Only the required environment qualification permits trusting this existing broker DLQ route.
                    await qualify(active, options, cancellationToken).ConfigureAwait(false);
                    await active.BasicRejectAsync(delivery.DeliveryTag, requeue: false, cancellationToken).ConfigureAwait(false);
                    rejected++;
                }
                else
                {
                    await active.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken).ConfigureAwait(false);
                    // Stop this batch so the hosted loop applies the explicitly configured retry backoff.
                    return new(received, completed, 1, rejected);
                }
            }
            return new(received, completed, 0, rejected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ResetAsync().ConfigureAwait(false); // Closing the owned channel returns any unacked delivery to RabbitMQ.
            throw;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            await ResetAsync().ConfigureAwait(false);
            throw new OutboxReceiveUnavailableException();
        }
        finally { receiveLock.Release(); }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (channel is { IsOpen: true } && connection is { IsOpen: true }) return channel;
        await ResetAsync().ConfigureAwait(false);
        connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        // These checks establish existence only, never proof of broker DLQ arguments or policy.
        await channel.QueueDeclarePassiveAsync(options.QueueName, cancellationToken).ConfigureAwait(false);
        await channel.QueueDeclarePassiveAsync(options.DeadLetterQueueName, cancellationToken).ConfigureAwait(false);
        return channel;
    }

    private async ValueTask ResetAsync()
    {
        var oldChannel = channel; var oldConnection = connection; channel = null; connection = null;
        try { if (oldChannel is not null) await oldChannel.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { /* Cleanup cannot expose raw broker details. */ }
        try { if (oldConnection is not null) await oldConnection.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { /* The connection is no longer reused. */ }
    }

    public async ValueTask DisposeAsync()
    {
        await receiveLock.WaitAsync().ConfigureAwait(false);
        try { if (disposed) return; disposed = true; await ResetAsync().ConfigureAwait(false); }
        finally { receiveLock.Release(); }
    }
}
