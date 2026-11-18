using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Tunner.Testing;
using Tunner.Worker.Messaging;
using Xunit;
using Xunit.Sdk;

namespace Tunner.Worker.Tests;

/// <summary>One cached, exact-owned RabbitMQ fixture, also compile-linkable by the Identity integration suite.</summary>
public sealed class OutboxReceiveBrokerFixture : IAsyncLifetime
{
    private const string Image = "sha256:eb338b3fc8f5c251028c0022ac0de3fe1db2d599d1c6a08b45898f756f248881";
    private const string Engine = "b81c329c-42bf-4ce2-812c-2570fbcda327";
    private const string RunLabel = "tunner.p2-016.run";
    private const string OwnerLabel = "tunner.p2-016.receiver-fixture";
    private readonly string instance = Guid.NewGuid().ToString("N");
    private readonly string password = Guid.NewGuid().ToString("N");
    private IContainer? container;
    private IContainer? portable;
    private DockerClient? docker;
    private string? run;
    private string[] volumes = [];

    public bool CleanupVerified { get; private set; }
    public static async Task QualifyQueueAsync(IChannel channel, RabbitMqOutboxReceiveOptions options, CancellationToken cancellationToken)
    {
        // Test-only explicit topology: RabbitMQ checks these exact queue arguments on every redeclaration.
        await channel.QueueDeclareAsync(options.DeadLetterQueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: null, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(options.QueueName, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            { ["x-dead-letter-exchange"] = "", ["x-dead-letter-routing-key"] = options.DeadLetterQueueName }, cancellationToken: cancellationToken);
    }

    public ConnectionFactory ConnectionFactory
    {
        get
        {
            var active = portable ?? container;
            if (active is null) throw SkipException.ForSkip("The admitted isolated container profile is required.");
            return new ConnectionFactory
            {
                HostName = portable?.Hostname ?? "127.0.0.1", Port = active.GetMappedPublicPort(5672), UserName = "fixture", Password = password,
                AutomaticRecoveryEnabled = false, RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
                ClientProvidedName = "tunner-test:owned-outbox-receive"
            };
        }
    }

    public async Task InitializeAsync()
    {
        if (!TestHarnessProfiles.IsContainerExecutionEnabled()) return;
        run = Environment.GetEnvironmentVariable("TUNNER_SESSION_LISTING_RUN_ID");
        if (run is null)
        {
            portable = TestcontainersBaseline.CreateRabbitMqContainer("fixture", password);
            try { await portable.StartAsync(); }
            catch { await DisposeAsync(); throw; }
            return;
        }
        var admittedEngine = Environment.GetEnvironmentVariable("TUNNER_SESSION_LISTING_ENGINE_ID");
        var endpoint = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (run.Length != 32 || run.Any(c => !char.IsAsciiHexDigitLower(c)) || admittedEngine != Engine ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var address) || address.Scheme != "npipe" || address.Host != "." ||
            address.Query.Length != 0 || address.Fragment.Length != 0 || address.UserInfo.Length != 0 ||
            Environment.GetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED") != "true")
            throw new InvalidOperationException("Explicit admitted isolated RabbitMQ test inputs are required.");
        docker = TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientBuilder(Guid.NewGuid())
            .WithLogger(NullLogger.Instance).Build();
        try
        {
            if (TestcontainersSettings.OS.DockerEndpointAuthConfig.Endpoint != address ||
                (await docker.System.GetSystemInfoAsync()).ID != Engine || (await docker.Images.InspectImageAsync(Image)).ID != Image)
                throw new InvalidOperationException("The admitted local RabbitMQ engine or cached image is unavailable.");
            container = new ContainerBuilder(Image).WithDockerEndpoint(address).WithImagePullPolicy(static _ => false)
                .WithLogger(NullLogger.Instance).WithCleanUp(false).WithOutputConsumer(Consume.DoNotConsumeStdoutAndStderr())
                .WithLabel(RunLabel, run).WithLabel(OwnerLabel, instance)
                .WithEnvironment("RABBITMQ_DEFAULT_USER", "fixture").WithEnvironment("RABBITMQ_DEFAULT_PASS", password)
                .WithPortBinding(5672, true)
                .WithCreateParameterModifier(parameters =>
                {
                    parameters.HostConfig ??= new();
                    parameters.HostConfig.LogConfig = new() { Type = "none" };
                    parameters.HostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>
                    { ["5672/tcp"] = [new() { HostIP = "127.0.0.1", HostPort = "" }] };
                })
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5672)).Build();
            await container.StartAsync();
            var actual = await docker.Containers.InspectContainerAsync(container.Id);
            RequireOwned(actual);
            volumes = (actual.Mounts ?? []).Where(mount => mount.Type == "volume" && !string.IsNullOrWhiteSpace(mount.Name))
                .Select(mount => mount.Name!).Distinct(StringComparer.Ordinal).ToArray();
            var ports = actual.NetworkSettings?.Ports;
            if (actual.Image != Image || ports is null || !ports.TryGetValue("5672/tcp", out var bindings) ||
                bindings is not { Count: 1 } || bindings[0].HostIP != "127.0.0.1")
                throw new InvalidOperationException("The owned RabbitMQ fixture boundary is invalid.");
        }
        catch { await DisposeAsync(); throw; }
    }

    private void RequireOwned(ContainerInspectResponse actual)
    {
        if (actual.Config?.Labels is not { } labels || !labels.TryGetValue(RunLabel, out var actualRun) || actualRun != run ||
            !labels.TryGetValue(OwnerLabel, out var owner) || owner != instance)
            throw new InvalidOperationException("RabbitMQ fixture ownership cannot be established.");
    }

    public async Task StopAsync()
    {
        if (portable is not null) { await portable.StopAsync(); return; }
        if (container is null || docker is null) throw new InvalidOperationException("The owned RabbitMQ fixture is unavailable.");
        RequireOwned(await docker.Containers.InspectContainerAsync(container.Id));
        await container.StopAsync();
    }

    public async Task StartAsync()
    {
        if (portable is not null) { await portable.StartAsync(); return; }
        if (container is null || docker is null) throw new InvalidOperationException("The owned RabbitMQ fixture is unavailable.");
        RequireOwned(await docker.Containers.InspectContainerAsync(container.Id));
        await container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (portable is not null) await portable.DisposeAsync();
            if (container is not null && docker is not null)
            {
                string? id;
                try { id = container.Id; } catch (InvalidOperationException) { id = null; }
                if (id is not null)
                {
                    ContainerInspectResponse? actual = null;
                    try { actual = await docker.Containers.InspectContainerAsync(id); }
                    catch (DockerApiException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound) { }
                    if (actual is not null)
                    {
                        RequireOwned(actual);
                        volumes = (actual.Mounts ?? []).Where(mount => mount.Type == "volume" && !string.IsNullOrWhiteSpace(mount.Name))
                            .Select(mount => mount.Name!).Distinct(StringComparer.Ordinal).ToArray();
                        await container.DisposeAsync();
                        try { await docker.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters { Force = true, RemoveVolumes = true }); }
                        catch (DockerApiException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound) { }
                        try { await docker.Containers.InspectContainerAsync(id); throw new InvalidOperationException("Owned RabbitMQ container remains."); }
                        catch (DockerApiException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound) { }
                    }
                    foreach (var volume in volumes)
                    {
                        try { await docker.Volumes.InspectAsync(volume); throw new InvalidOperationException("Owned RabbitMQ volume remains."); }
                        catch (DockerApiException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound) { }
                    }
                }
            }
            CleanupVerified = true;
        }
        finally { docker?.Dispose(); docker = null; container = null; portable = null; }
    }
}
