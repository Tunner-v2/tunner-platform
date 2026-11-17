using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Handler.Abstractions;
using Docker.DotNet.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Tunner.Database;
using Tunner.Database.Migrations;
using Tunner.Identity.Persistence;
using Tunner.Installer;

namespace Tunner.Testing.Recovery;

// Finite Windows TLS borrows the already approved key; Schannel cannot use the fixture's ephemeral import.
internal static class RecoveryCodeFiniteTls
{
    internal static X509Certificate2 LoadApprovedCertificate()
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("RECOVERY_FINITE_TLS_CERTIFICATE_UNAVAILABLE");
        var root = Tunner.Governance.RepositoryRootResolver.Resolve(new DirectoryInfo(Directory.GetCurrentDirectory()));
        var path = FiniteTotpBuildBinding.ResolveEvidence(root, "artifacts/browser-testing/runtime.json");
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > 65536)
            throw new InvalidOperationException("RECOVERY_FINITE_TLS_CERTIFICATE_UNAVAILABLE");
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(path));
        var thumbprint = metadata.RootElement.GetProperty("CertificateThumbprint").GetString();
        var expected = metadata.RootElement.GetProperty("CertificateSha256").GetString();
        if (metadata.RootElement.GetProperty("Mode").GetString() != "ISOLATED_LOCAL_BROWSER_TESTING" ||
            thumbprint is not { Length: 40 } || !thumbprint.All(Uri.IsHexDigit) || expected is not { Length: 64 } || !expected.All(Uri.IsHexDigit))
            throw new InvalidOperationException("RECOVERY_FINITE_TLS_CERTIFICATE_UNAVAILABLE");
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser); store.Open(OpenFlags.ReadOnly);
        var certificates = store.Certificates;
        try
        {
            var now = DateTime.Now;
            var certificate = certificates.OfType<X509Certificate2>().Single(x => string.Equals(x.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase) &&
                x.HasPrivateKey && x.NotBefore <= now && x.NotAfter > now);
            if (certificate.GetCertHashString(HashAlgorithmName.SHA256) != expected)
                throw new InvalidOperationException("RECOVERY_FINITE_TLS_CERTIFICATE_UNAVAILABLE");
            return new X509Certificate2(certificate); // Clone only the handle; never export/import or persist private key material.
        }
        finally { foreach (var certificate in certificates) certificate.Dispose(); }
    }
}

/// <summary>Qualified public metadata, never endpoint/config/credential discovery.</summary>
internal sealed class RecoveryCodeLocalDockerEndpoint : IDisposable
{
    private readonly DirectoryInfo configuration;
    private readonly string configurationParent;
    internal RecoveryCodeFixtureOwnershipChannel Ownership { get; }
    private readonly RecoveryCodePreparedFixtureOwner configurationOwner;
    private readonly SemaphoreSlim configurationGate = new(1, 1);
    private bool disposed;
    private string? engineId;
    private bool configurationInitialized;
    private bool configurationCreateAttempted;
    private RecoveryCodeLocalDockerEndpoint(Uri endpoint, RecoveryCodeFixtureOwnershipChannel ownership)
    {
        Value = endpoint; Ownership = ownership;
        configuration = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "tunner-p2-009-docker-" + Guid.NewGuid().ToString("N")));
        configurationParent = configuration.Parent!.FullName;
        configurationOwner = new(configuration.Name[^32..], "config", configuration.Name, null);
    }
    internal Uri Value { get; }
    internal bool ConfigurationCreated => configurationCreateAttempted;
    internal bool CleanupVerified { get; private set; }
    internal async Task VerifyEngineAsync(CancellationToken ct)
    {
        using var client = await ClientAsync(ct).ConfigureAwait(false);
        var actual = await client.System.GetSystemInfoAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(actual.ID) || actual.ID.Length > 200 || actual.ID.Any(char.IsControl) || actual.OSType != "linux" || actual.ID != Ownership.ExpectedEngineId ||
            engineId is not null && engineId != actual.ID) throw new InvalidOperationException("RECOVERY_DOCKER_ENGINE_CHANGED");
        engineId ??= actual.ID;
    }
    internal static RecoveryCodeLocalDockerEndpoint Admit(Uri endpoint, RecoveryCodeFixtureOwnershipChannel ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        return new(RequireLocalEndpoint(endpoint), ownership);
    }
    private static Uri RequireLocalEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var original = endpoint.OriginalString;
        if (original.StartsWith("npipe:////./", StringComparison.Ordinal))
            endpoint = new Uri("npipe://./" + original[12..]);
        if (!endpoint.IsAbsoluteUri || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 ||
            endpoint.Fragment.Length != 0 || endpoint.Port != -1 || original.Length > 512 || original.Any(char.IsControl))
            throw new InvalidOperationException("RECOVERY_LOCAL_DOCKER_REQUIRED");
        var path = Uri.UnescapeDataString(endpoint.AbsolutePath);
        var parts = path.Split('/');
        var pipe = OperatingSystem.IsWindows() && endpoint.Scheme == "npipe" && endpoint.Host == "." &&
            parts.Length == 3 && parts[0].Length == 0 && parts[1] == "pipe" && parts[2].Length > 0 &&
            parts[2] is not ("." or "..") && parts[2].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
        var socket = !OperatingSystem.IsWindows() && endpoint.Scheme == "unix" && endpoint.Host.Length == 0 &&
            path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal) && path.Length > 1 &&
            !path.Contains('\\') && !path.Any(char.IsControl) && !parts.Any(p => p is "." or "..");
        if (!pipe && !socket) throw new InvalidOperationException("RECOVERY_LOCAL_DOCKER_REQUIRED");
        // Finite test process only: native clients must never discover operator proxy credentials/routes.
        HttpClient.DefaultProxy = new WebProxy();
        return endpoint;
    }
    internal async Task<IDockerClient> ClientAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await configurationGate.WaitAsync(ct).ConfigureAwait(false);
        try { await InitializeConfigurationAsync(ct).ConfigureAwait(false); }
        finally { configurationGate.Release(); }
        return ClientForConfiguration(Value, configuration.FullName);
    }
    private async Task InitializeConfigurationAsync(CancellationToken ct)
    {
        if (configurationInitialized) return;
        await Ownership.PrepareAsync(configurationOwner, ct).ConfigureAwait(false);
        if (configurationCreateAttempted) throw new InvalidOperationException("RECOVERY_DOCKER_CONFIGURATION_UNAVAILABLE");
        configuration.Refresh();
        if (configuration.Exists || File.Exists(configuration.FullName)) throw new InvalidOperationException("RECOVERY_DOCKER_CONFIGURATION_OCCUPIED");
        configurationCreateAttempted = true;
        // Endpoint owner has already been returned/retained before any directory/file allocation.
        Directory.CreateDirectory(configuration.FullName);
        File.WriteAllText(Path.Combine(configuration.FullName, "config.json"), "{}");
        await Ownership.InitializedConfigurationAsync(configurationOwner, ct).ConfigureAwait(false);
        // Also constrain incidental static client configuration initialization to this process-owned empty file.
        Environment.SetEnvironmentVariable("DOCKER_CONFIG", configuration.FullName);
        foreach (var key in new[] { "DOCKER_AUTH_CONFIG", "DOCKER_CONTEXT", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH", "REGISTRY_AUTH_FILE" })
            Environment.SetEnvironmentVariable(key, null);
        configurationInitialized = true;
    }
    // Reconciliation uses only the outside controller's already-owned public empty configuration.
    internal static IDockerClient ReconciliationClient(Uri endpoint, string runId)
    {
        if (!RecoveryCodeFixtureOwnershipChannel.Hex(runId, 32)) throw new InvalidOperationException("RECOVERY_RECONCILIATION_INVALID");
        var folder = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "tunner-p2-009-helper-" + runId));
        var file = new FileInfo(Path.Combine(folder.FullName, "config.json"));
        if (!folder.Exists || folder.LinkTarget is not null || (folder.Attributes & FileAttributes.ReparsePoint) != 0 ||
            !file.Exists || file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length != 2 || File.ReadAllText(file.FullName) != "{}" ||
            folder.EnumerateFileSystemInfos().Count() != 1) throw new InvalidOperationException("RECOVERY_RECONCILIATION_CONFIG_INVALID");
        return ClientForConfiguration(RequireLocalEndpoint(endpoint), folder.FullName);
    }
    private static DockerClient ClientForConfiguration(Uri endpoint, string path)
    {
        Environment.SetEnvironmentVariable("DOCKER_CONFIG", path);
        return new DockerClientBuilder(new DockerConfig(new ClosedSettings(path)))
            .WithEndpoint(endpoint).WithAuthProvider(NoopAuthProvider.Instance).WithLogger(NullLogger.Instance)
            .WithTimeout(TimeSpan.FromSeconds(15)).Build();
    }
    // Explicit settings prevent the pinned client's default DockerConfig discovery. No file is opened.
    private sealed class ClosedSettings(string path) : IDockerCliSettings
    {
        public string DockerConfig => path;
        public string? DockerHost => null;
        public string? DockerTlsVerify => null;
        public string? DockerContext => null;
    }
    public void Dispose()
    {
        if (disposed) { if (!CleanupVerified) throw new InvalidOperationException("RECOVERY_DOCKER_CONFIGURATION_CLEANUP_FAILED"); return; }
        disposed = true; DeleteKnownConfiguration();
        CleanupVerified = !Directory.Exists(configuration.FullName);
        if (!CleanupVerified) throw new InvalidOperationException("RECOVERY_DOCKER_CONFIGURATION_CLEANUP_FAILED");
    }
    private void DeleteKnownConfiguration()
    {
        configuration.Refresh();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (configuration.Parent is null || !string.Equals(configuration.Parent.FullName, configurationParent, comparison) ||
            !configuration.Name.StartsWith("tunner-p2-009-docker-", StringComparison.Ordinal) || configuration.LinkTarget is not null)
            throw new InvalidOperationException("RECOVERY_DOCKER_CONFIGURATION_OWNER_INVALID");
        if (!configuration.Exists) return;
        if (!configurationCreateAttempted) throw new InvalidOperationException("RECOVERY_DOCKER_CONFIGURATION_OWNER_INVALID");
        var entries = configuration.EnumerateFileSystemInfos().ToArray();
        if (entries.Length > 1 || entries.Any(e => e.Name != "config.json" || e is not FileInfo || e.LinkTarget is not null ||
            (e.Attributes & FileAttributes.ReparsePoint) != 0)) throw new InvalidOperationException("RECOVERY_DOCKER_CONFIGURATION_UNEXPECTED_ENTRY");
        if (entries.Length == 1) File.Delete(entries[0].FullName);
        configuration.Delete(false);
    }
    public override string ToString() => nameof(RecoveryCodeLocalDockerEndpoint);
}

internal static class RecoveryCodeOwnedDockerFixture
{
    internal const long NativePostgresCapacityBytes = 4L * 1024 * 1024 * 1024;
    internal const string NativePostgresImage = "sha256:77f585114c32fbca283dc835b0596f4e52b51b4c6662d7810b2f4084f60a1873";
    internal const string MailpitImage = "sha256:0306debe903ec7a44a4d49266ac54c54667183efc5de1586d75093d9b60b823b";
    internal const string OpenBaoImage = "sha256:71156a1c6623a5fa3f5e61b0c6a8ead0faf0df29a778339188443551995d1315";
    internal static RecoveryCodeOwnedPostgres PrepareNativePostgres(RecoveryCodeLocalDockerEndpoint endpoint, DirectoryInfo root) => new(endpoint, RootHash(root));
    internal static RecoveryCodeOwnedMailpit PrepareMailpit(RecoveryCodeLocalDockerEndpoint endpoint, DirectoryInfo root) => new(endpoint, RootHash(root));
    internal static RecoveryCodeOwnedOpenBao PrepareOpenBao(RecoveryCodeLocalDockerEndpoint endpoint, DirectoryInfo root) => new(endpoint, RootHash(root));
    internal static Task<RecoveryCodeBorrowedPostgres> VerifyBorrowedBrowserPostgresAsync(RecoveryCodeLocalDockerEndpoint endpoint, DirectoryInfo root, CancellationToken ct) =>
        RecoveryCodeBorrowedPostgres.VerifyAsync(endpoint, "tunner-browser-" + RootHash(root), ct);
    private static string RootHash(DirectoryInfo root)
    {
        root.Refresh();
        if (!root.Exists || !File.Exists(Path.Combine(root.FullName, ".tunner-root")))
            throw new InvalidOperationException("RECOVERY_REPOSITORY_ROOT_REQUIRED");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(root.FullName.ToLowerInvariant())))[..10];
    }
}

/// <summary>Closed recovery roles only. Prepare allocates this owner before the first Docker API.</summary>
internal abstract class RecoveryCodeOwnedContainer : IAsyncDisposable
{
    private const string OwnerLabel = "tunner.p2-009.finite-owner";
    private const string RoleLabel = "tunner.p2-009.finite-role";
    private const string RepositoryLabel = "tunner.p2-009.repository";
    private readonly string role;
    private readonly string repository;
    private readonly int[] ports;
    private readonly Dictionary<string, int> published = new(StringComparer.Ordinal);
    private readonly HashSet<string> ownedAnonymousVolumes = new(StringComparer.Ordinal);
    private IDockerClient? docker;
    private IDockerClient Docker => docker ?? throw new InvalidOperationException("RECOVERY_RESOURCE_CLIENT_UNAVAILABLE");
    private readonly RecoveryCodeLocalDockerEndpoint? endpoint;
    private readonly string runId;
    private readonly RecoveryCodePreparedFixtureOwner descriptor;
    private bool registered;
    private string? containerId;
    private bool createAttempted;
    private bool absentBeforeCreate;
    private bool started;
    private bool disposed;
    private readonly string name;
    protected RecoveryCodeOwnedContainer(RecoveryCodeLocalDockerEndpoint endpoint, string repository, string role, string imageId, int[] ports)
    {
        InstanceId = Guid.NewGuid(); this.endpoint = endpoint; this.repository = repository; this.role = role; ImageId = imageId; this.ports = ports; runId = endpoint.Ownership.RunId;
        name = "tunner-p2-009-" + role + "-" + InstanceId.ToString("N");
        descriptor = new(InstanceId.ToString("N"), role, name, imageId);
    }
    protected RecoveryCodeOwnedContainer(IDockerClient client, string repository, string runId, RecoveryCodePreparedFixtureOwner descriptor, string? knownId)
    {
        RecoveryCodeFixtureOwnershipChannel.Validate(descriptor);
        if (descriptor.Role == "config" || descriptor.ImageId is null || !RecoveryCodeFixtureOwnershipChannel.Hex(runId, 32) ||
            knownId is not null && !RecoveryCodeFixtureOwnershipChannel.Hex(knownId, 64)) throw new InvalidOperationException("RECOVERY_RECONCILIATION_INVALID");
        this.repository = repository; this.runId = runId; this.descriptor = descriptor; role = descriptor.Role; name = descriptor.Name;
        InstanceId = Guid.ParseExact(descriptor.OwnerId, "N"); ImageId = descriptor.ImageId;
        ports = role == "native-postgres" ? [5432] : role == "mailpit" ? [1025, 8025] : [8200];
        docker = client; containerId = knownId; createAttempted = true; absentBeforeCreate = true;
    }
    internal async Task<bool> VerifyPreparedIdentityAsync(CancellationToken ct)
    {
        var image = await Docker.Images.InspectImageAsync(ImageId, ct).ConfigureAwait(false);
        var volumes = image.Config?.Volumes?.Keys.ToArray() ?? [];
        return image.ID == ImageId && image.Config is not null &&
            (role == "native-postgres" ? !volumes.Any(p => p is not ("/var/lib/postgresql" or "/var/lib/postgresql/data")) : volumes.Length == 0) &&
            await InspectAsync(name, ct).ConfigureAwait(false) is null;
    }
    internal async Task<bool> VerifyCreatedIdentityAsync(CancellationToken ct, bool expectedRunning = false)
    {
        if (containerId is null) return false;
        var actual = await InspectAsync(containerId, ct).ConfigureAwait(false);
        var image = await Docker.Images.InspectImageAsync(ImageId, ct).ConfigureAwait(false);
        var tmpfs = new HashSet<string>(StringComparer.Ordinal);
        if (role == "native-postgres")
        {
            tmpfs.Add("/var/lib/postgresql");
            if (image.Config?.Volumes is { } volumes) foreach (var target in volumes.Keys) tmpfs.Add(target);
        }
        var network = actual?.NetworkSettings?.Ports;
        var loopback = !expectedRunning || network is not null && network.Count == ports.Length && ports.All(p => network.TryGetValue(Key(p), out var values) &&
            values is { Count: 1 } && values[0].HostIP == "127.0.0.1" && int.TryParse(values[0].HostPort, NumberStyles.None, CultureInfo.InvariantCulture, out var publishedPort) && publishedPort is > 0 and <= 65535 && publishedPort != 25432);
        return actual is not null && Owns(actual) && actual.State?.Running == expectedRunning && SafeConfiguration(actual, tmpfs) && loopback;
    }
    internal async Task<bool> VerifyAbsenceAsync(CancellationToken ct) => await InspectAsync(name, ct).ConfigureAwait(false) is null &&
        (containerId is null || await InspectAsync(containerId, ct).ConfigureAwait(false) is null);
    internal Guid InstanceId { get; }
    internal string ImageId { get; }
    internal bool OwnershipVerified { get; private set; }
    internal bool CleanupVerified { get; private set; }
    internal bool ContainerAbsentVerified { get; private set; }
    internal bool CreatedIdentityCaptured => containerId is not null;
    internal bool NoLogPersistenceVerified { get; private set; }
    internal bool AnonymousVolumesAbsentVerified { get; private set; }
    protected bool Available => OwnershipVerified && !disposed;
    protected int Published(int port) => published.TryGetValue(Key(port), out var value) && Available ? value :
        throw new InvalidOperationException("RECOVERY_RESOURCE_NOT_READY");
    private static string Key(int port) => port.ToString(CultureInfo.InvariantCulture) + "/tcp";
    protected virtual IList<string> EnvironmentValues => [];
    protected virtual IList<string>? Command => null;
    protected virtual Task EndSessionsAsync() => Task.CompletedTask;
    protected abstract Task ReadyAsync(CancellationToken ct);
    internal Task StartAsync(CancellationToken ct) => StartCoreAsync(false, ct);
    internal Task StartPublicCreateCanaryAsync(CancellationToken ct) => StartCoreAsync(true, ct);
    private async Task StartCoreAsync(bool stopAfterCreate, CancellationToken ct)
    {
        if (started || disposed) throw new InvalidOperationException("RECOVERY_RESOURCE_ALREADY_STARTED");
        started = true;
        if (endpoint is null) throw new InvalidOperationException("RECOVERY_RECONCILIATION_CANNOT_START");
        await endpoint.Ownership.PrepareAsync(descriptor, ct).ConfigureAwait(false); registered = true;
        docker = await endpoint.ClientAsync(ct).ConfigureAwait(false);
        await endpoint.VerifyEngineAsync(ct).ConfigureAwait(false);
        var image = await Docker.Images.InspectImageAsync(ImageId, ct).ConfigureAwait(false);
        if (image.ID != ImageId || image.Config is null) throw new InvalidOperationException("RECOVERY_CACHED_IMAGE_INVALID");
        var volumes = image.Config.Volumes?.Keys.ToArray() ?? [];
        var postgres = role == "native-postgres";
        if (postgres ? volumes.Any(p => p is not ("/var/lib/postgresql" or "/var/lib/postgresql/data")) : volumes.Length != 0)
            throw new InvalidOperationException("RECOVERY_IMAGE_VOLUME_INVALID");
        if (await InspectAsync(name, ct).ConfigureAwait(false) is not null) throw new InvalidOperationException("RECOVERY_RESOURCE_NAME_OCCUPIED");
        absentBeforeCreate = true;
        var tmpfs = new Dictionary<string, string>(StringComparer.Ordinal);
        if (postgres)
        {
            var postgresTmpfs = "rw,nosuid,nodev,size=" +
                (RecoveryCodeOwnedDockerFixture.NativePostgresCapacityBytes / (1024L * 1024)).ToString(CultureInfo.InvariantCulture) + "m";
            tmpfs["/var/lib/postgresql"] = postgresTmpfs;
            foreach (var target in volumes) tmpfs[target] = postgresTmpfs;
        }
        var parameters = new CreateContainerParameters
        {
            Name = name, Image = ImageId, Env = EnvironmentValues,
            Labels = new Dictionary<string, string>(StringComparer.Ordinal)
            { [OwnerLabel] = InstanceId.ToString("N"), [RoleLabel] = role, [RepositoryLabel] = repository, ["tunner.p2-009.run"] = runId },
            ExposedPorts = ports.ToDictionary(Key, _ => default(EmptyStruct)),
            HostConfig = new HostConfig
            {
                AutoRemove = false, Privileged = false, PublishAllPorts = false,
                LogConfig = new LogConfig { Type = "none" }, Tmpfs = tmpfs,
                PortBindings = ports.ToDictionary(Key, _ => (IList<PortBinding>)[new PortBinding { HostIP = "127.0.0.1", HostPort = "" }])
            }
        };
        if (Command is { } command) parameters.Cmd = command;
        createAttempted = true;
        var created = await Docker.Containers.CreateContainerAsync(parameters, ct).ConfigureAwait(false);
        containerId = created.ID; // Retain before the next fallible action; ACK is mandatory before inspect/start.
        await endpoint.Ownership.CreatedAsync(descriptor, containerId, ct).ConfigureAwait(false);
        var actual = await InspectAsync(containerId, ct).ConfigureAwait(false);
        if (actual is not null && Owns(actual) && !CaptureOwnedAnonymousVolumes(actual))
            throw new InvalidOperationException("RECOVERY_UNEXPECTED_VOLUME_OWNER");
        if (actual is null || !Owns(actual) || !SafeConfiguration(actual, tmpfs.Keys))
            throw new InvalidOperationException("RECOVERY_RESOURCE_IDENTITY_INVALID");
        if (stopAfterCreate) return;
        if (!await Docker.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct).ConfigureAwait(false))
            throw new InvalidOperationException("RECOVERY_RESOURCE_START_REJECTED");
        actual = await Docker.Containers.InspectContainerAsync(containerId, ct).ConfigureAwait(false);
        if (!Owns(actual) || actual.State?.Running != true || !SafeConfiguration(actual, tmpfs.Keys))
            throw new InvalidOperationException("RECOVERY_RESOURCE_IDENTITY_INVALID");
        var network = actual.NetworkSettings?.Ports;
        if (network is null || network.Count != ports.Length) throw new InvalidOperationException("RECOVERY_LOOPBACK_BINDING_INVALID");
        foreach (var port in ports)
        {
            if (!network.TryGetValue(Key(port), out var bindings) || bindings is not { Count: 1 } || bindings[0].HostIP != "127.0.0.1" ||
                !int.TryParse(bindings[0].HostPort, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 1 or > 65535 || value == 25432)
                throw new InvalidOperationException("RECOVERY_LOOPBACK_BINDING_INVALID");
            published.Add(Key(port), value);
        }
        OwnershipVerified = true;
        await ReadyAsync(ct).ConfigureAwait(false);
    }
    private bool Owns(ContainerInspectResponse actual) => actual.Image == ImageId && actual.Name == "/" + name &&
        actual.Config?.Labels is { } labels && labels.TryGetValue(OwnerLabel, out var owner) && owner == InstanceId.ToString("N") &&
        labels.TryGetValue(RoleLabel, out var observedRole) && observedRole == role &&
        labels.TryGetValue(RepositoryLabel, out var root) && root == repository &&
        labels.TryGetValue("tunner.p2-009.run", out var observedRun) && observedRun == runId &&
        (containerId is null || containerId == actual.ID);
    private bool SafeConfiguration(ContainerInspectResponse actual, IEnumerable<string> expectedTmpfs)
    {
        var host = actual.HostConfig; var bindings = host?.PortBindings;
        var tmpfs = expectedTmpfs.ToHashSet(StringComparer.Ordinal);
        NoLogPersistenceVerified = host?.LogConfig?.Type == "none";
        return NoLogPersistenceVerified && host is not null && !host.Privileged && host.AutoRemove != true &&
            (host.Binds?.Count ?? 0) == 0 && (host.Mounts?.Count ?? 0) == 0 &&
            (actual.Mounts?.All(m => m.Type == "tmpfs" && tmpfs.Contains(m.Destination)) ?? true) &&
            (host.Tmpfs?.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(tmpfs) ?? tmpfs.Count == 0) &&
            bindings is not null && bindings.Count == ports.Length && ports.All(p => bindings.TryGetValue(Key(p), out var b) &&
                b is { Count: 1 } && b[0].HostIP == "127.0.0.1");
    }
    private bool CaptureOwnedAnonymousVolumes(ContainerInspectResponse actual)
    {
        // The create request has no binds/named mounts. Only generated anonymous mounts of this exact new owner qualify.
        if ((actual.HostConfig?.Binds?.Count ?? 0) != 0 || (actual.HostConfig?.Mounts?.Count ?? 0) != 0)
            return false;
        foreach (var mount in actual.Mounts ?? [])
        {
            if (mount.Type != "volume") continue;
            if (mount.Name is not { Length: 64 } || mount.Name.Any(c => !char.IsAsciiHexDigitLower(c)))
                return false;
            ownedAnonymousVolumes.Add(mount.Name);
        }
        return true;
    }
    private async Task<ContainerInspectResponse?> InspectAsync(string id, CancellationToken ct)
    {
        try { return await Docker.Containers.InspectContainerAsync(id, ct).ConfigureAwait(false); }
        catch (DockerApiException error) when (error.StatusCode == HttpStatusCode.NotFound) { return null; }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) { if (!CleanupVerified) throw new InvalidOperationException("RECOVERY_RESOURCE_CLEANUP_UNVERIFIED"); return; }
        disposed = true; var sessionsEnded = false; var removal = false; var volumeOwnership = true; var stopSucceeded = true;
        try { await EndSessionsAsync().ConfigureAwait(false); sessionsEnded = true; }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (docker is null)
            {
                if (endpoint is null) throw new InvalidOperationException("RECOVERY_RESOURCE_CLIENT_UNAVAILABLE");
                if (!registered) { await endpoint.Ownership.PrepareAsync(descriptor, deadline.Token).ConfigureAwait(false); registered = true; }
                docker = await endpoint.ClientAsync(deadline.Token).ConfigureAwait(false);
            }
            var actual = await InspectAsync(containerId ?? name, deadline.Token).ConfigureAwait(false);
            if (actual is not null)
            {
                // UNKNOWN Create is reconciled only by the generated name plus exact image and all ownership labels.
                if (!createAttempted || !absentBeforeCreate || !Owns(actual)) throw new InvalidOperationException("RECOVERY_RESOURCE_OWNER_MISMATCH");
                containerId ??= actual.ID;
                volumeOwnership = CaptureOwnedAnonymousVolumes(actual);
                using var stopDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { _ = await Docker.Containers.StopContainerAsync(actual.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 3 }, stopDeadline.Token).ConfigureAwait(false); }
                catch (DockerApiException error) when (error.StatusCode is HttpStatusCode.NotModified or HttpStatusCode.NotFound) { }
                catch (Exception error) when (error is not OutOfMemoryException) { stopSucceeded = false; }
                // A failed graceful stop must not suppress the separate removal attempt for this positively owned ID.
                using var removeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await Docker.Containers.RemoveContainerAsync(actual.ID, new ContainerRemoveParameters { Force = true, RemoveVolumes = volumeOwnership }, removeDeadline.Token).ConfigureAwait(false);
            }
            removal = true;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            ContainerAbsentVerified = await InspectAsync(name, deadline.Token).ConfigureAwait(false) is null &&
                (containerId is null || await InspectAsync(containerId, deadline.Token).ConfigureAwait(false) is null);
            AnonymousVolumesAbsentVerified = volumeOwnership;
            foreach (var volume in ownedAnonymousVolumes)
            {
                try { _ = await Docker.Volumes.InspectAsync(volume, deadline.Token).ConfigureAwait(false); AnonymousVolumesAbsentVerified = false; }
                catch (DockerApiException error) when (error.StatusCode == HttpStatusCode.NotFound) { }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        finally { docker?.Dispose(); }
        CleanupVerified = sessionsEnded && stopSucceeded && removal && ContainerAbsentVerified && AnonymousVolumesAbsentVerified;
        if (!CleanupVerified) throw new InvalidOperationException("RECOVERY_RESOURCE_CLEANUP_UNVERIFIED");
    }
    public override string ToString() => nameof(RecoveryCodeOwnedContainer);
}

internal sealed class RecoveryCodeOwnedPostgres(RecoveryCodeLocalDockerEndpoint endpoint, string repository)
    : RecoveryCodeOwnedContainer(endpoint, repository, "native-postgres", RecoveryCodeOwnedDockerFixture.NativePostgresImage, [5432])
{
    internal string Host { get; } = "127.0.0.1";
    internal int Port => Published(5432);
    private bool ready;
    private readonly HashSet<string> identityDatabaseLeases = new(StringComparer.Ordinal);
    protected override IList<string> EnvironmentValues => ["POSTGRES_DB=tunner_outbox_test", "POSTGRES_USER=tunner", "POSTGRES_HOST_AUTH_METHOD=trust", "PGDATA=/var/lib/postgresql/p2_009"];
    internal void RequireReady() { if (!ready || !Available) throw new InvalidOperationException("RECOVERY_POSTGRES_NOT_READY"); }
    internal string Connection(string database) => new NpgsqlConnectionStringBuilder
    {
        Host = Host, Port = Port, Database = database, Username = "tunner", Password = string.Empty, Passfile = string.Empty,
        Pooling = false, Timeout = 5, CommandTimeout = 15, IncludeErrorDetail = false, ApplicationName = "Tunner.RecoveryFiniteNative"
    }.ConnectionString;
    internal async Task<string> CreateIdentityDatabaseAsync(CancellationToken ct, string? targetMigration = null)
    {
        RequireReady();
        var connectionString = await RecoveryCodeIdentityDatabaseProvisioning.CreateAsync(new NpgsqlConnectionStringBuilder(Connection("postgres")), targetMigration, ct).ConfigureAwait(false);
        lock (identityDatabaseLeases)
        {
            if (!identityDatabaseLeases.Add(connectionString)) throw new InvalidOperationException("RECOVERY_DATABASE_LEASE_INVALID");
        }
        return connectionString;
    }
    internal bool IdentityDatabasesReleased
    {
        get { lock (identityDatabaseLeases) return identityDatabaseLeases.Count == 0; }
    }
    internal async Task ReleaseIdentityDatabaseAsync(string connectionString)
    {
        RequireReady();
        var selected = new NpgsqlConnectionStringBuilder(connectionString); var name = selected.Database;
        if (name is not { Length: 41 } || !name.StartsWith("identity_", StringComparison.Ordinal) ||
            name[9..].Any(c => !char.IsAsciiHexDigitLower(c)) || connectionString != new NpgsqlConnectionStringBuilder(Connection(name)).ConnectionString)
            throw new InvalidOperationException("RECOVERY_DATABASE_LEASE_INVALID");
        lock (identityDatabaseLeases)
        {
            if (!identityDatabaseLeases.Contains(connectionString)) throw new InvalidOperationException("RECOVERY_DATABASE_LEASE_INVALID");
        }
        if (!await VerifyCreatedIdentityAsync(CancellationToken.None, expectedRunning: true).ConfigureAwait(false))
            throw new InvalidOperationException("RECOVERY_DATABASE_OWNER_UNVERIFIED");
        await using var connection = new NpgsqlConnection(Connection("postgres")); await connection.OpenAsync().ConfigureAwait(false);
        using var quoting = new NpgsqlCommandBuilder();
        await using (var drop = new NpgsqlCommand("DROP DATABASE " + quoting.QuoteIdentifier(name), connection))
            _ = await drop.ExecuteNonQueryAsync().ConfigureAwait(false);
        await using var absence = new NpgsqlCommand("SELECT NOT EXISTS(SELECT 1 FROM pg_database WHERE datname=@name)", connection);
        _ = absence.Parameters.AddWithValue("name", name);
        if (await absence.ExecuteScalarAsync().ConfigureAwait(false) is not true)
            throw new InvalidOperationException("RECOVERY_DATABASE_RELEASE_UNVERIFIED");
        lock (identityDatabaseLeases)
        {
            if (!identityDatabaseLeases.Remove(connectionString)) throw new InvalidOperationException("RECOVERY_DATABASE_LEASE_INVALID");
        }
    }
    protected override async Task ReadyAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var connection = new NpgsqlConnection(Connection("postgres")); await connection.OpenAsync(ct).ConfigureAwait(false);
                await using var command = new NpgsqlCommand("SELECT current_setting('server_version_num')::int BETWEEN 180000 AND 189999", connection);
                if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is true) { ready = true; return; }
            }
            catch (NpgsqlException) { }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
        throw new InvalidOperationException("RECOVERY_POSTGRES_READINESS_FAILED");
    }
}

internal sealed class RecoveryCodeOwnedMailpit(RecoveryCodeLocalDockerEndpoint endpoint, string repository)
    : RecoveryCodeOwnedContainer(endpoint, repository, "mailpit", RecoveryCodeOwnedDockerFixture.MailpitImage, [1025, 8025])
{
    internal int SmtpPort => Published(1025);
    internal string Origin => "http://127.0.0.1:" + Published(8025).ToString(CultureInfo.InvariantCulture);
    protected override async Task ReadyAsync(CancellationToken ct)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try { using var response = await client.GetAsync(Origin + "/", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); if (response.StatusCode == HttpStatusCode.OK) return; }
            catch (HttpRequestException) { }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
        throw new InvalidOperationException("RECOVERY_MAILPIT_READINESS_FAILED");
    }
}

internal sealed class RecoveryCodeOwnedOpenBao(RecoveryCodeLocalDockerEndpoint endpoint, string repository)
    : RecoveryCodeOwnedContainer(endpoint, repository, "openbao", RecoveryCodeOwnedDockerFixture.OpenBaoImage, [8200])
{
    private readonly string bootstrap = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly List<LocalOpenBaoSession> sessions = [];
    internal Uri Endpoint => new("http://127.0.0.1:" + Published(8200).ToString(CultureInfo.InvariantCulture) + "/");
    protected override IList<string> EnvironmentValues => ["BAO_DEV_ROOT_TOKEN_ID=" + bootstrap];
    protected override IList<string> Command => ["server", "-dev", "-dev-no-store-token", "-dev-listen-address=0.0.0.0:8200"];
    internal LocalOpenBaoCredentialScope CredentialScope() => new("EPHEMERAL_TEST", Endpoint, "disposable-vault", "secret/data/credentials", InstanceId);
    internal async Task<LocalOpenBaoSession> CreateSessionAsync(LocalOpenBaoCredentialScope scope, CancellationToken ct)
    {
        var expected = CredentialScope();
        if (!Available || scope.Endpoint != expected.Endpoint || scope.InstanceId != expected.InstanceId ||
            scope.StoreReference != expected.StoreReference || scope.ResourcePrefix != expected.ResourcePrefix)
            throw new InvalidOperationException("RECOVERY_VAULT_SCOPE_INVALID");
        var session = await LocalOpenBaoSession.CreateAsync(scope, bootstrap, ct).ConfigureAwait(false); sessions.Add(session); return session;
    }
    protected override async Task ReadyAsync(CancellationToken ct)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 16384 };
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(new Uri(Endpoint, "v1/sys/health"), ct).ConfigureAwait(false);
                using var health = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false));
                if (response.StatusCode == HttpStatusCode.OK && health.RootElement.GetProperty("version").GetString() == "2.7.0" &&
                    health.RootElement.GetProperty("initialized").GetBoolean() && !health.RootElement.GetProperty("sealed").GetBoolean()) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
        throw new InvalidOperationException("RECOVERY_VAULT_READINESS_FAILED");
    }
    protected override async Task EndSessionsAsync()
    {
        var verified = true;
        foreach (var session in sessions)
        {
            try { await session.DisposeAsync().ConfigureAwait(false); verified &= session.CleanupVerified; }
            catch (Exception error) when (error is not OutOfMemoryException) { verified = false; }
        }
        if (!verified) throw new InvalidOperationException("RECOVERY_VAULT_SESSION_CLEANUP_UNVERIFIED");
    }
}

internal sealed class RecoveryCodeBorrowedPostgres
{
    private const string ExpectedImage = "sha256:c6a5b95f7348a70e743a9b5e2a3fd78cf22767a039ae81b36e1e561f138cb09c";
    private readonly RecoveryCodeLocalDockerEndpoint? endpoint;
    private RecoveryCodeBorrowedPostgres(RecoveryCodeLocalDockerEndpoint? endpoint, string id, string project)
    { this.endpoint = endpoint; ContainerId = id; ProjectName = project; }
    internal string ContainerId { get; }
    internal string ImageId { get; } = ExpectedImage;
    internal string ProjectName { get; }
    internal string Host { get; } = "127.0.0.1";
    internal int Port { get; } = 25432;
    internal static async Task<RecoveryCodeBorrowedPostgres> VerifyAsync(RecoveryCodeLocalDockerEndpoint endpoint, string project, CancellationToken ct)
    {
        await endpoint.VerifyEngineAsync(ct).ConfigureAwait(false);
        using var docker = await endpoint.ClientAsync(ct).ConfigureAwait(false);
        return await VerifyUsingClientAsync(docker, endpoint, project, ct).ConfigureAwait(false);
    }
    internal static async Task<RecoveryCodeBorrowedPostgres> VerifyUsingClientAsync(IDockerClient docker, RecoveryCodeLocalDockerEndpoint? endpoint, string project, CancellationToken ct)
    {
        var matches = await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true, Filters = new Dictionary<string, IDictionary<string, bool>>(StringComparer.Ordinal)
            { ["label"] = new Dictionary<string, bool>(StringComparer.Ordinal) { ["com.docker.compose.project=" + project] = true, ["com.docker.compose.service=postgres"] = true } }
        }, ct).ConfigureAwait(false);
        if (matches.Count != 1) throw new InvalidOperationException("RECOVERY_BORROWED_POSTGRES_AMBIGUOUS");
        var actual = await docker.Containers.InspectContainerAsync(matches[0].ID, ct).ConfigureAwait(false);
        var labels = actual.Config?.Labels;
        static bool Bound(IDictionary<string, IList<PortBinding>>? ports) => ports is { Count: 1 } && ports.TryGetValue("5432/tcp", out var p) &&
            p is { Count: 1 } && p[0].HostIP == "127.0.0.1" && p[0].HostPort == "25432";
        if (actual.Image != ExpectedImage || actual.State?.Running != true || labels is null ||
            !labels.TryGetValue("com.docker.compose.project", out var observed) || observed != project ||
            !labels.TryGetValue("com.docker.compose.service", out var service) || service != "postgres" ||
            actual.HostConfig?.LogConfig?.Type != "json-file" ||
            !Bound(actual.HostConfig?.PortBindings) || !Bound(actual.NetworkSettings?.Ports))
            throw new InvalidOperationException("RECOVERY_BORROWED_POSTGRES_IDENTITY_INVALID");
        return new(endpoint, actual.ID, project);
    }
    internal async Task<bool> VerifyPreservedAsync(CancellationToken ct)
    {
        if (endpoint is null) return false;
        try { var current = await VerifyAsync(endpoint, ProjectName, ct).ConfigureAwait(false); return current.ContainerId == ContainerId; }
        catch (Exception error) when (error is not OutOfMemoryException) { return false; }
    }
    public override string ToString() => nameof(RecoveryCodeBorrowedPostgres);
}

/// <summary>Existing Identity fixture database/migration mechanics extracted once; no case/account workflow.</summary>
internal static class RecoveryCodeIdentityDatabaseProvisioning
{
    internal static IdentityPersistenceDbContext Context(ServiceProvider services, string connectionString)
    {
        // AddTunnerIdentityPersistence configured these options through its existing public registration pipeline.
        // A fresh context (not a root-scoped resolved context) preserves the fixture's concurrent-context behavior.
        var options = services.GetRequiredService<DbContextOptions<IdentityPersistenceDbContext>>();
        if (options.Extensions.OfType<Microsoft.EntityFrameworkCore.Infrastructure.RelationalOptionsExtension>().Single().ConnectionString != connectionString)
            throw new InvalidOperationException("RECOVERY_IDENTITY_DATABASE_OPTIONS_MISMATCH");
        return new IdentityPersistenceDbContext(options);
    }
    internal static async Task<string> CreateAsync(NpgsqlConnectionStringBuilder adminConnection, string? targetMigration, CancellationToken ct = default)
    {
        var name = "identity_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(adminConnection.ConnectionString))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand(); command.CommandText = "CREATE DATABASE " + name;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        adminConnection.Database = name;
        await using (var platform = new TunnerMigrationsDbContext(new DbContextOptionsBuilder<TunnerMigrationsDbContext>()
            .UseNpgsql(adminConnection.ConnectionString, x => x.MigrationsAssembly(typeof(TunnerMigrationsDbContextFactory).Assembly.FullName)).Options))
            await platform.Database.MigrateAsync(ct).ConfigureAwait(false);
        await using var services = new ServiceCollection().AddTunnerIdentityPersistence(adminConnection.ConnectionString).BuildServiceProvider();
        await using var identity = Context(services, adminConnection.ConnectionString);
        await identity.GetService<IMigrator>().MigrateAsync(targetMigration, ct).ConfigureAwait(false);
        if (!(await identity.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false)).Any() || targetMigration is null && identity.Database.HasPendingModelChanges())
            throw new InvalidOperationException("Identity test database must have the complete current migration before a test can run.");
        return adminConnection.ConnectionString;
    }
}

/// <summary>Metadata-helper-only restoration. Start is rejected before any container mutation.</summary>
internal sealed class RecoveryCodeReconciliationContainer(IDockerClient client, string repository, string runId,
    RecoveryCodePreparedFixtureOwner descriptor, string? knownId)
    : RecoveryCodeOwnedContainer(client, repository, runId, descriptor, knownId)
{
    protected override Task ReadyAsync(CancellationToken ct) => throw new InvalidOperationException("RECOVERY_RECONCILIATION_CANNOT_START");
}
