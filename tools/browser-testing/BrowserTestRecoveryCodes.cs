using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tunner.Identity.Persistence;
using Tunner.Platform.Policies;

/// <summary>Explicit finite test inputs. No production publication, profile, effect or recovery workflow.</summary>
internal sealed class BrowserTestRecoveryCodes(string origin, string journeyId, string connectionString, TimeProvider clock) :
    IRecoveryCodePolicySource, IRecoveryCodeMutationAdmissionSource, IRecoveryCodePresentationSource
{
    internal const string Assurance = "SYNTHETIC_FINITE_RECOVERY_ASSURANCE";
    internal const string GenerateAssurance = "finite-recovery-generate-proof-v1";
    internal const string ReplaceAssurance = "finite-recovery-replace-proof-v1";
    internal static readonly string[] JourneyIds = ["RECOVERY_GENERATE_PASSWORD_V1", "RECOVERY_REPLACE_PASSWORD_V1", "RECOVERY_GENERATE_PASSKEY_V1", "RECOVERY_REPLACE_PASSKEY_V1", "RECOVERY_GENERATE_TOTP_V1", "RECOVERY_REPLACE_TOTP_V1"];
    internal static readonly string[] Actions = ["READ_SAFE_COUNTS", "DENY_OWNER_ONCE", "DROP_FIRST_DELIVERY_ONCE", "WITHDRAW_PUBLICATION_AFTER_APPLY", "SUPERSEDE_PUBLICATION_AFTER_APPLY", "EXPIRE_PUBLICATION_AFTER_APPLY"];
    private static readonly string[] ProofRoutes = ["/authentication/step-up/password", "/authentication/step-up/passkey", "/authentication/step-up/passkey/continue", "/authentication/step-up/totp", "/authentication/step-up/totp/continue"];
    internal static readonly string[] CountKeys = ["ownerCommits", "operations", "sets", "replacedSets", "entries", "proofs", "consumedProofs", "sessions", "outbox", "codeAcceptances"];
    private readonly DateTimeOffset began = clock.GetUtcNow();
    private int denied, drop, publicationReads, readsAtArm, publicationFault;
    private bool publicationWithdrawn, publicationSuperseded, publicationExpired;
    private readonly ConcurrentDictionary<(string Account, long Version, string Kind, string Facts), AdmissionBaseline> baselines = new();
    internal bool OwnerFailureObserved { get; private set; }
    internal bool PublicationFinalFaultObserved { get; private set; }
    internal bool BodyDropObserved { get; private set; }
    internal string JourneyId { get; } = JourneyIds.Contains(journeyId, StringComparer.Ordinal) ? journeyId : throw new InvalidOperationException("FINITE_RECOVERY_JOURNEY_INVALID");
    internal bool NeedsTotp => JourneyId.EndsWith("_TOTP_V1", StringComparison.Ordinal);
    internal string ProfileId => JourneyId == "RECOVERY_REPLACE_PASSWORD_V1" ? "R009_FINITE_SUBSET_V1" : "R009_FINITE_SINGLE_V1";
    internal RecoveryCodeCryptoProfile Crypto { get; } = new("SYNTHETIC_FINITE_RECOVERY_009_CRYPTO_V1", "10.0.13", 4, 4, 10000, 40000, 16, 1, TimeSpan.FromSeconds(2));
    internal static RecoveryCodePolicy FinitePolicy(string profileId, string hostOrigin, RecoveryCodeCryptoProfile crypto)
    {
        var (sets, accountAttempts, networkAttempts) = profileId switch
        {
            "R009_FINITE_SINGLE_V1" => (1, 20, 40),
            "R009_FINITE_SUBSET_V1" => (3, 20, 40),
            "R009_FINITE_RATE_V1" => (1, 2, 3),
            _ => throw new InvalidOperationException("FINITE_RECOVERY_PROFILE_INVALID")
        };
        var policy = new RecoveryCodePolicy("SYNTHETIC_FINITE_RECOVERY_009_" + profileId, hostOrigin, crypto, "NATIVE_HEX32_V1", "SYNTHETIC_FINITE_RECOVERY_009_DELIVERY_V1",
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), sets, 4, RecoveryCodeGenerationAdmission.WithinApprovedActiveSetCapacity,
            RecoveryCodeReplacementMechanism.RetireExactExpectedSetsAtIssueCommit, RecoveryCodeSessionEffect.NoSessionMutation,
            RecoveryCodeDeliveryMechanism.OneVolatileViewWithOptionalSingleLocalDownload, GenerateAssurance, ReplaceAssurance, accountAttempts, networkAttempts,
            "SYNTHETIC-DISPOSABLE-RUN-ONLY-PROCESSING", "SYNTHETIC-OWNED-RUN-CLEANUP-NOT-A-PRODUCTION-RETENTION-POLICY");
        policy.Validate(); return policy;
    }
    private EffectivePolicyInterval Interval => new(began.AddMinutes(-1), true, began.AddMinutes(30), false);
    public RecoveryCodePolicyVersion RequireEffective(DateTimeOffset at)
    {
        if (!Interval.Contains(at)) throw new InvalidOperationException("FINITE_RECOVERY_POLICY_EXPIRED");
        var policy = FinitePolicy(ProfileId, origin, Crypto);
        return new(new(policy.Version, "ACTIVE", "RECOVERY_CODES", Interval, null, ["SYNTHETIC-FINITE-TEST-ONLY"]), policy);
    }
    internal void Configure(IServiceCollection services)
    {
        services.AddSingleton(this); services.AddSingleton(Crypto); services.AddSingleton<IRecoveryCodePolicySource>(this);
        services.AddSingleton<IRecoveryCodeMutationAdmissionSource>(this); services.AddSingleton<IRecoveryCodePresentationSource>(this);
    }
    internal IEnumerable<AssurancePolicyVersion> AssurancePolicies()
    {
        foreach (var (version, operation) in new[] { (GenerateAssurance, "GENERATE_RECOVERY_CODES"), (ReplaceAssurance, "REPLACE_RECOVERY_CODES") })
            yield return new(new(version, "ACTIVE", new(operation, "USER_BFF"), Interval, null, ["SYNTHETIC-FINITE-TEST-ONLY"]),
                Assurance, TimeSpan.FromMinutes(5), new Dictionary<string, string>
                {
                    ["pwd"] = Assurance, ["totp"] = Assurance,
                    [PasskeyPolicy.MethodKey(false, false)] = Assurance, [PasskeyPolicy.MethodKey(true, false)] = Assurance, [PasskeyPolicy.MethodKey(true, true)] = Assurance
                });
    }
    public ValueTask<RecoveryCodeMutationAdmission> RequireAsync(RecoveryCodeMutationAdmissionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref denied, 0) == 1) { OwnerFailureObserved = true; throw new InvalidOperationException("FINITE_RECOVERY_OWNER_FAILURE"); }
        var decision = RequireCurrent(request);
        if (decision.Allowed)
        {
            if (baselines.Count >= 32 && !baselines.ContainsKey((request.AccountId, request.AccountVersion, request.Kind, request.FactsDigest))) throw new InvalidOperationException("FINITE_RECOVERY_BASELINE_LIMIT");
            baselines.TryAdd((request.AccountId, request.AccountVersion, request.Kind, request.FactsDigest), new(request.AccountVersion, Array.AsReadOnly(request.ActiveSets.ToArray()), Array.AsReadOnly(decision.RetirementTargets.ToArray())));
        }
        return ValueTask.FromResult(decision);
    }
    public RecoveryCodeMutationAdmission RequireCurrent(RecoveryCodeMutationAdmissionRequest request)
    {
        // Pure fixed-profile current resolution; no fault consumption, baseline mutation or captured-decision fallback.
        var targets = request.Kind == "REPLACE" ? request.ActiveSets.OrderBy(x => x.SetReference).Take(1).Select(x => new RecoveryCodeRetirementTarget(x.SetReference, x.LifecycleVersion)).ToArray() : [];
        var policy = RequireEffective(request.EffectiveAt).Values;
        var allowed = request.PolicyVersion == policy.Version && (request.Kind == "GENERATE" ? request.ActiveSets.Count < policy.MaximumActiveSets : request.Kind == "REPLACE" && targets.Length == 1);
        return new(request.AccountId, request.AccountVersion, request.Kind, request.FactsDigest, allowed,
            targets, "SYNTHETIC_FINITE_RECOVERY_009_ADMISSION_V1", "SYNTHETIC-FINITE-OWNER-TEST-ONLY", Interval);
    }
    public RecoveryCodePresentation RequirePublished(RecoveryCodePolicy policy, RecoveryCodeImpactFacts facts, DateTimeOffset at)
    {
        Interlocked.Increment(ref publicationReads);
        if (publicationWithdrawn || !Interval.Contains(at)) throw new InvalidOperationException("FINITE_RECOVERY_PUBLICATION_UNAVAILABLE");
        var interval = publicationExpired ? new EffectivePolicyInterval(Interval.From, true, at, false) : Interval;
        if (!interval.Contains(at)) throw new InvalidOperationException("FINITE_RECOVERY_PUBLICATION_EXPIRED");
        var key = facts.Kind == "GENERATE" ? "finiteRecovery.generateImpact" : "finiteRecovery.replaceImpact";
        var content = PublishedContent().Append(new RegistrationPublishedText(key, "synthetic-finite-test", "synthetic-finite-test-v1", "SYNTHETIC-FINITE-TEST-NOT-A-PUBLICATION", "en", "PUBLISHED",
            facts.Kind == "GENERATE" ? "Synthetic finite test only: create one set of four codes without changing sessions." : "Synthetic finite test only: replace the one server-selected set and its remaining unused codes at commit; other sets and sessions stay unchanged."));
        return new("en", content, key, facts.FactsDigest, publicationSuperseded ? "SYNTHETIC_FINITE_RECOVERY_009_PUBLICATION_V2" : "SYNTHETIC_FINITE_RECOVERY_009_PUBLICATION_V1", interval);
    }
    internal static RegistrationPublishedText[] PublishedContent()
    {
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "browser-test-recovery-content.json")));
        return source.RootElement.GetProperty("content").EnumerateObject().Select(p => new RegistrationPublishedText(p.Name, "synthetic-finite-test", "synthetic-finite-test-v1",
            "SYNTHETIC-FINITE-TEST-NOT-A-PUBLICATION", "en", "PUBLISHED", p.Value.GetString()!)).ToArray();
    }
    // Fixed after-apply barrier through the existing synthetic Account access source.
    // It is armed only after the actual proof UI is ready; no delegate or policy setter crosses IPC.
    internal void BeforeAccessDecision(AccountAuthenticationAccessRequest request)
    {
        if (request.Operation != "SESSION_USE" || Volatile.Read(ref publicationReads) <= Volatile.Read(ref readsAtArm)) return;
        var fault = Interlocked.Exchange(ref publicationFault, 0); if (fault == 0) return;
        publicationWithdrawn = fault == 1; publicationSuperseded = fault == 2; publicationExpired = fault == 3; PublicationFinalFaultObserved = true;
    }
    internal void Arm(string action)
    {
        switch (action)
        {
            case "READ_SAFE_COUNTS": break;
            case "DENY_OWNER_ONCE": Interlocked.Exchange(ref denied, 1); break;
            case "DROP_FIRST_DELIVERY_ONCE": Interlocked.Exchange(ref drop, 1); break;
            case "WITHDRAW_PUBLICATION_AFTER_APPLY": ArmPublication(1); break;
            case "SUPERSEDE_PUBLICATION_AFTER_APPLY": ArmPublication(2); break;
            case "EXPIRE_PUBLICATION_AFTER_APPLY": ArmPublication(3); break;
            default: throw new InvalidOperationException("FINITE_RECOVERY_ACTION_INVALID");
        }
    }
    private void ArmPublication(int fault) { readsAtArm = Volatile.Read(ref publicationReads); Interlocked.Exchange(ref publicationFault, fault); }
    internal sealed class AdmissionBaseline(long accountVersion, IReadOnlyList<RecoveryCodeSetFacts> sets, IReadOnlyList<RecoveryCodeRetirementTarget> targets)
    {
        internal long AccountVersion { get; } = accountVersion;
        internal IReadOnlyList<RecoveryCodeSetFacts> Sets { get; } = sets;
        internal IReadOnlyList<RecoveryCodeRetirementTarget> Targets { get; } = targets;
        public override string ToString() => nameof(AdmissionBaseline);
    }
    internal AdmissionBaseline? Baseline(string account, long version, string kind, string facts) => baselines.TryGetValue((account, version, kind, facts), out var value) ? value : null;
    internal void MapResponseFaults(WebApplication app) => app.Use(async (http, next) =>
    {
        if (!HttpMethods.IsPost(http.Request.Method) || !ProofRoutes.Contains(http.Request.Path.Value, StringComparer.Ordinal) ||
            !http.Request.Cookies.ContainsKey("__Host-Tunner.RecoveryCode.Operation") || Interlocked.Exchange(ref drop, 0) != 1) { await next(http).ConfigureAwait(false); return; }
        var original = http.Response.Body;
        try { http.Response.Body = Stream.Null; await next(http).ConfigureAwait(false); BodyDropObserved = http.Response.StatusCode == 200; http.Abort(); }
        finally { http.Response.Body = original; }
    });
    internal async Task<Dictionary<string, int>> CountsAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(ct).ConfigureAwait(false);
        await using (var accounts = new NpgsqlCommand("SELECT count(*) FROM accounts.accounts", connection))
            if ((long)(await accounts.ExecuteScalarAsync(ct).ConfigureAwait(false))! > 1) throw new InvalidOperationException("FINITE_RECOVERY_ACCOUNT_SCOPE_INVALID");
        var queries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ownerCommits"] = "SELECT count(*) FROM identity.recovery_code_operations WHERE state='COMPLETED'", ["operations"] = "SELECT count(*) FROM identity.recovery_code_operations",
            ["sets"] = "SELECT count(*) FROM identity.recovery_code_sets", ["replacedSets"] = "SELECT count(*) FROM identity.recovery_code_sets WHERE state='REPLACED'",
            ["entries"] = "SELECT count(*) FROM identity.recovery_code_entries", ["proofs"] = "SELECT count(*) FROM identity.step_up_proofs", ["consumedProofs"] = "SELECT count(*) FROM identity.step_up_proofs WHERE state='CONSUMED'",
            ["sessions"] = "SELECT count(*) FROM identity.tunner_sessions", ["outbox"] = "SELECT count(*) FROM platform.outbox_messages", ["codeAcceptances"] = "SELECT count(*) FROM identity.totp_used_codes"
        };
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, sql) in queries) { await using var command = new NpgsqlCommand(sql, connection); var count = Convert.ToInt32(await command.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture); if (count is < 0 or > 1000) throw new InvalidOperationException("FINITE_RECOVERY_COUNT_LIMIT"); result.Add(key, count); }
        return result;
    }
    public override string ToString() => nameof(BrowserTestRecoveryCodes);
}
