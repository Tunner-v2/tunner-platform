using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Tunner.Identity.Persistence.Tests;

/// <summary>Synthetic test-only authenticator. All private key operations stay inside this fixture, never in a Tunner command.</summary>
internal sealed class PasskeyAuthenticator : IDisposable
{
    private static readonly string[] InternalTransport = ["internal"];
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    internal byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);
    internal string? UserHandle { get; private set; }
    private uint count;
    internal JsonElement Attest(JsonElement options, string origin, string? wrongRp = null, string? challenge = null, bool uv = true, bool backupEligible = false, bool backedUp = false)
    {
        UserHandle = options.GetProperty("user").GetProperty("id").GetString();
        var parameters = key.ExportParameters(false);
        byte[] cose = [0xA5, 0x01, 0x02, 0x03, 0x26, 0x20, 0x01, 0x21, .. ByteString(parameters.Q.X!), 0x22, .. ByteString(parameters.Q.Y!)];
        var rp = wrongRp ?? options.GetProperty("rp").GetProperty("id").GetString()!;
        byte[] auth = [.. SHA256.HashData(Encoding.UTF8.GetBytes(rp)), (byte)((uv ? 0x45 : 0x41) | (backupEligible ? 0x08 : 0) | (backedUp ? 0x10 : 0)), 0, 0, 0, 0,
            .. new byte[16], 0, (byte)CredentialId.Length, .. CredentialId, .. cose];
        byte[] attestation = [0xA3, .. Text("fmt"), .. Text("none"), .. Text("authData"), .. ByteString(auth), .. Text("attStmt"), 0xA0];
        var client = Client("webauthn.create", challenge ?? options.GetProperty("challenge").GetString()!, origin);
        return JsonSerializer.SerializeToElement(new { id = Encode(CredentialId), rawId = Encode(CredentialId), type = "public-key", response = new
            { clientDataJSON = Encode(client), attestationObject = Encode(attestation), transports = InternalTransport }, clientExtensionResults = new { } });
    }
    internal JsonElement Assert(JsonElement options, string origin, string? wrongRp = null, string? challenge = null, string type = "webauthn.get", bool uv = true, bool badSignature = false, bool backup = false, bool backupEligible = false, bool backedUp = false, bool up = true, uint? counterOverride = null, string? wrongUserHandle = null)
    {
        var rp = wrongRp ?? options.GetProperty("rpId").GetString()!;
        Span<byte> counter = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(counter, counterOverride ?? ++count);
        byte[] auth = [.. SHA256.HashData(Encoding.UTF8.GetBytes(rp)), (byte)((uv ? 0x04 : 0) | (up ? 0x01 : 0) | (backup || backupEligible ? 0x08 : 0) | (backup || backedUp ? 0x10 : 0)), .. counter];
        var client = Client(type, challenge ?? options.GetProperty("challenge").GetString()!, origin);
        byte[] signed = [.. auth, .. SHA256.HashData(client)];
        var signature = key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        if (badSignature) signature[^1] ^= 0xFF;
        return JsonSerializer.SerializeToElement(new { id = Encode(CredentialId), rawId = Encode(CredentialId), type = "public-key", response = new
            { clientDataJSON = Encode(client), authenticatorData = Encode(auth), signature = Encode(signature), userHandle = wrongUserHandle ?? UserHandle }, clientExtensionResults = new { } });
    }
    private static byte[] Client(string type, string challenge, string origin) => JsonSerializer.SerializeToUtf8Bytes(new { type, challenge, origin, crossOrigin = false });
    private static string Encode(byte[] bytes) => WebEncoders.Base64UrlEncode(bytes);
    private static byte[] Text(string text) => [(byte)(0x60 + text.Length), .. Encoding.UTF8.GetBytes(text)];
    private static byte[] ByteString(byte[] bytes) => bytes.Length <= 255 ? [0x58, (byte)bytes.Length, .. bytes] : [0x59, (byte)(bytes.Length >> 8), (byte)bytes.Length, .. bytes];
    public void Dispose() => key.Dispose();
}
