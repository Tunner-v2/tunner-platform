using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Identity.Persistence;
using Tunner.Platform.Policies;

/// <summary>Explicit persistent local-test inputs only; never composed into a production BFF.</summary>
internal sealed class BrowserTestSessions : ISessionListingHttpBudgetSource,
    ISessionListingPresentationSource, ISessionRevocationPresentationSource
{
    internal const string AssuranceVersion = "browser-test-sessions-revoke-v1";
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
        services.AddTunnerSessionRevocation();
    }

    internal static AssurancePolicyVersion AssurancePolicy()
    {
        // Reuse the existing isolated test's explicitly qualified password/passkey method mappings.
        var existing = BrowserTestPasskeys.AssurancePolicies().First();
        return new(new(AssuranceVersion, "ACTIVE", new AssurancePolicyScope("SESSION_REVOKE", "USER_BFF"),
            Interval(), null, [Provenance]), existing.RequiredAssurance, existing.ProofLifetime,
            existing.MethodAssurances.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
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
