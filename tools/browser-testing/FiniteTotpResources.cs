using System.Net;
using System.Security.Cryptography;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

internal sealed record FiniteBorrowedPostgres(string ContainerId, string ImageId, string ProjectName, int Port);

internal static class FiniteTotpPostgresIdentity
{
    private const string ExpectedImage = "sha256:c6a5b95f7348a70e743a9b5e2a3fd78cf22767a039ae81b36e1e561f138cb09c";
    internal static async Task<FiniteBorrowedPostgres> VerifyAsync(DirectoryInfo root, string runtimeProject, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(root.FullName.ToLowerInvariant())))[..10];
        var project = "tunner-browser-" + hash;
        if (runtimeProject != project) throw new InvalidOperationException("FINITE_BORROWED_POSTGRES_PROJECT_INVALID");
        var endpoint = TestcontainersSettings.OS.DockerEndpointAuthConfig;
        if (endpoint.Endpoint != new Uri("npipe://./pipe/docker_engine")) throw new InvalidOperationException("FINITE_LOCAL_DOCKER_REQUIRED");
        using var docker = endpoint.GetDockerClientBuilder(Guid.NewGuid()).WithLogger(NullLogger.Instance).WithTimeout(TimeSpan.FromSeconds(20)).Build();
        var matches = await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>(StringComparer.Ordinal)
            {
                ["label"] = new Dictionary<string, bool>(StringComparer.Ordinal)
                { ["com.docker.compose.project=" + project] = true, ["com.docker.compose.service=postgres"] = true }
            }
        }, cancellationToken).ConfigureAwait(false);
        if (matches.Count != 1) throw new InvalidOperationException("FINITE_BORROWED_POSTGRES_AMBIGUOUS");
        var actual = await docker.Containers.InspectContainerAsync(matches[0].ID, cancellationToken).ConfigureAwait(false);
        var labels = actual.Config?.Labels;
        var host = actual.HostConfig?.PortBindings;
        var network = actual.NetworkSettings?.Ports;
        if (actual.Image != ExpectedImage || actual.State?.Running != true || labels is null ||
            !labels.TryGetValue("com.docker.compose.project", out var observedProject) || observedProject != project ||
            !labels.TryGetValue("com.docker.compose.service", out var service) || service != "postgres" ||
            host is not { Count: 1 } || !host.TryGetValue("5432/tcp", out var requested) || requested is not { Count: 1 } || requested[0].HostIP != "127.0.0.1" || requested[0].HostPort != "25432" ||
            network is not { Count: 1 } || !network.TryGetValue("5432/tcp", out var published) || published is not { Count: 1 } || published[0].HostIP != "127.0.0.1" || published[0].HostPort != "25432")
            throw new InvalidOperationException("FINITE_BORROWED_POSTGRES_IDENTITY_INVALID");
        return new(actual.ID, actual.Image, project, 25432);
    }
}

/// <summary>Only the internally generated database is owned; the verified PostgreSQL server is borrowed.</summary>
internal sealed class FiniteTotpDatabase(string runId)
{
    private readonly string name = "p2_008_" + runId;
    private readonly string marker = "tunner-finite-totp:" + runId;
    private uint? oid;
    private bool createAcknowledged;
    internal bool OwnershipVerified { get; private set; }
    internal bool CleanupVerified { get; private set; }
    internal string ServerVersion { get; private set; } = string.Empty;
    internal string ConnectionString => Connection(name);

    private static string Connection(string database) => new NpgsqlConnectionStringBuilder
    {
        Host = "127.0.0.1", Port = 25432, Database = database, Username = "tunner", Password = string.Empty,
        Passfile = string.Empty, Pooling = false, Timeout = 10, CommandTimeout = 15, IncludeErrorDetail = false, ApplicationName = "Tunner.FiniteTotp"
    }.ConnectionString;

    internal async Task CreateAsync(CancellationToken cancellationToken)
    {
        if (name.Length != 39 || name[7..].Any(value => !char.IsAsciiHexDigitLower(value))) throw new InvalidOperationException("FINITE_DATABASE_NAME_INVALID");
        await using var admin = new NpgsqlConnection(Connection("postgres"));
        await admin.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var version = new NpgsqlCommand("SELECT current_setting('server_version')", admin))
        {
            ServerVersion = (string)(await version.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            if (!ServerVersion.StartsWith("18.6", StringComparison.Ordinal) || ServerVersion.Length > 100) throw new InvalidOperationException("FINITE_BORROWED_POSTGRES_VERSION_INVALID");
        }
        await using (var command = new NpgsqlCommand("CREATE DATABASE \"" + name + "\" OWNER tunner", admin))
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            createAcknowledged = true;
        }
        await using (var command = new NpgsqlCommand("COMMENT ON DATABASE \"" + name + "\" IS '" + marker + "'", admin))
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using (var command = new NpgsqlCommand("SELECT oid FROM pg_database WHERE datname=@name AND pg_get_userbyid(datdba)=current_user AND shobj_description(oid,'pg_database')=@marker", admin))
        {
            command.Parameters.AddWithValue("name", name); command.Parameters.AddWithValue("marker", marker);
            oid = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is uint value ? value : null;
            OwnershipVerified = oid is not null;
        }
        if (!OwnershipVerified) throw new InvalidOperationException("FINITE_DATABASE_OWNERSHIP_UNVERIFIED");
    }
    internal async Task<bool> DropAsync()
    {
        CleanupVerified = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await using var admin = new NpgsqlConnection(Connection("postgres"));
            await admin.OpenAsync(deadline.Token).ConfigureAwait(false);
            await using var check = new NpgsqlCommand("SELECT oid,pg_get_userbyid(datdba)=current_user,shobj_description(oid,'pg_database') FROM pg_database WHERE datname=@name", admin);
            check.Parameters.AddWithValue("name", name);
            await using (var reader = await check.ExecuteReaderAsync(deadline.Token).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(deadline.Token).ConfigureAwait(false)) { CleanupVerified = true; return true; }
                // A create timeout is not ownership proof. Retain an unmarked/unacknowledged database for scoped reconciliation.
                if (!createAcknowledged || !reader.GetBoolean(1) || reader.IsDBNull(2) || reader.GetString(2) != marker ||
                    oid is not null && reader.GetFieldValue<uint>(0) != oid) return false;
            }
            await using (var drop = new NpgsqlCommand("DROP DATABASE \"" + name + "\"", admin))
                await drop.ExecuteNonQueryAsync(deadline.Token).ConfigureAwait(false); // No FORCE, termination or global pool clearing.
            await using var absent = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname=@name", admin);
            absent.Parameters.AddWithValue("name", name);
            CleanupVerified = (long)(await absent.ExecuteScalarAsync(deadline.Token).ConfigureAwait(false))! == 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        return CleanupVerified;
    }
    internal async Task<Dictionary<string, int>> CountsAsync(CancellationToken cancellationToken)
    {
        if (!OwnershipVerified) throw new InvalidOperationException("FINITE_DATABASE_OWNERSHIP_UNVERIFIED");
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ownerCommits"] = "SELECT (SELECT count(*) FROM identity.totp_operations WHERE state='COMPLETED')+(SELECT count(*) FROM browser_testing.step_up_results)",
            ["proofs"] = "SELECT count(*) FROM identity.step_up_proofs",
            ["operations"] = "SELECT count(*) FROM identity.totp_operations",
            ["sessions"] = "SELECT count(*) FROM identity.tunner_sessions",
            ["outbox"] = "SELECT count(*) FROM platform.outbox_messages",
            ["codeAcceptances"] = "SELECT count(*) FROM identity.totp_used_codes",
            ["mfaSessions"] = "SELECT count(*) FROM identity.tunner_sessions WHERE authentication_methods IN ('pwd+totp','passkey+totp')"
        };
        await using (var account = new NpgsqlCommand("SELECT count(*) FROM accounts.accounts", connection))
            if ((long)(await account.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! > 1) throw new InvalidOperationException("FINITE_ACCOUNT_SCOPE_INVALID");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, sql) in queries)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            if (count is < 0 or > 1000) throw new InvalidOperationException("FINITE_COUNT_BOUND_EXCEEDED");
            counts.Add(key, count);
        }
        return counts;
    }
    internal async Task<(bool Safe, bool Bound)> InspectSafePersistenceAsync(string storeReference, string prefix, CancellationToken cancellationToken)
    {
        if (!OwnershipVerified) return (false, false);
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var shape = new NpgsqlCommand("SELECT count(*) FROM information_schema.columns WHERE table_schema='identity' AND table_name LIKE 'totp_%' AND column_name IN ('seed','secret','code','otp','manual_key','provisioning_uri')", connection);
        var safeColumns = (long)(await shape.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! == 0;
        await using var tokens = new NpgsqlCommand("SELECT count(*) FROM identity.\"AspNetUserTokens\"", connection);
        var emptyTokens = (long)(await tokens.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! == 0;
        await using var events = new NpgsqlCommand("""
            SELECT count(*) FROM platform.outbox_messages m WHERE source='tunner/identity' AND
            (jsonb_typeof(payload)<>'object' OR EXISTS
              (SELECT 1 FROM jsonb_each(m.payload) p WHERE NOT CASE lower(p.key)
                WHEN 'reference' THEN jsonb_typeof(p.value)='string' AND (p.value #>> '{}') ~ '^(ACC-|OP-|OP-TOTP-|OP-PASSKEY-|OP-LOCAL-STEP-UP-)?[A-Fa-f0-9-]{32,36}$'
                WHEN 'operationreference' THEN jsonb_typeof(p.value)='string' AND (p.value #>> '{}') ~ '^[A-Fa-f0-9-]{32,36}$'
                WHEN 'authenticatorreference' THEN p.value='null'::jsonb OR (jsonb_typeof(p.value)='string' AND (p.value #>> '{}') ~ '^[A-Fa-f0-9-]{32,36}$')
                WHEN 'schemaversion' THEN p.value='"1"'::jsonb
                WHEN 'authenticatortype' THEN p.value IN ('"TOTP"'::jsonb,'"PASSKEY"'::jsonb)
                WHEN 'operation' THEN jsonb_typeof(p.value)='string' AND (p.value #>> '{}') IN
                  ('ENROLL_TOTP','DISABLE_TOTP','RESET_TOTP','REGISTER_PASSKEY','REVOKE_PASSKEY','AUTHENTICATE_PASSKEY','LOCAL_TEST_VERIFY_IDENTITY')
                WHEN 'eventtype' THEN jsonb_typeof(p.value)='string' AND (p.value #>> '{}') ~ '^[A-Za-z][A-Za-z0-9]{0,63}$'
                ELSE false END))
            """, connection);
        var safeEvents = (long)(await events.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! == 0;
        await using var binding = new NpgsqlCommand("SELECT count(*)>0 AND bool_and(secret_store_reference=@store AND secret_resource_reference LIKE @prefix AND secret_version=1 AND secret_binding_version=@binding) FROM identity.totp_authenticators", connection);
        binding.Parameters.AddWithValue("store", storeReference); binding.Parameters.AddWithValue("prefix", prefix + "/%"); binding.Parameters.AddWithValue("binding", BrowserTestTotp.BindingVersion);
        var bound = await binding.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
        return (safeColumns && emptyTokens && safeEvents, bound);
    }
    public override string ToString() => nameof(FiniteTotpDatabase);
}

/// <summary>Process-local Docker discovery isolation must be installed before any Testcontainers settings are used.</summary>
internal sealed class FiniteTotpDockerEnvironment
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "tunner-p2-008-docker-" + Guid.NewGuid().ToString("N"));
    private bool created;
    internal string DirectoryPath => directory;
    internal void Initialize()
    {
        if (Directory.Exists(directory)) throw new InvalidOperationException("FINITE_DOCKER_DIRECTORY_COLLISION");
        Directory.CreateDirectory(directory);
        created = true;
        File.WriteAllText(Path.Combine(directory, "config.json"), "{}\n");
        Environment.SetEnvironmentVariable("DOCKER_CONFIG", directory);
        foreach (var name in new[] { "DOCKER_AUTH_CONFIG", "REGISTRY_AUTH_FILE", "DOCKER_CONTEXT", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH", "PGPASSWORD", "PGPASSFILE", "PGSERVICE", "PGSERVICEFILE" })
            Environment.SetEnvironmentVariable(name, null);
        Environment.SetEnvironmentVariable("DOCKER_HOST", "npipe://./pipe/docker_engine");
    }
    internal bool Remove()
    {
        if (!created) return false;
        try
        {
            var full = Path.GetFullPath(directory); var parent = Path.GetFullPath(Path.GetTempPath());
            if (!full.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(full).StartsWith("tunner-p2-008-docker-", StringComparison.Ordinal) ||
                (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return false;
            File.Delete(Path.Combine(full, "config.json"));
            Directory.Delete(full, false);
            return !Directory.Exists(full);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return false; }
    }
}

internal sealed class FiniteTotpMailpit : IAsyncDisposable
{
    internal const string Image = "tunner-security/mailpit:1.33.1-r1";
    internal const string ImageId = "sha256:0306debe903ec7a44a4d49266ac54c54667183efc5de1586d75093d9b60b823b";
    internal const string OpenBaoImageId = "sha256:71156a1c6623a5fa3f5e61b0c6a8ead0faf0df29a778339188443551995d1315";
    private const string Label = "tunner.test.finite-mailpit";
    private readonly string instance = Guid.NewGuid().ToString("N");
    private DockerClient? docker;
    private IContainer? container;
    internal int SmtpPort { get; private set; }
    internal string Origin { get; private set; } = string.Empty;
    internal bool OwnershipVerified { get; private set; }
    internal bool CleanupVerified { get; private set; }
    private bool disposed;
    private static readonly string[] ExpectedPorts = ["1025/tcp", "8025/tcp"];

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        var endpoint = TestcontainersSettings.OS.DockerEndpointAuthConfig;
        if (endpoint.Endpoint != new Uri("npipe://./pipe/docker_engine")) throw new InvalidOperationException("FINITE_LOCAL_DOCKER_REQUIRED");
        docker = endpoint.GetDockerClientBuilder(Guid.NewGuid()).WithLogger(NullLogger.Instance).WithTimeout(TimeSpan.FromSeconds(20)).Build();
        var image = await docker.Images.InspectImageAsync(Image, cancellationToken).ConfigureAwait(false);
        var vault = await docker.Images.InspectImageAsync("openbao/openbao:2.7.0", cancellationToken).ConfigureAwait(false);
        if (image.ID != ImageId || image.Config?.Volumes?.Count > 0 || vault.ID != OpenBaoImageId || vault.Config?.Volumes?.Count > 0)
            throw new InvalidOperationException("FINITE_CACHED_IMAGE_ID_MISMATCH");
        container = new ContainerBuilder(Image).WithDockerEndpoint(endpoint).WithName("tunner-finite-mailpit-" + instance).WithLabel(Label, instance)
            .WithPortBinding(1025, true).WithPortBinding(8025, true).WithImagePullPolicy(static _ => false).WithCleanUp(false)
            .WithLogger(NullLogger.Instance).WithOutputConsumer(Consume.DoNotConsumeStdoutAndStderr())
            .WithCreateParameterModifier(parameters =>
            {
                parameters.HostConfig ??= new HostConfig(); parameters.HostConfig.LogConfig = new LogConfig { Type = "none" };
                parameters.HostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>(StringComparer.Ordinal)
                {
                    ["1025/tcp"] = [new PortBinding { HostIP = "127.0.0.1", HostPort = "" }],
                    ["8025/tcp"] = [new PortBinding { HostIP = "127.0.0.1", HostPort = "" }]
                };
            }).WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025).ForPath("/"))).Build();
        await container.StartAsync(cancellationToken).ConfigureAwait(false);
        var actual = await docker.Containers.InspectContainerAsync(container.Id, cancellationToken).ConfigureAwait(false);
        var ports = actual.NetworkSettings?.Ports;
        OwnershipVerified = actual.Image == ImageId && actual.Config?.Labels?.TryGetValue(Label, out var owner) == true && owner == instance &&
            actual.HostConfig?.LogConfig?.Type == "none" && actual.Mounts is { Count: 0 } && (actual.Config.Volumes?.Count ?? 0) == 0 &&
            (actual.HostConfig.Binds?.Count ?? 0) == 0 && ports is not null && ExpectedPorts.All(key =>
                ports.TryGetValue(key, out var bindings) && bindings is { Count: 1 } && bindings[0].HostIP == "127.0.0.1");
        if (!OwnershipVerified) throw new InvalidOperationException("FINITE_MAILPIT_OWNERSHIP_INVALID");
        SmtpPort = container.GetMappedPublicPort(1025); Origin = "http://127.0.0.1:" + container.GetMappedPublicPort(8025);
    }
    internal async Task<bool> VaultAbsentAsync(string vaultInstance)
    {
        if (docker is null) return false;
        try { _ = await docker.Containers.InspectContainerAsync("tunner-disposable-vault-" + vaultInstance).ConfigureAwait(false); return false; }
        catch (DockerApiException error) when (error.StatusCode == HttpStatusCode.NotFound) { return true; }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        if (docker is null) return;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            ContainerInspectResponse? actual = null;
            try { actual = await docker.Containers.InspectContainerAsync("tunner-finite-mailpit-" + instance, deadline.Token).ConfigureAwait(false); }
            catch (DockerApiException error) when (error.StatusCode == HttpStatusCode.NotFound) { }
            if (actual is not null)
            {
                if (actual.Config?.Labels?.TryGetValue(Label, out var owner) != true || owner != instance) return;
                await docker.Containers.RemoveContainerAsync(actual.ID, new ContainerRemoveParameters { Force = true, RemoveVolumes = true }, deadline.Token).ConfigureAwait(false);
            }
            if (container is not null) await container.DisposeAsync().AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
            try { _ = await docker.Containers.InspectContainerAsync("tunner-finite-mailpit-" + instance, deadline.Token).ConfigureAwait(false); }
            catch (DockerApiException error) when (error.StatusCode == HttpStatusCode.NotFound) { CleanupVerified = true; }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        finally { docker.Dispose(); }
    }
    public override string ToString() => nameof(FiniteTotpMailpit);
}
