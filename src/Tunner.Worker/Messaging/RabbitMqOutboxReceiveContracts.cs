using RabbitMQ.Client;
using Tunner.Database.Outbox;

namespace Tunner.Worker.Messaging;

/// <summary>Only a completed durable callback permits acknowledgement. Other domain outcomes map to Retry.</summary>
public enum OutboxReceiveOutcome { Completed, Retry, Rejected }

public delegate ValueTask<OutboxReceiveOutcome> OutboxMessageProcessor(IServiceProvider services, PendingOutboxMessage message,
    CancellationToken cancellationToken);

/// <summary>Trusted environment composition must verify the exact existing queue's DLQ routing. Passive existence is insufficient.</summary>
public delegate Task RabbitMqOutboxTopologyQualification(IChannel channel, RabbitMqOutboxReceiveOptions options,
    CancellationToken cancellationToken);

/// <summary>No queue, topology, payload, polling or retry policy is selected by the receiver.</summary>
public sealed record RabbitMqOutboxReceiveOptions(string QueueName, string DeadLetterQueueName, string TopologyQualificationReference,
    int MaximumMessagesPerBatch, int MaximumPayloadBytes, TimeSpan PollDelay, TimeSpan RetryDelay)
{
    internal void Require()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(QueueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(DeadLetterQueueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(TopologyQualificationReference);
        if (QueueName == DeadLetterQueueName) throw new ArgumentException("Receive and dead-letter queues must be distinct.");
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumMessagesPerBatch, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumPayloadBytes, 1);
        RequireDelay(PollDelay); RequireDelay(RetryDelay);
    }

    private static void RequireDelay(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero || delay.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(delay), "An explicit positive supported delay is required.");
    }

    public override string ToString() => nameof(RabbitMqOutboxReceiveOptions);
}

public sealed record OutboxReceiveBatchResult(int Received, int Completed, int Requeued, int Rejected);

/// <summary>Sanitized transport failure; broker URIs, credentials, payloads and callback errors never become its message/inner exception.</summary>
public sealed class OutboxReceiveUnavailableException() : Exception("Outbox receive is unavailable.");
