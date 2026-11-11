using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Tunner.Governance;
using Tunner.Identity.Persistence;
using Tunner.Installer;
using Tunner.Testing.OpenBao;

/// <summary>One finite Development-only run. Only fixed metadata crosses its private browser-control channel.</summary>
internal static class FiniteTotpCoordinator
{
    private static readonly string[] PublicFaults = ["oracle-roundtrip", "oracle-input", "oracle-exit", "oracle-timeout", "oracle-stderr", "journey-entry", "browser-error", "browser-unhandled", "browser-timeout", "unexpected-output", "invalid-result", "cleanup"];
    private static readonly string[] Actions = ["READ_SAFE_COUNTS", "DENY_OWNER_ONCE", "DROP_DISCLOSURE_ONCE", "DROP_ACTIVATION_ONCE", "DROP_MFA_ONCE"];
    private static readonly string[] CountKeys = ["ownerCommits", "proofs", "operations", "sessions", "outbox", "codeAcceptances", "mfaSessions"];
    private static readonly string[] BrowserCleanupKeys = ["processExited", "oracleExited", "surfaceClean", "surfaceRemoved", "cleanupCompleted"];
    private static readonly string[] ExpectedFailedCleanupFaults = ["cleanup", "oracle-input", "oracle-exit", "oracle-timeout", "oracle-stderr"];
    private static readonly string[] ResourceCanaries = ["after-db", "after-mailpit", "after-vault-session", "after-app-start", "cleanup-reject"];
    private static readonly string[] CoordinatorIds = ["SOURCE_BUILD_CURRENT", "BORROWED_POSTGRES_IDENTITY", "BORROWED_POSTGRES_PRESERVED", "OWNED_DATABASE_IDENTITY", "CURRENT_MIGRATIONS", "EPHEMERAL_DP", "OWNED_MAILPIT_IDENTITY", "SCOPED_VAULT_APPLICATION_ACCESS", "RAW_DIAGNOSTICS_CONTAINED", "BROWSER_EXIT_VERIFIED", "ORACLE_EXIT_VERIFIED", "APP_STOP_VERIFIED", "TOKEN_REVOKED", "POLICY_REMOVED", "VAULT_CONTAINER_ABSENT", "OWNED_DATABASE_ABSENT", "MAILPIT_CONTAINER_ABSENT", "OWNED_SURFACE_ABSENT", "CLEANUP_ALL_STAGES_ATTEMPTED", "SAFE_PERSISTENCE_AND_AUDIT", "VAULT_OWNER_VERSION_BINDING"];

    internal static async Task<int> RunAsync(string selection, bool publicFaultMode, bool resourceCanaryMode = false)
    {
        await using var finalOutput = Console.OpenStandardOutput();
        Console.SetOut(TextWriter.Null); Console.SetError(TextWriter.Null);
        var checks = CoordinatorIds.ToDictionary(id => id, _ => false, StringComparer.Ordinal);
        var runId = Guid.NewGuid().ToString("N");
        var journey = publicFaultMode || resourceCanaryMode ? BrowserTestTotp.JourneyIds[0] : selection;
        var failure = "INPUT_INVALID";
        var source = string.Empty; var committed = false;
        FiniteTotpBuildIdentity? buildBinding = null;
        var persistenceDiagnostics = new FiniteTotpPersistenceObservations(false, false, false, false, false);
        FiniteBorrowedPostgres? borrowedPostgres = null;
        DirectoryInfo? repository = null;
        var resourceFaultObserved = false;
        JsonElement? browser = null;
        WebApplication? app = null;
        X509Certificate2? certificate = null;
        FiniteTotpDatabase? database = null;
        FiniteTotpMailpit? mailpit = null;
        DisposableOpenBaoFixture? vault = null;
        LocalOpenBaoSession? vaultSession = null;
        FiniteTotpDockerEnvironment? dockerEnvironment = null;
        var browserExited = false; var rawOutput = false; var browserExitCode = -1;
        using var workDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(270));
        try
        {
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development" ||
                !(publicFaultMode ? PublicFaults.Contains(selection, StringComparer.Ordinal) : resourceCanaryMode ? ResourceCanaries.Contains(selection, StringComparer.Ordinal) : BrowserTestTotp.JourneyIds.Contains(selection, StringComparer.Ordinal)))
                throw new InvalidOperationException("INPUT_INVALID");
            var root = RepositoryRootResolver.Resolve();
            repository = root;
            source = Git(root, "rev-parse", "HEAD");
            committed = Regex.IsMatch(source, "^[a-f0-9]{40}$", RegexOptions.CultureInvariant) && Git(root, ["status", "--porcelain", "--", .. FiniteTotpBuildBinding.SourceSelectors]).Length == 0;
            if (!publicFaultMode && !committed) { failure = "SOURCE_UNCOMMITTED"; throw new InvalidOperationException(); }
            failure = "START_FAILED";
            var assembly = Path.GetFullPath(Path.Combine(root.FullName, "tools", "browser-testing", "bin", "Debug", "net10.0", "Tunner.BrowserTesting.dll"));
            if (!File.Exists(assembly) || Path.GetFullPath(typeof(FiniteTotpCoordinator).Assembly.Location) != assembly) throw new InvalidOperationException();
            var origin = "https://localhost:24443"; var mailpitOrigin = "http://127.0.0.1:28025";
            BrowserTestTotp? profile = null;
            if (!publicFaultMode)
            {
                failure = "SOURCE_BUILD_BINDING_INVALID";
                buildBinding = FiniteTotpBuildBinding.Verify(root);
                checks["SOURCE_BUILD_CURRENT"] = true;
                failure = "START_FAILED";
                // All credential/config discovery is isolated before the first Testcontainers setting or client is accessed.
                dockerEnvironment = new(); dockerEnvironment.Initialize();
                using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root.FullName, "artifacts", "browser-testing", "runtime.json"), workDeadline.Token).ConfigureAwait(false));
                if (metadata.RootElement.GetProperty("Mode").GetString() != "ISOLATED_LOCAL_BROWSER_TESTING" || metadata.RootElement.GetProperty("PostgresPort").GetInt32() != 25432) throw new InvalidOperationException();
                borrowedPostgres = await FiniteTotpPostgresIdentity.VerifyAsync(root, metadata.RootElement.GetProperty("ProjectName").GetString() ?? string.Empty, workDeadline.Token).ConfigureAwait(false);
                checks["BORROWED_POSTGRES_IDENTITY"] = true;
                var thumbprint = metadata.RootElement.GetProperty("CertificateThumbprint").GetString() ?? throw new InvalidOperationException();
                using (var certificates = new X509Store(StoreName.My, StoreLocation.CurrentUser))
                {
                    certificates.Open(OpenFlags.ReadOnly);
                    certificate = certificates.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false).OfType<X509Certificate2>()
                        .Single(value => value.HasPrivateKey && value.NotAfter > DateTime.Now);
                }
                if (certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256) != metadata.RootElement.GetProperty("CertificateSha256").GetString()) throw new InvalidOperationException();
                database = new(runId); await database.CreateAsync(workDeadline.Token).ConfigureAwait(false);
                checks["OWNED_DATABASE_IDENTITY"] = database.OwnershipVerified;
                InterruptAfter("after-db");
                mailpit = new(); await mailpit.StartAsync(workDeadline.Token).ConfigureAwait(false);
                checks["OWNED_MAILPIT_IDENTITY"] = mailpit.OwnershipVerified; mailpitOrigin = mailpit.Origin;
                InterruptAfter("after-mailpit");
                vault = await DisposableOpenBaoFixture.StartAsync(workDeadline.Token).ConfigureAwait(false);
                if (vault.Provenance.ImageId != FiniteTotpMailpit.OpenBaoImageId) throw new InvalidOperationException();
                var scope = vault.CredentialScope();
                vaultSession = await vault.CreateSessionAsync(scope, workDeadline.Token).ConfigureAwait(false);
                var seeds = new OpenBaoTotpSeedStore(vaultSession.Secrets, scope.StoreReference, scope.ResourcePrefix, BrowserTestTotp.BindingVersion);
                checks["SCOPED_VAULT_APPLICATION_ACCESS"] = true;
                InterruptAfter("after-vault-session");
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                origin = "https://localhost:" + port;
                profile = new(origin, journey, database.ConnectionString, seeds);
                var options = new BrowserTestHostOptions(origin, port, database.ConnectionString, certificate, mailpit.SmtpPort, null,
                    "FINITE_TOTP_BROWSER_TESTING", runId, profile);
                app = BrowserTestHost.Create(options);
                await BrowserTestHost.MigrateAndMapAsync(app, options, workDeadline.Token).ConfigureAwait(false);
                checks["CURRENT_MIGRATIONS"] = true; checks["EPHEMERAL_DP"] = true;
                await app.StartAsync(workDeadline.Token).ConfigureAwait(false);
                InterruptAfter("after-app-start");
                InterruptAfter("cleanup-reject");
            }
            failure = "BROWSER_FAILED";
            var control = new { version = 3, type = "run", journeyId = journey, browserEngine = "chromium", origin, mailpitOrigin,
                sourceCommit = source, sourceCommitted = committed, runId, oracleAssemblyPath = assembly,
                oracleNativeVersion = BrowserTestTotpOracle.NativeVersion, publicFault = publicFaultMode ? selection : null };
            (browser, browserExited, browserExitCode, rawOutput) = await RunBrowserAsync(root, control, profile, database, workDeadline.Token).ConfigureAwait(false);
            checks["RAW_DIAGNOSTICS_CONTAINED"] = !rawOutput;
            checks["BROWSER_EXIT_VERIFIED"] = browserExited && browserExitCode == 0 && Flag(browser.Value, "processExited");
            checks["ORACLE_EXIT_VERIFIED"] = Flag(browser.Value, "oracleExited");
            if (!publicFaultMode && database is not null && vault is not null)
            {
                checks["SOURCE_BUILD_CURRENT"] = false;
                failure = "SOURCE_BUILD_CHANGED";
                var finalBinding = FiniteTotpBuildBinding.Verify(root);
                if (finalBinding != buildBinding) throw new InvalidOperationException();
                checks["SOURCE_BUILD_CURRENT"] = true;
                var scope = vault.CredentialScope();
                persistenceDiagnostics = await database.InspectSafePersistenceAsync(scope.StoreReference, scope.ResourcePrefix, workDeadline.Token).ConfigureAwait(false);
                checks["SAFE_PERSISTENCE_AND_AUDIT"] = persistenceDiagnostics.Safe; checks["VAULT_OWNER_VERSION_BINDING"] = persistenceDiagnostics.Bound;
            }
            failure = "NONE";
        }
        catch (FiniteResourceInterruption) { resourceFaultObserved = true; failure = "EXPECTED_PUBLIC_RESOURCE_INTERRUPTION"; }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        finally
        {
            if (app is not null)
            {
                var stopped = true;
                using var stopDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try { await app.StopAsync(stopDeadline.Token).ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { stopped = false; }
                try { await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { stopped = false; }
                checks["APP_STOP_VERIFIED"] = stopped;
            }
            if (vaultSession is not null)
            {
                try { await vaultSession.DisposeAsync().ConfigureAwait(false); checks["TOKEN_REVOKED"] = checks["POLICY_REMOVED"] = vaultSession.CleanupVerified; }
                catch (Exception error) when (error is not OutOfMemoryException) { }
            }
            if (vault is not null)
            {
                try { await vault.DisposeAsync().ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { }
                if (mailpit is not null)
                    try { checks["VAULT_CONTAINER_ABSENT"] = await mailpit.VaultAbsentAsync(vault.InstanceId.ToString("N")).ConfigureAwait(false); }
                    catch (Exception error) when (error is not OutOfMemoryException) { }
            }
            if (mailpit is not null)
            {
                try { await mailpit.DisposeAsync().ConfigureAwait(false); checks["MAILPIT_CONTAINER_ABSENT"] = mailpit.CleanupVerified; }
                catch (Exception error) when (error is not OutOfMemoryException) { }
            }
            if (database is not null) checks["OWNED_DATABASE_ABSENT"] = await database.DropAsync().ConfigureAwait(false);
            if (borrowedPostgres is not null && repository is not null)
            {
                using var identityDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { checks["BORROWED_POSTGRES_PRESERVED"] = await FiniteTotpPostgresIdentity.VerifyAsync(repository, borrowedPostgres.ProjectName, identityDeadline.Token).ConfigureAwait(false) == borrowedPostgres; }
                catch (Exception error) when (error is not OutOfMemoryException) { }
            }
            var dockerRemoved = dockerEnvironment is null || dockerEnvironment.Remove();
            checks["OWNED_SURFACE_ABSENT"] = dockerRemoved && (resourceCanaryMode || browser is { } receipt && Flag(receipt, "surfaceRemoved"));
            checks["CLEANUP_ALL_STAGES_ATTEMPTED"] = true;
            certificate?.Dispose();
        }
        var browserAccepted = browser is { } result && ValidReceipt(result, journey, source, !publicFaultMode) && Flag(result, "accepted");
        var accepted = RealAcceptance(!publicFaultMode && !resourceCanaryMode, failure, browserAccepted, checks);
        var publicObserved = publicFaultMode && browser is { } canary && ValidReceipt(canary, journey, source, false) && Flag(canary, "publicFaultObserved") &&
            browserExited && browserExitCode == 1 && !rawOutput && !Flag(canary, "accepted") && Flag(canary, "processExited") && Flag(canary, "oracleExited") && Flag(canary, "surfaceClean") && Flag(canary, "surfaceRemoved") &&
            Flag(canary, "cleanupCompleted") == !ExpectedFailedCleanupFaults.Contains(selection, StringComparer.Ordinal);
        if (!publicFaultMode && failure == "NONE" && !accepted) failure = "CLEANUP_OR_PREDICATE_FAILED";
        var resourceContained = resourceCanaryMode && resourceFaultObserved && !accepted &&
            checks["BORROWED_POSTGRES_PRESERVED"] && checks["OWNED_SURFACE_ABSENT"] && checks["CLEANUP_ALL_STAGES_ATTEMPTED"] &&
            (database is null || checks["OWNED_DATABASE_ABSENT"]) && (mailpit is null || checks["MAILPIT_CONTAINER_ABSENT"]) &&
            (vault is null || checks["TOKEN_REVOKED"] && checks["POLICY_REMOVED"] && checks["VAULT_CONTAINER_ABSENT"]) &&
            (app is null || checks["APP_STOP_VERIFIED"]);
        var publicCleanupReducerRejected = false;
        if (resourceContained && selection == "cleanup-reject")
        {
            // Public reducer probe only; never overwrite actual infrastructure or browser observations.
            var publicFlags = CoordinatorIds.ToDictionary(id => id, _ => true, StringComparer.Ordinal);
            var positiveControl = RealAcceptance(true, "NONE", true, publicFlags);
            publicFlags["APP_STOP_VERIFIED"] = false;
            publicCleanupReducerRejected = positiveControl && !RealAcceptance(true, "NONE", true, publicFlags);
            resourceContained &= publicCleanupReducerRejected;
        }
        if (resourceCanaryMode && selection == "cleanup-reject" && resourceFaultObserved) failure = "EXPECTED_PUBLIC_CLEANUP_REJECTION";
        var summary = new { version = 3, journeyId = journey, runId, sourceCommit = source.Length == 40 ? source : null, sourceCommitted = committed,
            status = accepted ? "REAL_JOURNEY_OK" : "BOUNDARY_FAILED", failureCode = failure, accepted,
            publicFault = publicFaultMode ? selection : null, publicBoundaryCanaryPassed = publicObserved,
            resourceCanary = resourceCanaryMode ? selection : null, publicResourceCanaryPassed = resourceContained,
            resourceFaultObserved, injectedCleanupFailure = resourceCanaryMode && selection == "cleanup-reject" && resourceFaultObserved,
            publicCleanupReducerRejected,
            buildBinding, borrowedPostgres, postgresVersion = database?.ServerVersion,
            browserReceipt = browser, coordinatorChecks = checks, persistenceDiagnostics,
            boundary = "Finite local synthetic evidence only. No production policy, Product, merge or release approval." };
        await JsonSerializer.SerializeAsync(finalOutput, summary).ConfigureAwait(false);
        await finalOutput.WriteAsync("\n"u8.ToArray()).ConfigureAwait(false);
        return accepted || publicObserved || resourceContained ? 0 : 1;

        void InterruptAfter(string stage)
        {
            if (resourceCanaryMode && selection == stage) throw new FiniteResourceInterruption();
        }
    }

    private static async Task<(JsonElement Receipt, bool Exited, int ExitCode, bool RawOutput)> RunBrowserAsync<T>(DirectoryInfo root, T control,
        BrowserTestTotp? profile, FiniteTotpDatabase? database, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(FindNode()) { WorkingDirectory = root.FullName, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(root.FullName, "web", "tests", "totp", "sensitive", "real-supervisor.mjs"));
        var allowed = new[] { "PATH", "SystemRoot", "SystemDrive", "ProgramData", "WINDIR", "COMSPEC", "PATHEXT", "PROCESSOR_ARCHITECTURE", "OS", "LOCALAPPDATA", "APPDATA", "USERPROFILE", "TEMP", "TMP" };
        start.Environment.Clear();
        foreach (var name in allowed) if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"; start.Environment["NODE_OPTIONS"] = string.Empty;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("FINITE_BROWSER_START_FAILED");
        var raw = false;
        var errors = DrainErrorsAsync();
        JsonElement? terminal = null; var sequence = 0; var total = 0;
        try
        {
            await WriteFrameAsync(process.StandardInput.BaseStream, control, cancellationToken).ConfigureAwait(false);
            while (terminal is null)
            {
                var frame = await ReadFrameAsync(process.StandardOutput.BaseStream, cancellationToken).ConfigureAwait(false);
                total += frame.GetRawText().Length;
                if (total > 262144) throw new InvalidOperationException("FINITE_PROTOCOL_INVALID");
                if (frame.TryGetProperty("type", out var type) && type.GetString() == "fixtureRequest")
                {
                    RequireKeys(frame, "version", "type", "requestId", "action");
                    if (frame.GetProperty("version").GetInt32() != 3 || frame.GetProperty("requestId").GetInt32() != ++sequence || sequence > 16 ||
                        !Actions.Contains(frame.GetProperty("action").GetString(), StringComparer.Ordinal)) throw new InvalidOperationException("FINITE_PROTOCOL_INVALID");
                    var counts = ActionsCountZeros(); var ok = false;
                    if (profile is not null && database is not null)
                    {
                        using var requestDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                        try { profile.Arm(frame.GetProperty("action").GetString()!); counts = await database.CountsAsync(requestDeadline.Token).ConfigureAwait(false); ok = true; }
                        catch (Exception error) when (error is not OutOfMemoryException) { }
                    }
                    await WriteFrameAsync(process.StandardInput.BaseStream, new { version = 3, type = "fixtureResult", requestId = sequence, status = ok ? "OK" : "FAILED", counts }, cancellationToken).ConfigureAwait(false);
                }
                else terminal = frame;
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var trailing = new byte[1];
            if (await process.StandardOutput.BaseStream.ReadAsync(trailing, cancellationToken).ConfigureAwait(false) != 0) raw = true;
            await errors.ConfigureAwait(false);
            return (terminal.Value, process.HasExited, process.ExitCode, raw);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { await WriteFrameAsync(process.StandardInput.BaseStream, new { version = 3, type = "cancel" }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException) { }
                try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException) { }
                if (!process.HasExited)
                    try { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception error) when (error is not OutOfMemoryException) { }
            }
        }
        async Task DrainErrorsAsync()
        {
            var buffer = new byte[4096];
            while (await process.StandardError.BaseStream.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false) is var count && count > 0) raw = true;
        }
    }
    private static Dictionary<string, int> ActionsCountZeros() => CountKeys.ToDictionary(key => key, _ => 0, StringComparer.Ordinal);
    private static bool RealAcceptance(bool realRun, string failure, bool browserAccepted, Dictionary<string, bool> observations) =>
        realRun && failure == "NONE" && browserAccepted && observations.Count == CoordinatorIds.Length &&
        CoordinatorIds.All(id => observations.TryGetValue(id, out var observed) && observed);
    private static bool Flag(JsonElement value, string key) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.True;
    private static bool ValidReceipt(JsonElement value, string journey, string source, bool requireSuccess)
    {
        try
        {
            RequireKeys(value, "version", "type", "journeyId", "browserEngine", "browserVersion", "status", "stage", "failureCode", "checks", "oracleRequests", "rawOutputRejected", "processExited", "oracleExited", "surfaceClean", "surfaceRemoved", "cleanupCompleted", "sourceCommit", "sourceCommitted", "accepted", "publicFaultObserved");
            if (value.GetProperty("version").GetInt32() != 3 || value.GetProperty("type").GetString() != "browserReceipt" || value.GetProperty("journeyId").GetString() != journey ||
                value.GetProperty("browserEngine").GetString() != "chromium" || value.GetProperty("sourceCommit").GetString() != source ||
                value.GetProperty("oracleRequests").GetInt32() is < 0 or > 64) return false;
            foreach (var key in new[] { "rawOutputRejected", "processExited", "oracleExited", "surfaceClean", "surfaceRemoved", "cleanupCompleted", "sourceCommitted", "accepted", "publicFaultObserved" })
                if (value.GetProperty(key).ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            var required = BrowserChecks(journey);
            var checks = value.GetProperty("checks").EnumerateObject().ToArray();
            if (checks.Select(check => check.Name).Distinct(StringComparer.Ordinal).Count() != checks.Length ||
                checks.Any(check => !required.Contains(check.Name, StringComparer.Ordinal) || check.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))) return false;
            if (!new[] { "REAL_JOURNEY_OK", "BOUNDARY_FAILED" }.Contains(value.GetProperty("status").GetString(), StringComparer.Ordinal) ||
                !new[] { "START", "ENVIRONMENT", "LIBRARY", "BROWSER", "ORACLE", "JOURNEY", "JOURNEY_ENROLL_BEGIN", "JOURNEY_ENROLL_DISCLOSURE", "JOURNEY_ENROLL_RESULT_BINDING", "JOURNEY_ENROLL_DUPLICATE", "JOURNEY_ENROLL_DONE", "JOURNEY_MUTATION_BEGIN", "JOURNEY_MUTATION_READY", "JOURNEY_PROOF_ENTRY", "JOURNEY_PROOF_RESPONSE", "JOURNEY_PROOF_RECOVERY", "JOURNEY_PROOF_CONTINUE", "JOURNEY_PROOF_CONTINUED", "JOURNEY_MUTATION_CANCEL", "JOURNEY_MUTATION_CANCELLED", "SURFACE_SCAN", "CLEANUP", "COMPLETE" }.Contains(value.GetProperty("stage").GetString(), StringComparer.Ordinal) ||
                !new[] { "NONE", "INPUT_INVALID", "BOUNDARY_ERROR", "PREDICATE_FAILED", "ORACLE_FAILED", "SUPERVISOR_TIMEOUT", "CLEANUP_FAILED", "SURFACE_FAILED", "SOURCE_INVALID" }.Contains(value.GetProperty("failureCode").GetString(), StringComparer.Ordinal)) return false;
            if (!requireSuccess) return true;
            return value.GetProperty("status").GetString() == "REAL_JOURNEY_OK" && value.GetProperty("stage").GetString() == "COMPLETE" && value.GetProperty("failureCode").GetString() == "NONE" &&
                Regex.IsMatch(value.GetProperty("browserVersion").GetString() ?? "", "^\\d{1,5}\\.\\d{1,5}\\.\\d{1,5}\\.\\d{1,5}$", RegexOptions.CultureInvariant) &&
                !Flag(value, "rawOutputRejected") && !Flag(value, "publicFaultObserved") && Flag(value, "sourceCommitted") &&
                BrowserCleanupKeys.All(key => Flag(value, key)) &&
                checks.Length == required.Length && checks.All(check => check.Value.ValueKind == JsonValueKind.True);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException) { return false; }
    }
    private static string[] BrowserChecks(string journey)
    {
        string[] common = ["BROWSER_ENGINE", "BFF_ISOLATED", "REQUEST_BINDING", "OPERATION_BINDING", "DUPLICATE_COUNTS", "ONE_TIME_DISCLOSURE", "INACTIVE_BEFORE_ACTIVATION", "NATIVE_ORACLE_IDENTITY", "NATIVE_FIRST_CODE_ACTIVE", "WRONG_CODE_REJECTED", "REPLAY_REJECTED", "RESULT_RECOVERY", "COOKIE_FLAGS", "NO_URL_OR_STORAGE_SECRET", "UI_ASSET_TYPOGRAPHY", "CLEANUP_ATTEMPTED"];
        string[] extra = journey switch
        {
            "TOTP_ENROLLMENT_PASSWORD_MFA_PRESERVE_V1" => ["DISCLOSURE_LOSS_NO_REVEAL", "NATIVE_PASSWORD_MFA", "NO_EARLY_SESSION", "REPLAY_SEQUENCE_REJECTED", "CONCURRENT_CODE_SINGLE_ACCEPT", "SOURCE_FRESHNESS", "PASSWORD_REGRESSION", "DISABLE_PRESERVES_CONFIGURED_SESSIONS"],
            "TOTP_OPERATION_PROOF_CAUSAL_REVOKE_V1" => ["NATIVE_OPERATION_PROOF", "OWNER_FAILURE_SAME_PROOF", "SOURCE_FRESHNESS", "EXPLICIT_CAUSAL_SESSION_EFFECT", "UNRELATED_FACTOR_UNCHANGED", "DISABLE_REJECTS_OLD_SOURCE", "PASSWORD_REGRESSION"],
            "TOTP_PASSKEY_MFA_RESET_V1" => ["NATIVE_PASSKEY_MFA", "NO_EARLY_SESSION", "SOURCE_FRESHNESS", "RESET_RETIRE_THEN_SEPARATE_ENROLLMENT", "UNRELATED_FACTOR_UNCHANGED", "PASSKEY_REGRESSION", "PASSWORD_REGRESSION"],
            _ => []
        };
        return [.. common, .. extra];
    }
    internal static void RequireKeys(JsonElement value, params string[] keys)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("FINITE_PROTOCOL_INVALID");
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != keys.Length || actual.Distinct(StringComparer.Ordinal).Count() != keys.Length || keys.Except(actual, StringComparer.Ordinal).Any()) throw new InvalidOperationException("FINITE_PROTOCOL_INVALID");
    }
    internal static async Task<JsonElement> ReadFrameAsync(Stream input, CancellationToken cancellationToken)
    {
        var header = new byte[4]; await input.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length is 0 or > 32768) throw new InvalidOperationException("FINITE_PROTOCOL_INVALID");
        var bytes = new byte[(int)length]; await input.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }
    internal static async Task WriteFrameAsync<T>(Stream output, T value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length is 0 or > 32768) throw new InvalidOperationException("FINITE_PROTOCOL_INVALID");
        var header = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(header, (uint)bytes.Length);
        await output.WriteAsync(header, cancellationToken).ConfigureAwait(false); await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false); await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
    internal static string FindNode()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var candidate = Path.GetFullPath(Path.Combine(directory, OperatingSystem.IsWindows() ? "node.exe" : "node"));
            if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException("FINITE_NODE_UNAVAILABLE");
    }
    internal static string Git(DirectoryInfo root, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root.FullName, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("FINITE_SOURCE_INVALID");
        var output = process.StandardOutput.ReadToEnd(); _ = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("FINITE_SOURCE_INVALID");
        return output.Trim();
    }
    private sealed class FiniteResourceInterruption : Exception { }
}
