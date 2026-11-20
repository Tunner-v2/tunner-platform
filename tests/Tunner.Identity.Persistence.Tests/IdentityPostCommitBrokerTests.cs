using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Tunner.Database.Outbox;
using Tunner.Platform;
using Tunner.Worker;
using Tunner.Worker.Messaging;
using Tunner.Worker.Tests;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed partial class IdentityPostCommitTests
{
    [Fact]
    public async Task RealHostedWorkerProcessesCommittedIdentityOutboxRetriesHandoffAndAcknowledgesExactDuplicates()
    {
        await using var h = await Harness.Start(fixture);
        var broker = new OutboxReceiveBrokerFixture();
        try
        {
            await broker.InitializeAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2)); var ct = deadline.Token;
            var id = Guid.NewGuid().ToString("N");
            var options = new RabbitMqOutboxReceiveOptions("fixture.identity." + id, "fixture.identity.dead." + id,
                "fixture-explicit-topology", 8, 131072, TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(25));
            var factory = broker.ConnectionFactory;
            await using var connection = await factory.CreateConnectionAsync(ct);
            await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
            await OutboxReceiveBrokerFixture.QualifyQueueAsync(channel, options, ct);
            var exchange = "fixture.identity.events." + id;
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
            await channel.QueueBindAsync(options.QueueName, exchange, h.Message.EventType, cancellationToken: ct);
            var outcomes = Channel.CreateUnbounded<IdentityPostCommitOutcome>(); var failedCallbacks = 0;
            h.Dispatcher.FailNext = true;
            var module = new RabbitMqOutboxReceiveModule(options, broker.ConnectionFactory, async (services, message, token) =>
            {
                IdentityPostCommitOutcome outcome;
                try { outcome = await services.GetRequiredService<IdentityPostCommitProcessor>().ProcessAsync(message, token); }
                catch (InvalidOperationException) { Interlocked.Increment(ref failedCallbacks); throw; }
                Assert.True(outcomes.Writer.TryWrite(outcome));
                return outcome switch
                {
                    IdentityPostCommitOutcome.Completed => OutboxReceiveOutcome.Completed,
                    IdentityPostCommitOutcome.Rejected => OutboxReceiveOutcome.Rejected,
                    _ => OutboxReceiveOutcome.Retry
                };
            }, OutboxReceiveBrokerFixture.QualifyQueueAsync);
            using var worker = TunnerWorkerHost.CreateHost([], builder =>
            {
                builder.Services.AddSingleton(h.Processor);
                builder.Services.AddTunnerModules(module);
            });
            Assert.Contains(worker.Services.GetServices<IHostedService>(), service => service is RabbitMqOutboxReceiveHostedService);
            await worker.StartAsync(ct);
            try
            {
                var endpoint = new Uri($"amqp://fixture:{factory.Password}@127.0.0.1:{factory.Port}/");
                await using var publisher = new RabbitMqConfirmedOutboxPublisher(endpoint, exchange);
                var coordinator = new OutboxDispatchCoordinator(new EfOutboxDispatchStore(h.Platform), publisher, h.Host.Clock);
                Assert.Equal(1, await coordinator.DispatchPendingAsync(8, ct));
                Assert.Equal(IdentityPostCommitOutcome.Completed, await outcomes.Reader.ReadAsync(ct));
                var receiver = worker.Services.GetRequiredService<RabbitMqOutboxReceiver>();
                // The receiver's batch lock is held through ACK. This empty read waits for the hosted batch to finish.
                Assert.Equal(new OutboxReceiveBatchResult(0, 0, 0, 0), await receiver.ReceiveBatchAsync(ct));
                Assert.Equal(1, Volatile.Read(ref failedCallbacks)); Assert.Equal(2, h.Dispatcher.Attempts);
                await using (var platform = h.Platform.CreateDbContext())
                {
                    Assert.Single(await platform.AuditEvents.ToListAsync(ct));
                    Assert.NotNull((await platform.ConsumerInboxEntries.SingleAsync(ct)).CompletedAtUtc);
                    Assert.NotNull((await platform.OutboxMessages.SingleAsync(ct)).PublishedAtUtc);
                }
                Assert.Equal(h.Message.EventId, Assert.Single(h.Dispatcher.Intents).Key);
                h.Contracts.Unavailable = true;
                await publisher.PublishConfirmedAsync(h.Message, ct);
                Assert.Equal(IdentityPostCommitOutcome.Completed, await outcomes.Reader.ReadAsync(ct));
                Assert.Equal(new OutboxReceiveBatchResult(0, 0, 0, 0), await receiver.ReceiveBatchAsync(ct));
                Assert.Equal(2, h.Dispatcher.Attempts); Assert.Single(h.Dispatcher.Intents);
                var forged = h.Message with { CorrelationId = "fixture-tampered-correlation" };
                await publisher.PublishConfirmedAsync(forged, ct);
                Assert.Equal(IdentityPostCommitOutcome.Rejected, await outcomes.Reader.ReadAsync(ct));
                Assert.Equal(new OutboxReceiveBatchResult(0, 0, 0, 0), await receiver.ReceiveBatchAsync(ct));
                BasicGetResult? rejected = null;
                while (rejected is null)
                {
                    rejected = await channel.BasicGetAsync(options.DeadLetterQueueName, autoAck: true, ct);
                    if (rejected is null) await Task.Delay(TimeSpan.FromMilliseconds(20), ct);
                }
                Assert.True(RabbitMqOutboxEnvelope.TryDecode(rejected.BasicProperties, rejected.Body, options.MaximumPayloadBytes, out var quarantined));
                Assert.Equal(forged, quarantined);
                Assert.Null(await channel.BasicGetAsync(options.QueueName, autoAck: true, ct));
                Assert.Single(h.Dispatcher.Intents);
            }
            finally { await worker.StopAsync(CancellationToken.None); }
        }
        finally { await broker.DisposeAsync(); }
        Assert.True(broker.CleanupVerified);
    }
}
