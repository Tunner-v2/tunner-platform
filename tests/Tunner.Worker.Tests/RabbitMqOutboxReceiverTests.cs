using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Tunner.Platform;
using Tunner.Worker.Messaging;
using Xunit;

namespace Tunner.Worker.Tests;

public sealed class RabbitMqOutboxReceiverTests
{
    [Fact]
    public async Task ModuleIsExplicitAndRegistersTheExistingModuleAndHostedServicePathWithoutOpeningABroker()
    {
        var services = new ServiceCollection();
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IHostedService));
        var module = new RabbitMqOutboxReceiveModule(Options(), new ConnectionFactory { AutomaticRecoveryEnabled = false },
            static (_, _, _) => ValueTask.FromResult(OutboxReceiveOutcome.Completed), OutboxReceiveBrokerFixture.QualifyQueueAsync);
        services.AddTunnerModules(module);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Assert.IsType<RabbitMqOutboxReceiveHostedService>(Assert.Single(provider.GetServices<IHostedService>()));
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerModules(module));
    }

    [Fact]
    public void MissingQualifiedTopologyOrUnboundedBatchIsRejectedAtComposition()
    {
        var options = Options();
        Assert.Throws<ArgumentException>(() => Module(options with { TopologyQualificationReference = "" }));
        Assert.Throws<ArgumentException>(() => Module(options with { DeadLetterQueueName = options.QueueName }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Module(options with { MaximumMessagesPerBatch = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Module(options with { MaximumPayloadBytes = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Module(options with { RetryDelay = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(() => new RabbitMqOutboxReceiveModule(options, new ConnectionFactory { AutomaticRecoveryEnabled = true },
            static (_, _, _) => ValueTask.FromResult(OutboxReceiveOutcome.Completed), OutboxReceiveBrokerFixture.QualifyQueueAsync));
    }

    private static RabbitMqOutboxReceiveModule Module(RabbitMqOutboxReceiveOptions options) => new(options,
        new ConnectionFactory { AutomaticRecoveryEnabled = false }, static (_, _, _) => ValueTask.FromResult(OutboxReceiveOutcome.Completed),
        OutboxReceiveBrokerFixture.QualifyQueueAsync);

    internal static RabbitMqOutboxReceiveOptions Options(int maximum = 4) => new("fixture.receive." + Guid.NewGuid().ToString("N"),
        "fixture.dead." + Guid.NewGuid().ToString("N"), "synthetic-test-topology-qualified-by-exact-declaration", maximum, 65536,
        TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100));
}
