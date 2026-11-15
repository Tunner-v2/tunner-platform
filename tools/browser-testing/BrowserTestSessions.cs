using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Identity.Persistence;
using Tunner.Platform.Policies;

/// <summary>Explicit persistent local-test inputs only; never composed into a production BFF.</summary>
internal sealed class BrowserTestSessions : ISessionListingHttpBudgetSource,
    ISessionListingPresentationSource, ISessionRevocationPresentationSource, ISessionBulkRevocationDiscoverySource,
    ISessionBulkRevocationHttpBudgetSource, ISessionBulkRevocationPresentationSource
{
    internal const string AssuranceVersion = "browser-test-sessions-revoke-v1";
    internal const string BulkAssuranceVersion = "browser-test-sessions-revoke-others-v1";
    private const string Provenance = "SYNTHETIC-LOCAL-TEST-ONLY";
    private readonly SessionListingHttpBudgetCatalog budgets = new([
        new(new("browser-test-sessions-budget-v1", "ACTIVE", SessionListingHttpBudget.Scope,
            Interval(), null, [Provenance]), 20, 4096)]);

    internal static void Configure(IServiceCollection services)
    {
        var fixture = new BrowserTestSessions();
        services.AddSingleton(fixture);
        services.AddSingleton<ISessionListingHttpBudgetSource>(fixture);
        services.AddSingleton<ISessionListingPresentationSource>(fixture);
        services.AddSingleton<ISessionRevocationPresentationSource>(fixture);
        services.AddSingleton<ISessionBulkRevocationDiscoverySource>(fixture);
        services.AddSingleton<ISessionBulkRevocationHttpBudgetSource>(fixture);
        services.AddSingleton<ISessionBulkRevocationPresentationSource>(fixture);
        services.AddTunnerSessionRevocation();
        services.AddTunnerSessionBulkRevocation();
    }

    internal static AssurancePolicyVersion AssurancePolicy()
    {
        // Reuse the existing isolated test's explicitly qualified password/passkey method mappings.
        var existing = BrowserTestPasskeys.AssurancePolicies().First();
        return new(new(AssuranceVersion, "ACTIVE", new AssurancePolicyScope("SESSION_REVOKE", "USER_BFF"),
            Interval(), null, [Provenance]), existing.RequiredAssurance, existing.ProofLifetime,
            existing.MethodAssurances.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }
    internal static AssurancePolicyVersion BulkAssurancePolicy()
    {
        var existing = AssurancePolicy();
        return new(new(BulkAssuranceVersion, "ACTIVE", new AssurancePolicyScope("SESSION_REVOKE_OTHERS", "USER_BFF"),
            Interval(), null, [Provenance]), existing.RequiredAssurance, existing.ProofLifetime,
            existing.MethodAssurances.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    SessionBulkRevocationDiscoveryBudget ISessionBulkRevocationDiscoverySource.RequireEffective(DateTimeOffset at) => new(
        new("browser-test-sessions-bulk-discovery-v1", "ACTIVE", SessionBulkRevocationDiscoveryBudget.Scope, Interval(), null, [Provenance]), 20);
    SessionBulkRevocationHttpBudget ISessionBulkRevocationHttpBudgetSource.RequireEffective(DateTimeOffset at) => new(
        new("browser-test-sessions-bulk-transport-v1", "ACTIVE", SessionBulkRevocationHttpBudget.Scope, Interval(), null, [Provenance]), 4096, 4096);
    SessionBulkRevocationPresentation ISessionBulkRevocationPresentationSource.RequirePublished(AuthenticationPolicy policy)
    {
        RequireLocal(policy);
        // Scoped approved UI copy, qualified only for this existing isolated synthetic profile.
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = "Sign out other sessions", ["review.title"] = "Sign out these other sessions?",
            ["review.description"] = "End access to Tunner for the {count} sessions listed below.",
            ["review.originalSet"] = "{count} sessions in this request · Prepared", ["confirm"] = "Sign out other sessions",
            ["cancel"] = "Keep signed in", ["loading"] = "Please wait while we check this original request.",
            ["empty"] = "An original session set could not be prepared for review.",
            ["changed"] = "This original session request has changed. Sign-out is not confirmed. Check its status before starting another request.",
            ["completed"] = "Other sessions signed out", ["unknown"] = "Sign-out is not confirmed", ["check"] = "Check sign-out status",
            ["description"] = "Review the other Tunner sessions included in this request.",
            ["keepCurrent"] = "This action keeps your current session signed in.",
            ["originalOnly"] = "Only these sessions are included. Sessions started later are not part of this request.",
            ["completed.description"] = "Access to Tunner ended for the {count} sessions in this request.",
            ["unknown.description"] = "We could not confirm the result for this request. Check its status before starting another sign-out request.",
            ["unknown.help"] = "Checking this request does not send a new sign-out request.", ["back"] = "Back to sessions"
        };
        return new("en", values.Select(value => new RegistrationPublishedText("account.sessions.bulk." + value.Key, "synthetic-local-test",
            "synthetic-local-test", Provenance, "en", "PUBLISHED", value.Value)));
    }

    public SessionListingHttpBudget RequireEffective(DateTimeOffset at) => budgets.RequireEffective(at);

    SessionListingPresentation ISessionListingPresentationSource.RequirePublished(AuthenticationPolicy policy)
    {
        RequireLocal(policy);
        return new("en", Published(false));
    }

    SessionRevocationPresentation ISessionRevocationPresentationSource.RequirePublished(AuthenticationPolicy policy)
    {
        RequireLocal(policy);
        return new("en", Published(true));
    }

    private static RegistrationPublishedText[] Published(bool revocation)
    {
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "browser-test-sessions-content.json")));
        return source.RootElement.GetProperty("content").EnumerateObject()
            .Where(value => value.Name.StartsWith("account.sessions.revoke.", StringComparison.Ordinal) == revocation)
            .Select(value => new RegistrationPublishedText(value.Name, "synthetic-local-test", "synthetic-local-test",
                Provenance, "en", "PUBLISHED", value.Value.GetString()!)).ToArray();
    }

    private static void RequireLocal(AuthenticationPolicy policy)
    {
        if (policy.Version != "browser-test-auth-v1") throw new InvalidOperationException("Isolated test presentation only.");
    }
    private static EffectivePolicyInterval Interval() => new(DateTimeOffset.UtcNow.AddDays(-1), true, null, false);
}
