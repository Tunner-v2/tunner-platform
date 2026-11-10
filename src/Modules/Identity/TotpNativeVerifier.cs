using System.Reflection;
using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tunner.Identity.Persistence;

/// <summary>Native acceptance with transaction-owned bounded replay history. Contains no OTP calculation.</summary>
internal sealed class TotpNativeVerifier(IdentityPersistenceDbContext database, TimeProvider clock)
{
    internal static string? CanonicalCode(string? value)
    {
        if (value is null || value.Length > 32) return null;
        var code = string.Concat(value.Where(x => x is not (' ' or '-')));
        return code.Length == 6 && code.All(char.IsAsciiDigit) ? code : null;
    }
    internal async Task<string?> AcceptAsync(TotpAuthenticatorRow factor, TotpPolicy policy, string seed, string? input, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("TOTP replay acceptance requires its owner transaction.");
        policy.Validate(); var code = CanonicalCode(input); if (code is null) return "VALIDATION_ERROR";
        var actual = typeof(AuthenticatorTokenProvider<IdentityUserAdapter>).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (actual is null || !actual.StartsWith(policy.NativeVerifierVersion + "+", StringComparison.Ordinal)) return "DEPENDENCY_UNAVAILABLE";
        var timer = Stopwatch.StartNew(); var systemBefore = DateTimeOffset.UtcNow; var before = clock.GetUtcNow();
        if ((systemBefore - before).Duration() > policy.MaximumClockDeviation || factor.ClockHighWaterAt is { } high && systemBefore < high)
            return "CLOCK_OUT_OF_BOUNDS";
        var live = await database.Set<TotpUsedCodeRow>().Where(x => x.AuthenticatorReference == factor.AuthenticatorReference && x.Generation == factor.Generation && x.ExpiresAt > systemBefore).ToArrayAsync(ct).ConfigureAwait(false);
        var hasher = new PasswordHasher<IdentityUserAdapter>(Options.Create(new PasswordHasherOptions { CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3, IterationCount = policy.ReplayHashIterations }));
        var user = new IdentityUserAdapter(); var repeated = false;
        foreach (var entry in live)
        {
            ct.ThrowIfCancellationRequested(); if (timer.Elapsed > policy.MaximumVerificationDuration) return "VERIFICATION_BUDGET_EXHAUSTED";
            try { repeated |= hasher.VerifyHashedPassword(user, entry.Verifier, code) != PasswordVerificationResult.Failed; }
            catch (FormatException) { return "DEPENDENCY_UNAVAILABLE"; }
        }
        ct.ThrowIfCancellationRequested(); if (timer.Elapsed > policy.MaximumVerificationDuration) return "VERIFICATION_BUDGET_EXHAUSTED";
        if (repeated) return "CODE_REPLAYED";
        if (live.Length >= policy.MaximumLiveReplayClaims) return "REPLAY_CAPACITY_EXHAUSTED";
        using var manager = new EphemeralSeedManager(seed);
        bool accepted;
        try { accepted = await new AuthenticatorTokenProvider<IdentityUserAdapter>().ValidateAsync("TOTP", code, manager, user).ConfigureAwait(false); }
        catch (FormatException) { return "DEPENDENCY_UNAVAILABLE"; }
        if (!accepted) return "AUTHENTICATION_FAILED";
        ct.ThrowIfCancellationRequested(); if (timer.Elapsed > policy.MaximumVerificationDuration) return "VERIFICATION_BUDGET_EXHAUSTED";
        var verifier = hasher.HashPassword(user, code); var after = clock.GetUtcNow(); var systemAfter = DateTimeOffset.UtcNow;
        if (timer.Elapsed > policy.MaximumVerificationDuration || after < before || systemAfter < systemBefore || after - before > policy.MaximumVerificationDuration ||
            systemAfter - systemBefore > policy.MaximumVerificationDuration || (systemAfter - after).Duration() > policy.MaximumClockDeviation)
            return "CLOCK_OUT_OF_BOUNDS";
        // Retain the entire bool verifier acceptance horizon, not the current server counter.
        database.Add(new TotpUsedCodeRow { ClaimId = Guid.NewGuid(), AuthenticatorReference = factor.AuthenticatorReference, Generation = factor.Generation,
            Verifier = verifier, AcceptedAt = systemAfter, ExpiresAt = systemAfter + policy.ReplayRetention, PolicyVersion = policy.Version });
        // Expired claims are removed only after the high-water check. A future backwards clock fails closed.
        database.RemoveRange(await database.Set<TotpUsedCodeRow>().Where(x => x.AuthenticatorReference == factor.AuthenticatorReference && x.Generation == factor.Generation && x.ExpiresAt <= systemBefore).ToArrayAsync(ct).ConfigureAwait(false));
        factor.ClockHighWaterAt = systemAfter; factor.LastUsedAt = after; factor.UsageVersion++;
        return null;
    }

    private sealed class EphemeralSeedManager(string seed) : UserManager<IdentityUserAdapter>(new ReadOnlySeedStore(seed), Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
        new PasswordHasher<IdentityUserAdapter>(), [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!, NullLogger<UserManager<IdentityUserAdapter>>.Instance);
    private sealed class ReadOnlySeedStore(string seed) : IUserStore<IdentityUserAdapter>, IUserAuthenticatorKeyStore<IdentityUserAdapter>
    {
        private string? seed = seed;
        public Task<string?> GetAuthenticatorKeyAsync(IdentityUserAdapter user, CancellationToken cancellationToken) => Task.FromResult(seed);
        public Task SetAuthenticatorKeyAsync(IdentityUserAdapter user, string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Dispose() => seed = null;
        public Task<string> GetUserIdAsync(IdentityUserAdapter user, CancellationToken cancellationToken) => Task.FromResult(user.Id);
        public Task<string?> GetUserNameAsync(IdentityUserAdapter user, CancellationToken cancellationToken) => Task.FromResult(user.UserName);
        public Task<string?> GetNormalizedUserNameAsync(IdentityUserAdapter user, CancellationToken cancellationToken) => Task.FromResult(user.NormalizedUserName);
        public Task SetUserNameAsync(IdentityUserAdapter user, string? userName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetNormalizedUserNameAsync(IdentityUserAdapter user, string? normalizedName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityResult> CreateAsync(IdentityUserAdapter user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityResult> UpdateAsync(IdentityUserAdapter user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityResult> DeleteAsync(IdentityUserAdapter user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityUserAdapter?> FindByIdAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityUserAdapter?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
