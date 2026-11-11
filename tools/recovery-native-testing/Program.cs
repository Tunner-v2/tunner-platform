using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using Tunner.Governance;
using Tunner.Identity.Persistence.Tests;

// Fixed test-only process. This is never an application API or an xUnit/TRX runner.
var receiptStream = Console.OpenStandardOutput();
var discarded = new CountingDiscardWriter(); Console.SetOut(discarded); Console.SetError(discarded);
Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");
HttpClient.DefaultProxy = new WebProxy();
foreach (var name in new[] { "DOCKER_AUTH_CONFIG", "DOCKER_CONTEXT", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH", "REGISTRY_AUTH_FILE",
    "PGPASSWORD", "PGPASSFILE", "PGSERVICE", "PGSERVICEFILE", "OTEL_EXPORTER_OTLP_HEADERS", "OTEL_EXPORTER_OTLP_TRACES_HEADERS",
    "OTEL_EXPORTER_OTLP_METRICS_HEADERS", "OTEL_EXPORTER_OTLP_LOGS_HEADERS" }) Environment.SetEnvironmentVariable(name, null);
RecoveryCodeNativeReceipt? receipt = null;
try
{
    if (args.Length != 0) throw new InvalidOperationException("RECOVERY_NATIVE_INPUT_INVALID");
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var input = Console.OpenStandardInput(); var header = new byte[4];
    await input.ReadExactlyAsync(header, deadline.Token); var length = BinaryPrimitives.ReadInt32BigEndian(header);
    if (length is < 2 or > 1024) throw new InvalidOperationException("RECOVERY_NATIVE_INPUT_INVALID");
    var bytes = new byte[length]; await input.ReadExactlyAsync(bytes, deadline.Token);
    var extra = new byte[1]; if (await input.ReadAsync(extra, deadline.Token) != 0) throw new InvalidOperationException("RECOVERY_NATIVE_INPUT_INVALID");
    var text = new UTF8Encoding(false, true).GetString(bytes); if (text.StartsWith('\uFEFF')) throw new InvalidOperationException("RECOVERY_NATIVE_INPUT_INVALID");
    using var frame = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 3 }); var control = frame.RootElement;
    var names = control.EnumerateObject().Select(property => property.Name).ToArray();
    if (names.Length != 4 || names.Distinct(StringComparer.Ordinal).Count() != 4 || !names.ToHashSet(StringComparer.Ordinal).SetEquals(["version", "type", "cohort", "localDockerEndpoint"]) ||
        control.GetProperty("version").GetInt32() != 1 || control.GetProperty("type").GetString() != "run") throw new InvalidOperationException("RECOVERY_NATIVE_INPUT_INVALID");
    var cohort = control.GetProperty("cohort").GetString() switch
    {
        "PUBLIC_CANARIES" => RecoveryCodeNativeCohort.PublicCanaries,
        "PUBLIC_SQL" => RecoveryCodeNativeCohort.PublicSql,
        "PRIVATE_RECOVERY" => RecoveryCodeNativeCohort.PrivateRecovery,
        _ => throw new InvalidOperationException("RECOVERY_NATIVE_INPUT_INVALID")
    };
    var endpoint = new Uri(control.GetProperty("localDockerEndpoint").GetString()!, UriKind.Absolute);
    // Qualified wrapper sets CWD to the verified checkout; explicit resolver never consults env/Git.
    var root = RepositoryRootResolver.Resolve(new DirectoryInfo(Directory.GetCurrentDirectory()));
    receipt = await RecoveryCodeFiniteNativeExecution.RunAsync(cohort, endpoint, root);
}
catch (Exception error) when (error is not OutOfMemoryException) { }
var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var unexpectedManagedOutput = discarded.Count != 0;
var accepted = receipt is not null && !unexpectedManagedOutput && RecoveryCodeFiniteNativeExecution.AcceptReceipt(receipt);
var output = receipt is null
    ? JsonSerializer.SerializeToUtf8Bytes(new { version = 1, type = "nativeAdmissionFailure", failureCode = "INPUT_OR_BOUNDARY_FAILED", accepted = false, unexpectedManagedOutput }, jsonOptions)
    : JsonSerializer.SerializeToUtf8Bytes(new { version = 1, type = "nativeExecution", receipt, unexpectedManagedOutput, accepted }, jsonOptions);
var outputHeader = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(outputHeader, output.Length);
using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
{
    await receiptStream.WriteAsync(outputHeader, deadline.Token); await receiptStream.WriteAsync(output, deadline.Token); await receiptStream.FlushAsync(deadline.Token);
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
