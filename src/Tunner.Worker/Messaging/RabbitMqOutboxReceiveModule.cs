using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        services.AddLogging();
        services.AddSingleton(options);
        services.AddSingleton(provider => new RabbitMqOutboxReceiver(options, factory,
            provider.GetRequiredService<IServiceScopeFactory>(), process, qualify));
        services.AddHostedService<RabbitMqOutboxReceiveHostedService>();
    }
}

public sealed class RabbitMqOutboxReceiveHostedService(RabbitMqOutboxReceiver receiver, RabbitMqOutboxReceiveOptions options,
    ILogger<RabbitMqOutboxReceiveHostedService> logger) : BackgroundService
{
    private static readonly Action<ILogger, int, int, int, int, Exception?> BatchObserved = LoggerMessage.Define<int, int, int, int>(
        LogLevel.Information, new EventId(1601, "OutboxReceiveBatch"),
        "Outbox receive batch: Received={Received}, Completed={Completed}, Requeued={Requeued}, Rejected={Rejected}.");
    private static readonly Action<ILogger, Exception?> TransportUnavailable = LoggerMessage.Define(
        LogLevel.Warning, new EventId(1602, "OutboxReceiveUnavailable"), "Outbox receive unavailable; retry scheduled.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = options.PollDelay;
            try
            {
                var batch = await receiver.ReceiveBatchAsync(stoppingToken).ConfigureAwait(false);
                if (batch.Received > 0) BatchObserved(logger, batch.Received, batch.Completed, batch.Requeued, batch.Rejected, null);
                if (batch.Requeued > 0) delay = options.RetryDelay;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (OutboxReceiveUnavailableException) { TransportUnavailable(logger, null); delay = options.RetryDelay; }
            try { await Task.Delay(delay, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
