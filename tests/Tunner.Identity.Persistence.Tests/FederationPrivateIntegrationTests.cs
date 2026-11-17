using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Tunner.Accounts;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

/// <summary>Finite private owner transactions only. Never output a grant, ticket, cookie, code, seed or response.
/// Synthetic terminal protocol evidence exercises Identity ownership, not provider or native OIDC verification.</summary>
// No xUnit/default-fixture dispatch. Only the fixed private registry may supply an admitted finite owner.
internal sealed class FederationPrivateIntegrationTests(IdentityPostgresFixture fixture)
{
    private const string Public = "SYNTHETIC-TEST-ONLY";
    private const string Subject = "SyntheticOpaqueSubject";
    private static readonly string[] FingerprintChanges = ["cookie", "original-cookie", "session", "browser", "origin", "key"];
    private static readonly string[] RegistrationDenials = ["missing", "unqualified", "microsoft", "expired", "browser", "version", "verified-contact", "pending-contact", "external-owner"];
    private static readonly string[] RegistrationDrifts = ["profile", "notice", "source"];

    private sealed class Profile : IFederationProfileSource
    {
        internal FederationProfile Values { get; set; } = new("fixture-federation-v1", FederationProviderKind.Google,
            Origin, "fixture-in-process-no-egress", "fixture-stable-namespace", "https://issuer.example.test", "fixture-client", [],
            "fixture-explicit-issuer-map", 128, 128, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3), 4,
            "/authentication/sign-in", ["/authentication/sign-in"], "fixture-sign-in-admission", "fixture-federation-primary",
            "fixture-federation-assurance-map", "fixture-session-use", "fixture-processing", "fixture-retention");
        internal Action? BeforeRead { get; set; }
        public FederationProfileVersion RequireCurrent(FederationProviderKind provider, DateTimeOffset effectiveAt)
        {
            BeforeRead?.Invoke();
            var interval = new EffectivePolicyInterval(new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), true,
                new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero), false);
            if (provider != Values.Provider || !interval.Contains(effectiveAt)) throw new InvalidOperationException("FINITE_PROFILE_UNAVAILABLE");
            return new(new(Values.Version, "ACTIVE", provider, interval, null, [Public]), Values);
        }
        public override string ToString() => nameof(Profile);
    }

    private static TotpTests.Profile TotpProfile()
    {
        var profile = new TotpTests.Profile();
        profile.Policy = profile.Policy with { MfaAssurances = new Dictionary<string, string>(StringComparer.Ordinal)
            { ["pwd"] = "fixture-mfa", ["passkey"] = "fixture-mfa", ["federation"] = "fixture-federation-mfa" } };
        return profile;
    }

    private static Task<AuthenticationTestHost> Host(IdentityTestDatabase db, Profile federation, TotpTests.Profile totp,
        Action<IServiceCollection>? configure = null) =>
        TotpTests.StartTotp(db, totp, services =>
        {
            services.AddSingleton<IFederationProfileSource>(federation);
            services.AddScoped<FederationAttemptApplication>();
            configure?.Invoke(services);
        });

    // The real registration owner is composed into the existing host, without activating another HTTP host or transport.
    private sealed class RegistrationInputs : IRegistrationPolicySource, IRegistrationPasswordBlocklist, IRegistrationNotificationDispatcher
    {
        internal RegistrationPolicy Policy { get; set; } = new("fixture-registration-v2", "PDR-0011", "fixture-processing", "fixture-retention",
            "fixture-terms", "fixture-privacy", "fixture-verification-template", "fixture-existing-template", "PASSWORD_AND_VERIFIED_EMAIL",
            15, 128, 10_000, 6, TimeSpan.FromMinutes(5), 2, TimeSpan.FromSeconds(30), 2, TimeSpan.FromMinutes(1),
            1000, 1000, 1000, 1000, TimeSpan.FromMinutes(30), TimeSpan.FromHours(1));
        internal Action? BeforeRead { get; set; }
        internal int Notifications { get; private set; }
        public RegistrationPolicy RequireEffective(DateTimeOffset effectiveAt)
        {
            BeforeRead?.Invoke();
            var version = new RegistrationPolicyVersion(new(Policy.Version, "ACTIVE", "REGISTRATION",
                new EffectivePolicyInterval(new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), true, new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero), false),
                null, [Public]), Policy);
            return new RegistrationPolicyCatalog([version]).RequireEffective(effectiveAt);
        }
        public ValueTask<bool> IsBlockedAsync(string password, CancellationToken cancellationToken) => ValueTask.FromResult(false);
        public Task QueueAsync(RegistrationNotification notification, CancellationToken cancellationToken)
        { Notifications++; return Task.CompletedTask; }
        public override string ToString() => nameof(RegistrationInputs);
    }

    private static Task<AuthenticationTestHost> RegistrationHost(IdentityTestDatabase db, Profile profile, RegistrationInputs input)
    {
        var age = new FixtureAccountAgePolicies();
        profile.Values = profile.Values with { Registration = new("fixture-qualified-registration-v1", input.Policy.Version, "fixture-google-verified-email-map") };
        return Host(db, profile, TotpProfile(), services => services.AddScoped<IRegistrationApplication>(provider =>
        {
            var context = provider.GetRequiredService<IdentityPersistenceDbContext>();
            var protection = provider.GetRequiredService<IDataProtectionProvider>(); var clock = provider.GetRequiredService<TimeProvider>();
            return new RegistrationApplication(context, input, input, protection, input, clock,
                new AccountPersonalInformationApplication(context, age, age, protection, clock), [profile]);
        }));
    }

    private static FederationCurrentBrowser RegistrationAuthenticationBrowser(AuthenticationTestHost host)
    {
        var browser = Browser(host);
        return new(browser.Caller with { BrowserDigest = AuthenticationIdentity.Digest(Guid.NewGuid().ToString("N")) }, browser.BrowserKey,
            browser.BrowserExpiresAt, browser.AuthenticationPolicyVersion, browser.OriginalCookieDigest, browser.OriginalPreviousSessionId, browser.ActualCookieDigest);
    }
    private static FederationRegistrationBrowser RegistrationBrowser(AuthenticationTestHost host) => new(
        new(AuthenticationIdentity.Digest(Guid.NewGuid().ToString("N")), new('E', 64)), Guid.NewGuid(), host.Clock.GetUtcNow().AddMinutes(30));
    private sealed record IntakePending(Pending Original, long Version)
    { public override string ToString() => nameof(IntakePending); }

    private static async Task<IntakePending> RegistrationVerified(AuthenticationTestHost host, Profile profile,
        FederationCurrentBrowser browser, FederationRegistrationBrowser registration, string? email = "applicant@example.test", bool qualified = true)
    {
        await using var scope = host.Services.CreateAsyncScope(); var app = scope.ServiceProvider.GetRequiredService<FederationAttemptApplication>();
        var microsoft = profile.Values.Provider == FederationProviderKind.Microsoft;
        var started = await app.StartAsync(browser, registration.IdempotencyKey, profile.Values.Provider,
            FederationAttemptPurpose.SignIn, false, CancellationToken.None, microsoft ? null : registration);
        Assert.True(started.ErrorCode is null && started.Row is { State: "PREPARED" });
        var pending = new Pending(started.Row!.AttemptId, started.Row.RequestId);
        Assert.True(await app.AdmitChallengeAsync(browser, pending.AttemptId, CancellationToken.None,
            registrationBrowser: microsoft ? null : registration) is not null);
        // This is a synthetic terminal adapter projection, not a JWT/provider validation claim. Unverified native contact has no qualified projection.
        var verified = await app.RecordVerifiedAsync(new(profile.Values.Provider, profile.Values.Version, pending.AttemptId,
            profile.Values.Issuer, Subject, profile.Values.ClientId, providerVerifiedEmail: email,
            providerEmailMappingEvidenceReference: qualified ? profile.Values.Registration!.ProviderVerifiedEmailMappingEvidenceReference : null), CancellationToken.None);
        var original = await app.ReadOriginalAsync(browser, pending.AttemptId, CancellationToken.None, registration);
        Assert.True(original.Row is not null && verified.Verified == (email is not null && qualified && !microsoft));
        return new(pending, original.Row!.Version);
    }
    private static FederationRegistrationAcknowledgement Notice(RegistrationInputs input, IntakePending pending, FederationRegistrationBrowser registration) =>
        new(pending.Original.AttemptId, pending.Original.RequestId, registration.IdempotencyKey, pending.Version, input.Policy.TermsVersion, input.Policy.PrivacyVersion, true);
    private static async Task<FederationAttemptResult> CompleteRegistration(AuthenticationTestHost host, FederationCurrentBrowser browser,
        FederationRegistrationBrowser registration, IntakePending pending, FederationRegistrationAcknowledgement? notice)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FederationAttemptApplication>().CompleteSignInAsync(browser,
            pending.Original.AttemptId, CancellationToken.None, registration, notice);
    }
    private sealed record CanonicalCounts(int Accounts, int Users, int Contacts, int Links, int Registrations, int Events, int Sessions, int Mfa);
    private static async Task<CanonicalCounts> Counts(IdentityTestDatabase db)
    {
        await using var read = db.Context();
        return new(await read.Accounts.CountAsync(), await read.Users.CountAsync(), await read.VerifiedContacts.CountAsync(),
            await read.ExternalIdentityLinks.CountAsync(), await read.Set<RegistrationOperationRow>().CountAsync(), await read.Set<OutboxMessage>().CountAsync(),
            await read.Set<AuthenticationSessionRow>().CountAsync(), await read.Set<TotpPendingAuthenticationRow>().CountAsync());
    }

    private static FederationCurrentBrowser Browser(AuthenticationTestHost host, Guid? previous = null) =>
        new(new(new('A', 64), new('B', 64), Origin, previous), Guid.NewGuid(), host.Clock.GetUtcNow().AddMinutes(20),
            host.Policies.Policy.Version, new('C', 64), previous, new('C', 64));

    private static async Task<Guid> SeedLink(IdentityTestDatabase db, string account, Profile profile)
    {
        await using var context = db.Context(); var values = profile.Values; var id = Guid.NewGuid();
        context.ExternalIdentityLinks.Add(new ExternalIdentityLinkRow
        {
            LinkId = id, AccountId = account, Provider = values.StableNamespaceId, Subject = Subject, Version = 1,
            EvidenceReference = "fixture-original-qualified-link", LinkState = "ACTIVE", QualifiedClientId = values.ClientId,
            QualificationProfileVersion = values.Version, IssuerMappingEvidenceReference = values.IssuerMappingEvidenceReference,
            QualificationEvidenceReference = "fixture-explicit-link-qualification"
        });
        await context.SaveChangesAsync(); return id;
    }

    private sealed record Pending(Guid AttemptId, Guid RequestId)
    { public override string ToString() => nameof(Pending); }

    private static FederationProtocolEvidence Evidence(Profile profile, Pending pending) =>
        new(profile.Values.Provider, profile.Values.Version, pending.AttemptId, profile.Values.Issuer, Subject, profile.Values.ClientId);

    private static async Task<Pending> Verified(AuthenticationTestHost host, Profile profile, FederationCurrentBrowser browser)
    {
        await using var scope = host.Services.CreateAsyncScope(); var app = scope.ServiceProvider.GetRequiredService<FederationAttemptApplication>();
        var request = Guid.NewGuid(); var started = await app.StartAsync(browser, request, profile.Values.Provider,
            FederationAttemptPurpose.SignIn, false, CancellationToken.None);
        Assert.True(started.ErrorCode is null && started.Row is { State: "PREPARED" } && started.Row.RequestId == request);
        var pending = new Pending(started.Row!.AttemptId, request);
        var admitted = await app.AdmitChallengeAsync(browser, pending.AttemptId, CancellationToken.None);
        Assert.True(admitted is not null && admitted.AttemptId == pending.AttemptId.ToString("N"));
        var verified = await app.RecordVerifiedAsync(Evidence(profile, pending), CancellationToken.None);
        Assert.True(verified.Verified && verified.Continuation == profile.Values.ContinuationPath);
        return pending;
    }

    private static async Task<FederationAttemptResult> Complete(AuthenticationTestHost host, FederationCurrentBrowser browser, Pending pending)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FederationAttemptApplication>().CompleteSignInAsync(browser, pending.AttemptId, CancellationToken.None);
    }

    private static async Task<FederationAttemptResult> Original(AuthenticationTestHost host, FederationCurrentBrowser browser, Pending pending)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FederationAttemptApplication>().ReadOriginalAsync(browser, pending.AttemptId, CancellationToken.None);
    }

    private static Guid SessionId(FederationAttemptResult result)
    {
        Assert.True(result.ErrorCode is null && result.Authentication is { ErrorCode: null, Grant: not null, PendingMfa: null });
        var id = AuthenticationIdentity.SessionId(result.Authentication!.Grant!.Ticket.Principal);
        Assert.True(id is not null && id != Guid.Empty); return id!.Value;
    }

    public async Task LinkedSignInCommitsOneOriginalSessionAndNeverReacceptsCallback()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile();
        await using var host = await Host(db, profile, TotpProfile()); var account = await host.SeedAsync(db);
        var link = await SeedLink(db, account, profile); var browser = Browser(host); var pending = await Verified(host, profile, browser);
        await using (var before = db.Context()) Assert.True(!await before.Set<AuthenticationSessionRow>().AnyAsync());
        var results = await Task.WhenAll(Complete(host, browser, pending), Complete(host, browser, pending));
        var session = SessionId(results[0]); Assert.True(SessionId(results[1]) == session);
        Assert.True(results[0].Authentication!.Grant!.Key == results[1].Authentication!.Grant!.Key);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var replay = await scope.ServiceProvider.GetRequiredService<FederationAttemptApplication>().RecordVerifiedAsync(Evidence(profile, pending), CancellationToken.None);
            Assert.True(!replay.Verified);
        }
        // A protocol/profile version change alone does not select a session revocation effect.
        profile.Values = profile.Values with { Version = "fixture-federation-v2" };
        Assert.True(SessionId(await Original(host, browser, pending)) == session);
        await using var read = db.Context(); var attempt = await read.Set<FederationAttemptRow>().SingleAsync();
        var row = await read.Set<AuthenticationSessionRow>().SingleAsync(); var owner = await read.ExternalIdentityLinks.SingleAsync();
        Assert.True(attempt.State == "COMPLETED" && attempt.Version == 4 && attempt.ResultSessionId == session && attempt.PendingMfaId is null);
        Assert.True(row.AccountId == account && row.SessionId == session && row.AuthenticationMethods == "federation" && row.State == "ACTIVE");
        Assert.True(row.FederationAttemptId == pending.AttemptId && row.FederationLinkId == link && row.FederationLinkVersion == 1 &&
            row.FederationProvider == "Google" && row.FederationNamespace == owner.Provider && row.FederationClientId == owner.QualifiedClientId &&
            row.FederationIssuer == profile.Values.Issuer && row.FederationProfileVersion == "fixture-federation-v1" &&
            row.FederationIssuerMappingEvidenceReference == owner.IssuerMappingEvidenceReference &&
            row.FederationQualificationProfileVersion == owner.QualificationProfileVersion && row.FederationQualificationEvidenceReference == owner.QualificationEvidenceReference &&
            row.FederationSignInAdmissionPolicyVersion == profile.Values.SignInAdmissionPolicyVersion &&
            row.FederationAssuranceMappingEvidenceReference == profile.Values.AssuranceMappingEvidenceReference &&
            row.FederationSessionUsePolicyVersion == profile.Values.SessionUsePolicyVersion && row.FederationPrimaryAssurance == profile.Values.PrimaryAssurance);
        Assert.True(owner.AccountId == account && owner.LinkId == link && owner.Version == 1 && owner.Subject == Subject);
        Assert.True(await read.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionAuthenticated") == 1);
    }

    public async Task RequiredTotpDefersSessionAndRecoversOnlyOriginalCompositeResult()
    {
        var checkpoint = new RecoveryCodeFirstCaseCheckpoint { Phase = RecoveryCodeFirstCasePhase.FEDERATION_MFA };
        try
        {
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_DATABASE);
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); var totp = TotpProfile();
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_HOST);
        await using var host = await Host(db, profile, totp);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_SEED);
        var account = await host.SeedAsync(db);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_LINK);
        await SeedLink(db, account, profile);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_LOGIN);
        var cookies = await TotpTests.Login(host);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_ENROLL);
        var factor = await TotpTests.Enroll(host, totp, cookies); totp.RequireMfa = true;
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_BASELINE);
        int priorSessions; int priorCodes;
        await using (var baseline = db.Context())
        { priorSessions = await baseline.Set<AuthenticationSessionRow>().CountAsync(); priorCodes = await baseline.Set<TotpUsedCodeRow>().CountAsync(); }
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_VERIFIED);
        var browser = Browser(host); var pending = await Verified(host, profile, browser);
        // The first completion response is deliberately ignored; recover through the actual original-result owner.
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_PRIMARY_COMPLETE);
        _ = await Complete(host, browser, pending);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_PENDING_ORIGINAL);
        var recovered = await Original(host, browser, pending);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_PENDING_RESULT);
        Assert.True(recovered.ErrorCode is null && recovered.Authentication is { ErrorCode: null, Grant: null, PendingMfa: not null });
        var mfaId = recovered.Authentication!.PendingMfa!.PendingId;
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_PENDING_ROWS);
        await using (var read = db.Context())
        {
            var attempt = await read.Set<FederationAttemptRow>().SingleAsync(); var row = await read.Set<TotpPendingAuthenticationRow>().SingleAsync();
            Assert.True(await read.Set<AuthenticationSessionRow>().CountAsync() == priorSessions && attempt.PendingMfaId == mfaId && attempt.ResultSessionId is null);
            Assert.True(row.State == "PENDING" && row.AccountId == account && row.RequestId == pending.RequestId && row.PrimaryMethod == "federation" &&
                row.FederationAttemptId == pending.AttemptId && row.FederationLinkId == attempt.LinkId && row.FederationLinkVersion == attempt.LinkVersion &&
                row.FederationProvider == "Google" && row.FederationNamespace == profile.Values.StableNamespaceId && row.FederationClientId == profile.Values.ClientId &&
                row.FederationIssuer == profile.Values.Issuer && row.FederationProfileVersion == profile.Values.Version &&
                row.FederationIssuerMappingEvidenceReference == profile.Values.IssuerMappingEvidenceReference &&
                row.FederationQualificationProfileVersion == attempt.LinkQualificationProfileVersion &&
                row.FederationQualificationEvidenceReference == attempt.LinkQualificationEvidenceReference &&
                row.FederationSignInAdmissionPolicyVersion == profile.Values.SignInAdmissionPolicyVersion &&
                row.FederationAssuranceMappingEvidenceReference == profile.Values.AssuranceMappingEvidenceReference &&
                row.FederationSessionUsePolicyVersion == profile.Values.SessionUsePolicyVersion && row.PrimaryAssurance == profile.Values.PrimaryAssurance);
        }
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_CODE);
        var command = new TotpCodeCommand { RequestId = pending.RequestId, AuthenticatorReference = factor.Reference,
            Code = TotpTests.NativeCode(totp.Seed(factor.Reference), 1) };
        await using (var scope = host.Services.CreateAsyncScope())
        {
            checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_COMPLETE);
            var completed = await scope.ServiceProvider.GetRequiredService<AuthenticationApplication>().CompleteTotpMfaAsync(browser.Caller, mfaId,
                command, totp, scope.ServiceProvider.GetRequiredService<TotpNativeVerifier>(), CancellationToken.None);
            checkpoint.Mark(completed.ErrorCode switch
            {
                null => RecoveryCodeFirstCaseStage.FEDERATION_MFA_COMPLETE_RESULT,
                "CLOCK_OUT_OF_BOUNDS" => RecoveryCodeFirstCaseStage.FEDERATION_MFA_CLOCK_OUT_OF_BOUNDS,
                "CODE_REPLAYED" => RecoveryCodeFirstCaseStage.FEDERATION_MFA_CODE_REPLAYED,
                "VERIFICATION_BUDGET_EXHAUSTED" => RecoveryCodeFirstCaseStage.FEDERATION_MFA_VERIFICATION_BUDGET,
                "AUTHENTICATION_FAILED" => RecoveryCodeFirstCaseStage.FEDERATION_MFA_AUTHENTICATION_FAILED,
                "VERSION_CONFLICT" => RecoveryCodeFirstCaseStage.FEDERATION_MFA_VERSION_CONFLICT,
                "DEPENDENCY_UNAVAILABLE" => RecoveryCodeFirstCaseStage.FEDERATION_MFA_DEPENDENCY_UNAVAILABLE,
                "RATE_OR_ABUSE_LIMITED" => RecoveryCodeFirstCaseStage.FEDERATION_MFA_RATE_LIMITED,
                "VALIDATION_ERROR" => RecoveryCodeFirstCaseStage.FEDERATION_MFA_VALIDATION_ERROR,
                _ => RecoveryCodeFirstCaseStage.FEDERATION_MFA_OTHER_ERROR
            });
            Assert.True(completed.ErrorCode is null && completed.Grant is not null && completed.PendingMfa is null);
        }
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_ORIGINAL);
        var original = await Original(host, browser, pending);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_ORIGINAL_RESULT);
        var session = SessionId(original);
        await using (var retry = host.Services.CreateAsyncScope())
        {
            checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_RETRY);
            var repeated = await retry.ServiceProvider.GetRequiredService<AuthenticationApplication>().CompleteTotpMfaAsync(browser.Caller, mfaId,
                command, totp, retry.ServiceProvider.GetRequiredService<TotpNativeVerifier>(), CancellationToken.None);
            checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_RETRY_RESULT);
            Assert.True(repeated.ErrorCode is null && repeated.Grant is not null && AuthenticationIdentity.SessionId(repeated.Grant.Ticket.Principal) == session);
        }
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_FINAL_ROWS);
        await using var final = db.Context(); var saved = await final.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == session);
        var finalMfa = await final.Set<TotpPendingAuthenticationRow>().SingleAsync();
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_FINAL_COUNTS);
        Assert.True(await final.Set<AuthenticationSessionRow>().CountAsync() == priorSessions + 1 && await final.Set<TotpUsedCodeRow>().CountAsync() == priorCodes + 1);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_FINAL_PENDING);
        Assert.True(finalMfa.State == "COMPLETED" && finalMfa.ResultSessionId == session && finalMfa.AcceptedAuthenticatorReference == factor.Reference);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_FINAL_SESSION);
        Assert.True(saved.AuthenticationMethods == "federation+totp" && saved.Assurance == "fixture-federation-mfa" &&
            saved.FederationPrimaryAssurance == profile.Values.PrimaryAssurance && saved.FederationAttemptId == pending.AttemptId &&
            saved.TotpAuthenticatorReference == factor.Reference && saved.TotpGeneration == finalMfa.AcceptedGeneration);
        checkpoint.Mark(RecoveryCodeFirstCaseStage.FEDERATION_MFA_DISPOSAL);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw RecoveryCodeFirstCaseFailure.DatabaseFailure(error)
                ?? (checkpoint.Stage == RecoveryCodeFirstCaseStage.FEDERATION_MFA_HOST ? RecoveryCodeFirstCaseFailure.HostFailure(error) : null)
                ?? new RecoveryCodeFirstCaseFailure(checkpoint.Phase, checkpoint.Stage);
        }
    }

    public async Task FinalFederationAuthorityDriftRollsBackStagedOwnerResultAndPreviousSessionEffect()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile();
        await using var host = await Host(db, profile, TotpProfile()); var account = await host.SeedAsync(db); await SeedLink(db, account, profile);
        var browser = Browser(host); Guid previous;
        await using (var login = host.Services.CreateAsyncScope())
        {
            var signed = await login.ServiceProvider.GetRequiredService<AuthenticationApplication>().SignInAsync(browser.Caller, Command(Guid.NewGuid()), CancellationToken.None);
            Assert.True(signed.ErrorCode is null && signed.Grant is not null);
            var id = AuthenticationIdentity.SessionId(signed.Grant!.Ticket.Principal); Assert.True(id is not null); previous = id!.Value;
        }
        browser = new(browser.Caller with { PreviousSessionId = previous }, browser.BrowserKey, browser.BrowserExpiresAt,
            browser.AuthenticationPolicyVersion, browser.OriginalCookieDigest, previous, browser.ActualCookieDigest);
        var pending = await Verified(host, profile, browser); var observed = false; var sawPreviousEffect = false; var sawStagedEvent = false;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
            profile.BeforeRead = () =>
            {
                if (observed || !context.ChangeTracker.Entries<AuthenticationSessionRow>().Any(x => x.State == EntityState.Added && x.Entity.FederationAttemptId == pending.AttemptId)) return;
                observed = true;
                sawPreviousEffect = context.ChangeTracker.Entries<AuthenticationSessionRow>().Any(x => x.Entity.SessionId == previous && x.Entity.State == "REVOKED");
                sawStagedEvent = context.ChangeTracker.Entries<OutboxMessage>().Any(x => x.State == EntityState.Added && x.Entity.EventType == "TunnerSessionAuthenticated");
                profile.Values = profile.Values with { SignInAdmissionPolicyVersion = "fixture-withdrawn-admission-v2" };
            };
            try
            {
                var rejected = await scope.ServiceProvider.GetRequiredService<FederationAttemptApplication>().CompleteSignInAsync(browser, pending.AttemptId, CancellationToken.None);
                Assert.True(observed && sawPreviousEffect && sawStagedEvent && rejected.ErrorCode is not null && rejected.Authentication?.Grant is null);
            }
            finally { profile.BeforeRead = null; }
        }
        // These are staged owner mutations, not a claim that an INSERT preceded the final decision.
        await using var read = db.Context(); var attempt = await read.Set<FederationAttemptRow>().SingleAsync();
        var surviving = await read.Set<AuthenticationSessionRow>().SingleAsync();
        Assert.True(attempt.State == "REJECTED" && attempt.ResultSessionId is null && attempt.PendingMfaId is null);
        Assert.True(surviving.SessionId == previous && surviving.State == "ACTIVE" && surviving.Version == 1 && surviving.RevokedAt is null);
        Assert.True(!await read.Set<TotpPendingAuthenticationRow>().AnyAsync());
        Assert.True(await read.Set<OutboxMessage>().CountAsync(x => x.EventType == "TunnerSessionAuthenticated") == 1 &&
            !await read.Set<OutboxMessage>().AnyAsync(x => x.EventType == "TunnerSessionRevoked"));
        var original = await Original(host, browser, pending);
        Assert.True(original.ErrorCode is not null && original.Authentication?.Grant is null);
    }

    public async Task ChangedBrowserCookieCannotCompleteOrBorrowOriginalFederatedResult()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile();
        await using var host = await Host(db, profile, TotpProfile()); var account = await host.SeedAsync(db); await SeedLink(db, account, profile);
        var browser = Browser(host); var pending = await Verified(host, profile, browser);
        foreach (var change in FingerprintChanges)
        {
            var caller = browser.Caller with
            {
                PreviousSessionId = change == "session" ? Guid.NewGuid() : browser.Caller.PreviousSessionId,
                BrowserDigest = change == "browser" ? new('D', 64) : browser.Caller.BrowserDigest,
                Origin = change == "origin" ? "https://other.example.test" : browser.Caller.Origin
            };
            var altered = new FederationCurrentBrowser(caller, change == "key" ? Guid.NewGuid() : browser.BrowserKey, browser.BrowserExpiresAt,
                browser.AuthenticationPolicyVersion, change == "original-cookie" ? new('D', 64) : browser.OriginalCookieDigest,
                browser.OriginalPreviousSessionId, change is "cookie" or "original-cookie" ? new('D', 64) : browser.ActualCookieDigest);
            var rejected = await Complete(host, altered, pending); var hidden = await Original(host, altered, pending);
            Assert.True(rejected.ErrorCode is not null && rejected.Authentication?.Grant is null && hidden.ErrorCode is not null && hidden.Authentication?.Grant is null);
        }
        await using (var before = db.Context())
        {
            Assert.True(!await before.Set<AuthenticationSessionRow>().AnyAsync());
            Assert.True(await before.Set<FederationAttemptRow>().AllAsync(x => x.State == "VERIFIED" && x.Version == 3));
        }
        var session = SessionId(await Complete(host, browser, pending));
        var current = new FederationCurrentBrowser(browser.Caller with { PreviousSessionId = session }, browser.BrowserKey, browser.BrowserExpiresAt,
            browser.AuthenticationPolicyVersion, browser.OriginalCookieDigest, browser.OriginalPreviousSessionId, new('E', 64));
        Assert.True(SessionId(await Original(host, current, pending)) == session);
        var foreign = new FederationCurrentBrowser(current.Caller with { PreviousSessionId = Guid.NewGuid() }, current.BrowserKey, current.BrowserExpiresAt,
            current.AuthenticationPolicyVersion, current.OriginalCookieDigest, current.OriginalPreviousSessionId, current.ActualCookieDigest);
        var denied = await Original(host, foreign, pending); Assert.True(denied.ErrorCode is not null && denied.Authentication?.Grant is null);
        host.Clock.Advance(profile.Values.ResultRecoveryLifetime + TimeSpan.FromSeconds(1));
        var expired = await Original(host, current, pending); Assert.True(expired.ErrorCode is not null && expired.Authentication?.Grant is null);
        await using var final = db.Context();
        Assert.True(await final.Set<AuthenticationSessionRow>().CountAsync() == 1 &&
            await final.Set<FederationAttemptRow>().AllAsync(x => x.State == "COMPLETED" && x.ResultSessionId == session));
    }

    public async Task QualifiedGoogleRegistrationRequiresNoticesThenSharesPersonalInformationAndOriginalRecoveryWithoutSession()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); var input = new RegistrationInputs();
        await using var host = await RegistrationHost(db, profile, input); var browser = RegistrationAuthenticationBrowser(host); var registration = RegistrationBrowser(host);
        var pending = await RegistrationVerified(host, profile, browser, registration);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var descriptor = await scope.ServiceProvider.GetRequiredService<FederationAttemptApplication>().ReadRegistrationIntakeAsync(browser,
                registration, pending.Original.AttemptId, CancellationToken.None);
            Assert.True(descriptor is { Mode: "FEDERATED", Provider: "Google", CanContinue: true } && descriptor.AttemptId == pending.Original.AttemptId &&
                descriptor.FederationRequestId == pending.Original.RequestId && descriptor.RegistrationKey == registration.IdempotencyKey &&
                descriptor.ExpectedVersion == pending.Version && descriptor.VerifiedEmail == "applicant@example.test");
        }
        var requirements = await CompleteRegistration(host, browser, registration, pending, null);
        var deniedNotice = await CompleteRegistration(host, browser, registration, pending, Notice(input, pending, registration) with { AcknowledgedNotices = false });
        Assert.True(requirements.ErrorCode is null && requirements.Registration is null && requirements.Authentication is null && deniedNotice.ErrorCode is not null);
        Assert.True(await Counts(db) == new CanonicalCounts(0, 0, 0, 0, 0, 0, 0, 0));
        var result = await CompleteRegistration(host, browser, registration, pending, Notice(input, pending, registration));
        Assert.True(result.ErrorCode is null && result.Authentication is null && result.Row is { State: "COMPLETED", Resolution: "REGISTRATION_INTAKE" } &&
            result.Registration is { ErrorCode: null, NextAction: "PERSONAL_INFORMATION", PersonalInformation: not null });
        var operation = result.Registration!;
        Assert.True(operation.PersonalInformation!.VerifiedEmail == "applicant@example.test" && operation.AccountId is not null);
        await using (var read = db.Context())
        {
            var account = await read.Accounts.SingleAsync(); var user = await read.Users.SingleAsync(); var contact = await read.VerifiedContacts.SingleAsync();
            var lineage = await read.Set<RegistrationOperationRow>().SingleAsync(); var attempt = await read.Set<FederationAttemptRow>().SingleAsync();
            var link = await read.ExternalIdentityLinks.SingleAsync();
            Assert.True(account.State == AccountState.VerificationPending && user.PasswordHash is null && user.EmailConfirmed && contact.State == ContactVerificationState.Verified);
            Assert.True(lineage.Mode == "FEDERATED" && lineage.FederationAttemptId == pending.Original.AttemptId && lineage.FederationLinkId == link.LinkId &&
                lineage.FederationContactVersion == contact.Version && lineage.FederationVerifiedContactDigest == AuthenticationIdentity.Digest(contact.NormalizedEmail!) &&
                lineage.ProtectedCredential is null && lineage.ProtectedCode is null && lineage.ProtectedAddress is null && lineage.NormalizedEmail.Length == 0);
            Assert.True(attempt.ResultRegistrationOperationId == operation.OperationId && attempt.ResultSessionId is null && attempt.PendingMfaId is null &&
                attempt.ProtectedRegistrationEvidence is null && link.AccountId == account.AccountId && link.Subject == Subject && link.LinkState == "ACTIVE" && link.Version == 1);
            Assert.True(await read.Set<OutboxMessage>().CountAsync(x => x.EventType == "AccountRegistered") == 1 &&
                await read.Set<OutboxMessage>().CountAsync(x => x.EventType == "VerifiedContactChanged") == 1 &&
                !await read.Set<OutboxMessage>().AnyAsync(x => x.EventType == "AccountActivated"));
            Assert.True(!(await read.Set<OutboxMessage>().Select(x => x.Payload).ToArrayAsync()).Any(payload =>
                payload.Contains("applicant@example.test", StringComparison.Ordinal) || payload.Contains(Subject, StringComparison.Ordinal)));
        }
        var beforeReplay = await Counts(db);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var app = scope.ServiceProvider.GetRequiredService<FederationAttemptApplication>();
            var recovered = await app.ReadOriginalAsync(browser, pending.Original.AttemptId, CancellationToken.None, registration);
            Assert.True(recovered.ErrorCode is null && recovered.Authentication is null && recovered.Registration is { PersonalInformation: not null } restored &&
                restored.OperationId == operation.OperationId);
            var callback = await app.RecordVerifiedAsync(new(profile.Values.Provider, profile.Values.Version, pending.Original.AttemptId,
                profile.Values.Issuer, Subject, profile.Values.ClientId, providerVerifiedEmail: "applicant@example.test",
                providerEmailMappingEvidenceReference: profile.Values.Registration!.ProviderVerifiedEmailMappingEvidenceReference), CancellationToken.None);
            Assert.True(!callback.Verified);
        }
        var repeated = await CompleteRegistration(host, browser, registration, pending, Notice(input, pending, registration));
        Assert.True(repeated.ErrorCode is null && repeated.Registration?.OperationId == operation.OperationId && repeated.Authentication is null && await Counts(db) == beforeReplay);
        var personal = new CompletePersonalInformationCommand { OperationId = operation.OperationId, IdempotencyKey = Guid.NewGuid(), ExpectedVersion = operation.Version,
            DateOfBirth = new(2015, 4, 6), FullName = "Synthetic Person", DisplayName = "Synthetic" };
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var app = scope.ServiceProvider.GetRequiredService<IRegistrationApplication>();
            var activated = await app.CompletePersonalInformationAsync(registration.Caller, personal, CancellationToken.None);
            Assert.True(activated.ErrorCode is null && activated.Status == "SUCCEEDED" && activated.NextAction == "COMPLETE" &&
                activated.AgeCapabilities is { AgeClassification: "UNDER_18", AgeAllowsAccountLifecycle: true, IndependentCommercialEligible: false });
            Assert.True((await app.CompletePersonalInformationAsync(registration.Caller, personal, CancellationToken.None)).Status == "SUCCEEDED");
        }
        await using var final = db.Context();
        Assert.True(await final.Accounts.AllAsync(x => x.State == AccountState.Active) && await final.Users.AllAsync(x => x.PasswordHash == null && x.EmailConfirmed) &&
            await final.Set<OutboxMessage>().CountAsync(x => x.EventType == "AccountActivated") == 1 &&
            !await final.Set<AuthenticationSessionRow>().AnyAsync() && !await final.Set<TotpPendingAuthenticationRow>().AnyAsync() && input.Notifications == 0);
    }

    public async Task UnqualifiedExpiredForeignOrCollidingFederationIntakeCannotAdoptAccountOrIssueSession()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); var input = new RegistrationInputs();
        await using var host = await RegistrationHost(db, profile, input); var google = profile.Values; string? existingAccount = null;
        foreach (var denial in RegistrationDenials)
        {
            profile.Values = denial == "microsoft" ? google with { Provider = FederationProviderKind.Microsoft } : google;
            var browser = RegistrationAuthenticationBrowser(host); var registration = RegistrationBrowser(host); var email = "applicant@example.test";
            if (denial == "verified-contact") { existingAccount = await host.SeedAsync(db); email = "person@example.test"; }
            if (denial == "pending-contact")
            {
                email = "pending@example.test";
                await using var scope = host.Services.CreateAsyncScope();
                var passwordPending = await scope.ServiceProvider.GetRequiredService<IRegistrationApplication>().RegisterAsync(registration.Caller,
                    new RegisterAccountCommand { IdempotencyKey = Guid.NewGuid(), Email = email, Password = Password, PasswordConfirmation = Password,
                        TermsVersion = input.Policy.TermsVersion, PrivacyVersion = input.Policy.PrivacyVersion, AcknowledgedNotices = true }, CancellationToken.None);
                Assert.True(passwordPending.ErrorCode is null);
            }
            var pending = await RegistrationVerified(host, profile, browser, registration, denial == "missing" ? null : email, denial != "unqualified");
            Guid? existingLink = null;
            if (denial == "external-owner") existingLink = await SeedLink(db, existingAccount!, profile);
            var baseline = await Counts(db);
            if (denial == "expired") host.Clock.Advance(profile.Values.VerifiedLifetime + TimeSpan.FromSeconds(1));
            var notice = Notice(input, pending, registration);
            if (denial == "version") notice = notice with { ExpectedVersion = notice.ExpectedVersion + 1 };
            var current = denial == "browser" ? new FederationCurrentBrowser(browser.Caller, browser.BrowserKey, browser.BrowserExpiresAt,
                browser.AuthenticationPolicyVersion, browser.OriginalCookieDigest, browser.OriginalPreviousSessionId, new('F', 64)) : browser;
            var rejected = await CompleteRegistration(host, current, registration, pending, notice);
            Assert.True(rejected.ErrorCode is not null && rejected.Authentication is null && rejected.Registration is null && await Counts(db) == baseline);
            await using var read = db.Context();
            Assert.True(!await read.Set<RegistrationOperationRow>().AnyAsync(x => x.Mode == "FEDERATED") &&
                !await read.Set<AuthenticationSessionRow>().AnyAsync() && !await read.Set<TotpPendingAuthenticationRow>().AnyAsync());
            if (denial == "pending-contact")
                Assert.True(await read.Set<RegistrationOperationRow>().AllAsync(x => x.Mode == "PASSWORD" && x.ProtectedCredential != null && x.Stage == "VERIFY_CONTACT"));
            if (existingLink is { } link)
                Assert.True(await read.ExternalIdentityLinks.AnyAsync(x => x.LinkId == link && x.AccountId == existingAccount && x.Version == 1 && x.LinkState == "ACTIVE"));
        }
    }

    public async Task LateRegistrationProfileNoticeOrContactDriftRollsBackCanonicalIntakeAndRecoversOriginal()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); var input = new RegistrationInputs();
        await using var host = await RegistrationHost(db, profile, input); var originalProfile = profile.Values; var originalPolicy = input.Policy;
        foreach (var drift in RegistrationDrifts)
        {
            var browser = RegistrationAuthenticationBrowser(host); var registration = RegistrationBrowser(host);
            var pending = await RegistrationVerified(host, profile, browser, registration); var observed = false; var savedCanonical = false; var rejected = false;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
                void ChangeAtFinalWitness()
                {
                    if (observed || !context.ChangeTracker.Entries<FederationAttemptRow>().Any(entry => entry.Entity.AttemptId == pending.Original.AttemptId &&
                        entry.Entity.State == "COMPLETED" && entry.Entity.ResultRegistrationOperationId is not null)) return;
                    observed = true;
                    savedCanonical = context.ChangeTracker.Entries<AccountRow>().Any(entry => entry.State == EntityState.Unchanged) &&
                        context.ChangeTracker.Entries<OutboxMessage>().Any(entry => entry.Entity.EventType == "VerifiedContactChanged" && entry.State == EntityState.Unchanged);
                    if (drift == "profile") profile.Values = profile.Values with { Version = "fixture-withdrawn-registration-v2" };
                    else if (drift == "notice") input.Policy = input.Policy with { Version = "fixture-registration-v3", TermsVersion = "fixture-terms-v2" };
                    else
                    {
                        // Same-owner staged source changes are checked by the final pure witness; no production seam is added.
                        var contact = context.ChangeTracker.Entries<VerifiedContactRow>().Single().Entity;
                        contact.Version++; contact.EvidenceReference = "fixture-withdrawn-contact-source";
                    }
                }
                if (drift == "notice") input.BeforeRead = ChangeAtFinalWitness; else profile.BeforeRead = ChangeAtFinalWitness;
                try
                {
                    var result = await scope.ServiceProvider.GetRequiredService<FederationAttemptApplication>().CompleteSignInAsync(browser,
                        pending.Original.AttemptId, CancellationToken.None, registration, Notice(input, pending, registration));
                    rejected = result.ErrorCode is not null;
                }
                catch (InvalidOperationException) { rejected = true; }
                finally { profile.BeforeRead = null; input.BeforeRead = null; profile.Values = originalProfile; input.Policy = originalPolicy; }
            }
            Assert.True(observed && savedCanonical && rejected && await Counts(db) == new CanonicalCounts(0, 0, 0, 0, 0, 0, 0, 0));
            await using (var read = db.Context())
                Assert.True(await read.Set<FederationAttemptRow>().AnyAsync(x => x.AttemptId == pending.Original.AttemptId && x.State == "VERIFIED" &&
                    x.Version == pending.Version && x.ResultRegistrationOperationId == null && x.ProtectedRegistrationEvidence != null));
            await using var recovered = host.Services.CreateAsyncScope();
            var original = await recovered.ServiceProvider.GetRequiredService<FederationAttemptApplication>().ReadOriginalAsync(browser,
                pending.Original.AttemptId, CancellationToken.None, registration);
            Assert.True(original.ErrorCode is null && original.Row is { State: "VERIFIED", Resolution: "REGISTRATION_INTAKE" } && original.Registration is null && original.Authentication is null);
        }
    }
}
