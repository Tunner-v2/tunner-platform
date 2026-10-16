using System.Text;
using RabbitMQ.Client;
using Tunner.Testing;
using Tunner.Worker.Messaging;
using Xunit;
using Xunit.Sdk;

namespace Tunner.Worker.Tests;

public sealed partial class OutboxDispatchCoordinatorTests
{
    [Fact]
    public async Task RealBrokerConfirmsRoutedPersistentEnvelopeAndReturnsUnroutableMessages()
    {
        RequireRabbitMqTests();
        var password = Guid.NewGuid().ToString("N");
        await using var broker = TestcontainersBaseline.CreateRabbitMqContainer("fixture", password);
        await broker.StartAsync();
        var endpoint = new Uri($"amqp://fixture:{password}@{broker.Hostname}:{broker.GetMappedPublicPort(5672)}");
        await using var connection = await new ConnectionFactory { Uri = endpoint }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        const string exchange = "fixture.outbox";
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true);
        var queue = await channel.QueueDeclareAsync();
        await channel.QueueBindAsync(queue.QueueName, exchange, "platform.foundation.routed");
        var message = CreateMessage("platform.foundation.routed");
        var store = new RecordingStore([message]);
        await using var publisher = new RabbitMqConfirmedOutboxPublisher(endpoint, exchange);
        Assert.Equal(1, await new OutboxDispatchCoordinator(store, publisher, new FixedTimeProvider(PublishedAtUtc)).DispatchPendingAsync(1));
        var received = await channel.BasicGetAsync(queue.QueueName, autoAck: true);
        Assert.NotNull(received);
        Assert.Equal(message.Payload, Encoding.UTF8.GetString(received.Body.Span));
        Assert.Equal(message.EventId.ToString("D"), received.BasicProperties.MessageId);
        Assert.Equal(DeliveryModes.Persistent, received.BasicProperties.DeliveryMode);
        Assert.Equal(message.EventType, received.BasicProperties.Type);
        Assert.Equal("1.0", Encoding.UTF8.GetString((byte[])received.BasicProperties.Headers!["envelope_version"]!));
        Assert.Single(store.MarkAttempts);

        var unroutable = new RecordingStore([CreateMessage("platform.foundation.no-route")]);
        await Assert.ThrowsAnyAsync<Exception>(() => new OutboxDispatchCoordinator(unroutable, publisher, new FixedTimeProvider(PublishedAtUtc)).DispatchPendingAsync(1));
        Assert.Empty(unroutable.MarkAttempts);
    }

    [Fact]
    public async Task RealBrokerNackAndUnavailableBrokerLeaveTheOutboxPending()
    {
        RequireRabbitMqTests();
        var password = Guid.NewGuid().ToString("N");
        await using var broker = TestcontainersBaseline.CreateRabbitMqContainer("fixture", password);
        await broker.StartAsync();
        var endpoint = new Uri($"amqp://fixture:{password}@{broker.Hostname}:{broker.GetMappedPublicPort(5672)}");
        await using (var connection = await new ConnectionFactory { Uri = endpoint }.CreateConnectionAsync())
        await using (var channel = await connection.CreateChannelAsync())
        {
            const string exchange = "fixture.overflow";
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true);
            var queue = await channel.QueueDeclareAsync(arguments: new Dictionary<string, object?>
            {
                ["x-max-length"] = 1,
                ["x-overflow"] = "reject-publish"
            });
            await channel.QueueBindAsync(queue.QueueName, exchange, "platform.foundation.overflow");
            await using var publisher = new RabbitMqConfirmedOutboxPublisher(endpoint, exchange);
            await publisher.PublishConfirmedAsync(CreateMessage("platform.foundation.overflow"));
            var overflow = new RecordingStore([CreateMessage("platform.foundation.overflow")]);
            await Assert.ThrowsAnyAsync<Exception>(() => new OutboxDispatchCoordinator(overflow, publisher, new FixedTimeProvider(PublishedAtUtc)).DispatchPendingAsync(1));
            Assert.Empty(overflow.MarkAttempts);
        }
        await broker.StopAsync();
        await using var unavailable = new RabbitMqConfirmedOutboxPublisher(endpoint, "fixture.unavailable");
        var pending = new RecordingStore([CreateMessage("platform.foundation.unavailable")]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<Exception>(() => new OutboxDispatchCoordinator(pending, unavailable, new FixedTimeProvider(PublishedAtUtc)).DispatchPendingAsync(1, timeout.Token));
        Assert.Empty(pending.MarkAttempts);
    }

    private static void RequireRabbitMqTests()
    {
        if (Environment.GetEnvironmentVariable("TUNNER_RUN_CONTAINER_TESTS") != "1")
            throw SkipException.ForSkip("Set TUNNER_RUN_CONTAINER_TESTS=1 to run RabbitMQ container integration tests.");
    }
}
