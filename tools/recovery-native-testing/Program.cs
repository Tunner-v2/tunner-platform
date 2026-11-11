using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Tunner.Governance;
using Tunner.Identity.Persistence.Tests;

var output = Console.OpenStandardOutput();
var discarded = new CountingDiscardWriter(); Console.SetOut(discarded); Console.SetError(discarded);
Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");
HttpClient.DefaultProxy = new WebProxy();
foreach (var key in new[] { "DOCKER_AUTH_CONFIG", "DOCKER_CONTEXT", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH", "REGISTRY_AUTH_FILE",
    "PGPASSWORD", "PGPASSFILE", "PGSERVICE", "PGSERVICEFILE", "OTEL_EXPORTER_OTLP_HEADERS", "OTEL_EXPORTER_OTLP_TRACES_HEADERS",
    "OTEL_EXPORTER_OTLP_METRICS_HEADERS", "OTEL_EXPORTER_OTLP_LOGS_HEADERS" }) Environment.SetEnvironmentVariable(key, null);
RecoveryCodeNativeProtocolResult? result = null;
using (var process = Process.GetCurrentProcess())
{
    var ready = JsonSerializer.SerializeToUtf8Bytes(new { version = 2, type = "processReady", processId = Environment.ProcessId,
        processStartUtc = process.StartTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) });
    var readyHeader = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(readyHeader, ready.Length);
    using var readyDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    await output.WriteAsync(readyHeader, readyDeadline.Token); await output.WriteAsync(ready, readyDeadline.Token); await output.FlushAsync(readyDeadline.Token);
}
try
{
    var fixtureCheck = args.Length == 1 && args[0] == "--recovery-fixture-check";
    if (args.Length != 0 && !fixtureCheck) throw new InvalidOperationException("RECOVERY_NATIVE_INPUT_INVALID");
    var root = RepositoryRootResolver.Resolve(new DirectoryInfo(Directory.GetCurrentDirectory()));
    // Mandatory v2 run + ownership ACK transport. The legacy one-frame resource entry no longer exists.
    result = await RecoveryCodeFiniteNativeProtocol.RunAsync(root, Console.OpenStandardInput(), output, fixtureCheck);
}
catch (Exception error) when (error is not OutOfMemoryException) { }
var unexpectedManagedOutput = discarded.Count != 0;
var accepted = result is not null && !unexpectedManagedOutput && RecoveryCodeFiniteNativeProtocol.Accept(result);
var bytes = JsonSerializer.SerializeToUtf8Bytes(new { version = 2, type = "recoveryNativeTerminal", result,
    unexpectedManagedOutput, accepted }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
if (bytes.Length > 8192) return 1;
var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
{
    await output.WriteAsync(header, deadline.Token); await output.WriteAsync(bytes, deadline.Token); await output.FlushAsync(deadline.Token);
}
return accepted ? 0 : 1;

sealed class CountingDiscardWriter : TextWriter
{
    private long count;
    public override Encoding Encoding => Encoding.UTF8;
    internal long Count => Interlocked.Read(ref count);
    public override void Write(char value) => Interlocked.Increment(ref count);
    public override void Write(string? value) { if (value is not null) Interlocked.Add(ref count, value.Length); }
    public override void Write(char[] buffer, int index, int length) => Interlocked.Add(ref count, length);
    public override void Write(ReadOnlySpan<char> buffer) => Interlocked.Add(ref count, buffer.Length);
}
