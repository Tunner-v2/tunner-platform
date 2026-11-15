using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

/// <summary>Private Identity owner. Device evidence and trust never participate in authentication admission.</summary>
internal sealed partial class DeviceApplication(IdentityPersistenceDbContext database, AuthenticationApplication authentication,
    IAuthenticationPolicySource authenticationPolicies, IDeviceEvidencePolicySource policies,
    IDeviceTrustEligibilitySource eligibility, TimeProvider clock)
{
    internal const string ObserveOperation = "RECORD_DEVICE_EVIDENCE";
    internal const string ListOperation = "LIST_DEVICES";
    internal static readonly AssurancePolicyScope RevokeScope = new("DEVICE_TRUST_REVOKE", "USER_BFF");

    private DeviceEvidencePolicy Policy(string origin, DateTimeOffset at)
    { var policy = policies.RequireCurrent(origin, at); policy.Require(origin, at); return policy; }
    private void Clean()
    {
        if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Device operations require a clean Identity transaction boundary.");
    }
    private Task<int> LockDevice(Guid id, CancellationToken ct) => database.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT pg_advisory_xact_lock(hashtextextended({"device:" + id.ToString("N")},0))", ct);
    private async Task<DeviceTrustRow?> LatestTrust(Guid id, CancellationToken ct) => await database.Set<DeviceTrustRow>()
        .AsNoTracking().Where(x => x.DeviceId == id).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    private DeviceTrustEligibilityDecision Decision(DeviceTrustEligibilityFacts facts, DeviceEvidencePolicy policy, DateTimeOffset at)
    {
        var decision = eligibility.RequireCurrent(facts, at);
        if (decision.FactsDigest != facts.Digest || decision.PolicyVersion != policy.EligibilityPolicyVersion ||
            string.IsNullOrWhiteSpace(decision.EvidenceReference) || !decision.EffectiveInterval.Contains(at))
            throw new InvalidOperationException("Current exact device trust eligibility evidence is unavailable.");
        return decision;
    }
    private static bool SameDecision(DeviceTrustEligibilityDecision a, DeviceTrustEligibilityDecision b, DateTimeOffset at) =>
        a.FactsDigest == b.FactsDigest && a.Allowed == b.Allowed && a.PolicyVersion == b.PolicyVersion &&
        a.EvidenceReference == b.EvidenceReference && a.EffectiveInterval.Contains(at) && b.EffectiveInterval.Contains(at);
    private static string Correlation(string account, string origin, string token) =>
        AuthenticationIdentity.Digest(JsonSerializer.Serialize(new[] { account, origin, token }));
    private void Event(Guid eventId, string type, Guid subject, long version, string operation, string correlation,
        string classification, DateTimeOffset at) => database.Add(new OutboxMessage(eventId, "1.0", type, "1", at,
            "tunner/identity", subject.ToString("N"), classification, JsonSerializer.Serialize(new { reference = subject.ToString("N") }),
            version.ToString(CultureInfo.InvariantCulture), operation, correlation));

    internal async Task<DeviceListingPage?> ListAsync(StepUpCaller caller, int maximumRows,
        DeviceListingContinuation? after = null, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows); Clean();
        if (after is not null && (after.AccountId != caller.AccountId || after.ActorSessionId != caller.SessionId ||
            after.Origin != caller.Origin || after.DeviceId == Guid.Empty)) throw new ArgumentException("The device continuation has a different actor.", nameof(after));
        var initial = Policy(caller.Origin, clock.GetUtcNow());
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumRows, initial.MaximumListingRows);
        try
        {
            await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var session = await authentication.LockForOperationAsync(caller, ListOperation, ct).ConfigureAwait(false);
            if (session is null)
            { await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false); return null; }
            var query = database.Set<DeviceRecordRow>().AsNoTracking().Where(x => x.AccountId == caller.AccountId && x.Origin == caller.Origin);
            if (after is not null) query = query.Where(x => x.CreatedAt > after.CreatedAt || x.CreatedAt == after.CreatedAt && x.DeviceId.CompareTo(after.DeviceId) > 0);
            var records = await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.DeviceId).Take(maximumRows + 1).ToArrayAsync(ct).ConfigureAwait(false);
            var currentDevice = await database.Set<DeviceSessionEvidenceRow>().AsNoTracking().Where(x => x.SessionId == caller.SessionId)
                .Select(x => (Guid?)x.DeviceId).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            var observations = new List<(DeviceRecordRow Record, DeviceTrustRow Trust)>();
            foreach (var record in records.Take(maximumRows))
            {
                var trust = await LatestTrust(record.DeviceId, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Device trust lineage is unavailable.");
                observations.Add((record, trust));
            }
            var binding = new StepUpOperation("OP-DEVICE-LIST", "DEVICE-LIST", new(ListOperation, "USER_BFF"), caller.SessionId.ToString("N"), 0, new('0', 64));
            var authorization = await authentication.VerifyContinuedOperationAsync(caller, binding, session, ct).ConfigureAwait(false);
            var authPolicy = authenticationPolicies.RequireEffective(clock.GetUtcNow()); authPolicy.Validate();
            var method = await authentication.MethodIsCurrentAsync(session, authPolicy, clock.GetUtcNow(), ct).ConfigureAwait(false);
            var at = clock.GetUtcNow(); var policy = Policy(caller.Origin, at); var finalAuth = authenticationPolicies.RequireEffective(at); finalAuth.Validate();
            if (initial.Snapshot != policy.Snapshot || authPolicy != finalAuth || !method || session.State != "ACTIVE" ||
                AuthenticationApplication.Expired(session, finalAuth, at) || !authentication.MethodFactsCurrentAt(session, finalAuth, at) ||
                authorization is null || !authorization.SessionUseInterval.Contains(at) || !authorization.OperationInterval.Contains(at))
                throw new InvalidOperationException("Current device listing authority is unavailable.");
            var items = new List<DeviceListingItem>();
            foreach (var (record, trust) in observations)
            {
                var effective = trust.State;
                if (trust.State == "TRUSTED")
                {
                    var facts = JsonSerializer.Deserialize<DeviceTrustEligibilityFacts>(trust.EligibilityFactsJson)
                        ?? throw new InvalidOperationException("Device eligibility lineage is unavailable.");
                    var original = JsonSerializer.Deserialize<DeviceTrustEligibilityDecision>(trust.EligibilityDecisionJson)
                        ?? throw new InvalidOperationException("Device eligibility lineage is unavailable.");
                    var current = Decision(facts, policy, at);
                    if (trust.PolicySnapshot != policy.Snapshot || !current.Allowed || !SameDecision(original, current, at)) effective = "UNTRUSTED";
                }
                // Historical characteristics are disclosed only under the currently qualified vocabulary.
                var observed = JsonSerializer.Deserialize<Dictionary<string, string>>(record.CharacteristicsJson)!;
                _ = policy.Characteristics(observed);
                items.Add(new(record.DeviceId, record.Version, trust.Version, currentDevice == record.DeviceId, record.CreatedAt,
                    record.LastObservedAt, new ReadOnlyDictionary<string, string>(observed), trust.State, effective, at));
            }
            DeviceListingContinuation? next = records.Length > maximumRows && items.Count > 0
                ? new(caller.AccountId, caller.SessionId, caller.Origin, items[^1].FirstObservedAt, items[^1].DeviceReference) : null;
            await database.SaveChangesAsync(ct).ConfigureAwait(false); await tx.CommitAsync(ct).ConfigureAwait(false);
            return new(items.AsReadOnly(), next is not null, next);
        }
        catch { database.ChangeTracker.Clear(); throw; }
    }
}
