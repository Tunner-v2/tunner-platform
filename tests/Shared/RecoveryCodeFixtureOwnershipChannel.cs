using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Tunner.Testing.Recovery;

internal sealed record RecoveryCodePreparedFixtureOwner(string OwnerId, string Role, string Name, string? ImageId)
{
    public override string ToString() => nameof(RecoveryCodePreparedFixtureOwner);
}

/// <summary>One closed metadata-only private stream. There is no callback, no no-op implementation and no resource fallback.</summary>
internal sealed class RecoveryCodeFixtureOwnershipChannel : IDisposable
{
    private static readonly string[] ProtocolQualificationKeys = ["SOURCE_BOUND", "FRAMES_STRICT", "ACK_REQUIRED", "OUTPUT_CONTAINED", "PROCESS_CLOSED", "SURFACES_REMOVED", "PROXY_DIRECT", "TELEMETRY_DISABLED"];
    private static readonly string[] ResourceQualificationKeys = ["SOURCE_BOUND", "OWNERS_RETAINED", "CREATED_BEFORE_START", "FORCED_CLOSE_VERIFIED", "RECONCILED_EXACT", "CONTAINERS_ABSENT", "VOLUMES_ABSENT", "CONFIGURATIONS_ABSENT", "BORROWED_PRESERVED", "CAPACITY_VERIFIED"];
    internal static void RequireQualified(DirectoryInfo root, string hash, string kind)
    {
        if (kind is not ("protocol" or "resources") || !Hex(hash, 64)) throw new InvalidOperationException("RECOVERY_NATIVE_QUALIFICATION_REQUIRED");
        var name = kind == "protocol" ? "native-protocol-qualification.json" : "native-resource-qualification.json";
        var path = FiniteTotpBuildBinding.ResolveEvidence(root, "artifacts/p2-009-recovery-20261111/" + name);
        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > 65536)
            throw new InvalidOperationException("RECOVERY_NATIVE_QUALIFICATION_REQUIRED");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path)); var value = document.RootElement;
        RecoveryCodeFixtureOwnershipChannel.Keys(value, "version", "kind", "buildBindingSha256", "passed", "checks");
        if (value.GetProperty("version").GetInt32() != 2 || value.GetProperty("kind").GetString() != kind || value.GetProperty("buildBindingSha256").GetString() != hash ||
            !value.GetProperty("passed").GetBoolean()) throw new InvalidOperationException("RECOVERY_NATIVE_QUALIFICATION_REQUIRED");
        var checks = value.GetProperty("checks");
        var expected = kind == "protocol" ? ProtocolQualificationKeys : ResourceQualificationKeys;
        RecoveryCodeFixtureOwnershipChannel.Keys(checks, expected);
        if (expected.Any(key => !checks.GetProperty(key).GetBoolean())) throw new InvalidOperationException("RECOVERY_NATIVE_QUALIFICATION_REQUIRED");
    }
    private static readonly JsonSerializerOptions WireJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly Stream input;
    private readonly Stream output;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, RecoveryCodePreparedFixtureOwner> owners = new(StringComparer.Ordinal);
    private readonly HashSet<string> created = new(StringComparer.Ordinal);
    private int sequence;
    private bool failed;
    private bool configurationInitialized;
    internal async Task PublicStageAsync(string ownerId, string canaryId, CancellationToken ct)
    {
        if (canaryId is not ("AFTER_CREATE" or "AFTER_START" or "AFTER_MIGRATION" or "CASE_FAILURE" or "CLEANUP_REJECT"))
            throw new InvalidOperationException("RECOVERY_PUBLIC_CANARY_INVALID");
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (failed || !owners.TryGetValue(ownerId, out var owner) || owner.Role == "config") throw new InvalidOperationException("RECOVERY_OWNERSHIP_CONTROL_INVALID");
            await ExchangeAsync(owner, "CANARY", "publicStage", canaryId, ct).ConfigureAwait(false);
        }
        catch { failed = true; throw; }
        finally { gate.Release(); }
    }
    internal string RunId { get; }
    internal string ExpectedEngineId { get; }
    internal string LastFailure { get; private set; } = "NONE";
    internal Task ArmPublicTimeoutAsync(CancellationToken ct) => WriteAsync(output, new { version = 2, type = "publicArmed", runId = RunId, scenario = "TIMEOUT" }, ct);
    internal Task ReportCapacityAsync(int databases, long databaseBytes, long walBytes, CancellationToken ct) => WriteAsync(output,
        new { version = 2, type = "capacityMeasured", runId = RunId, databases, databaseBytes, walBytes }, ct);
    internal RecoveryCodeFixtureOwnershipChannel(Stream input, Stream output, string runId, string expectedEngineId)
    {
        if (!Hex(runId, 32) || string.IsNullOrWhiteSpace(expectedEngineId) || expectedEngineId.Length > 200 || expectedEngineId.Any(char.IsControl))
            throw new InvalidOperationException("RECOVERY_OWNERSHIP_CONTROL_INVALID");
        this.input = input; this.output = output; RunId = runId; ExpectedEngineId = expectedEngineId;
    }
    internal async Task PrepareAsync(RecoveryCodePreparedFixtureOwner owner, CancellationToken ct)
    {
        Validate(owner);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (failed || owners.Count >= 8 || owners.ContainsKey(owner.OwnerId) || owners.Values.Any(x => x.Name == owner.Name) ||
                owner.Role == "config" && owners.Values.Any(x => x.Role == "config"))
                throw new InvalidOperationException("RECOVERY_OWNERSHIP_CONTROL_INVALID");
            await ExchangeAsync(owner, "PREPARED", "ownerPrepared", null, ct).ConfigureAwait(false);
            owners.Add(owner.OwnerId, owner);
        }
        catch { failed = true; throw; }
        finally { gate.Release(); }
    }
    internal async Task CreatedAsync(RecoveryCodePreparedFixtureOwner owner, string containerId, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (failed || owner.Role == "config" || !owners.TryGetValue(owner.OwnerId, out var prior) || prior != owner ||
                !Hex(containerId, 64) || created.Count >= 7 || !created.Add(owner.OwnerId))
                throw new InvalidOperationException("RECOVERY_OWNERSHIP_CONTROL_INVALID");
            await ExchangeAsync(owner, "CREATED", "ownerCreated", containerId, ct).ConfigureAwait(false);
        }
        catch { failed = true; throw; }
        finally { gate.Release(); }
    }
    internal async Task InitializedConfigurationAsync(RecoveryCodePreparedFixtureOwner owner, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (failed || configurationInitialized || owner.Role != "config" || !owners.TryGetValue(owner.OwnerId, out var prior) || prior != owner)
                throw new InvalidOperationException("RECOVERY_OWNERSHIP_CONTROL_INVALID");
            await ExchangeAsync(owner, "INITIALIZED", "ownerInitialized", null, ct).ConfigureAwait(false);
            configurationInitialized = true;
        }
        catch { failed = true; throw; }
        finally { gate.Release(); }
    }
    private async Task ExchangeAsync(RecoveryCodePreparedFixtureOwner owner, string stage, string type, string? containerId, CancellationToken ct)
    {
        if (++sequence > 24) throw new InvalidOperationException("RECOVERY_OWNERSHIP_CONTROL_INVALID");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(3));
        if (type == "ownerPrepared")
            await WriteAsync(output, new { version = 2, type, sequence, runId = RunId, ownerId = owner.OwnerId, role = owner.Role, name = owner.Name, imageId = owner.ImageId }, deadline.Token).ConfigureAwait(false);
        else if (type == "ownerCreated")
            await WriteAsync(output, new { version = 2, type, sequence, runId = RunId, ownerId = owner.OwnerId, containerId }, deadline.Token).ConfigureAwait(false);
        else if (type == "publicStage")
            await WriteAsync(output, new { version = 2, type, sequence, runId = RunId, ownerId = owner.OwnerId, canaryId = containerId }, deadline.Token).ConfigureAwait(false);
        else await WriteAsync(output, new { version = 2, type, sequence, runId = RunId, ownerId = owner.OwnerId }, deadline.Token).ConfigureAwait(false);
        JsonDocument ack;
        try { ack = await ReadAsync(input, deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { LastFailure = "ACK_TIMEOUT"; throw; }
        catch { LastFailure = "ACK_INVALID"; throw; }
        using var ownedAck = ack; var value = ack.RootElement;
        LastFailure = "ACK_INVALID";
        Keys(value, "version", "type", "sequence", "runId", "ownerId", "stage", "status");
        LastFailure = value.GetProperty("version").GetInt32() != 2 || value.GetProperty("type").GetString() != "ownerAck" || value.GetProperty("runId").GetString() != RunId ? "ACK_INVALID" :
            value.GetProperty("sequence").GetInt32() != sequence ? "ACK_SEQUENCE" : value.GetProperty("ownerId").GetString() != owner.OwnerId ? "ACK_OWNER" :
            value.GetProperty("stage").GetString() != stage ? "ACK_STAGE" : value.GetProperty("status").GetString() != "OK" ? "ACK_REJECTED" : "NONE";
        if (LastFailure != "NONE")
            throw new InvalidOperationException("RECOVERY_OWNERSHIP_ACK_REJECTED");
    }
    internal static void Validate(RecoveryCodePreparedFixtureOwner owner)
    {
        if (!Hex(owner.OwnerId, 32)) throw new InvalidOperationException("RECOVERY_OWNERSHIP_CONTROL_INVALID");
        var image = owner.Role switch
        {
            "config" => null,
            "native-postgres" => RecoveryCodeOwnedDockerFixture.NativePostgresImage,
            "mailpit" => RecoveryCodeOwnedDockerFixture.MailpitImage,
            "openbao" => RecoveryCodeOwnedDockerFixture.OpenBaoImage,
            _ => throw new InvalidOperationException("RECOVERY_OWNERSHIP_CONTROL_INVALID")
        };
        var name = owner.Role == "config" ? "tunner-p2-009-docker-" + owner.OwnerId : "tunner-p2-009-" + owner.Role + "-" + owner.OwnerId;
        if (owner.Name != name || owner.ImageId != image) throw new InvalidOperationException("RECOVERY_OWNERSHIP_CONTROL_INVALID");
    }
    internal static RecoveryCodePreparedFixtureOwner ParseOwner(JsonElement value)
    {
        Keys(value, "ownerId", "role", "name", "imageId");
        var owner = new RecoveryCodePreparedFixtureOwner(value.GetProperty("ownerId").GetString()!, value.GetProperty("role").GetString()!,
            value.GetProperty("name").GetString()!, value.GetProperty("imageId").GetString());
        Validate(owner); return owner;
    }
    internal static bool Hex(string? value, int length) => value is not null && value.Length == length && value.All(char.IsAsciiHexDigitLower);
    internal static void Keys(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("RECOVERY_PRIVATE_FRAME_INVALID");
        var actual = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (actual.Length != expected.Length || actual.Distinct(StringComparer.Ordinal).Count() != expected.Length || expected.Except(actual, StringComparer.Ordinal).Any())
            throw new InvalidOperationException("RECOVERY_PRIVATE_FRAME_INVALID");
    }
    internal static async Task<JsonDocument> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 2 or > 8192) throw new InvalidOperationException("RECOVERY_PRIVATE_FRAME_INVALID");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        var text = new UTF8Encoding(false, true).GetString(bytes);
        if (text.StartsWith('\uFEFF')) throw new InvalidOperationException("RECOVERY_PRIVATE_FRAME_INVALID");
        var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
        try { Unique(document.RootElement); return document; } catch { document.Dispose(); throw; }
    }
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) { if (!names.Add(property.Name)) throw new InvalidOperationException("RECOVERY_PRIVATE_FRAME_INVALID"); Unique(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() > 32) throw new InvalidOperationException("RECOVERY_PRIVATE_FRAME_INVALID");
            foreach (var item in value.EnumerateArray()) Unique(item);
        }
    }
    public void Dispose() => gate.Dispose();
    internal static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, WireJson);
        if (bytes.Length is < 2 or > 8192) throw new InvalidOperationException("RECOVERY_PRIVATE_FRAME_INVALID");
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false); await stream.WriteAsync(bytes, ct).ConfigureAwait(false); await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
