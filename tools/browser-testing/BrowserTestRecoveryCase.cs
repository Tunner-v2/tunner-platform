using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Governance;
using Tunner.Identity.Persistence;
using Tunner.Platform.Policies;

/// <summary>Explicit finite V01–V03 inputs for real neutral entry/original/cancel HTTP journeys.
/// No production publication, admitted proof, disposition producer or repair outcome is supplied.</summary>
internal sealed class BrowserTestRecoveryCase(string origin, TimeProvider clock, bool passwordRepair = false) :
    IRecoveryPasswordPolicySource, IRecoveryCaseHierarchySource, IRecoveryPasswordPresentationSource
{
    internal const string JourneyId = "RECOVERY_CASE_V01_V03_V1";
    internal const string PasswordJourneyId = "RECOVERY_CASE_PASSWORD_REPAIR_V1";
    private IssuedSetBinding? issuedSet;
    private CompletedRepairBinding? completedRepair;
    private sealed record CompletedRepairBinding(Guid CaseId, Guid RequestId)
    {
        public override string ToString() => nameof(CompletedRepairBinding);
    }
    internal IssuedSetBinding? IssuedSet => Volatile.Read(ref issuedSet);
    internal sealed record IssuedSetBinding(Guid OperationId, Guid SetReference, string AccountId, long AccountVersion, long LifecycleVersion)
    {
        public override string ToString() => nameof(IssuedSetBinding);
    }
    // Mirror only the existing protected native pointer shape; never expose it over fixture IPC.
    private sealed record IssuancePointer(Guid OperationId, Guid OwnerRequestId, string AccountId, Guid SessionId, string Origin, DateTimeOffset ExpiresAt)
    {
        public override string ToString() => nameof(IssuancePointer);
    }
    internal const string GoalReference = "SYNTHETIC_FINITE_RECOVERY_011_GOAL_V1";
    internal const string GoalLabel = "Recover account access";
    private const string Provenance = "SYNTHETIC-FINITE-TEST-NOT-A-PRODUCTION-PUBLICATION";
    private const string GoalLabelKey = "finiteRecoveryCase.goal";
    private const string GoalDescriptionKey = "finiteRecoveryCase.goalDescription";
    private const string EffectLabelKey = "finiteRecoveryCase.effect";
    private const string EffectValueKey = "finiteRecoveryCase.effectValue";
    private const string FollowUpLabelKey = "finiteRecoveryCase.followUp";
    private const string FollowUpValueKey = "finiteRecoveryCase.followUpValue";
    private const string UnresolvedReference = "ACCESS_RESTORATION";
    private readonly DateTimeOffset began = clock.GetUtcNow();

    private EffectivePolicyInterval Interval => new(began.AddMinutes(-1), true, began.AddMinutes(30), false);

    internal void Configure(IServiceCollection services)
    {
        services.AddSingleton(this);
        services.AddSingleton<IRecoveryPasswordPolicySource>(this);
        services.AddSingleton<IRecoveryCaseHierarchySource>(this);
        services.AddSingleton<IRecoveryPasswordPresentationSource>(this);
        // BrowserTestHost already composes the actual native passkey owner. Do not fabricate a proof owner here.
    }

    public RecoveryPasswordPolicyVersion RequireEffective(DateTimeOffset at)
    {
        if (!Interval.Contains(at)) throw new InvalidOperationException("FINITE_RECOVERY_CASE_POLICY_EXPIRED");
        // Same bounded native password-policy shape as the existing private recovery fixture, isolated to this finite journey.
        var policy = new RecoveryPasswordPolicy("SYNTHETIC_FINITE_RECOVERY_011_POLICY_V1", origin, GoalReference,
            15, 128, 10_000, RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions, false,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), 32, 100, 100,
            [UnresolvedReference], "SYNTHETIC-DISPOSABLE-RUN-ONLY-PROCESSING",
            "SYNTHETIC-OWNED-RUN-CLEANUP-NOT-A-PRODUCTION-RETENTION-POLICY");
        return new(new(policy.Version, "ACTIVE", "ACCOUNT_RECOVERY_PASSWORD", Interval, null, [Provenance]), policy);
    }

    public RecoveryCaseHierarchyDecision RequireCurrent(RecoveryCaseHierarchyRequest request, DateTimeOffset at)
    {
        _ = RequireEffective(at);
        if (passwordRepair)
        {
            var binding = IssuedSet;
            var sets = request.Facts.CodeSets;
            var completed = Volatile.Read(ref completedRepair);
            var selected = binding is null ? null : sets?.SingleOrDefault(set => set.SetReference == binding.SetReference);
            // Explicit synthetic hierarchy authority for this literal only. The set comes from the exact
            // native issuance captured below, never a browser selector, latest set or single-active-set fallback.
            var allowed = binding is not null && request.CaseId != Guid.Empty && request.RequestId != Guid.Empty &&
                request.ContextDigest.Length == 64 && request.PolicyVersion == RequireEffective(at).Values.Version &&
                request.Goal == RecoveryCaseGoal.RepairPassword && request.Facts.AccountId == binding.AccountId &&
                request.Facts.AccountVersion == binding.AccountVersion && request.Facts.AccountState == "Active" &&
                request.Facts.PasswordAvailable && request.Facts.EmailConfirmed && request.Facts.Paths.Count == 0 &&
                sets?.Count == 1 && selected is { State: "ACTIVE" } && selected.LifecycleVersion == binding.LifecycleVersion &&
                (selected.UsageVersion == 0 && completed is null || selected.UsageVersion == 1 &&
                    completed is not null && completed.CaseId == request.CaseId && completed.RequestId == request.RequestId);
            return new(allowed, allowed, "SYNTHETIC_FINITE_RECOVERY_011_PASSWORD_HIERARCHY_V1", Provenance, request.Facts.Digest(), Interval)
            { CodeSelection = allowed ? new(binding!.SetReference, binding.LifecycleVersion, null) : null };
        }
        // No tier is admitted. An original neutral entry is not a proof, a governed review or a completed recovery.
        return new(false, false, "SYNTHETIC_FINITE_RECOVERY_011_HIERARCHY_V1", Provenance, request.Facts.Digest(), Interval);
    }

    public RecoveryPasswordPresentation RequirePublished(RecoveryPasswordPolicy policy, DateTimeOffset at)
    {
        var current = RequireEffective(at).Values;
        if (policy.Version != current.Version || policy.Origin != origin || policy.GoalReference != GoalReference)
            throw new InvalidOperationException("FINITE_RECOVERY_CASE_PUBLICATION_UNAVAILABLE");
        var root = RepositoryRootResolver.Resolve();
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "web", "apps", "user", "recovery.content.proposal.json")));
        var content = source.RootElement.GetProperty("content").EnumerateObject().Select(property => Published(property.Name, property.Value.GetString()!)).ToList();
        content.AddRange([
            Published(GoalLabelKey, GoalLabel),
            Published(GoalDescriptionKey, "Start a request to regain access to your Tunner account."),
            Published(EffectLabelKey, "Existing Tunner sessions"),
            Published(EffectValueKey, "Invalidate existing Tunner sessions after an authorized repair."),
            Published(FollowUpLabelKey, "Account access"),
            Published(FollowUpValueKey, "No restoration outcome has been confirmed.")
        ]);
        return new("en", content, "SYNTHETIC_FINITE_RECOVERY_011_PUBLICATION_V1", Interval,
            GoalLabelKey, GoalDescriptionKey, EffectLabelKey, EffectValueKey,
            [new(UnresolvedReference, FollowUpLabelKey, FollowUpValueKey)]);
    }

    internal void MapIssuedBinding(WebApplication app, string connectionString)
    {
        if (!passwordRepair) return;
        app.Use(async (http, next) =>
        {
            if (HttpMethods.IsPost(http.Request.Method) && (http.Request.Path == "/authentication/step-up/password" || http.Request.Path == "/recovery/repair"))
                http.Response.OnStarting(async () =>
                {
                    if (http.Response.StatusCode != StatusCodes.Status200OK) return;
                    if (http.Request.Path == "/recovery/repair")
                    {
                        await PinCompletedRepairAsync(connectionString, http.RequestAborted).ConfigureAwait(false); return;
                    }
                    // The normal native transaction has committed before this callback; await exact binding
                    // before releasing response headers. No response body, code, password or proof is inspected.
                    if (!http.Request.Cookies.TryGetValue("__Host-Tunner.RecoveryCode.Operation", out var cookie) || cookie.Length > 8192)
                        throw new InvalidOperationException("FINITE_RECOVERY_ISSUANCE_BINDING_INVALID");
                    IssuancePointer pointer;
                    try
                    {
                        var protector = http.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector("Tunner.User.RecoveryCode.Operation.v1");
                        using var document = JsonDocument.Parse(protector.Unprotect(cookie));
                        string[] keys = ["OperationId", "OwnerRequestId", "AccountId", "SessionId", "Origin", "ExpiresAt"];
                        if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Count() != keys.Length ||
                            keys.Any(key => !document.RootElement.TryGetProperty(key, out _))) throw new InvalidOperationException("FINITE_RECOVERY_ISSUANCE_BINDING_INVALID");
                        pointer = document.RootElement.Deserialize<IssuancePointer>() ?? throw new InvalidOperationException("FINITE_RECOVERY_ISSUANCE_BINDING_INVALID");
                    }
                    catch (Exception error) when (error is CryptographicException or JsonException)
                    { throw new InvalidOperationException("FINITE_RECOVERY_ISSUANCE_BINDING_INVALID"); }
                    if (pointer.OperationId == Guid.Empty || pointer.OwnerRequestId == Guid.Empty || pointer.SessionId == Guid.Empty ||
                        string.IsNullOrWhiteSpace(pointer.AccountId) || pointer.AccountId.Length > 128 || pointer.Origin != origin || pointer.ExpiresAt <= clock.GetUtcNow())
                        throw new InvalidOperationException("FINITE_RECOVERY_ISSUANCE_BINDING_INVALID");
                    var source = http.RequestServices.GetRequiredService<BrowserTestRecoveryCodes>();
                    if (source.JourneyId != PasswordJourneyId) throw new InvalidOperationException("FINITE_RECOVERY_ISSUANCE_BINDING_INVALID");
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted); deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(deadline.Token).ConfigureAwait(false);
                    await using var command = new NpgsqlCommand("""
                        SELECT o.set_reference,o.expected_version,s.lifecycle_version
                        FROM identity.recovery_code_operations o JOIN identity.recovery_code_sets s
                          ON s.set_reference=o.set_reference AND s.issuance_operation_id=o.operation_id AND s.account_id=o.account_id
                        WHERE o.operation_id=@operation AND o.request_id=@request AND o.account_id=@account AND o.session_id=@session
                          AND o.origin=@origin AND o.kind='GENERATE' AND o.state='COMPLETED' AND o.policy_version=@policy
                          AND o.proof_reference IS NOT NULL AND o.first_response_committed_at IS NOT NULL
                          AND s.state='ACTIVE' AND s.lifecycle_version=1 AND s.usage_version=0
                        """, connection);
                    command.Parameters.AddWithValue("operation", pointer.OperationId); command.Parameters.AddWithValue("request", pointer.OwnerRequestId);
                    command.Parameters.AddWithValue("account", pointer.AccountId); command.Parameters.AddWithValue("session", pointer.SessionId);
                    command.Parameters.AddWithValue("origin", origin); command.Parameters.AddWithValue("policy", source.RequireEffective(clock.GetUtcNow()).Values.Version);
                    await using var rows = await command.ExecuteReaderAsync(deadline.Token).ConfigureAwait(false);
                    if (!await rows.ReadAsync(deadline.Token).ConfigureAwait(false)) throw new InvalidOperationException("FINITE_RECOVERY_ISSUANCE_BINDING_INVALID");
                    var binding = new IssuedSetBinding(pointer.OperationId, rows.GetGuid(0), pointer.AccountId, rows.GetInt64(1), rows.GetInt64(2));
                    if (await rows.ReadAsync(deadline.Token).ConfigureAwait(false)) throw new InvalidOperationException("FINITE_RECOVERY_ISSUANCE_BINDING_INVALID");
                    var original = Interlocked.CompareExchange(ref issuedSet, binding, null);
                    if (original is not null && original != binding) throw new InvalidOperationException("FINITE_RECOVERY_ISSUANCE_BINDING_INVALID");
                });
            await next().ConfigureAwait(false);
        });
    }

    private async Task PinCompletedRepairAsync(string connectionString, CancellationToken token)
    {
        var issued = IssuedSet ?? throw new InvalidOperationException("FINITE_RECOVERY_REPAIR_BINDING_INVALID");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(2));
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(deadline.Token).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("""
            SELECT c.case_id,c.request_id FROM identity.recovery_cases c JOIN identity.recovery_results r ON r.case_id=c.case_id AND r.result_id=c.result_id
            JOIN identity.recovery_code_sets s ON s.set_reference=c.set_reference AND s.account_id=c.account_id
            WHERE c.account_id=@account AND c.set_reference=@set AND c.expected_set_lifecycle_version=@lifecycle
              AND c.state='REPAIRED' AND c.version=2 AND c.proof_kind='CODE' AND c.goal='RepairPassword'
              AND r.outcome='PASSWORD_REPAIRED_NO_SESSION' AND s.issuance_operation_id=@issued
              AND s.lifecycle_version=@lifecycle AND s.usage_version=1
            """, connection);
        command.Parameters.AddWithValue("account", issued.AccountId); command.Parameters.AddWithValue("set", issued.SetReference);
        command.Parameters.AddWithValue("issued", issued.OperationId); command.Parameters.AddWithValue("lifecycle", issued.LifecycleVersion);
        await using var rows = await command.ExecuteReaderAsync(deadline.Token).ConfigureAwait(false);
        if (!await rows.ReadAsync(deadline.Token).ConfigureAwait(false)) throw new InvalidOperationException("FINITE_RECOVERY_REPAIR_BINDING_INVALID");
        var completed = new CompletedRepairBinding(rows.GetGuid(0), rows.GetGuid(1));
        if (await rows.ReadAsync(deadline.Token).ConfigureAwait(false)) throw new InvalidOperationException("FINITE_RECOVERY_REPAIR_BINDING_INVALID");
        var original = Interlocked.CompareExchange(ref completedRepair, completed, null);
        if (original is not null && original != completed) throw new InvalidOperationException("FINITE_RECOVERY_REPAIR_BINDING_INVALID");
    }

    private static RegistrationPublishedText Published(string key, string text) =>
        new(key, "synthetic-finite-test", "synthetic-finite-recovery-011-v1", Provenance, "en", "PUBLISHED", text);
}
