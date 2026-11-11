using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tunner.Identity.Persistence;
using Tunner.Platform.Policies;

/// <summary>Three literal finite test profiles. Never registered by the ordinary starter or production host.</summary>
internal sealed class BrowserTestTotp(string origin, string journeyId, string connectionString, ITotpSeedStore seedStore) :
    ITotpPolicySource, ITotpMutationEligibilitySource, ITotpMfaRequirementSource, ITotpPresentationSource
{
    internal const string OperationAssurance = "test-only-sensitive-operation-assurance";
    internal const string EnrollmentPolicyVersion = "finite-totp-enrollment-v1";
    internal const string DisablePolicyVersion = "finite-totp-disable-v1";
    internal const string ResetPolicyVersion = "finite-totp-reset-v1";
    internal const string BindingVersion = "finite-totp-owner-binding-v1";
    internal static readonly string[] JourneyIds = ["TOTP_ENROLLMENT_PASSWORD_MFA_PRESERVE_V1", "TOTP_OPERATION_PROOF_CAUSAL_REVOKE_V1", "TOTP_PASSKEY_MFA_RESET_V1"];
    private int denyOwner;
    private int dropDisclosure;
    private int dropActivation;
    private int dropMfa;
    internal string JourneyId { get; } = JourneyIds.Contains(journeyId, StringComparer.Ordinal) ? journeyId : throw new InvalidOperationException("FINITE_JOURNEY_INVALID");
    private TotpSessionEffect Effect => JourneyId == JourneyIds[0] ? TotpSessionEffect.PreserveExistingSessions : TotpSessionEffect.RevokeFactorSessions;

    public TotpPolicy RequireEffective(DateTimeOffset effectiveAt)
    {
        var policy = new TotpPolicy("finite-totp-v1", origin, "Tunner finite test", BrowserTestTotpOracle.NativeVersion, "SHA1", 6, 30, 2,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(15),
            10000, 32, 2, 16, Effect, Effect, TotpReplacementOrdering.IndependentEnrollment,
            TotpResetBehavior.RetireAffectedFactorThenSeparateEnrollment, EnrollmentPolicyVersion, DisablePolicyVersion, ResetPolicyVersion,
            OperationAssurance, new Dictionary<string, string> { ["pwd"] = "finite-test-mfa-assurance", ["passkey"] = "finite-test-mfa-assurance" },
            BindingVersion, "SYNTHETIC-FINITE-PROCESSING", "SYNTHETIC-FINITE-RETENTION");
        policy.Validate(); return policy;
    }
    internal void Configure(IServiceCollection services)
    {
        services.AddSingleton(this);
        services.AddSingleton<ITotpPolicySource>(this);
        services.AddSingleton<ITotpSeedStore>(seedStore);
        services.AddSingleton<ITotpMutationEligibilitySource>(this);
        services.AddSingleton<ITotpMfaRequirementSource>(this);
        services.AddSingleton<ITotpPresentationSource>(this);
    }
    internal static IEnumerable<AssurancePolicyVersion> AssurancePolicies()
    {
        foreach (var (version, operation) in new[] { (EnrollmentPolicyVersion, "ENROLL_TOTP"), (DisablePolicyVersion, "DISABLE_TOTP"), (ResetPolicyVersion, "RESET_TOTP") })
            yield return new(new(version, "ACTIVE", new(operation, "USER_BFF"), new(DateTimeOffset.UtcNow.AddDays(-1), true, null, false), null,
                ["SYNTHETIC-FINITE-TEST-ONLY"]), OperationAssurance, TimeSpan.FromMinutes(5), new Dictionary<string, string>
                {
                    ["pwd"] = OperationAssurance, ["totp"] = OperationAssurance,
                    [PasskeyPolicy.MethodKey(false, false)] = OperationAssurance,
                    [PasskeyPolicy.MethodKey(true, false)] = OperationAssurance,
                    [PasskeyPolicy.MethodKey(true, true)] = OperationAssurance
                });
    }
    public ValueTask<TotpMutationEligibilityDecision> RequireAsync(TotpMutationEligibilityRequest request, CancellationToken cancellationToken)
    {
        if (ConsumeOwnerFailure()) throw new InvalidOperationException("FINITE_OWNER_DEPENDENCY_FAILURE");
        return ValueTask.FromResult(new TotpMutationEligibilityDecision(request.AccountId, request.AccountVersion, request.AuthenticatorReference,
            request.AuthenticatorVersion, request.Operation, request.AlternativeMethodsDigest, true,
            "finite-test-eligibility-v1", "SYNTHETIC-FINITE-TEST-ONLY", Interval(request.EffectiveAt)));
    }
    public async ValueTask<TotpMfaRequirementDecision> RequireAsync(TotpMfaRequirementRequest request, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM identity.totp_authenticators WHERE account_id=@account AND state='ACTIVE')", connection);
        command.Parameters.AddWithValue("account", request.AccountId);
        var required = (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        return new(request.AccountId, request.AccountVersion, request.PrimaryMethod, request.PrimaryAssurance, required,
            "finite-test-mfa-requirement-v1", "SYNTHETIC-FINITE-TEST-ONLY", Interval(request.EffectiveAt), TimeSpan.FromMinutes(2));
    }
    public TotpPresentation RequirePublished(TotpPolicy policy) => new("en", PublishedContent());
    internal static IEnumerable<RegistrationPublishedText> PublishedContent(bool includeStepUp = false)
    {
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "browser-test-totp-content.json")));
        IEnumerable<JsonProperty> values = source.RootElement.GetProperty("content").EnumerateObject();
        if (includeStepUp) values = values.Concat(source.RootElement.GetProperty("step_up_content").EnumerateObject());
        return values.Select(property => new RegistrationPublishedText(property.Name,
            "synthetic-finite-test", "synthetic-finite-test-v1", "SYNTHETIC-FINITE-TEST-NOT-A-PUBLICATION", "en", "PUBLISHED", property.Value.GetString()!)).ToArray();
    }
    internal bool ConsumeOwnerFailure() => Interlocked.Exchange(ref denyOwner, 0) == 1;
    internal void Arm(string action)
    {
        switch (action)
        {
            case "DENY_OWNER_ONCE": Interlocked.Exchange(ref denyOwner, 1); break;
            case "DROP_DISCLOSURE_ONCE": Interlocked.Exchange(ref dropDisclosure, 1); break;
            case "DROP_ACTIVATION_ONCE": Interlocked.Exchange(ref dropActivation, 1); break;
            case "DROP_MFA_ONCE": Interlocked.Exchange(ref dropMfa, 1); break;
            case "READ_SAFE_COUNTS": break;
            default: throw new InvalidOperationException("FINITE_ACTION_INVALID");
        }
    }
    internal void MapResponseFaults(WebApplication app) => app.Use(async (context, next) =>
    {
        var drop = HttpMethods.IsPost(context.Request.Method) && (context.Request.Path.Value switch
        {
            "/account/totp/enrollment/disclose" => Interlocked.Exchange(ref dropDisclosure, 0) == 1,
            "/account/totp/enrollment/activate" => Interlocked.Exchange(ref dropActivation, 0) == 1,
            "/authentication/totp/complete" => Interlocked.Exchange(ref dropMfa, 0) == 1,
            _ => false
        });
        if (!drop) { await next(context).ConfigureAwait(false); return; }
        var original = context.Response.Body;
        try
        {
            context.Response.Body = Stream.Null;
            await next(context).ConfigureAwait(false); // Real handler and owner commit run to completion.
            context.Abort(); // No substituted response, buffering or persisted secret-bearing body.
        }
        finally { context.Response.Body = original; }
    });
    private static EffectivePolicyInterval Interval(DateTimeOffset now) => new(now.AddMinutes(-1), true, now.AddMinutes(1), false);
}
