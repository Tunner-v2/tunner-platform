using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Tunner.Identity.Persistence;
using Tunner.Testing.Totp;

/// <summary>No host, database or vault services. Sensitive values use private redirected pipes only.</summary>
internal static class BrowserTestTotpOracle
{
    internal const string NativeVersion = "10.0.13";
    private const int MaximumFrame = 2048;
    private const int MaximumBytes = 131072;

    internal static async Task<int> RunAsync()
    {
        Console.SetError(TextWriter.Null);
        if (!Console.IsInputRedirected || !Console.IsOutputRedirected || Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development") return 2;
        await using var input = Console.OpenStandardInput();
        await using var output = Console.OpenStandardOutput();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var id = 0; var received = 0; var sent = 0;
        try
        {
            var actual = typeof(AuthenticatorTokenProvider<IdentityUserAdapter>).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (actual is null || !actual.StartsWith(NativeVersion + "+", StringComparison.Ordinal)) throw new OracleFailure("NATIVE_UNAVAILABLE");
            await WriteAsync(new { version = 1, type = "ready", nativeVersion = NativeVersion }).ConfigureAwait(false);
            while (true)
            {
                var header = new byte[4];
                await ReadExactAsync(header).ConfigureAwait(false);
                var length = BinaryPrimitives.ReadUInt32BigEndian(header);
                if (length is 0 or > MaximumFrame || received + length + 4 > MaximumBytes) throw new OracleFailure("FRAME_INVALID");
                var bytes = new byte[(int)length]; received += bytes.Length + 4;
                try
                {
                    await ReadExactAsync(bytes).ConfigureAwait(false);
                    using var document = JsonDocument.Parse(bytes);
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number != 1 ||
                        !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) throw new OracleFailure("INPUT_INVALID");
                    if (type.GetString() == "stop")
                    {
                        RequireKeys(root, "version", "type");
                        var trailing = new byte[1];
                        if (await input.ReadAsync(trailing, lifetime.Token).ConfigureAwait(false) != 0) throw new OracleFailure("FRAME_INVALID");
                        return 0;
                    }
                    if (type.GetString() == "cancel")
                    {
                        RequireKeys(root, "version", "type", "requestId");
                        if (!root.GetProperty("requestId").TryGetInt32(out var canceled) || canceled != id) throw new OracleFailure("REQUEST_ORDER_INVALID");
                        throw new OracleFailure("CANCELED");
                    }
                    if (type.GetString() == "publicFault")
                    {
                        RequireKeys(root, "version", "type", "fault");
                        if (id != 0) throw new OracleFailure("REQUEST_ORDER_INVALID");
                        var fault = root.GetProperty("fault").ValueKind == JsonValueKind.String ? root.GetProperty("fault").GetString() : null;
                        if (fault == "exit") return 1;
                        if (fault == "timeout") { await Task.Delay(Timeout.InfiniteTimeSpan, lifetime.Token).ConfigureAwait(false); return 1; }
                        if (fault == "stderr")
                        {
                            await using var errors = Console.OpenStandardError();
                            await errors.WriteAsync("TUNNER_PUBLIC_NONFUNCTIONAL_CANARY_008"u8.ToArray(), lifetime.Token).ConfigureAwait(false);
                            await errors.FlushAsync(lifetime.Token).ConfigureAwait(false);
                            return 1;
                        }
                        throw new OracleFailure("INPUT_INVALID");
                    }
                    RequireKeys(root, "version", "type", "requestId", "counterOffset", "seed");
                    if (type.GetString() != "compute" || !root.GetProperty("requestId").TryGetInt32(out var next) || next != id + 1 || next > 64)
                        throw new OracleFailure("REQUEST_ORDER_INVALID");
                    id = next;
                    if (!root.GetProperty("counterOffset").TryGetInt32(out var offset) || offset is < -2 or > 2 || root.GetProperty("seed").ValueKind != JsonValueKind.String)
                        throw new OracleFailure("INPUT_INVALID");
                    var seed = root.GetProperty("seed").GetString();
                    if (seed is null || seed.Length is < 1 or > 128 || seed.Any(value => value is not (>= 'A' and <= 'Z') and not (>= '2' and <= '7')))
                        throw new OracleFailure("INPUT_INVALID");
                    var code = NativeTotpCodeFixture.NativeCode(seed, offset);
                    if (code.Length != 6 || code.Any(value => !char.IsAsciiDigit(value))) throw new OracleFailure("NATIVE_UNAVAILABLE");
                    await WriteAsync(new { version = 1, type = "code", requestId = id, nativeVersion = NativeVersion, code }).ConfigureAwait(false);
                    seed = null; code = string.Empty;
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var failure = error is OracleFailure known ? known.Code : error is OperationCanceledException ? "CANCELED" : error is JsonException ? "FRAME_INVALID" : error is InvalidOperationException ? "INPUT_INVALID" : "ORACLE_FAILED";
            try { await WriteAsync(new { version = 1, type = "error", requestId = id, failureCode = failure }).ConfigureAwait(false); }
            catch (Exception nested) when (nested is not OutOfMemoryException) { }
            return 1;
        }

        async Task ReadExactAsync(Memory<byte> buffer)
        {
            var position = 0;
            while (position < buffer.Length)
            {
                var count = await input.ReadAsync(buffer[position..], lifetime.Token).ConfigureAwait(false);
                if (count == 0) throw new OracleFailure("FRAME_INVALID");
                position += count;
            }
        }
        async Task WriteAsync<T>(T value)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            try
            {
                if (bytes.Length > MaximumFrame || sent + bytes.Length + 4 > MaximumBytes) throw new OracleFailure("FRAME_INVALID");
                var header = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(header, (uint)bytes.Length);
                await output.WriteAsync(header, lifetime.Token).ConfigureAwait(false);
                await output.WriteAsync(bytes, lifetime.Token).ConfigureAwait(false);
                await output.FlushAsync(lifetime.Token).ConfigureAwait(false);
                sent += bytes.Length + 4;
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
    }
    private static void RequireKeys(JsonElement root, params string[] expected)
    {
        var keys = root.EnumerateObject().Select(property => property.Name).ToArray();
        if (keys.Length != expected.Length || keys.Distinct(StringComparer.Ordinal).Count() != expected.Length || expected.Except(keys, StringComparer.Ordinal).Any())
            throw new OracleFailure("INPUT_INVALID");
    }
    private sealed class OracleFailure(string code) : Exception
    {
        internal string Code { get; } = code;
        public override string ToString() => nameof(OracleFailure);
    }
}
