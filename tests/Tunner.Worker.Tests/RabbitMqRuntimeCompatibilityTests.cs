using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Containers;
using RabbitMQ.Client;
using Tunner.Testing;
using Tunner.Worker.Messaging;
using Xunit;

namespace Tunner.Worker.Tests;

public sealed partial class OutboxDispatchCoordinatorTests
{
    [Fact]
    public async Task RealBrokerQuorumEnvelopeAndManagementSurviveRestart()
    {
        RequireRabbitMqTests();
        var password = Guid.NewGuid().ToString("N");
        await using var broker = TestcontainersBaseline.CreateRabbitMqContainer("fixture", password);
        await broker.StartAsync();
        await RequireManagementAsync(broker, password);
        var endpoint = new Uri($"amqp://fixture:{password}@{broker.Hostname}:{broker.GetMappedPublicPort(5672)}");
        var queueName = "fixture.quorum." + Guid.NewGuid().ToString("N");
        const string exchange = "fixture.quorum.outbox";
        var message = CreateMessage("platform.foundation.quorum");
        await using (var connection = await new ConnectionFactory { Uri = endpoint }.CreateConnectionAsync())
        await using (var channel = await connection.CreateChannelAsync())
        {
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true);
            await channel.QueueDeclareAsync(queue: queueName, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });
            await channel.QueueBindAsync(queueName, exchange, message.EventType);
            await using var publisher = new RabbitMqConfirmedOutboxPublisher(endpoint, exchange);
            await publisher.PublishConfirmedAsync(message);
        }
        await broker.StopAsync();
        await broker.StartAsync();
        await RequireManagementAsync(broker, password);
        endpoint = new Uri($"amqp://fixture:{password}@{broker.Hostname}:{broker.GetMappedPublicPort(5672)}");
        await using var restarted = await new ConnectionFactory { Uri = endpoint }.CreateConnectionAsync();
        await using var readChannel = await restarted.CreateChannelAsync();
        var received = await readChannel.BasicGetAsync(queueName, autoAck: true);
        Assert.NotNull(received);
        Assert.Equal(message.EventId.ToString("D"), received.BasicProperties.MessageId);
        Assert.Equal(message.Payload, Encoding.UTF8.GetString(received.Body.Span));
        Assert.Equal(DeliveryModes.Persistent, received.BasicProperties.DeliveryMode);
    }

    private static async Task RequireManagementAsync(IContainer broker, string password)
    {
        // Synthetic fixture credentials stay inside an isolated container; no host management port is opened.
        var authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes("fixture:" + password));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        do
        {
            var result = await broker.ExecAsync(new[] { "wget", "-qO-", "-T", "3", "-t", "1",
                "--header=Authorization: Basic " + authorization, "http://127.0.0.1:15672/api/overview" });
            if (result.ExitCode == 0)
            {
                using var overview = JsonDocument.Parse(result.Stdout);
                Assert.Equal("4.3.7", overview.RootElement.GetProperty("rabbitmq_version").GetString());
                Assert.Equal("4.3.7", overview.RootElement.GetProperty("management_version").GetString());
                return;
            }
            await Task.Delay(500);
        } while (DateTimeOffset.UtcNow < deadline);
        Assert.Fail("The complete management API must become ready before and after fixture restart.");
    }
}
