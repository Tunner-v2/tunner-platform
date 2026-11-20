using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Identity.Persistence;
using Tunner.Platform.Policies;

// Explicit synthetic configuration confined to the Development-only browser-testing executable.
// Production values and eligibility remain Security/Compliance owner inputs under P2-019.
internal static class BrowserTestPasskeys
{
    internal const string EnrollmentPolicyVersion = "browser-test-passkey-enrollment-v2";
    internal const string RevocationPolicyVersion = "browser-test-passkey-revocation-v2";
    private const string FixtureAssurance = "test-only-passkey-assurance";
    private const string OperationAssurance = "test-only-sensitive-operation-assurance";
    private static readonly int[] Algorithms = [-7, -257];
    internal static void Configure(IServiceCollection services, string origin)
    {
        var policy = new PasskeyPolicy("browser-test-passkeys-v2", "localhost", origin,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(20),
            PasskeyRevocationSessionEffect.PreserveExistingSessions, "required", Algorithms, 32, 131072, 10, 5,
            EnrollmentPolicyVersion, RevocationPolicyVersion, "test-only-processing", "test-only-retention", "required", "none",
            new Dictionary<string, string>
            {
                [PasskeyPolicy.AssuranceKey(false, false)] = FixtureAssurance,
                [PasskeyPolicy.AssuranceKey(true, false)] = FixtureAssurance,
                [PasskeyPolicy.AssuranceKey(true, true)] = FixtureAssurance
            });
        var now = DateTimeOffset.UtcNow;
        var metadata = new PolicyVersionCandidate<string>(policy.Version, "ACTIVE", "PASSKEY",
            new EffectivePolicyInterval(now.AddDays(-1), true, null, false), null, ["SYNTHETIC-LOCAL-TEST-ONLY"]);
        services.AddSingleton<IPasskeyPolicySource>(new PasskeyPolicyCatalog([new(metadata, policy)]));
        services.AddSingleton<IPasskeyRevocationEligibilitySource, BrowserTestPasskeyEligibility>();
        services.AddSingleton<IPasskeyPresentationSource, BrowserTestPasskeyPresentation>();
    }
    internal static IEnumerable<AssurancePolicyVersion> AssurancePolicies()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (version, operation) in new[] { (EnrollmentPolicyVersion, "REGISTER_PASSKEY"), (RevocationPolicyVersion, "REVOKE_PASSKEY") })
            yield return new(new(version, "ACTIVE", new(operation, "USER_BFF"),
                new EffectivePolicyInterval(now.AddDays(-1), true, null, false), null, ["SYNTHETIC-LOCAL-TEST-ONLY"]),
                OperationAssurance, TimeSpan.FromMinutes(1), new Dictionary<string, string>
                {
                    ["pwd"] = OperationAssurance,
                    [PasskeyPolicy.MethodKey(false, false)] = OperationAssurance,
                    [PasskeyPolicy.MethodKey(true, false)] = OperationAssurance,
                    [PasskeyPolicy.MethodKey(true, true)] = OperationAssurance
                });
    }
}
internal sealed class BrowserTestPasskeyEligibility : IPasskeyRevocationEligibilitySource
{
    public ValueTask<PasskeyRevocationEligibilityDecision> RequireAsync(PasskeyRevocationEligibilityRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new PasskeyRevocationEligibilityDecision(request.AccountId, request.AccountVersion, request.CredentialReference,
            request.CredentialVersion, request.AlternativeMethodsDigest, request.HasPassword || request.RemainingActivePasskeys > 0,
            "browser-test-passkey-alternatives-v1", "SYNTHETIC-LOCAL-TEST-ONLY",
            new EffectivePolicyInterval(request.EffectiveAt.AddMinutes(-1), true, request.EffectiveAt.AddMinutes(1), false)));
}
internal sealed class BrowserTestPasskeyPresentation : IPasskeyPresentationSource
{
    public PasskeyPresentation RequirePublished(PasskeyPolicy policy)
    {
        // The separate Development-only host labels this draft copy as synthetic, never a production publication.
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "browser-test-passkey-content.json")));
        return new("en", source.RootElement.GetProperty("content").EnumerateObject().Select(p =>
            new RegistrationPublishedText(p.Name, "synthetic-local-test", "synthetic-local-test-v1", "SYNTHETIC-LOCAL-TEST-ONLY", "en", "PUBLISHED", p.Value.GetString()!)));
    }
}
