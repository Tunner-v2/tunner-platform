using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Admin.Bff;
using Tunner.Identity.Persistence;
using Tunner.Platform.Policies;

// Proposed isolated fixture only. No production authority, runtime source activation or User policy replacement.
internal sealed class BrowserTestIdentityConfiguration : IAdminIdentityConfigurationSessionSource,
    IAdminIdentityConfigurationAuthorizationSource, IAdminIdentityConfigurationHttpProfileSource,
    IIdentityConfigurationGovernanceSource, IIdentityConfigurationBrowseBudgetSource
{
    internal const string Author = "fixture-security-author", Approver = "fixture-security-approver", Publisher = "fixture-security-publisher";
    internal const string Evidence = "fixture-identity-admin-evidence";
    internal const string ChromeScope = "synthetic.local.admin.identity.configuration";
    private const string Variant = "synthetic-local-en";
    private static readonly EffectivePolicyInterval Interval = new(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), true, null, false);
    private readonly BrowserTestGovernedContent owner;
    private readonly IdentityConfigurationScope[] scopes;
    private readonly RecoveryPasswordPolicyCatalog recoveryPolicies;

    internal BrowserTestIdentityConfiguration(BrowserTestGovernedContent owner, string authenticationVersion, string userOrigin)
    {
        this.owner = owner;
        // Same explicit synthetic policy inputs as PublishedRecoveryPasswordPresentationTests.Fixture;
        // only the isolated User origin and the existing persistent-fixture interval are rebound.
        var recovery = new RecoveryPasswordPolicy("fixture-published-recovery-v1", userOrigin, "fixture-repair-password", 15, 128, 10_000,
            RecoveryPasswordSessionEffect.InvalidateExistingTunnerSessions, false, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5), 32, 100, 100, ["ACCESS_RESTORATION", "NOTIFICATION_DELIVERY"], "fixture-processing", "fixture-retention");
        recoveryPolicies = new([new(new(recovery.Version, "ACTIVE", "ACCOUNT_RECOVERY_PASSWORD", Interval, null, ["SYNTHETIC-ONLY"]), recovery)]);
        // Explicit fixture workspace inputs follow the existing typed Admin/browser configuration fixtures.
        // These references do not activate the corresponding User workflow or select candidate policy values.
        scopes = [new(IdentityConfigurationFamily.Authentication),
            new(IdentityConfigurationFamily.Assurance, new("fixture-security-operation", "fixture-user-context")),
            new(IdentityConfigurationFamily.AuthenticationContent, AuthenticationPolicyVersion: authenticationVersion),
            new(IdentityConfigurationFamily.Registration), new(IdentityConfigurationFamily.Totp),
            new(IdentityConfigurationFamily.RegistrationContent, RegistrationPolicyVersion: "fixture-registration-v1"),
            new(IdentityConfigurationFamily.Passkey),
            new(IdentityConfigurationFamily.TotpContent, TotpPolicyVersion: "fixture-totp-policy-v1"),
            new(IdentityConfigurationFamily.PasskeyContent, PasskeyPolicyVersion: "fixture-passkey-policy-v1"),
            new(IdentityConfigurationFamily.StepUpContent, new("fixture.step-up-operation", "fixture.step-up-context"), AssurancePolicyVersion: "fixture-assurance-policy-v1"),
            new(IdentityConfigurationFamily.RecoveryCodes), new(IdentityConfigurationFamily.SessionListing),
            new(IdentityConfigurationFamily.SessionListingContent, AuthenticationPolicyVersion: authenticationVersion),
            new(IdentityConfigurationFamily.SessionRevocationContent, AuthenticationPolicyVersion: authenticationVersion),
            new(IdentityConfigurationFamily.SessionBulkRevocationContent, AuthenticationPolicyVersion: authenticationVersion),
            new(IdentityConfigurationFamily.RecoveryPasswordContent, RecoveryPasswordPolicyVersion: recovery.Version)];
    }

    internal static bool IsActor(string value) => value is Author or Approver or Publisher;
    internal static string? Actor(string? selected) => selected switch
    { "identity-author" => Author, "identity-approver" => Approver, "identity-publisher" => Publisher, _ => null };

    internal void Configure(IServiceCollection services, string connectionString)
    {
        owner.RequireIdentityConfiguration();
        services.AddTunnerIdentityPersistence(connectionString);
        services.AddScoped<IdentityRuntimeConfigurationApplication>();
        services.AddSingleton<IAdminIdentityConfigurationSessionSource>(this);
        services.AddSingleton<IAdminIdentityConfigurationAuthorizationSource>(this);
        services.AddSingleton<IAdminIdentityConfigurationHttpProfileSource>(this);
        services.AddSingleton<IIdentityConfigurationGovernanceSource>(this);
        services.AddSingleton<IIdentityConfigurationBrowseBudgetSource>(this);
        // Admin-only owner facts for family15. Do not install runtime persisted-policy adapters or Recovery application/routes.
        services.AddSingleton<IRecoveryPasswordPolicySource>(recoveryPolicies);
    }

    public async ValueTask<AdminIdentityConfigurationSession?> RequireCurrentAsync(ClaimsPrincipal principal, DateTimeOffset at, CancellationToken cancellationToken)
    {
        owner.RequireIdentityConfiguration();
        var session = await owner.RequireIdentitySessionAsync(principal, at, cancellationToken).ConfigureAwait(false);
        return session is null ? null : new(session.ActorReference, session.SessionReference, IdentityConfigurationActorKind.Human, session.QualificationReference, session.EffectiveInterval);
    }

    public ValueTask<IdentityConfigurationAuthorizationDecision> AuthorizeAsync(AdminIdentityConfigurationSession session,
        IdentityConfigurationAuthorizationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); RequireScope(request.Scope, DateTimeOffset.UtcNow);
        var common = request.Operation is IdentityConfigurationOperation.Browse or IdentityConfigurationOperation.Preview or IdentityConfigurationOperation.History;
        var allowed = session.ActorKind == IdentityConfigurationActorKind.Human && session.QualificationReference == Evidence && IsActor(session.ActorReference) &&
            session.EffectiveInterval.Contains(DateTimeOffset.UtcNow) && (common || (session.ActorReference switch
            {
                Author => request.Operation is IdentityConfigurationOperation.CreateDraft or IdentityConfigurationOperation.UpdateDraft or IdentityConfigurationOperation.Validate,
                Approver => request.Operation == IdentityConfigurationOperation.Approve,
                Publisher => request.Operation is IdentityConfigurationOperation.Publish or IdentityConfigurationOperation.Rollback,
                _ => false
            }));
        return ValueTask.FromResult(new IdentityConfigurationAuthorizationDecision(request.Digest, allowed, session.ActorReference, session.ActorKind,
            "fixture-identity-admin", "fixture-identity-admin-policy", Evidence, Interval));
    }

    public AdminIdentityConfigurationHttpProfile RequireEffective(DateTimeOffset at)
    {
        owner.RequireIdentityConfiguration(); if (!Interval.Contains(at)) throw new IdentityConfigurationUnavailableException();
        return new(new("fixture-config-http", "ACTIVE", AdminIdentityConfigurationHttpProfile.Scope, Interval, null, ["SYNTHETIC-ONLY"]), Array.AsReadOnly(scopes), 65536)
        { Presentation = new(ChromeScope, Variant, Interval) };
    }

    IdentityConfigurationBrowseBudget IIdentityConfigurationBrowseBudgetSource.RequireEffective(IdentityConfigurationScope scope, DateTimeOffset effectiveAt)
    {
        RequireScope(scope, effectiveAt);
        return new(new("fixture-config-browse", "ACTIVE", scope, Interval, null, ["SYNTHETIC-ONLY"]), 5, 20, 64);
    }

    IdentityConfigurationGovernance IIdentityConfigurationGovernanceSource.RequireEffective(IdentityConfigurationScope scope, DateTimeOffset effectiveAt)
    {
        RequireScope(scope, effectiveAt);
        return new(new("fixture-identity-governance-v1", "ACTIVE", scope, Interval, null, ["fixture-security-governance-approval"]), "fixture-security-governance-evidence",
            "fixture-processing", "fixture-retention", "fixture-audit-retention", "RESTRICTED_SECURITY", 32768, true, true, true,
            Enum.GetValues<IdentityConfigurationOperation>().ToDictionary(operation => operation, operation => new IdentityConfigurationEffect(
                "fixture-identity-config-" + operation, "fixture-complete", "fixture/identity", "1.0",
                operation is IdentityConfigurationOperation.Preview or IdentityConfigurationOperation.History ? null : "fixture.identity.config." + operation,
                operation is IdentityConfigurationOperation.Preview or IdentityConfigurationOperation.History ? null : "1")));
    }

    private void RequireScope(IdentityConfigurationScope scope, DateTimeOffset at)
    {
        owner.RequireIdentityConfiguration();
        if (!Interval.Contains(at) || !scopes.Contains(scope)) throw new IdentityConfigurationUnavailableException();
    }
}
