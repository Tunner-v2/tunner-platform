using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class RecoveryCodeNativeTests
{
    // Explicit finite synthetic work profile; never a production policy selection.
    internal static RecoveryCodeCryptoProfile Profile() => new("fixture-recovery-crypto-v1", "10.0.13", 2, 2,
        10_000, 20_000, 8, 1, TimeSpan.FromSeconds(10));

    [Fact]
    public void CanonicalizationIsAsciiOnlyBoundedAndDoesNotPrintValues()
    {
        using var plain = RecoveryCodeNative.Canonical(new string('a', 32));
        using var grouped = RecoveryCodeNative.Canonical(string.Join('-', Enumerable.Repeat(new string('A', 4), 8)));
        Assert.True(plain is not null && grouped is not null && plain.Span.SequenceEqual(grouped.Span));
        Assert.True(plain!.ToString() == nameof(RecoveryCodeValue));
        Assert.True(JsonSerializer.Serialize(plain) == "{}");
        Assert.True(RecoveryCodeNative.Canonical(new string('A', 40)) is null);
        Assert.True(RecoveryCodeNative.Canonical(new string('A', 31) + " ") is null);
        Assert.True(RecoveryCodeNative.Canonical(new string('A', 31) + "Ａ") is null);
        Assert.True(RecoveryCodeNative.Canonical(string.Join('_', Enumerable.Repeat(new string('A', 4), 8))) is null);
        plain.Dispose(); var disposed = false;
        try { _ = plain.Span.Length; } catch (ObjectDisposedException) { disposed = true; }
        Assert.True(disposed);
    }

    [Fact]
    public void GenerationIsUniqueVerifierOnlyAndBoundToExactOwnerSetEntryAndProfile()
    {
        using var native = new RecoveryCodeNative(Profile()); var set = Guid.NewGuid();
        using var prepared = native.Generate("fixture-account", set);
        Assert.True(prepared.Entries.Count == 2);
        Assert.True(!prepared.Entries[0].Code.Span.SequenceEqual(prepared.Entries[1].Code.Span));
        Assert.True(prepared.ToString() == nameof(RecoveryCodePreparedSet));
        Assert.True(JsonSerializer.Serialize(prepared) == "{}");
        var candidates = prepared.Entries.Select(x => new RecoveryCodeVerifierCandidate(
            new("fixture-account", set, x.Reference, Profile().Version), x.Verifier)).ToArray();
        Assert.True(native.VerifyCandidates(candidates, prepared.Entries[0].Code) == prepared.Entries[0].Reference);
        var entry = prepared.Entries[0]; var binding = candidates[0].Binding;
        Assert.True(native.Verify(binding, entry.Verifier, entry.Code));
        Assert.False(native.Verify(binding with { AccountId = "fixture-other" }, entry.Verifier, entry.Code));
        Assert.False(native.Verify(binding with { SetReference = Guid.NewGuid() }, entry.Verifier, entry.Code));
        Assert.False(native.Verify(binding with { EntryReference = Guid.NewGuid() }, entry.Verifier, entry.Code));
        Assert.False(native.Verify(binding with { ProfileVersion = "fixture-other" }, entry.Verifier, entry.Code));
        Assert.True(JsonSerializer.Serialize(candidates[0]) == "{}");
    }

    [Fact]
    public void EncodedNativeCostAndShapeAreAdmittedBeforeVerification()
    {
        using var native = new RecoveryCodeNative(Profile()); using var prepared = native.Generate("fixture-account", Guid.NewGuid());
        var encoded = prepared.Entries[0].Verifier; Assert.True(native.AdmittedVerifier(encoded));
        var bytes = Convert.FromBase64String(encoded);
        try
        {
            bytes[0] = 0; Assert.False(native.AdmittedVerifier(Convert.ToBase64String(bytes))); bytes[0] = 1;
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(1, 4), 1); Assert.False(native.AdmittedVerifier(Convert.ToBase64String(bytes)));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(1, 4), 2);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(5, 4), uint.MaxValue); Assert.False(native.AdmittedVerifier(Convert.ToBase64String(bytes)));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(5, 4), 10_000);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(9, 4), 32); Assert.False(native.AdmittedVerifier(Convert.ToBase64String(bytes)));
            Assert.False(native.AdmittedVerifier(encoded + "A")); Assert.False(native.AdmittedVerifier("not-a-verifier"));
        }
        finally { Array.Clear(bytes); }
    }

    [Fact]
    public void WholeScanRejectsDuplicateEntriesMixedOwnersAndExcessWorkAndHonorsCancellation()
    {
        using var native = new RecoveryCodeNative(Profile()); var set = Guid.NewGuid();
        using var prepared = native.Generate("fixture-account", set); var entry = prepared.Entries[0];
        var candidate = new RecoveryCodeVerifierCandidate(new("fixture-account", set, entry.Reference, Profile().Version), entry.Verifier);
        Assert.True(native.VerifyCandidates([candidate, candidate], entry.Code) is null);
        Assert.True(native.VerifyCandidates([candidate, candidate, candidate], entry.Code) is null);
        var other = new RecoveryCodeVerifierCandidate(candidate.Binding with { AccountId = "fixture-other", EntryReference = Guid.NewGuid() }, entry.Verifier);
        Assert.True(native.VerifyCandidates([candidate, other], entry.Code) is null);
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); var stopped = false;
        try { _ = native.VerifyCandidates([candidate], entry.Code, canceled.Token); } catch (OperationCanceledException) { stopped = true; }
        Assert.True(stopped);
    }

    [Fact]
    public void ProfileRequiresCompleteExplicitSupportedWorkInputs()
    {
        var rejected = false;
        try { _ = new RecoveryCodeCryptoProfile("fixture", "10.0.13", 2, 2, 10_000, 19_999, 8, 1, TimeSpan.FromSeconds(10)); }
        catch (ArgumentException) { rejected = true; }
        Assert.True(rejected);
        rejected = false;
        try { _ = new RecoveryCodeCryptoProfile("fixture", "unsupported", 2, 2, 10_000, 20_000, 8, 1, TimeSpan.FromSeconds(10)); }
        catch (ArgumentException) { rejected = true; }
        Assert.True(rejected); Assert.True(Profile().ToString() == nameof(RecoveryCodeCryptoProfile));
    }

    // Fixed public nonfunctional test bytes only; never registered or selectable in production composition.
    private sealed class CollisionRandom(bool distinctAfterCollision) : RandomNumberGenerator
    {
        internal int Draws { get; private set; }
        public override void GetBytes(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data); GetBytes(data.AsSpan());
        }
        public override void GetBytes(Span<byte> data)
        {
            Draws++; data.Fill(distinctAfterCollision && Draws > 2 ? (byte)0x22 : (byte)0x11);
        }
    }

    [Fact]
    public void GenerationRetriesOneCollisionWithoutResettingAttemptBudget()
    {
        using var native = new RecoveryCodeNative(Profile()); using var random = new CollisionRandom(true);
        using var prepared = native.Generate("PUBLIC_NONFUNCTIONAL_RNG_NO_ACCOUNT", Guid.NewGuid(), random, CancellationToken.None);
        Assert.True(random.Draws == 3 && prepared.Entries.Count == 2);
        Assert.True(!prepared.Entries[0].Code.Span.SequenceEqual(prepared.Entries[1].Code.Span));
        Assert.True(prepared.Entries.All(entry => native.AdmittedVerifier(entry.Verifier)));
    }

    [Fact]
    public void GenerationStopsAtExactAttemptLimitAndReleasesGateAfterCollisions()
    {
        using var native = new RecoveryCodeNative(Profile()); using var random = new CollisionRandom(false); var rejected = false;
        try { using var unexpected = native.Generate("PUBLIC_NONFUNCTIONAL_RNG_NO_ACCOUNT", Guid.NewGuid(), random, CancellationToken.None); }
        catch (InvalidOperationException) { rejected = true; }
        Assert.True(rejected && random.Draws == Profile().MaximumGenerationAttempts);
        using var retry = native.Generate("PUBLIC_NONFUNCTIONAL_RNG_NO_ACCOUNT", Guid.NewGuid());
        Assert.True(retry.Entries.Count == Profile().CodesPerSet);
    }
}
