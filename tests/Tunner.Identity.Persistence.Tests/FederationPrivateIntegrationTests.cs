using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    private static Task<AuthenticationTestHost> Host(IdentityTestDatabase db, Profile federation, TotpTests.Profile totp) =>
        TotpTests.StartTotp(db, totp, services =>
        {
            services.AddSingleton<IFederationProfileSource>(federation);
            services.AddScoped<FederationAttemptApplication>();
        });

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
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile(); var totp = TotpProfile();
        await using var host = await Host(db, profile, totp); var account = await host.SeedAsync(db); await SeedLink(db, account, profile);
        var factor = await TotpTests.Enroll(host, totp, await TotpTests.Login(host)); totp.RequireMfa = true;
        int priorSessions; int priorCodes;
        await using (var baseline = db.Context())
        { priorSessions = await baseline.Set<AuthenticationSessionRow>().CountAsync(); priorCodes = await baseline.Set<TotpUsedCodeRow>().CountAsync(); }
        var browser = Browser(host); var pending = await Verified(host, profile, browser);
        // The first completion response is deliberately ignored; recover through the actual original-result owner.
        _ = await Complete(host, browser, pending);
        var recovered = await Original(host, browser, pending);
        Assert.True(recovered.ErrorCode is null && recovered.Authentication is { ErrorCode: null, Grant: null, PendingMfa: not null });
        var mfaId = recovered.Authentication!.PendingMfa!.PendingId;
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
        var command = new TotpCodeCommand { RequestId = pending.RequestId, AuthenticatorReference = factor.Reference,
            Code = TotpTests.NativeCode(totp.Seed(factor.Reference), 1) };
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var completed = await scope.ServiceProvider.GetRequiredService<AuthenticationApplication>().CompleteTotpMfaAsync(browser.Caller, mfaId,
                command, totp, scope.ServiceProvider.GetRequiredService<TotpNativeVerifier>(), CancellationToken.None);
            Assert.True(completed.ErrorCode is null && completed.Grant is not null && completed.PendingMfa is null);
        }
        var original = await Original(host, browser, pending); var session = SessionId(original);
        await using (var retry = host.Services.CreateAsyncScope())
        {
            var repeated = await retry.ServiceProvider.GetRequiredService<AuthenticationApplication>().CompleteTotpMfaAsync(browser.Caller, mfaId,
                command, totp, retry.ServiceProvider.GetRequiredService<TotpNativeVerifier>(), CancellationToken.None);
            Assert.True(repeated.ErrorCode is null && repeated.Grant is not null && AuthenticationIdentity.SessionId(repeated.Grant.Ticket.Principal) == session);
        }
        await using var final = db.Context(); var saved = await final.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == session);
        var finalMfa = await final.Set<TotpPendingAuthenticationRow>().SingleAsync();
        Assert.True(await final.Set<AuthenticationSessionRow>().CountAsync() == priorSessions + 1 && await final.Set<TotpUsedCodeRow>().CountAsync() == priorCodes + 1);
        Assert.True(finalMfa.State == "COMPLETED" && finalMfa.ResultSessionId == session && finalMfa.AcceptedAuthenticatorReference == factor.Reference);
        Assert.True(saved.AuthenticationMethods == "federation+totp" && saved.Assurance == "fixture-federation-mfa" &&
            saved.FederationPrimaryAssurance == profile.Values.PrimaryAssurance && saved.FederationAttemptId == pending.AttemptId &&
            saved.TotpAuthenticatorReference == factor.Reference && saved.TotpGeneration == finalMfa.AcceptedGeneration);
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
}
