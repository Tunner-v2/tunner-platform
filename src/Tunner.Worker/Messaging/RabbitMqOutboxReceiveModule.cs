using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Tunner.Platform;

namespace Tunner.Worker.Messaging;

/// <summary>Opt-in Worker composition only; the application supplies its processor via a fresh DI scope for each delivery.</summary>
public sealed class RabbitMqOutboxReceiveModule : ITunnerModule
{
    private readonly RabbitMqOutboxReceiveOptions options;
    private readonly ConnectionFactory factory;
    private readonly OutboxMessageProcessor process;
    private readonly RabbitMqOutboxTopologyQualification qualify;

    public RabbitMqOutboxReceiveModule(RabbitMqOutboxReceiveOptions options, ConnectionFactory factory,
        OutboxMessageProcessor process, RabbitMqOutboxTopologyQualification qualify)
    {
        ArgumentNullException.ThrowIfNull(options); ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(process); ArgumentNullException.ThrowIfNull(qualify);
        options.Require();
        if (factory.AutomaticRecoveryEnabled) throw new ArgumentException("Explicit manual connection recovery is required.", nameof(factory));
        this.options = options; this.factory = factory; this.process = process; this.qualify = qualify;
    }

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(service => service.ServiceType == typeof(RabbitMqOutboxReceiver)))
            throw new InvalidOperationException("An outbox receiver is already configured.");
        services.AddSingleton(options);
        services.AddSingleton(provider => new RabbitMqOutboxReceiver(options, factory,
            provider.GetRequiredService<IServiceScopeFactory>(), process, qualify));
        services.AddHostedService<RabbitMqOutboxReceiveHostedService>();
    }
}

public sealed class RabbitMqOutboxReceiveHostedService(RabbitMqOutboxReceiver receiver, RabbitMqOutboxReceiveOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = options.PollDelay;
            try
            {
                var batch = await receiver.ReceiveBatchAsync(stoppingToken).ConfigureAwait(false);
                if (batch.Requeued > 0) delay = options.RetryDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (OutboxReceiveUnavailableException) { delay = options.RetryDelay; }
            try { await Task.Delay(delay, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
