using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Tunner.Installer;
using Tunner.Platform.Secrets;

namespace Tunner.Testing.OpenBao;

/// <summary>Test-only coordinator; application composition receives only a scoped session's Secrets.</summary>
internal sealed class DisposableOpenBaoFixture : IAsyncDisposable
{
    internal const string ImageReference = "openbao/openbao:2.7.0";
    private const string OwnerLabel = "tunner.test.disposable-vault";
    private readonly IContainer container;
    private readonly IDockerClient docker;
    private readonly string bootstrapCredential;
    private readonly List<LocalOpenBaoSession> sessions = [];
    private HttpClient? admin;
    private bool disposed;

    private DisposableOpenBaoFixture(IContainer container, IDockerClient docker, string bootstrapCredential, Guid instanceId)
    {
        this.container = container; this.docker = docker; this.bootstrapCredential = bootstrapCredential; InstanceId = instanceId;
    }
    internal Guid InstanceId { get; }
    private string ContainerName => "tunner-disposable-vault-" + InstanceId.ToString("N");
    internal Uri Endpoint { get; private set; } = null!;
    internal DisposableOpenBaoProvenance Provenance { get; private set; } = null!;
    internal bool CleanupVerified { get; private set; }
    // Privileged fixture setup only; never register this client or the fixture as an application service.
    internal HttpClient BootstrapClient => admin ?? throw new InvalidOperationException("DISPOSABLE_VAULT_UNAVAILABLE");

    internal static Task<DisposableOpenBaoFixture> StartAsync(CancellationToken cancellationToken = default) =>
        StartAsync(TestcontainersSettings.OS.DockerEndpointAuthConfig, cancellationToken);

    internal static async Task<DisposableOpenBaoFixture> StartAsync(IDockerEndpointAuthenticationConfiguration dockerEndpoint,
        CancellationToken cancellationToken = default)
    {
        // Host-port binding cannot make a remote daemon local. Check the actual client API endpoint first.
        RequireLocalDockerEndpoint(dockerEndpoint.Endpoint);
        var instance = Guid.NewGuid();
        var credential = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var docker = dockerEndpoint.GetDockerClientBuilder(instance)
            .WithLogger(NullLogger.Instance).WithTimeout(TimeSpan.FromSeconds(20)).Build();
        DisposableOpenBaoFixture? fixture = null;
        try
        {
            var image = await docker.Images.InspectImageAsync(ImageReference, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(image.ID) || image.Config is null || image.Config.Volumes?.Count > 0)
                throw new InvalidOperationException("DISPOSABLE_VAULT_IMAGE_INVALID");
            var container = new ContainerBuilder(ImageReference)
                .WithDockerEndpoint(dockerEndpoint)
                .WithName("tunner-disposable-vault-" + instance.ToString("N"))
                .WithLabel(OwnerLabel, instance.ToString("N"))
                .WithEnvironment("BAO_DEV_ROOT_TOKEN_ID", credential)
                .WithCommand("server", "-dev", "-dev-no-store-token", "-dev-listen-address=0.0.0.0:8200")
                .WithPortBinding(8200, true)
                .WithImagePullPolicy(static _ => false)
                // Explicit finite-owner deletion avoids starting/downloading a resource-reaper helper.
                .WithCleanUp(false)
                .WithLogger(NullLogger.Instance)
                .WithOutputConsumer(Consume.DoNotConsumeStdoutAndStderr())
                .WithCreateParameterModifier(parameters =>
                {
                    parameters.HostConfig ??= new HostConfig();
                    parameters.HostConfig.LogConfig = new LogConfig { Type = "none" };
                    parameters.HostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>(StringComparer.Ordinal)
                    { ["8200/tcp"] = [new PortBinding { HostIP = "127.0.0.1", HostPort = "" }] };
                })
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8200).ForPath("/v1/sys/health")))
                .Build();
            fixture = new(container, docker, credential, instance);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(1));
            await container.StartAsync(deadline.Token).ConfigureAwait(false);
            fixture.Endpoint = new Uri("http://127.0.0.1:" + container.GetMappedPublicPort(8200).ToString(System.Globalization.CultureInfo.InvariantCulture) + "/");
            fixture.admin = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                { BaseAddress = fixture.Endpoint, Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 65536 };
            fixture.admin.DefaultRequestHeaders.Add("X-Vault-Token", credential);
            // Selected non-secret metadata only: never access/serialize Config.Env or container output/logs.
            var actual = await docker.Containers.InspectContainerAsync(container.Id, deadline.Token).ConfigureAwait(false);
            var host = actual.HostConfig ?? throw new InvalidOperationException("DISPOSABLE_VAULT_CONFIGURATION_INVALID");
            var config = actual.Config ?? throw new InvalidOperationException("DISPOSABLE_VAULT_CONFIGURATION_INVALID");
            var ports = host.PortBindings;
            var networkPorts = actual.NetworkSettings?.Ports;
            var loopback = ports is { Count: 1 } && ports.TryGetValue("8200/tcp", out var bindings) && bindings is { Count: 1 } && bindings[0].HostIP == "127.0.0.1" &&
                networkPorts is not null && networkPorts.TryGetValue("8200/tcp", out var published) && published is { Count: 1 } && published[0].HostIP == "127.0.0.1";
            var noVolumes = actual.Mounts is { Count: 0 } && (host.Binds?.Count ?? 0) == 0 && (config.Volumes?.Count ?? 0) == 0;
            var noLogs = host.LogConfig?.Type == "none";
            if (actual.Image != image.ID || config.Labels is null || !config.Labels.TryGetValue(OwnerLabel, out var owner) || owner != instance.ToString("N") || !loopback || !noVolumes || !noLogs ||
                config.Cmd is null || !config.Cmd.Contains("-dev-no-store-token", StringComparer.Ordinal)) throw new InvalidOperationException("DISPOSABLE_VAULT_CONFIGURATION_INVALID");
            using var response = await fixture.admin.GetAsync("v1/sys/health", deadline.Token).ConfigureAwait(false);
            using var health = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(deadline.Token).ConfigureAwait(false));
            var version = health.RootElement.GetProperty("version").GetString();
            if (!response.IsSuccessStatusCode || version != "2.7.0" || !health.RootElement.GetProperty("initialized").GetBoolean() || health.RootElement.GetProperty("sealed").GetBoolean())
                throw new InvalidOperationException("DISPOSABLE_VAULT_HEALTH_INVALID");
            fixture.Provenance = new(ImageReference, image.ID, version, instance, true, true, noLogs, loopback, noVolumes, "SYNTHETIC_ONLY_UNTIL_VERIFIED_FIXTURE_REMOVAL");
            return fixture;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (fixture is not null) await fixture.DisposeAsync().ConfigureAwait(false); else docker.Dispose();
            throw new InvalidOperationException("DISPOSABLE_VAULT_START_FAILED");
        }
    }

    private static void RequireLocalDockerEndpoint(Uri endpoint)
    {
        if (endpoint is null || !endpoint.IsAbsoluteUri || endpoint.UserInfo.Length != 0 || endpoint.Port != -1 ||
            endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.OriginalString.Any(char.IsControl))
            throw new InvalidOperationException("DISPOSABLE_VAULT_LOCAL_DOCKER_REQUIRED");
        var path = Uri.UnescapeDataString(endpoint.AbsolutePath);
        var pipeParts = path.Split('/');
        var localPipe = endpoint.Scheme == "npipe" && (endpoint.Host == "." || endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) &&
            pipeParts.Length == 3 && pipeParts[0].Length == 0 && pipeParts[1] == "pipe" && pipeParts[2].Length > 0 && pipeParts[2] is not ("." or "..") &&
            pipeParts[2].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
        var localSocket = endpoint.Scheme == "unix" && endpoint.Host.Length == 0 && path.Length > 1 && path[0] == '/' &&
            !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains('\\') && !path.Any(char.IsControl);
        if (!localPipe && !localSocket) throw new InvalidOperationException("DISPOSABLE_VAULT_LOCAL_DOCKER_REQUIRED");
    }

    internal LocalOpenBaoCredentialScope CredentialScope() => new("EPHEMERAL_TEST", Endpoint, "disposable-vault", "secret/data/credentials", InstanceId);
    internal async Task<LocalOpenBaoSession> CreateSessionAsync(LocalOpenBaoCredentialScope scope, CancellationToken cancellationToken = default)
    {
        if (disposed || scope.Endpoint != Endpoint || scope.InstanceId != InstanceId) throw new InvalidOperationException("DISPOSABLE_VAULT_SCOPE_INVALID");
        var session = await LocalOpenBaoSession.CreateAsync(scope, bootstrapCredential, cancellationToken).ConfigureAwait(false);
        sessions.Add(session); return session;
    }
    internal async Task<LocalOpenBaoSession> CreateSessionAsync(R2InstallationProfile profile, CancellationToken cancellationToken = default)
    {
        if (disposed || new Uri(profile.OpenBaoEndpoint) != Endpoint) throw new InvalidOperationException("DISPOSABLE_VAULT_SCOPE_INVALID");
        var session = await LocalOpenBaoSession.CreateAsync(profile, bootstrapCredential, cancellationToken).ConfigureAwait(false);
        sessions.Add(session); return session;
    }
    internal OpenBaoSecretStore BootstrapStoreForRejectionTest(string storeReference) => new(Endpoint, storeReference, bootstrapCredential);

    public async ValueTask DisposeAsync()
    {
        if (disposed) { if (!CleanupVerified) throw new InvalidOperationException("DISPOSABLE_VAULT_CLEANUP_UNVERIFIED"); return; }
        disposed = true;
        try
        {
            CleanupVerified = await VerifyCleanupAsync(async () =>
            {
                var verified = true;
                foreach (var session in sessions)
                {
                    try { await session.DisposeAsync().ConfigureAwait(false); verified &= session.CleanupVerified; }
                    catch (Exception exception) when (exception is not OutOfMemoryException) { verified = false; }
                }
                return verified;
            }, async () =>
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                ContainerInspectResponse? owned = null;
                try { owned = await docker.Containers.InspectContainerAsync(ContainerName, deadline.Token).ConfigureAwait(false); }
                catch (DockerApiException error) when (error.StatusCode == HttpStatusCode.NotFound) { }
                if (owned is not null)
                {
                    if (owned.Config?.Labels is null || !owned.Config.Labels.TryGetValue(OwnerLabel, out var owner) || owner != InstanceId.ToString("N"))
                        throw new InvalidOperationException("DISPOSABLE_VAULT_OWNER_MISMATCH");
                    try { await docker.Containers.RemoveContainerAsync(owned.ID, new ContainerRemoveParameters { Force = true, RemoveVolumes = true }, deadline.Token).ConfigureAwait(false); }
                    catch (DockerApiException error) when (error.StatusCode == HttpStatusCode.NotFound) { }
                }
                await container.DisposeAsync().AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
            }, async () =>
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try { _ = await docker.Containers.InspectContainerAsync(ContainerName, deadline.Token).ConfigureAwait(false); return false; }
                catch (DockerApiException error) when (error.StatusCode == HttpStatusCode.NotFound) { return true; }
            }).ConfigureAwait(false);
        }
        finally { admin?.Dispose(); docker.Dispose(); }
        if (!CleanupVerified) throw new InvalidOperationException("DISPOSABLE_VAULT_CLEANUP_UNVERIFIED");
    }
    internal static async Task<bool> VerifyCleanupAsync(Func<Task<bool>> endSessions, Func<Task> removeContainer, Func<Task<bool>> containerAbsent)
    {
        var sessionsEnded = false; var removalSucceeded = false; var absent = false;
        try { sessionsEnded = await endSessions().ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { }
        try { await removeContainer().ConfigureAwait(false); removalSucceeded = true; } catch (Exception error) when (error is not OutOfMemoryException) { }
        try { absent = await containerAbsent().ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { }
        return sessionsEnded && removalSucceeded && absent;
    }
    public override string ToString() => nameof(DisposableOpenBaoFixture);
}

internal sealed record DisposableOpenBaoProvenance(string ImageReference, string ImageId, string Version, Guid InstanceId,
    bool CachedOnly, bool HelperDisabled, bool LogPersistenceDisabled, bool LoopbackOnly, bool NoVolumes, string Retention);
