using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using RabbitMQ.Client;
using Tunner.Database.Outbox;
using Tunner.Worker.Messaging;
using Xunit;

namespace Tunner.Worker.Tests;

public sealed class RabbitMqOutboxReceiverIntegrationTests(OutboxReceiveBrokerFixture fixture) : IClassFixture<OutboxReceiveBrokerFixture>
{
    [Fact]
    public async Task RealBrokerRetryKeepsOriginalIdsStopsTheBatchAndUsesOneFreshDisposedScopePerDelivery()
    {
        var options = RabbitMqOutboxReceiverTests.Options();
        await using var connection = await fixture.ConnectionFactory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await OutboxReceiveBrokerFixture.QualifyQueueAsync(channel, options, CancellationToken.None);
        var original = RabbitMqOutboxEnvelopeTests.Message(); var second = RabbitMqOutboxEnvelopeTests.Message();
        var exchange = await PublishAsync(channel, options, original, second);
        var tracker = new ScopeTracker(); var services = new ServiceCollection().AddScoped(_ => new ScopedProbe(tracker));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var deliveries = new List<PendingOutboxMessage>(); var first = true;
        await using var receiver = new RabbitMqOutboxReceiver(options, fixture.ConnectionFactory, provider.GetRequiredService<IServiceScopeFactory>(),
            (scope, message, _) =>
            {
                scope.GetRequiredService<ScopedProbe>(); deliveries.Add(message);
                if (first) { first = false; return ValueTask.FromResult(OutboxReceiveOutcome.Retry); }
                return ValueTask.FromResult(OutboxReceiveOutcome.Completed);
            }, OutboxReceiveBrokerFixture.QualifyQueueAsync);
        var retry = await receiver.ReceiveBatchAsync();
        Assert.Equal(new OutboxReceiveBatchResult(1, 0, 1, 0), retry);
        Assert.Equal(original, Assert.Single(deliveries));
        var completed = await receiver.ReceiveBatchAsync();
        Assert.Equal(new OutboxReceiveBatchResult(2, 2, 0, 0), completed);
        Assert.Equal(2, deliveries.Count(message => message.EventId == original.EventId));
        Assert.Contains(second, deliveries); Assert.Equal(3, tracker.Created); Assert.Equal(3, tracker.Disposed);
        Assert.Null(await channel.BasicGetAsync(options.QueueName, autoAck: true));
        await channel.ExchangeDeleteAsync(exchange, ifUnused: false);
    }

    [Fact]
    public async Task RealBrokerRejectedAndMalformedMessagesReachOnlyTheQualifiedDeadLetterQueueWithoutNewIds()
    {
        var options = RabbitMqOutboxReceiverTests.Options();
        await using var connection = await fixture.ConnectionFactory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await OutboxReceiveBrokerFixture.QualifyQueueAsync(channel, options, CancellationToken.None);
        var original = RabbitMqOutboxEnvelopeTests.Message(); var invalid = RabbitMqOutboxEnvelopeTests.Message() with { Payload = "{" };
        _ = await PublishAsync(channel, options, original, invalid);
        await using var provider = new ServiceCollection().BuildServiceProvider(); var callbacks = 0;
        await using var receiver = new RabbitMqOutboxReceiver(options, fixture.ConnectionFactory, provider.GetRequiredService<IServiceScopeFactory>(),
            (_, _, _) => { callbacks++; return ValueTask.FromResult(OutboxReceiveOutcome.Rejected); }, OutboxReceiveBrokerFixture.QualifyQueueAsync);
        Assert.Equal(new OutboxReceiveBatchResult(2, 0, 0, 2), await receiver.ReceiveBatchAsync());
        Assert.Equal(1, callbacks);
        var dead = await ReadEventuallyAsync(channel, options.DeadLetterQueueName);
        Assert.True(RabbitMqOutboxEnvelope.TryDecode(dead.BasicProperties, dead.Body, options.MaximumPayloadBytes, out var restored));
        Assert.Equal(original, restored); Assert.Contains("x-death", dead.BasicProperties.Headers!.Keys);
        var malformed = await ReadEventuallyAsync(channel, options.DeadLetterQueueName);
        Assert.Equal(invalid.EventId.ToString("D"), malformed.BasicProperties.MessageId); Assert.Equal("{"u8.ToArray(), malformed.Body.ToArray());
        Assert.Null(await channel.BasicGetAsync(options.QueueName, autoAck: true));
    }

    [Fact]
    public async Task RealBrokerCancellationBeforeDurableCompletionReturnsTheUnacknowledgedOriginalDelivery()
    {
        var options = RabbitMqOutboxReceiverTests.Options();
        await using var connection = await fixture.ConnectionFactory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await OutboxReceiveBrokerFixture.QualifyQueueAsync(channel, options, CancellationToken.None);
        var original = RabbitMqOutboxEnvelopeTests.Message(); _ = await PublishAsync(channel, options, original);
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var receiver = new RabbitMqOutboxReceiver(options, fixture.ConnectionFactory, provider.GetRequiredService<IServiceScopeFactory>(),
            async (_, _, cancellationToken) => { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return OutboxReceiveOutcome.Completed; },
            OutboxReceiveBrokerFixture.QualifyQueueAsync);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pending = receiver.ReceiveBatchAsync(cancellation.Token); await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(await channel.BasicGetAsync(options.QueueName, autoAck: true)); // It is still unacked on the receiver channel.
        await cancellation.CancelAsync(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var returned = await ReadEventuallyAsync(channel, options.QueueName);
        Assert.True(returned.Redelivered); Assert.Equal(original.EventId.ToString("D"), returned.BasicProperties.MessageId);
    }

    [Fact]
    public async Task RealBrokerTopologyFailureAndUncertainCallbackCannotAcknowledgeOrExposeRawErrors()
    {
        var options = RabbitMqOutboxReceiverTests.Options();
        await using var connection = await fixture.ConnectionFactory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await OutboxReceiveBrokerFixture.QualifyQueueAsync(channel, options, CancellationToken.None);
        var original = RabbitMqOutboxEnvelopeTests.Message(); _ = await PublishAsync(channel, options, original);
        await using var provider = new ServiceCollection().BuildServiceProvider(); var callbacks = 0;
        await using (var unavailable = new RabbitMqOutboxReceiver(options, fixture.ConnectionFactory, provider.GetRequiredService<IServiceScopeFactory>(),
            (_, _, _) => { callbacks++; return ValueTask.FromResult(OutboxReceiveOutcome.Completed); },
            static (_, _, _) => throw new InvalidOperationException("SYNTHETIC_UNSAFE_BROKER_ERROR")))
        {
            var failure = await Assert.ThrowsAsync<OutboxReceiveUnavailableException>(() => unavailable.ReceiveBatchAsync());
            Assert.Equal("Outbox receive is unavailable.", failure.Message); Assert.Null(failure.InnerException); Assert.Equal(0, callbacks);
        }
        await using (var uncertain = new RabbitMqOutboxReceiver(options, fixture.ConnectionFactory, provider.GetRequiredService<IServiceScopeFactory>(),
            static (_, _, _) => throw new InvalidOperationException("SYNTHETIC_UNSAFE_CALLBACK_ERROR"), OutboxReceiveBrokerFixture.QualifyQueueAsync))
            Assert.Equal(new OutboxReceiveBatchResult(1, 0, 1, 0), await uncertain.ReceiveBatchAsync());
        var returned = await ReadEventuallyAsync(channel, options.QueueName);
        Assert.Equal(original.EventId.ToString("D"), returned.BasicProperties.MessageId); Assert.True(returned.Redelivered);
    }

    [Fact]
    public async Task RealBrokerChannelLossAfterCallbackCommitRedeliversTheSameDownstreamIdentityOnARequalifiedChannel()
    {
        var options = RabbitMqOutboxReceiverTests.Options(1);
        await using var connection = await fixture.ConnectionFactory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await OutboxReceiveBrokerFixture.QualifyQueueAsync(channel, options, CancellationToken.None);
        var original = RabbitMqOutboxEnvelopeTests.Message(); _ = await PublishAsync(channel, options, original);
        await using var provider = new ServiceCollection().BuildServiceProvider();
        IChannel? receiverChannel = null; var qualified = 0; var callbacks = 0; var durableIds = new HashSet<Guid>();
        await using var receiver = new RabbitMqOutboxReceiver(options, fixture.ConnectionFactory, provider.GetRequiredService<IServiceScopeFactory>(),
            async (_, message, _) =>
            {
                callbacks++; durableIds.Add(message.EventId); // Synthetic durable-idempotent downstream boundary.
                if (callbacks == 1) await receiverChannel!.CloseAsync(CancellationToken.None);
                return OutboxReceiveOutcome.Completed;
            }, async (active, configured, cancellationToken) =>
            {
                qualified++; receiverChannel = active;
                await OutboxReceiveBrokerFixture.QualifyQueueAsync(active, configured, cancellationToken);
            });
        await Assert.ThrowsAsync<OutboxReceiveUnavailableException>(() => receiver.ReceiveBatchAsync());
        Assert.Equal(new OutboxReceiveBatchResult(1, 1, 0, 0), await receiver.ReceiveBatchAsync());
        Assert.Equal(original.EventId, Assert.Single(durableIds)); Assert.Equal(2, callbacks); Assert.Equal(2, qualified);
        Assert.Null(await channel.BasicGetAsync(options.QueueName, autoAck: true));
    }

    [Fact]
    public async Task RealHostedReceiverLogsRetryCountsAndUnavailableEventWithoutRawCallbackOrTransportMaterial()
    {
        var options = RabbitMqOutboxReceiverTests.Options(1);
        await using var connection = await fixture.ConnectionFactory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await OutboxReceiveBrokerFixture.QualifyQueueAsync(channel, options, CancellationToken.None);
        var original = RabbitMqOutboxEnvelopeTests.Message() with
        { Payload = "{\"secret\":\"SYNTHETIC_PRIVATE_PAYLOAD\"}", Subject = "SYNTHETIC_PRIVATE_HEADER" };
        _ = await PublishAsync(channel, options, original);
        using var logs = new CapturedReceiveLogs(); var calls = 0;
        var services = new ServiceCollection(); services.AddLogging(builder => builder.AddProvider(logs));
        new RabbitMqOutboxReceiveModule(options, fixture.ConnectionFactory,
            static (_, _, _) => throw new InvalidOperationException("SYNTHETIC_PRIVATE_CALLBACK"),
            async (active, configured, cancellationToken) =>
            {
                calls++;
                if (calls > 1) throw new InvalidOperationException("amqp://fixture:SYNTHETIC_PRIVATE_CREDENTIAL@private.invalid");
                await OutboxReceiveBrokerFixture.QualifyQueueAsync(active, configured, cancellationToken);
            }).Register(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var hosted = Assert.Single(provider.GetServices<IHostedService>());
        await hosted.StartAsync(CancellationToken.None);
        try { await logs.Unavailable.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await hosted.StopAsync(shutdown.Token);
        }
        var batch = Assert.Single(logs.Entries, entry => entry.EventId.Id == 1601);
        Assert.Equal("OutboxReceiveBatch", batch.EventId.Name);
        Assert.Equal(1, batch.State["Received"]); Assert.Equal(0, batch.State["Completed"]);
        Assert.Equal(1, batch.State["Requeued"]); Assert.Equal(0, batch.State["Rejected"]);
        Assert.Contains(logs.Entries, entry => entry.EventId.Id == 1602 && entry.EventId.Name == "OutboxReceiveUnavailable");
        Assert.All(logs.Entries, entry =>
        {
            Assert.Null(entry.Exception);
            Assert.DoesNotContain("SYNTHETIC_PRIVATE", entry.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(options.QueueName, entry.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("amqp://", entry.Text, StringComparison.Ordinal);
            Assert.All(entry.State.Values, value => Assert.DoesNotContain("SYNTHETIC_PRIVATE", value?.ToString() ?? "", StringComparison.Ordinal));
        });
        var returned = await ReadEventuallyAsync(channel, options.QueueName);
        Assert.Equal(original.EventId.ToString("D"), returned.BasicProperties.MessageId);
    }

    private async Task<string> PublishAsync(IChannel channel, RabbitMqOutboxReceiveOptions options, params PendingOutboxMessage[] messages)
    {
        var exchange = "fixture.receive.exchange." + Guid.NewGuid().ToString("N");
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        foreach (var eventType in messages.Select(message => message.EventType).Distinct(StringComparer.Ordinal))
            await channel.QueueBindAsync(options.QueueName, exchange, eventType);
        await using var publisher = new RabbitMqConfirmedOutboxPublisher(fixture.ConnectionFactory.Uri, exchange);
        foreach (var message in messages) await publisher.PublishConfirmedAsync(message);
        return exchange;
    }

    private static async Task<BasicGetResult> ReadEventuallyAsync(IChannel channel, string queue)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var message = await channel.BasicGetAsync(queue, autoAck: true, deadline.Token);
            if (message is not null) return message;
            await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token);
        }
    }

    private sealed class ScopeTracker { public int Created; public int Disposed; }
    private sealed record ReceiveLog(EventId EventId, string Text, IReadOnlyDictionary<string, object?> State, Exception? Exception);
    private sealed class CapturedReceiveLogs : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<ReceiveLog> Entries { get; } = new();
        public TaskCompletionSource Unavailable { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ILogger CreateLogger(string categoryName) => this;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> structured
                ? structured.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>();
            Entries.Enqueue(new(eventId, formatter(state, exception), values, exception));
            if (eventId.Id == 1602) Unavailable.TrySetResult();
        }
        public void Dispose() { }
    }
    private sealed class ScopedProbe : IAsyncDisposable
    {
        private readonly ScopeTracker tracker;
        public ScopedProbe(ScopeTracker tracker) { this.tracker = tracker; tracker.Created++; }
        public ValueTask DisposeAsync() { tracker.Disposed++; return ValueTask.CompletedTask; }
    }
}
