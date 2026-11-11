using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Tunner.Identity.Persistence;

/// <summary>One explicitly admitted instance bounds native work. Composition must share it across its callers.</summary>
internal sealed class RecoveryCodeNative : IDisposable
{
    private readonly RecoveryCodeCryptoProfile profile;
    private readonly PasswordHasher<IdentityUserAdapter> hasher;
    private readonly SemaphoreSlim work;
    internal RecoveryCodeNative(RecoveryCodeCryptoProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile); this.profile = profile;
        var version = typeof(PasswordHasher<IdentityUserAdapter>).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (version is null || !version.StartsWith(profile.NativeIdentityVersion + "+", StringComparison.Ordinal))
            throw new InvalidOperationException("The approved recovery-code native implementation is unavailable.");
        hasher = new(Options.Create(new PasswordHasherOptions { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = profile.HashIterations }));
        work = new(profile.MaximumConcurrentWork, profile.MaximumConcurrentWork);
    }

    internal static RecoveryCodeValue? Canonical(string? input)
    {
        if (input is null || input.Length is not (32 or 39)) return null;
        var output = new char[32]; var at = 0;
        for (var i = 0; i < input.Length; i++)
        {
            if (input.Length == 39 && i % 5 == 4) { if (input[i] != '-') { Array.Clear(output); return null; } continue; }
            var c = input[i];
            if (c is >= 'a' and <= 'f') c = (char)(c - ('a' - 'A'));
            if (c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F') || at >= output.Length) { Array.Clear(output); return null; }
            output[at++] = c;
        }
        if (at == 32) return new(output);
        Array.Clear(output); return null;
    }

    internal RecoveryCodePreparedSet Generate(string accountId, Guid setReference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountId) || setReference == Guid.Empty) throw new ArgumentException("A recovery-code set owner is required.");
        Enter(); var timer = Stopwatch.StartNew(); var entries = new List<RecoveryCodePreparedEntry>();
        Span<byte> random = stackalloc byte[16];
        try
        {
            for (var attempts = 0; entries.Count < profile.CodesPerSet && attempts < profile.MaximumGenerationAttempts; attempts++)
            {
                Check(timer, ct); RandomNumberGenerator.Fill(random);
                var text = Convert.ToHexString(random); CryptographicOperations.ZeroMemory(random);
                using var candidate = Canonical(text) ?? throw new InvalidOperationException("Native recovery-code format is unavailable.");
                if (entries.Any(entry => entry.Code.Span.SequenceEqual(candidate.Span))) continue;
                var reference = Guid.NewGuid(); var binding = new RecoveryCodeVerifierBinding(accountId, setReference, reference, profile.Version);
                var verifier = hasher.HashPassword(new IdentityUserAdapter(), Frame(binding, candidate)); Check(timer, ct);
                entries.Add(new(reference, verifier, new(candidate.Span.ToArray())));
            }
            if (entries.Count != profile.CodesPerSet) throw new InvalidOperationException("Recovery-code generation capacity is exhausted.");
            var result = new RecoveryCodePreparedSet(entries); entries = []; return result;
        }
        finally { CryptographicOperations.ZeroMemory(random); foreach (var entry in entries) entry.Dispose(); work.Release(); }
    }

    internal bool Verify(RecoveryCodeVerifierBinding binding, string verifier, RecoveryCodeValue code, CancellationToken ct = default)
    {
        return VerifyCandidates([new(binding, verifier)], code, ct) == binding.EntryReference;
    }

    // A whole bounded scan shares one gate and budget. Admit every encoded header before any native KDF call.
    internal Guid? VerifyCandidates(IReadOnlyList<RecoveryCodeVerifierCandidate> candidates, RecoveryCodeValue code, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidates); ArgumentNullException.ThrowIfNull(code);
        if (candidates.Count == 0 || candidates.Count > profile.MaximumCodesPerSet ||
            (long)candidates.Count * profile.HashIterations > profile.MaximumHashWork) return null;
        var first = candidates[0].Binding;
        foreach (var candidate in candidates)
        {
            candidate.Binding.Validate();
            if (candidate.Binding.AccountId != first.AccountId || candidate.Binding.SetReference != first.SetReference ||
                candidate.Binding.ProfileVersion != profile.Version || !AdmittedVerifier(candidate.Verifier)) return null;
        }
        if (candidates.Select(x => x.Binding.EntryReference).Distinct().Count() != candidates.Count) return null;
        Enter(); var timer = Stopwatch.StartNew();
        try
        {
            Guid? match = null; var matches = 0;
            foreach (var candidate in candidates)
            {
                Check(timer, ct);
                var accepted = hasher.VerifyHashedPassword(new IdentityUserAdapter(), candidate.Verifier, Frame(candidate.Binding, code)) != PasswordVerificationResult.Failed;
                Check(timer, ct);
                if (accepted) { match = candidate.Binding.EntryReference; matches++; }
            }
            return matches == 1 ? match : null;
        }
        finally { work.Release(); }
    }

    internal bool AdmittedVerifier(string? verifier)
    {
        if (verifier is null || verifier.Length != 84) return false;
        Span<byte> decoded = stackalloc byte[61];
        try
        {
            return Convert.TryFromBase64String(verifier, decoded, out var length) && length == 61 && Convert.ToBase64String(decoded) == verifier && decoded[0] == 1 &&
                BinaryPrimitives.ReadUInt32BigEndian(decoded[1..5]) == 2 && // Native KeyDerivationPrf.HMACSHA512.
                BinaryPrimitives.ReadUInt32BigEndian(decoded[5..9]) == (uint)profile.HashIterations &&
                BinaryPrimitives.ReadUInt32BigEndian(decoded[9..13]) == 16;
        }
        finally { CryptographicOperations.ZeroMemory(decoded); }
    }

    private static string Frame(RecoveryCodeVerifierBinding binding, RecoveryCodeValue code)
    {
        binding.Validate();
        return JsonSerializer.Serialize(new[] { "Tunner.Identity.RecoveryCode.Verifier.v1", binding.AccountId,
            binding.SetReference.ToString("N"), binding.EntryReference.ToString("N"), binding.ProfileVersion, code.NativeInput() });
    }
    private void Enter()
    {
        if (!work.Wait(0)) throw new InvalidOperationException("Recovery-code native work capacity is exhausted.");
    }
    private void Check(Stopwatch timer, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (timer.Elapsed > profile.MaximumWorkDuration) throw new InvalidOperationException("Recovery-code native work budget is exhausted.");
    }
    public void Dispose() => work.Dispose();
    public override string ToString() => nameof(RecoveryCodeNative);
}
