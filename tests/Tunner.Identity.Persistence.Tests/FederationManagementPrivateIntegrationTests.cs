using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

/// <summary>Actual local owners + shared password-proof transactions. Synthetic protocol facts are not native/provider verification.</summary>
// Deliberately no xUnit/default Docker dispatch. The existing fixed private registry must supply an admitted finite owner.
internal sealed class FederationManagementPrivateIntegrationTests(IdentityPostgresFixture fixture)
{
    private const string Subject = "SYNTHETIC-TEST-ONLY-EXACT-SUBJECT";
    private const string CookieDigest = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
    private static readonly bool[] DriftModes = [false, true];
    private static readonly string[] FreshMethods = ["pwd", "mfa"];
    private static readonly string[] FreshRejections = ["subject", "link-version", "stale", "future", "late-profile"];
    private static EffectivePolicyInterval Interval => new(new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), true,
        new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero), false);
    private sealed class Profile : IFederationProfileSource, IFederationLinkPolicySource, IFederationUnlinkPostureSource, IAssurancePolicySource
    {
        internal FederationProfile Federation { get; set; } = new("fixture-federation-v1", FederationProviderKind.Google, Origin,
            "fixture-in-process-no-egress", "fixture-stable-namespace", "https://issuer.example.test", "fixture-client", [],
            "fixture-explicit-issuer-map", 128, 128, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3), 8,
            "/authentication/sign-in", ["/authentication/sign-in"], "fixture-sign-in-admission", "fixture-federation-primary",
            "fixture-federation-assurance-map", "fixture-session-use", "fixture-processing", "fixture-retention");
        internal FederationLinkManagementPolicy Management { get; set; } = new("fixture-management-v1", Origin, "/authentication/sign-in",
            "fixture-proof", "fixture-proof", TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3), 8,
            FederationUnlinkSessionEffect.PreserveExistingSessions, "fixture-session-use", "fixture-session-effect-evidence", "fixture-processing", "fixture-retention");
        internal bool AllowPosture { get; set; } = true;
        internal Action? BeforePolicy { get; set; }
        internal Action? BeforeFederation { get; set; }
        internal Action? BeforePosture { get; set; }
        internal FederationUnlinkPostureRequest? LastPosture { get; private set; }
        public FederationProfileVersion RequireCurrent(FederationProviderKind provider, DateTimeOffset effectiveAt)
        {
            BeforeFederation?.Invoke();
            if (provider != Federation.Provider) throw new InvalidOperationException("FINITE_PROFILE_UNAVAILABLE");
            return new(new(Federation.Version, "ACTIVE", provider, Interval, null, ["SYNTHETIC-TEST-ONLY"]), Federation);
        }
        public FederationLinkManagementPolicyVersion RequireCurrent(string origin, DateTimeOffset effectiveAt)
        {
            BeforePolicy?.Invoke();
            return new(new(Management.Version, "ACTIVE", origin, Interval, null, ["SYNTHETIC-TEST-ONLY"]), Management);
        }
        public FederationUnlinkPostureDecision RequireCurrent(FederationUnlinkPostureRequest request, DateTimeOffset effectiveAt)
        {
            BeforePosture?.Invoke(); LastPosture = request;
            return new(request.AccountId, request.AccountVersion, request.LinkId, request.LinkVersion, request.FactsDigest,
                AllowPosture, "fixture-remaining-posture", "fixture-posture-evidence", Interval);
        }
        public AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset executedAt, string? expectedVersion = null)
        {
            if (expectedVersion is not null && expectedVersion != "fixture-proof") throw new AssurancePolicyVersionConflictException();
            return new(new("fixture-proof", "ACTIVE", scope, Interval, null, ["SYNTHETIC-TEST-ONLY"]), "fixture-proof", TimeSpan.FromMinutes(5),
                new Dictionary<string, string>(StringComparer.Ordinal) { ["pwd"] = "fixture-proof", ["totp"] = "fixture-proof", ["federation:Google"] = "fixture-proof" });
        }
    }
    private static Task<AuthenticationTestHost> Host(IdentityTestDatabase database, Profile profile)
    {
        var totp = new TotpTests.Profile();
        totp.Policy = totp.Policy with { MfaAssurances = new Dictionary<string, string>(StringComparer.Ordinal)
            { ["pwd"] = "fixture-mfa", ["passkey"] = "fixture-mfa", ["federation"] = "fixture-federation-mfa" } };
        return TotpTests.StartTotp(database, totp, services =>
        {
            services.AddSingleton<IFederationProfileSource>(profile);
            // This fixture exercises domain owners, not HTTP management activation. Supply the existing Auth owner's
            // optional policy collection explicitly; individual HTTP capability registrations require the full facade.
            services.AddSingleton<IEnumerable<IFederationLinkPolicySource>>(Array.AsReadOnly<IFederationLinkPolicySource>([profile]));
            services.AddScoped<FederationAttemptApplication>();
            services.AddScoped(provider => new FederationLinkApplication(provider.GetRequiredService<IdentityPersistenceDbContext>(),
                provider.GetRequiredService<AuthenticationApplication>(), provider.GetRequiredService<FederationAttemptApplication>(),
                profile, profile, profile, provider.GetRequiredService<IAuthenticationPolicySource>(),
                provider.GetRequiredService<IAssurancePolicySource>(), provider.GetRequiredService<TimeProvider>()));
            services.RemoveAll<IAssurancePolicySource>(); services.AddSingleton<IAssurancePolicySource>(profile);
        });
    }
    private sealed record Session(StepUpCaller Caller, string Key)
    { public override string ToString() => nameof(Session); }
    private sealed record Pending(Guid OperationId, Guid RequestId, Guid AttemptId, FederationCurrentBrowser Browser)
    { public override string ToString() => nameof(Pending); }
    private sealed record Proof(UserStepUpOperationContext Context, string Key)
    { public override string ToString() => nameof(Proof); }
    // One fixed real management operation, resolved by its actual owner. This is not a protocol/identity emulator.
    private sealed class FixedOwner(FederationLinkApplication application, IdentityPersistenceDbContext database,
        StepUpCaller caller, Pending pending) : IUserStepUpOperationOutcomeSource
    {
        public Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken ct) =>
            actor.AccountId == caller.AccountId && actor.SessionId == caller.SessionId && actor.Origin == caller.Origin
                ? application.ResolveAsync(caller, pending.OperationId, pending.RequestId, ct) : Task.FromResult<UserStepUpOperationContext?>(null);
        public Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext context, CancellationToken ct) =>
            throw new InvalidOperationException("FINITE_TYPED_OWNER_REQUIRED");
        public Task<UserStepUpOperationApplyOutcome> ApplyWithOutcomeAsync(UserStepUpActor actor, UserStepUpOperationContext operation,
            IdentityPersistenceDbContext context, CancellationToken ct) => ReferenceEquals(database, context) &&
            actor.AccountId == caller.AccountId && actor.SessionId == caller.SessionId && actor.Origin == caller.Origin && operation.Binding.CorrelationId == pending.OperationId.ToString("N")
                ? application.ApplyAsync(caller, operation, ct) : Task.FromResult(UserStepUpOperationApplyOutcome.Conflict());
    }
    private static Profile FreshProfile()
    {
        var profile = new Profile();
        profile.Federation = profile.Federation with { FreshAuthentication = new("fixture-fresh-v1", "SYNTHETIC-TEST-ONLY-CAPABILITY",
            "fixture-fresh-assurance-map", TimeSpan.FromSeconds(90), "/authentication/sign-in", [new(FreshMethods, "fixture-proof")]) };
        return profile;
    }
    private static async Task<UserStepUpOperationContext> ConfirmOnly(AuthenticationTestHost host, StepUpCaller caller, Pending pending)
    {
        await using var scope = host.Services.CreateAsyncScope(); var app = scope.ServiceProvider.GetRequiredService<FederationLinkApplication>();
        var result = await app.ConfirmAsync(caller, pending.OperationId, pending.RequestId, 1, true, CancellationToken.None);
        Assert.True(result.ErrorCode is null && result.Row?.State == "PENDING_PROOF");
        var context = await app.ResolveAsync(caller, pending.OperationId, pending.RequestId, CancellationToken.None);
        Assert.True(context is not null); return context!;
    }
    private static async Task<Pending> StartFreshProof(AuthenticationTestHost host, Profile profile, StepUpCaller caller,
        Pending operation, UserStepUpOperationContext context, Guid linkId)
    {
        var browser = Browser(host, caller); var request = Guid.NewGuid();
        await using var scope = host.Services.CreateAsyncScope(); var provider = scope.ServiceProvider;
        var app = provider.GetRequiredService<FederationAttemptApplication>();
        var owner = new FixedOwner(provider.GetRequiredService<FederationLinkApplication>(), provider.GetRequiredService<IdentityPersistenceDbContext>(), caller, operation);
        var result = await app.StartOperationProofAsync(browser, caller, context, request, profile.Federation.Provider, linkId, owner,
            provider.GetRequiredService<IAssurancePolicySource>(), CancellationToken.None);
        Assert.True(result.ErrorCode is null && result.Row is { State: "PREPARED", Purpose: "OperationProof" });
        var admitted = await app.AdmitChallengeAsync(browser, result.Row!.AttemptId, CancellationToken.None, caller);
        Assert.True(admitted is not null && admitted.Purpose == "OperationProof");
        return new(operation.OperationId, request, result.Row.AttemptId, browser);
    }
    private static Task<FederationCallbackResult> FreshEvidence(IServiceProvider provider, Profile profile, Pending proof,
        DateTimeOffset authenticatedAt, string subject = Subject) => provider.GetRequiredService<FederationAttemptApplication>().RecordVerifiedAsync(
            new(profile.Federation.Provider, profile.Federation.Version, proof.AttemptId, profile.Federation.Issuer, subject,
                profile.Federation.ClientId, authenticatedAt, FreshMethods), CancellationToken.None);

    public async Task QualifiedFederationProofCompletesOneOriginalOwnerAndConsumesOneRealProof()
    {
        await using var database = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = FreshProfile();
        await using var host = await Host(database, profile); await host.SeedAsync(database); var actor = await PasswordSession(host);
        var linkId = await Link(host, profile, actor.Caller); var operation = await Unlink(host, profile, actor.Caller, linkId, 1);
        var ownerContext = await ConfirmOnly(host, actor.Caller, operation);
        var proofAttempt = await StartFreshProof(host, profile, actor.Caller, operation, ownerContext, linkId);
        var authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(host.Clock.GetUtcNow().AddSeconds(-5).ToUnixTimeSeconds());
        Proof issued;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider; var app = provider.GetRequiredService<FederationAttemptApplication>();
            Assert.True((await FreshEvidence(provider, profile, proofAttempt, authenticatedAt)).Verified);
            var owner = new FixedOwner(provider.GetRequiredService<FederationLinkApplication>(), provider.GetRequiredService<IdentityPersistenceDbContext>(), actor.Caller, operation);
            var result = await app.CompleteOperationProofAsync(proofAttempt.Browser, actor.Caller, ownerContext, proofAttempt.AttemptId,
                owner, provider.GetRequiredService<StepUpApplication>(), CancellationToken.None);
            Assert.True(result.ErrorCode is null && result.Grant is not null); issued = new(ownerContext, result.Grant!.Key);
            var recovered = await app.CompleteOperationProofAsync(proofAttempt.Browser, actor.Caller, ownerContext, proofAttempt.AttemptId,
                owner, provider.GetRequiredService<StepUpApplication>(), CancellationToken.None);
            Assert.True(recovered.ErrorCode is null && recovered.Grant is not null && recovered.Grant.Key == issued.Key);
        }
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider; var owner = new FixedOwner(provider.GetRequiredService<FederationLinkApplication>(),
                provider.GetRequiredService<IdentityPersistenceDbContext>(), actor.Caller, operation);
            using var result = await provider.GetRequiredService<StepUpApplication>().ExecuteOutcomeAsync(actor.Caller, ownerContext.Binding, issued.Key,
                token => owner.ApplyWithOutcomeAsync(new(actor.Caller), ownerContext, provider.GetRequiredService<IdentityPersistenceDbContext>(), token), CancellationToken.None);
            Assert.True(result.Applied && !result.SignInRequired);
        }
        await using (var read = database.Context())
        {
            var attempt = await read.Set<FederationAttemptRow>().SingleAsync(x => x.AttemptId == proofAttempt.AttemptId);
            var proof = await read.Set<StepUpProofRow>().SingleAsync(x => x.FederationAttemptId == proofAttempt.AttemptId);
            Assert.True(attempt.State == "COMPLETED" && attempt.ResultProofId == proof.ProofId && attempt.ResultSessionId is null && attempt.PendingMfaId is null);
            Assert.True(proof.State == "CONSUMED" && proof.AuthenticationMethod == "federation:Google" && proof.AuthenticatedAt == authenticatedAt &&
                attempt.ProviderAuthenticatedAt == authenticatedAt && proof.AuthenticatedAt != attempt.VerifiedAt && proof.ExpiresAt <= attempt.VerificationExpiresAt);
            Assert.True(JsonSerializer.Deserialize<string[]>(attempt.ProviderMethodsJson!) is { Length: 2 } methods && methods.Contains("pwd", StringComparer.Ordinal) && methods.Contains("mfa", StringComparer.Ordinal));
            Assert.True(await read.ExternalIdentityLinks.AnyAsync(x => x.LinkId == linkId && x.LinkState == "UNLINKED" && x.Version == 2));
            Assert.True(await read.Set<OutboxMessage>().CountAsync(x => x.EventType == "ExternalIdentityUnlinked") == 1);
        }
        await using var recovery = host.Services.CreateAsyncScope();
        var originalProof = await recovery.ServiceProvider.GetRequiredService<FederationAttemptApplication>().ReadOperationProofOriginalAsync(proofAttempt.Browser,
            actor.Caller, ownerContext, proofAttempt.RequestId, proofAttempt.AttemptId, CancellationToken.None);
        var originalOwner = await recovery.ServiceProvider.GetRequiredService<FederationLinkApplication>().ReadOriginalByRequestAsync(actor.Caller, operation.RequestId, CancellationToken.None);
        Assert.True(originalProof.ErrorCode is null && originalProof.Row?.AttemptId == proofAttempt.AttemptId && originalProof.Row.State == "COMPLETED");
        Assert.True(originalOwner.ErrorCode is null && originalOwner.Row?.OperationId == operation.OperationId && originalOwner.Row.State == "COMPLETED");
    }

    public async Task RejectedFederationProofEvidenceAndLateSourceDriftCannotIssueOrMutateOwner()
    {
        foreach (var rejection in FreshRejections)
        {
            await using var database = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = FreshProfile();
            await using var host = await Host(database, profile); await host.SeedAsync(database); var actor = await PasswordSession(host);
            var linkId = await Link(host, profile, actor.Caller); var operation = await Unlink(host, profile, actor.Caller, linkId, 1);
            var ownerContext = await ConfirmOnly(host, actor.Caller, operation);
            var pending = await StartFreshProof(host, profile, actor.Caller, operation, ownerContext, linkId);
            if (rejection == "link-version")
            {
                await using var touch = database.Context(); var link = await touch.ExternalIdentityLinks.SingleAsync(x => x.LinkId == linkId);
                link.Version++; link.EvidenceReference = "SYNTHETIC-TEST-ONLY-INDEPENDENT-SOURCE-VERSION"; await touch.SaveChangesAsync();
            }
            var time = host.Clock.GetUtcNow().AddSeconds(rejection == "future" ? 1 : rejection == "stale" ? -91 : -5);
            var observedFinalDrift = false;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var provider = scope.ServiceProvider; var context = provider.GetRequiredService<IdentityPersistenceDbContext>();
                var verified = await FreshEvidence(provider, profile, pending, time, rejection == "subject" ? "SYNTHETIC-TEST-ONLY-WRONG-SUBJECT" : Subject);
                Assert.True(verified.Verified == (rejection == "late-profile"));
                if (rejection == "late-profile") profile.BeforeFederation = () =>
                {
                    if (observedFinalDrift || !context.ChangeTracker.Entries<StepUpProofRow>().Any(x => x.State == EntityState.Added && x.Entity.FederationAttemptId == pending.AttemptId)) return;
                    observedFinalDrift = true; profile.Federation = profile.Federation with { Version = "fixture-federation-withdrawn-v2" };
                };
                try
                {
                    var owner = new FixedOwner(provider.GetRequiredService<FederationLinkApplication>(), context, actor.Caller, operation);
                    var issuedProof = await provider.GetRequiredService<FederationAttemptApplication>().CompleteOperationProofAsync(pending.Browser, actor.Caller,
                        ownerContext, pending.AttemptId, owner, provider.GetRequiredService<StepUpApplication>(), CancellationToken.None);
                    Assert.True(issuedProof.ErrorCode is not null && issuedProof.Grant is null);
                }
                finally { profile.BeforeFederation = null; }
            }
            Assert.True(rejection != "late-profile" || observedFinalDrift);
            await using var read = database.Context();
            Assert.True(!await read.Set<StepUpProofRow>().AnyAsync(x => x.FederationAttemptId == pending.AttemptId));
            Assert.True(!await read.Set<OutboxMessage>().AnyAsync(x => x.EventType == "ExternalIdentityUnlinked"));
            Assert.True(await read.Set<FederationLinkOperationRow>().AnyAsync(x => x.OperationId == operation.OperationId && x.State == "PENDING_PROOF" && x.ProofReference == null));
            Assert.True(await read.ExternalIdentityLinks.AnyAsync(x => x.LinkId == linkId && x.LinkState == "ACTIVE" && x.Version == (rejection == "link-version" ? 2 : 1)));
        }
    }
    private static async Task<Session> PasswordSession(AuthenticationTestHost host, string email = "person@example.test")
    {
        await using var scope = host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<AuthenticationApplication>().SignInAsync(
            new(new('A', 64), new('B', 64), Origin, null), Command(Guid.NewGuid(), email: email), CancellationToken.None);
        Assert.True(result.ErrorCode is null && result.Grant is not null);
        var grant = result.Grant!;
        return new(StepUpCaller.FromPrincipal(grant.Ticket.Principal, Origin, new('B', 64)), grant.Key);
    }
    private static FederationCurrentBrowser Browser(AuthenticationTestHost host, StepUpCaller caller) =>
        new(new(PasskeyApplication.MutationBrowser(caller), caller.NetworkDigest, Origin, caller.SessionId), Guid.NewGuid(),
            host.Clock.GetUtcNow() + host.Policies.Policy.BrowserContextLifetime, host.Policies.Policy.Version, CookieDigest, caller.SessionId, CookieDigest);
    private static async Task<Pending> ProviderVerifiedLink(AuthenticationTestHost host, Profile profile, StepUpCaller caller,
        Guid? existing = null, long version = 0)
    {
        var browser = Browser(host, caller); var request = Guid.NewGuid();
        await using var scope = host.Services.CreateAsyncScope(); var app = scope.ServiceProvider.GetRequiredService<FederationLinkApplication>();
        var started = await app.StartLinkAsync(browser, caller, request, profile.Federation.Provider, existing, version, 1, CancellationToken.None);
        Assert.True(started.ErrorCode is null && started.Row is { State: "PENDING_PROVIDER" } && started.Attempt is { State: "PREPARED" });
        var pending = new Pending(started.Row!.OperationId, request, started.Attempt!.AttemptId, browser);
        Assert.True(await app.ResolveAsync(caller, pending.OperationId, pending.RequestId, CancellationToken.None) is null);
        var attempts = scope.ServiceProvider.GetRequiredService<FederationAttemptApplication>();
        var admitted = await attempts.AdmitChallengeAsync(browser, pending.AttemptId, CancellationToken.None, caller);
        Assert.True(admitted is not null && admitted.Purpose == "Link");
        var verified = await attempts.RecordVerifiedAsync(new(profile.Federation.Provider, profile.Federation.Version, pending.AttemptId,
            profile.Federation.Issuer, Subject, profile.Federation.ClientId), CancellationToken.None);
        Assert.True(verified.Verified);
        return pending;
    }
    private static async Task<Pending> Unlink(AuthenticationTestHost host, Profile profile, StepUpCaller caller, Guid link, long version)
    {
        await using var scope = host.Services.CreateAsyncScope(); var request = Guid.NewGuid();
        var result = await scope.ServiceProvider.GetRequiredService<FederationLinkApplication>().StartUnlinkAsync(caller, request,
            profile.Federation.Provider, link, version, 1, CancellationToken.None, CookieDigest);
        Assert.True(result.ErrorCode is null && result.Row is { State: "PENDING_CONFIRMATION" });
        return new(result.Row!.OperationId, request, Guid.Empty, Browser(host, caller));
    }
    private static async Task<Proof> ConfirmAndProve(AuthenticationTestHost host, StepUpCaller caller, Pending pending)
    {
        await using var scope = host.Services.CreateAsyncScope(); var app = scope.ServiceProvider.GetRequiredService<FederationLinkApplication>();
        var confirmed = await app.ConfirmAsync(caller, pending.OperationId, pending.RequestId, 1, true, CancellationToken.None);
        Assert.True(confirmed.ErrorCode is null && confirmed.Row is { State: "PENDING_PROOF", ConfirmedAt: not null });
        var context = await app.ResolveAsync(caller, pending.OperationId, pending.RequestId, CancellationToken.None);
        Assert.True(context is not null);
        var resolved = context!;
        var proof = await scope.ServiceProvider.GetRequiredService<StepUpApplication>().IssuePasswordAsync(caller, resolved.Binding,
            resolved.ExpectedPolicyVersion, Password, CancellationToken.None);
        Assert.True(proof.ErrorCode is null && proof.Grant is not null);
        return new(resolved, proof.Grant!.Key);
    }
    private static async Task<StepUpExecutionResult> Apply(AuthenticationTestHost host, StepUpCaller caller, Proof proof)
    {
        await using var scope = host.Services.CreateAsyncScope(); var provider = scope.ServiceProvider;
        return await provider.GetRequiredService<StepUpApplication>().ExecuteOutcomeAsync(caller, proof.Context.Binding, proof.Key,
            token => provider.GetRequiredService<FederationLinkApplication>().ApplyAsync(caller, proof.Context, token), CancellationToken.None);
    }
    private static async Task<Guid> Link(AuthenticationTestHost host, Profile profile, StepUpCaller caller)
    {
        var pending = await ProviderVerifiedLink(host, profile, caller); var proof = await ConfirmAndProve(host, caller, pending);
        using var completed = await Apply(host, caller, proof); Assert.True(completed.Applied && !completed.SignInRequired);
        await using var scope = host.Services.CreateAsyncScope();
        var original = await scope.ServiceProvider.GetRequiredService<FederationLinkApplication>().ReadOriginalByRequestAsync(caller, pending.RequestId, CancellationToken.None);
        Assert.True(original.ErrorCode is null && original.Row is { State: "COMPLETED", ResultLinkId: not null });
        return original.Row!.ResultLinkId!.Value;
    }

    public async Task ExplicitConfirmationAndExactExternalOwnershipHaveOneWinnerWithoutEmailLinking()
    {
        await using var database = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile();
        await using var host = await Host(database, profile); var firstAccount = await host.SeedAsync(database);
        var secondAccount = await host.SeedAsync(database, foreignEmail: true);
        var first = await PasswordSession(host); var second = await PasswordSession(host, "foreign@example.test");
        var firstPending = await ProviderVerifiedLink(host, profile, first.Caller); var secondPending = await ProviderVerifiedLink(host, profile, second.Caller);
        await using (var read = database.Context()) Assert.True(!await read.ExternalIdentityLinks.AnyAsync());
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var stale = await scope.ServiceProvider.GetRequiredService<FederationLinkApplication>().ConfirmAsync(first.Caller,
                firstPending.OperationId, firstPending.RequestId, 2, true, CancellationToken.None);
            Assert.True(stale.ErrorCode == "VERSION_CONFLICT" && stale.Row?.State == "PENDING_PROVIDER");
        }
        var firstProof = await ConfirmAndProve(host, first.Caller, firstPending); var secondProof = await ConfirmAndProve(host, second.Caller, secondPending);
        var results = await Task.WhenAll(Apply(host, first.Caller, firstProof), Apply(host, second.Caller, secondProof));
        try { Assert.True(results.Count(result => result.Applied) == 1 && results.Count(result => !result.Applied) == 1); }
        finally { foreach (var result in results) result.Dispose(); }
        await using var final = database.Context(); var link = await final.ExternalIdentityLinks.SingleAsync();
        Assert.True(link.AccountId == firstAccount || link.AccountId == secondAccount);
        Assert.True(link.Provider == profile.Federation.StableNamespaceId && link.Subject == Subject && link.LinkState == "ACTIVE" && link.Version == 1);
        Assert.True(await final.VerifiedContacts.CountAsync() == 2 && await final.Users.CountAsync() == 2);
        Assert.True(await final.Set<StepUpProofRow>().CountAsync(x => x.State == "CONSUMED") == 1 && await final.Set<StepUpProofRow>().CountAsync(x => x.State == "ACTIVE") == 1);
        Assert.True(await final.Set<OutboxMessage>().CountAsync(x => x.EventType == "ExternalIdentityLinked") == 1);
    }

    public async Task RequiredRemainingPostureDenialPreservesLinkAndUnconsumedProof()
    {
        await using var database = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile();
        await using var host = await Host(database, profile); await host.SeedAsync(database); var actor = await PasswordSession(host);
        var link = await Link(host, profile, actor.Caller); var pending = await Unlink(host, profile, actor.Caller, link, 1);
        var proof = await ConfirmAndProve(host, actor.Caller, pending); profile.AllowPosture = false;
        using var denied = await Apply(host, actor.Caller, proof); Assert.True(!denied.Applied && !denied.SignInRequired);
        await using var read = database.Context(); var source = await read.ExternalIdentityLinks.SingleAsync();
        Assert.True(source.LinkId == link && source.Version == 1 && source.LinkState == "ACTIVE");
        Assert.True(await read.Set<StepUpProofRow>().AnyAsync(x => x.OperationId == proof.Context.Binding.OperationId && x.State == "ACTIVE"));
        Assert.True(!await read.Set<OutboxMessage>().AnyAsync(x => x.EventType == "ExternalIdentityUnlinked"));
        Assert.True(profile.LastPosture is { PasswordAvailable: true } request && !request.Paths.Any(path => path.Reference == link.ToString("N")));
        await using var scope = host.Services.CreateAsyncScope();
        var original = await scope.ServiceProvider.GetRequiredService<FederationLinkApplication>().ReadOriginalByRequestAsync(actor.Caller, pending.RequestId, CancellationToken.None);
        Assert.True(original.ErrorCode is null && original.Row?.OperationId == pending.OperationId && original.Row.State == "PENDING_PROOF");
    }

    public async Task ExplicitSessionEffectsAndFreshSameAccountRelinkPreserveExactHistory()
    {
        foreach (var effect in Enum.GetValues<FederationUnlinkSessionEffect>())
        {
            await using var database = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile();
            profile.Management = profile.Management with { UnlinkSessionEffect = effect };
            await using var host = await Host(database, profile); var account = await host.SeedAsync(database); var password = await PasswordSession(host);
            var link = await Link(host, profile, password.Caller);
            var actor = await FederatedSession(host, profile); var other = await FederatedSession(host, profile);
            var neverVisited = await FederatedSession(host, profile);
            var pending = await Unlink(host, profile, actor.Caller, link, 1); var proof = await ConfirmAndProve(host, actor.Caller, pending);
            using var applied = await Apply(host, actor.Caller, proof);
            var revoke = effect == FederationUnlinkSessionEffect.RevokeLinkedSessions;
            Assert.True(applied.Applied && applied.SignInRequired == revoke);
            await using (var beforeRead = database.Context())
            {
                Assert.True(await beforeRead.Set<AuthenticationSessionRow>().AnyAsync(x => x.SessionId == other.Caller.SessionId && x.State == "ACTIVE"));
                Assert.True(await beforeRead.Set<AuthenticationSessionRow>().AnyAsync(x => x.SessionId == actor.Caller.SessionId && x.State == (revoke ? "REVOKED" : "ACTIVE")));
            }
            Assert.True(await SessionCurrent(host, other.Key) == !revoke && await SessionCurrent(host, password.Key));
            var relink = await ProviderVerifiedLink(host, profile, password.Caller, link, 2); var relinkProof = await ConfirmAndProve(host, password.Caller, relink);
            using var restored = await Apply(host, password.Caller, relinkProof); Assert.True(restored.Applied && !restored.SignInRequired);
            await using (var beforeLateVisit = database.Context())
                Assert.True(await beforeLateVisit.Set<AuthenticationSessionRow>().AnyAsync(x => x.SessionId == neverVisited.Caller.SessionId && x.State == "ACTIVE"));
            Assert.True(await SessionCurrent(host, neverVisited.Key) == !revoke);
            var newGeneration = await FederatedSession(host, profile);
            Assert.True(await SessionCurrent(host, newGeneration.Key));
            await using var read = database.Context(); var identity = await read.ExternalIdentityLinks.SingleAsync();
            var historical = await read.Set<FederationLinkOperationRow>().SingleAsync(x => x.OperationId == pending.OperationId);
            Assert.True(identity.LinkId == link && identity.AccountId == account && identity.Provider == profile.Federation.StableNamespaceId &&
                identity.Subject == Subject && identity.LinkState == "ACTIVE" && identity.Version == 3);
            Assert.True(historical.State == "COMPLETED" && historical.ResultLinkId == link && historical.ResultLinkVersion == 2 && historical.SessionEffect == effect.ToString());
            Assert.True(await read.Set<FederationLinkOperationRow>().CountAsync(x => x.State == "COMPLETED") == 3);
            Assert.True(!revoke || await read.Set<AuthenticationSessionRow>().AnyAsync(x => x.SessionId == other.Caller.SessionId && x.State == "REVOKED"));
            Assert.True(await read.Set<AuthenticationSessionRow>().AnyAsync(x => x.SessionId == neverVisited.Caller.SessionId && x.State == (revoke ? "REVOKED" : "ACTIVE")));
            Assert.True(await read.Set<AuthenticationSessionRow>().AnyAsync(x => x.SessionId == newGeneration.Caller.SessionId && x.State == "ACTIVE" && x.FederationLinkVersion == 3));
        }
    }

    public async Task FinalPureAuthorityDriftRollsBackLinkProofAndOutboxWithExactOriginalRecovery()
    {
        foreach (var policyDrift in DriftModes)
        {
            await using var database = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var profile = new Profile();
            await using var host = await Host(database, profile); await host.SeedAsync(database); var actor = await PasswordSession(host);
            var link = await Link(host, profile, actor.Caller); var pending = await Unlink(host, profile, actor.Caller, link, 1);
            var proof = await ConfirmAndProve(host, actor.Caller, pending); var observed = false; var stagedOutbox = false; var rejected = false;
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var provider = scope.ServiceProvider; var context = provider.GetRequiredService<IdentityPersistenceDbContext>();
                void Drift()
                {
                    if (observed || !context.ChangeTracker.Entries<FederationLinkOperationRow>().Any(x => x.Entity.OperationId == pending.OperationId && x.Entity.State == "COMPLETED")) return;
                    observed = true; stagedOutbox = context.ChangeTracker.Entries<OutboxMessage>().Any(x => x.Entity.EventType == "ExternalIdentityUnlinked");
                    if (policyDrift) profile.Management = profile.Management with { Version = "fixture-management-v2" };
                    else profile.AllowPosture = false;
                }
                if (policyDrift) profile.BeforePolicy = Drift; else profile.BeforePosture = Drift;
                try
                {
                    using var result = await provider.GetRequiredService<StepUpApplication>().ExecuteOutcomeAsync(actor.Caller, proof.Context.Binding,
                        proof.Key, token => provider.GetRequiredService<FederationLinkApplication>().ApplyAsync(actor.Caller, proof.Context, token), CancellationToken.None);
                    rejected = !result.Applied;
                }
                catch (InvalidOperationException) { rejected = true; }
                finally { profile.BeforePolicy = null; profile.BeforePosture = null; }
            }
            Assert.True(observed && stagedOutbox && rejected);
            await using (var read = database.Context())
            {
                Assert.True(await read.ExternalIdentityLinks.AnyAsync(x => x.LinkId == link && x.LinkState == "ACTIVE" && x.Version == 1));
                Assert.True(await read.Set<FederationLinkOperationRow>().AnyAsync(x => x.OperationId == pending.OperationId && x.State == "PENDING_PROOF" && x.ProofReference == null));
                Assert.True(await read.Set<StepUpProofRow>().AnyAsync(x => x.OperationId == proof.Context.Binding.OperationId && x.State == "ACTIVE"));
                Assert.True(!await read.Set<OutboxMessage>().AnyAsync(x => x.EventType == "ExternalIdentityUnlinked"));
            }
            await using var recovered = host.Services.CreateAsyncScope();
            var original = await recovered.ServiceProvider.GetRequiredService<FederationLinkApplication>().ReadOriginalByRequestAsync(actor.Caller, pending.RequestId, CancellationToken.None);
            Assert.True(original.ErrorCode is null && original.Row?.OperationId == pending.OperationId && original.Row.RequestId == pending.RequestId);
        }
    }

    private static async Task<Session> FederatedSession(AuthenticationTestHost host, Profile profile)
    {
        await using var scope = host.Services.CreateAsyncScope(); var app = scope.ServiceProvider.GetRequiredService<FederationAttemptApplication>();
        var browser = new FederationCurrentBrowser(new(new('D', 64), new('B', 64), Origin, null), Guid.NewGuid(),
            host.Clock.GetUtcNow() + host.Policies.Policy.BrowserContextLifetime, host.Policies.Policy.Version, CookieDigest, null, CookieDigest);
        var started = await app.StartAsync(browser, Guid.NewGuid(), profile.Federation.Provider, FederationAttemptPurpose.SignIn, false, CancellationToken.None);
        Assert.True(started.ErrorCode is null && started.Row is not null);
        Assert.True(await app.AdmitChallengeAsync(browser, started.Row!.AttemptId, CancellationToken.None) is not null);
        Assert.True((await app.RecordVerifiedAsync(new(profile.Federation.Provider, profile.Federation.Version, started.Row.AttemptId,
            profile.Federation.Issuer, Subject, profile.Federation.ClientId), CancellationToken.None)).Verified);
        var completed = await app.CompleteSignInAsync(browser, started.Row.AttemptId, CancellationToken.None);
        Assert.True(completed.ErrorCode is null && completed.Authentication?.Grant is not null);
        var grant = completed.Authentication!.Grant!;
        return new(StepUpCaller.FromPrincipal(grant.Ticket.Principal, Origin, new('B', 64)), grant.Key);
    }
    private static async Task<bool> SessionCurrent(AuthenticationTestHost host, string key)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AuthenticationApplication>().RetrieveAsync(key, Origin, CancellationToken.None) is not null;
    }
}
