using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Tunner.Observability;
using Tunner.Testing.Recovery;

namespace Tunner.Identity.Persistence.Tests;

public sealed record RecoveryCodeFixtureCheckReceipt(string Action, string OwnerId, bool Verified, bool Absent, bool CleanupCompleted, string? BorrowedContainerId = null);
public sealed record RecoveryCodeProtocolCanaryReceipt(string Scenario, bool Observed, bool ConfigurationCreated, bool ConfigurationAbsent, bool ProbeSucceeded);
public sealed record RecoveryCodeNativeProtocolResult(RecoveryCodeNativeReceipt? Native, RecoveryCodeFixtureCheckReceipt? Fixture, RecoveryCodeProtocolCanaryReceipt? Canary);

/// <summary>Only fixed fixture/control metadata crosses this public test-assembly entry. No domain object or value is exported.</summary>
public static class RecoveryCodeFiniteNativeProtocol
{
    private static readonly Action<ILogger, Exception?> PublicLog = LoggerMessage.Define(LogLevel.Information, new EventId(9009), "PUBLIC_NONFUNCTIONAL_RECOVERY_TELEMETRY");
    public static bool Accept(RecoveryCodeNativeProtocolResult result) =>
        result.Native is { } native && result.Fixture is null && result.Canary is null ? RecoveryCodeFiniteNativeExecution.AcceptReceipt(native) :
        result.Fixture is { } fixture && result.Native is null && result.Canary is null ? fixture.Verified && (fixture.Action != "REMOVE_ACKNOWLEDGED" || fixture.Absent && fixture.CleanupCompleted) :
        result.Canary is { } canary && result.Native is null && result.Fixture is null && canary.Scenario == "HAPPY" && canary.Observed && canary.ConfigurationAbsent;

    public static async Task<RecoveryCodeNativeProtocolResult> RunAsync(DirectoryInfo root, Stream input, Stream output, bool fixtureCheck)
    {
        using var readDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var control = await RecoveryCodeFixtureOwnershipChannel.ReadAsync(input, readDeadline.Token).ConfigureAwait(false);
        var frame = control.RootElement;
        if (fixtureCheck) return new(null, await CheckFixtureAsync(root, frame).ConfigureAwait(false), null);
        RecoveryCodeFixtureOwnershipChannel.Keys(frame, "version", "type", "runId", "cohort", "publicFault", "localDockerEndpoint", "expectedEngineId", "sourceCommit", "buildBindingSha256", "publicProbeOrigin", "publicTelemetryOrigin");
        var binding = FiniteTotpBuildBinding.VerifyRecoveryNative(root);
        if (frame.GetProperty("version").GetInt32() != 2 || frame.GetProperty("type").GetString() != "nativeRun" ||
            frame.GetProperty("buildBindingSha256").GetString() != binding.ManifestSha256 || frame.GetProperty("sourceCommit").GetString() != FiniteTotpBuildBinding.CurrentCommit(root))
            throw new InvalidOperationException("RECOVERY_NATIVE_SOURCE_INVALID");
        RequireRegistry();
        var runId = frame.GetProperty("runId").GetString()!; var engine = frame.GetProperty("expectedEngineId").GetString()!;
        var endpoint = new Uri(frame.GetProperty("localDockerEndpoint").GetString()!, UriKind.Absolute);
        using var channel = new RecoveryCodeFixtureOwnershipChannel(input, output, runId, engine);
        var cohort = frame.GetProperty("cohort").GetString();
        if (cohort == "PUBLIC_PROTOCOL") return new(null, null, await PublicProtocolAsync(endpoint, channel, frame).ConfigureAwait(false));
        if (frame.GetProperty("publicFault").ValueKind != JsonValueKind.Null || frame.GetProperty("publicProbeOrigin").ValueKind != JsonValueKind.Null || frame.GetProperty("publicTelemetryOrigin").ValueKind != JsonValueKind.Null)
            throw new InvalidOperationException("RECOVERY_NATIVE_CONTROL_INVALID");
        var selected = cohort switch { "PUBLIC_CANARIES" => RecoveryCodeNativeCohort.PublicCanaries, "PUBLIC_SQL" => RecoveryCodeNativeCohort.PublicSql,
            "PRIVATE_RECOVERY" => RecoveryCodeNativeCohort.PrivateRecovery, "PUBLIC_CAPACITY" => RecoveryCodeNativeCohort.PublicCapacity, _ => throw new InvalidOperationException("RECOVERY_NATIVE_CONTROL_INVALID") };
        RecoveryCodeFixtureOwnershipChannel.RequireQualified(root, binding.ManifestSha256, selected is RecoveryCodeNativeCohort.PublicCanaries or RecoveryCodeNativeCohort.PublicCapacity ? "protocol" : "resources");
        var result = await RecoveryCodeFiniteNativeExecution.RunAsync(selected, endpoint, root, channel).ConfigureAwait(false);
        try { if (FiniteTotpBuildBinding.VerifyRecoveryNative(root).ManifestSha256 != binding.ManifestSha256) result = result with { FailureCode = "SOURCE_INVALID", SensitiveAccepted = false }; }
        catch (Exception error) when (error is not OutOfMemoryException) { result = result with { FailureCode = "SOURCE_INVALID", SensitiveAccepted = false }; }
        return new(result, null, null);
    }
    private static void RequireRegistry()
    {
        using var data = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "cases.json")));
        RecoveryCodeFixtureOwnershipChannel.Keys(data.RootElement, "PUBLIC_CANARIES", "PUBLIC_SQL", "PRIVATE_RECOVERY", "PUBLIC_PROTOCOL", "PUBLIC_CAPACITY");
        RecoveryCodeFiniteNativeExecution.RequireCaseRegistry(data.RootElement);
    }
    private static async Task<RecoveryCodeFixtureCheckReceipt> CheckFixtureAsync(DirectoryInfo root, JsonElement frame)
    {
        if (frame.GetProperty("type").GetString() == "borrowedCheck")
        {
            var run = string.Empty; var stage = "BORROWED_CONTROL";
            try
            {
                RecoveryCodeFixtureOwnershipChannel.Keys(frame, "version", "type", "runId", "localDockerEndpoint", "expectedEngineId", "expectedContainerId", "buildBindingSha256");
                run = frame.GetProperty("runId").GetString()!; var expected = frame.GetProperty("expectedContainerId").GetString();
                if (frame.GetProperty("version").GetInt32() != 2 || !RecoveryCodeFixtureOwnershipChannel.Hex(run, 32) || expected is not null && !RecoveryCodeFixtureOwnershipChannel.Hex(expected, 64))
                    throw new InvalidOperationException("RECOVERY_BORROWED_CHECK_INVALID");
                stage = "BORROWED_SOURCE_PIN";
                _ = FiniteTotpBuildBinding.VerifyPinnedRecoveryNativeOutputs(root, frame.GetProperty("buildBindingSha256").GetString()!);
                using var borrowedDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                stage = "BORROWED_EMPTY_CONFIG";
                using var borrowedClient = RecoveryCodeLocalDockerEndpoint.ReconciliationClient(new Uri(frame.GetProperty("localDockerEndpoint").GetString()!), run);
                stage = "BORROWED_ENGINE_QUERY";
                var info = await borrowedClient.System.GetSystemInfoAsync(borrowedDeadline.Token).ConfigureAwait(false);
                stage = "BORROWED_ENGINE_IDENTITY";
                if (info.ID != frame.GetProperty("expectedEngineId").GetString() || info.OSType != "linux") throw new InvalidOperationException("RECOVERY_DOCKER_ENGINE_CHANGED");
                stage = "BORROWED_METADATA";
                var repository = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(root.FullName.ToLowerInvariant())))[..10];
                var borrowed = await RecoveryCodeBorrowedPostgres.VerifyUsingClientAsync(borrowedClient, null, "tunner-browser-" + repository, borrowedDeadline.Token).ConfigureAwait(false);
                stage = "BORROWED_CLIENT_DISPOSE";
                return new("VERIFY_BORROWED", run, expected is null || expected == borrowed.ContainerId, false, false, borrowed.ContainerId);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // This existing metadata action field carries only a closed stage on rejection. Never project the exception.
                return new(stage, RecoveryCodeFixtureOwnershipChannel.Hex(run, 32) ? run : string.Empty, false, false, false);
            }
        }
        if (frame.GetProperty("type").GetString() == "processCheck")
        {
            RecoveryCodeFixtureOwnershipChannel.Keys(frame, "version", "type", "runId", "processId", "processStartUtc", "buildBindingSha256", "registration", "mode", "selection", "browserBuildBindingSha256");
            _ = FiniteTotpBuildBinding.VerifyPinnedRecoveryNativeOutputs(root, frame.GetProperty("buildBindingSha256").GetString()!);
            var run = frame.GetProperty("runId").GetString()!; var pid = frame.GetProperty("processId").GetInt32();
            if (frame.GetProperty("version").GetInt32() != 2 || !RecoveryCodeFixtureOwnershipChannel.Hex(run, 32) || pid <= 0)
                throw new InvalidOperationException("RECOVERY_PROCESS_CHECK_INVALID");
            var registration = frame.GetProperty("registration").GetString()!;
            if (registration == "BROWSER")
                _ = FiniteTotpBuildBinding.VerifyPinnedRecoveryBrowserOutputs(root, frame.GetProperty("browserBuildBindingSha256").GetString()!);
            else if (registration != "NATIVE" || frame.GetProperty("browserBuildBindingSha256").ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException("RECOVERY_PROCESS_CHECK_INVALID");
            using var processDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var verified = await VerifyClosedProcessAsync(root, pid, frame.GetProperty("processStartUtc").GetString()!, registration,
                frame.GetProperty("mode").GetString(), frame.GetProperty("selection").GetString(), processDeadline.Token).ConfigureAwait(false);
            return new("VERIFY_PROCESS", run, verified, false, false);
        }
        RecoveryCodeFixtureOwnershipChannel.Keys(frame, "version", "type", "action", "runId", "owner", "containerId", "preparationVerified", "localDockerEndpoint", "expectedEngineId", "buildBindingSha256");
        if (frame.GetProperty("version").GetInt32() != 2 || frame.GetProperty("type").GetString() != "fixtureCheck") throw new InvalidOperationException("RECOVERY_FIXTURE_CHECK_INVALID");
        _ = FiniteTotpBuildBinding.VerifyPinnedRecoveryNativeOutputs(root, frame.GetProperty("buildBindingSha256").GetString()!);
        var action = frame.GetProperty("action").GetString() ?? throw new InvalidOperationException("RECOVERY_FIXTURE_CHECK_INVALID"); var owner = RecoveryCodeFixtureOwnershipChannel.ParseOwner(frame.GetProperty("owner"));
        var id = frame.GetProperty("containerId").GetString(); var acked = frame.GetProperty("preparationVerified").GetBoolean();
        if (owner.Role == "config" || action is not ("VERIFY_PREPARED" or "VERIFY_CREATED" or "VERIFY_STARTED" or "VERIFY_ABSENT" or "REMOVE_ACKNOWLEDGED") ||
            (action == "VERIFY_PREPARED" ? acked || id is not null : !acked || id is not null && !RecoveryCodeFixtureOwnershipChannel.Hex(id, 64)))
            throw new InvalidOperationException("RECOVERY_FIXTURE_CHECK_INVALID");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var runId = frame.GetProperty("runId").GetString()!;
        var client = RecoveryCodeLocalDockerEndpoint.ReconciliationClient(new Uri(frame.GetProperty("localDockerEndpoint").GetString()!), runId);
        try
        {
            var engine = await client.System.GetSystemInfoAsync(deadline.Token).ConfigureAwait(false);
            if (engine.ID != frame.GetProperty("expectedEngineId").GetString() || engine.OSType != "linux") throw new InvalidOperationException("RECOVERY_DOCKER_ENGINE_CHANGED");
            var repository = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(root.FullName.ToLowerInvariant())))[..10];
            var resource = new RecoveryCodeReconciliationContainer(client, repository, runId, owner, id);
            if (action == "VERIFY_PREPARED") return new(action, owner.OwnerId, await resource.VerifyPreparedIdentityAsync(deadline.Token).ConfigureAwait(false), false, false);
            if (action == "VERIFY_CREATED") return new(action, owner.OwnerId, await resource.VerifyCreatedIdentityAsync(deadline.Token).ConfigureAwait(false), false, false);
            if (action == "VERIFY_STARTED") return new(action, owner.OwnerId, await resource.VerifyCreatedIdentityAsync(deadline.Token, true).ConfigureAwait(false), false, false);
            if (action == "VERIFY_ABSENT") { var absent = await resource.VerifyAbsenceAsync(deadline.Token).ConfigureAwait(false); return new(action, owner.OwnerId, absent, absent, false); }
            // This sole branch may remove an ACKed exact owner. Read-only branches never Dispose the restored owner.
            try { await resource.DisposeAsync().ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { }
            return new(action!, owner.OwnerId, resource.ContainerAbsentVerified && resource.AnonymousVolumesAbsentVerified,
                resource.ContainerAbsentVerified, resource.CleanupVerified);
        }
        finally { client.Dispose(); }
    }
    private static async Task<RecoveryCodeProtocolCanaryReceipt> PublicProtocolAsync(Uri uri, RecoveryCodeFixtureOwnershipChannel channel, JsonElement control)
    {
        var scenario = control.GetProperty("publicFault").GetString();
        if (scenario is not ("HAPPY" or "ACK_OWNER" or "ACK_SEQUENCE" or "ACK_STAGE" or "ACK_TIMEOUT" or "MANAGED_OUTPUT" or "RAW_STDERR" or "TIMEOUT" or "PROXY" or "TELEMETRY"))
            throw new InvalidOperationException("RECOVERY_PUBLIC_SCENARIO_INVALID");
        var endpoint = RecoveryCodeLocalDockerEndpoint.Admit(uri, channel); var observed = false; var probe = false;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var client = await endpoint.ClientAsync(deadline.Token).ConfigureAwait(false); // Client construction only, no Docker API/resource.
            if (scenario == "TIMEOUT") { await channel.ArmPublicTimeoutAsync(deadline.Token).ConfigureAwait(false); await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token).ConfigureAwait(false); }
            if (scenario == "MANAGED_OUTPUT") Console.Error.Write("PUBLIC_NONFUNCTIONAL_RECOVERY_NATIVE_OUTPUT");
            if (scenario == "RAW_STDERR") await Console.OpenStandardError().WriteAsync(Encoding.ASCII.GetBytes("PUBLIC_NONFUNCTIONAL_RECOVERY_NATIVE_OUTPUT"), deadline.Token).ConfigureAwait(false);
            if (scenario == "PROXY")
            {
                var origin = ProbeOrigin(control.GetProperty("publicProbeOrigin").GetString());
                using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(3) };
                using var response = await http.GetAsync(new Uri(origin, "/recovery-public-probe"), HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                probe = response.StatusCode == HttpStatusCode.OK;
            }
            if (scenario == "TELEMETRY")
            {
                var origin = ProbeOrigin(control.GetProperty("publicTelemetryOrigin").GetString());
                Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", origin.AbsoluteUri);
                await using var services = new ServiceCollection().AddTunnerObservability().BuildServiceProvider();
                _ = services.GetRequiredService<TracerProvider>();
                using var source = new ActivitySource(TunnerTelemetry.ActivitySourceName); using var activity = source.StartActivity("PUBLIC_NONFUNCTIONAL_RECOVERY");
                using var meter = new Meter(TunnerTelemetry.MeterName); meter.CreateCounter<long>("public_recovery_canary").Add(1);
                PublicLog(services.GetRequiredService<ILoggerFactory>().CreateLogger("PublicRecoveryCanary"), null);
                probe = activity is null && Environment.GetEnvironmentVariable("OTEL_SDK_DISABLED") == "true";
            }
            observed = scenario is "HAPPY" or "MANAGED_OUTPUT" or "RAW_STDERR" || (scenario is "PROXY" or "TELEMETRY") && probe;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { observed = scenario == channel.LastFailure; }
        finally { try { endpoint.Dispose(); } catch (Exception error) when (error is not OutOfMemoryException) { } }
        return new(scenario!, observed, endpoint.ConfigurationCreated, endpoint.CleanupVerified, probe);
    }
    private static Uri ProbeOrigin(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) || origin.Scheme != "http" || origin.Host != "127.0.0.1" || origin.Port is < 1 or > 65535 ||
            origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0) throw new InvalidOperationException("RECOVERY_PUBLIC_PROBE_INVALID");
        return origin;
    }

    // Integrate only into the fixed native metadata helper. Caller verifies pinned native
    // outputs (and pinned browser outputs for BROWSER) before invoking this method.
    // The caller exports only the returned Boolean. There is no executable/path input.
    private static readonly string[] ClosedProcessBrowserJourneys = ["RECOVERY_GENERATE_PASSWORD_V1", "RECOVERY_REPLACE_PASSWORD_V1", "RECOVERY_GENERATE_PASSKEY_V1", "RECOVERY_REPLACE_PASSKEY_V1", "RECOVERY_GENERATE_TOTP_V1", "RECOVERY_REPLACE_TOTP_V1"];
    private static readonly string[] ClosedProcessBrowserResources = ["after-db", "after-mailpit", "after-vault-session", "after-app-start", "cleanup-reject"];
    private static readonly string[] ClosedProcessBrowserPublic = ["happy", "unarmed", "duplicate", "wrong-page", "wrong-bytes", "oversize", "outside-identity", "stream-error", "delete-error", "unexpected-output", "invalid-result", "action-timeout", "child-timeout", "unexpected-artifact", "fatal-rejection", "journey-entry", "missing-predicate", "cross-component", "cleanup", "oracle-roundtrip", "oracle-input", "oracle-exit", "oracle-timeout", "oracle-stderr", "production-controller", "relay-roundtrip", "relay-malformed", "relay-timeout", "popup-extra", "iframe-extra", "simultaneous-extra", "cancel-initiation", "stalled-partial-read", "cancel-stream-read", "stalled-delete", "cancel-delete", "secondary-context-extra"];
    private static readonly string[] ClosedProcessShellFlags = ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command"];
    private static readonly string[] ClosedProcessShellEnvironment = ["SystemRoot", "SystemDrive", "ProgramData", "WINDIR", "COMSPEC", "PATHEXT", "LOCALAPPDATA", "APPDATA", "USERPROFILE", "TEMP", "TMP"];

    private static string[] ClosedProcessArguments(DirectoryInfo root, string registration, string? mode, string? selection)
    {
        if (registration == "NATIVE" && mode is null && selection is null)
            return ["dotnet", Path.GetFullPath(Path.Combine(root.FullName, "tools", "recovery-native-testing", "bin", "Debug", "net10.0", "Tunner.Recovery.NativeTesting.dll"))];
        var admitted = registration == "BROWSER" && (mode switch
        {
            "--finite-recovery-browser-testing" => ClosedProcessBrowserJourneys.Contains(selection, StringComparer.Ordinal),
            "--finite-recovery-resource-canary" => ClosedProcessBrowserResources.Contains(selection, StringComparer.Ordinal),
            "--finite-recovery-public-fault" => ClosedProcessBrowserPublic.Contains(selection, StringComparer.Ordinal),
            _ => false
        });
        if (!admitted) throw new InvalidOperationException("RECOVERY_PROCESS_CHECK_INVALID");
        return ["dotnet", Path.GetFullPath(Path.Combine(root.FullName, "tools", "browser-testing", "bin", "Debug", "net10.0", "Tunner.BrowserTesting.dll")), mode!, selection!];
    }

    private static async Task<bool> VerifyClosedProcessAsync(DirectoryInfo root, int pid, string startUtc, string registration, string? mode, string? selection, CancellationToken ct)
    {
        try
        {
            if (pid <= 0 || !System.Text.RegularExpressions.Regex.IsMatch(startUtc, "^\\d{4}-\\d\\d-\\d\\dT\\d\\d:\\d\\d:\\d\\d\\.\\d{7}Z$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)) return false;
            var arguments = ClosedProcessArguments(root, registration, mode, selection);
            if (arguments.Any(value => value.Length > 2048 || value.Any(char.IsControl) || value.Contains('"'))) return false;
            var ownExecutable = Environment.ProcessPath;
            if (ownExecutable is null || !Path.GetFileNameWithoutExtension(ownExecutable).Equals("dotnet", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return false;
            using var process = Process.GetProcessById(pid);
            if (!ClosedProcessStartMatches(process, startUtc)) return false;
            bool identity;
            if (OperatingSystem.IsWindows())
            {
                var metadata = await ReadClosedWindowsProcessAsync(pid, ct).ConfigureAwait(false);
                identity = metadata.Closed && metadata.ProcessId == pid && metadata.Executable is not null && metadata.CommandLine is not null &&
                    ClosedProcessPathEquals(metadata.Executable, ownExecutable) && ClosedWindowsCommandLine(metadata.CommandLine, arguments);
            }
            else if (OperatingSystem.IsLinux())
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(3));
                var folder = "/proc/" + pid.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await using var input = new FileStream(folder + "/cmdline", FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024, FileOptions.Asynchronous);
                var bytes = await ReadClosedProcessBytesAsync(input, deadline.Token).ConfigureAwait(false);
                try
                {
                    var raw = new UTF8Encoding(false, true).GetString(bytes);
                    var actual = raw.Split('\0');
                    var executable = new FileInfo(folder + "/exe").ResolveLinkTarget(true)?.FullName;
                    identity = raw.EndsWith('\0') && actual.Length == arguments.Length + 1 && actual[^1].Length == 0 &&
                        actual.Take(arguments.Length).SequenceEqual(arguments, StringComparer.Ordinal) && executable is not null && ClosedProcessPathEquals(executable, ownExecutable);
                }
                finally { Array.Clear(bytes); }
            }
            else return false;
            using var current = Process.GetProcessById(pid);
            return identity && !process.HasExited && ClosedProcessStartMatches(current, startUtc);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return false; }
    }

    private static bool ClosedProcessStartMatches(Process process, string startUtc) => !process.HasExited &&
        process.StartTime.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture) == startUtc;

    private static bool ClosedProcessPathEquals(string first, string second)
    {
        static string Canonical(string path)
        {
            var file = new FileInfo(Path.GetFullPath(path));
            return file.ResolveLinkTarget(true)?.FullName ?? file.FullName;
        }
        return string.Equals(Canonical(first), Canonical(second), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    // Exact serialization of this closed Node spawn registration, not a general shell/argv parser.
    // Node argv0 is literal dotnet; the fixed DLL contains no quote/control and ends .dll.
    // libuv's ordinary quoting therefore only wraps a DLL path containing whitespace.
    private static bool ClosedWindowsCommandLine(string actual, string[] arguments)
    {
        var dll = arguments[1];
        if (!dll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || dll.Contains('"') || dll.Any(char.IsControl)) return false;
        var prefix = "dotnet " + (dll.Contains(' ') ? "\"" + dll + "\"" : dll);
        var suffix = arguments.Length == 2 ? string.Empty : " " + arguments[2] + " " + arguments[3];
        return actual.Length == prefix.Length + suffix.Length && actual.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && actual.AsSpan(prefix.Length).SequenceEqual(suffix.AsSpan());
    }

    private static async Task<(bool Closed, int ProcessId, string? Executable, string? CommandLine)> ReadClosedWindowsProcessAsync(int pid, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var flag in ClosedProcessShellFlags) start.ArgumentList.Add(flag);
        // Only this validated positive integer is interpolated. Never query all processes.
        start.ArgumentList.Add("[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); CimCmdlets\\Get-CimInstance -ClassName Win32_Process -Filter 'ProcessId = " + pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + "' -ErrorAction Stop | Microsoft.PowerShell.Utility\\Select-Object ProcessId,ExecutablePath,CommandLine | Microsoft.PowerShell.Utility\\ConvertTo-Json -Compress -Depth 2");
        start.Environment.Clear();
        foreach (var name in ClosedProcessShellEnvironment)
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        start.Environment["PSModulePath"] = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "Modules");
        using var probe = Process.Start(start) ?? throw new InvalidOperationException("RECOVERY_PROCESS_CHECK_INVALID");
        probe.StandardInput.Close();
        var output = ReadClosedProcessBytesAsync(probe.StandardOutput.BaseStream, deadline.Token);
        var errors = CountClosedProcessErrorsAsync(probe.StandardError.BaseStream, deadline.Token);
        byte[]? bytes = null; var closed = false;
        try
        {
            await probe.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            bytes = await output.ConfigureAwait(false); var errorCount = await errors.ConfigureAwait(false);
            closed = probe.HasExited;
            if (!closed || probe.ExitCode != 0 || errorCount != 0) return (closed, 0, null, null);
            using var data = JsonDocument.Parse(bytes); var value = data.RootElement;
            RecoveryCodeFixtureOwnershipChannel.Keys(value, "ProcessId", "ExecutablePath", "CommandLine");
            return (true, value.GetProperty("ProcessId").GetInt32(), value.GetProperty("ExecutablePath").GetString(), value.GetProperty("CommandLine").GetString());
        }
        finally
        {
            deadline.Cancel();
            if (!closed)
            {
                try { if (!probe.HasExited) probe.Kill(entireProcessTree: true); } catch (Exception error) when (error is not OutOfMemoryException) { }
                try { await probe.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { }
            }
            try { await Task.WhenAll(output, errors).WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { }
            if (bytes is not null) Array.Clear(bytes);
        }
    }

    private static async Task<byte[]> ReadClosedProcessBytesAsync(Stream input, CancellationToken ct)
    {
        using var output = new MemoryStream(); var buffer = new byte[1024];
        try
        {
            for (;;) { var count = await input.ReadAsync(buffer, ct).ConfigureAwait(false); if (count == 0) break; if (output.Length + count > 8192) throw new InvalidOperationException("RECOVERY_PROCESS_CHECK_INVALID"); output.Write(buffer, 0, count); }
            return output.ToArray();
        }
        finally { Array.Clear(buffer); }
    }

    private static async Task<int> CountClosedProcessErrorsAsync(Stream input, CancellationToken ct)
    {
        var buffer = new byte[1024]; var total = 0;
        try
        {
            for (;;) { var count = await input.ReadAsync(buffer, ct).ConfigureAwait(false); if (count == 0) return total; total += count; if (total > 8192) throw new InvalidOperationException("RECOVERY_PROCESS_CHECK_INVALID"); }
        }
        finally { Array.Clear(buffer); }
    }
}
