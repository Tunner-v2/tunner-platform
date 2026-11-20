using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed partial class PasskeyTests
{
    private sealed record CompositeActor(AuthenticationTestHost Host, TotpTests.Profile Totp, string AccountId, Guid PasskeyReference, Guid TotpReference, string Cookies)
    { public override string ToString() => nameof(CompositeActor); }
    private static async Task<CompositeActor> CreateCompositeActor(IdentityTestDatabase db)
    {
        var profile = new TotpTests.Profile(); var host = await StartPasskeys(db, policies: new Policies { Effect = PasskeyRevocationSessionEffect.RevokeCredentialSessions }, configure: services =>
        {
            services.AddSingleton<ITotpPolicySource>(profile); services.AddSingleton<ITotpSeedStore>(profile); services.AddSingleton<ITotpMutationEligibilitySource>(profile);
            services.AddSingleton<ITotpMfaRequirementSource>(profile); services.AddSingleton<ITotpPresentationSource>(profile); services.AddSingleton<IStepUpPresentationSource>(profile);
            services.AddSingleton<IAssurancePolicySource>(new TotpAndPasskeyAssurance(profile));
        });
        try
        {
            host.Clock.SynchronizeToSystem(); var account = await host.SeedAsync(db); using var key = new PasskeyAuthenticator(); var passkey = await Enroll(host, await Login(host), key);
            var totp = await TotpTests.Enroll(host, profile, passkey.Pending.Cookies); profile.RequireMfa = true; var primary = await AssertionOptions(host);
            using var signed = await host.Post("/authentication/passkeys/sign-in", new { requestId = primary.Bootstrap.Key, ceremonyId = primary.Ceremony.GetProperty("ceremonyId").GetGuid(), credential = key.Assert(primary.Ceremony.GetProperty("publicKey"), Origin) }, primary.Bootstrap.Cookies, primary.Bootstrap.Csrf);
            Assert.Equal(HttpStatusCode.OK, signed.StatusCode); var cookies = Merge(primary.Bootstrap.Cookies, signed);
            using var options = await host.Get("/authentication/totp/options", cookies); Assert.Equal(HttpStatusCode.OK, options.StatusCode); var view = await options.Content.ReadFromJsonAsync<JsonElement>(); cookies = Merge(cookies, options);
            using var completed = await host.Post("/authentication/totp/complete", new { requestId = primary.Bootstrap.Key, authenticatorReference = totp.Reference, code = TotpTests.NativeCode(profile.Seed(totp.Reference), 1) }, cookies, view.GetProperty("csrfToken").GetString());
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode); return new(host, profile, account, passkey.Reference, totp.Reference, Merge(cookies, completed));
        }
        catch { await host.DisposeAsync(); throw; }
    }
    [Fact]
    public async Task CompositeMfaActorCanRevokeOwnPasskeyWithExactSessionTerminationAndUnaffectedTotp()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var actor = await CreateCompositeActor(db); await using var host = actor.Host;
        await using (var before = db.Context())
        {
            var session = await before.Set<AuthenticationSessionRow>().SingleAsync(x => x.AuthenticationMethods == "passkey+totp"); Assert.Equal("fixture-mfa", session.Assurance);
            Assert.NotEqual("fixture-passkey-assurance", session.Assurance);
        }
        var pending = await Authorize(host, await Begin(host, actor.Cookies, actor.PasskeyReference, 1)); Assert.True(pending.SignInRequired);
        using var denied = await host.Get("/authentication/session", pending.Cookies); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await using var read = db.Context(); var credential = await read.Set<PasskeyCredentialRow>().SingleAsync(); Assert.Equal("REVOKED", credential.State); Assert.Equal(2, credential.Version);
        var sessionAfter = await read.Set<AuthenticationSessionRow>().SingleAsync(x => x.AuthenticationMethods == "passkey+totp"); Assert.Equal("REVOKED", sessionAfter.State); Assert.Equal("PASSKEY_REVOKED", sessionAfter.TerminationCause);
        Assert.Equal("ACTIVE", (await read.Set<TotpAuthenticatorRow>().SingleAsync()).State); Assert.Equal(2, (await read.Set<TotpAuthenticatorRow>().SingleAsync()).Version);
        Assert.Equal("COMPLETED", await read.Set<PasskeyCeremonyRow>().Where(x => x.CeremonyId == pending.CeremonyId).Select(x => x.State).SingleAsync());
        Assert.Equal(1, await read.Set<AuthenticationSessionRow>().CountAsync(x => x.AuthenticationMethods == "pwd" && x.State == "ACTIVE"));
    }
    [Fact]
    public async Task CompositeOwnPasskeyTerminationRollsBackIfUnaffectedTotpPolicyChangesInFinalAccess()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var actor = await CreateCompositeActor(db); await using var host = actor.Host;
        var pending = await Begin(host, actor.Cookies, actor.PasskeyReference, 1); var calls = 0;
        host.Access.BeforeDecision = request => { if (request.Operation == PasskeyApplication.RevocationScope.Operation && ++calls == 4) actor.Totp.Policy = actor.Totp.Policy with { Version = "fixture-totp-v2" }; };
        using var options = await host.Get("/authentication/step-up/options", pending.Cookies); Assert.Equal(HttpStatusCode.OK, options.StatusCode); var view = await options.Content.ReadFromJsonAsync<JsonElement>();
        using var result = await host.Post("/authentication/step-up/password", new { requestId = view.GetProperty("requestId").GetGuid(), password = Password }, Merge(pending.Cookies, options), view.GetProperty("csrfToken").GetString());
        Assert.True(calls >= 4, "FINAL_COMPOSITE_ACCESS_NOT_EXERCISED"); Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        await using var read = db.Context(); Assert.Equal("ACTIVE", (await read.Set<PasskeyCredentialRow>().SingleAsync()).State); Assert.Equal("ACTIVE", await read.Set<AuthenticationSessionRow>().Where(x => x.AuthenticationMethods == "passkey+totp").Select(x => x.State).SingleAsync());
        Assert.Equal("PENDING", await read.Set<PasskeyCeremonyRow>().Where(x => x.CeremonyId == pending.CeremonyId).Select(x => x.State).SingleAsync());
    }
    [Fact]
    public async Task ExactPrimaryTerminationReceiptCannotHideAnUnrelatedTotpSourceDisable()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var actor = await CreateCompositeActor(db); await using var host = actor.Host;
        await using var scope = host.Services.CreateAsyncScope(); var context = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>(); var authentication = scope.ServiceProvider.GetRequiredService<AuthenticationApplication>();
        var original = await context.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(x => x.AuthenticationMethods == "passkey+totp");
        var caller = StepUpCaller.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, actor.AccountId), new(AuthenticationIdentity.SessionClaim, original.SessionId.ToString("N")), new(AuthenticationIdentity.OriginClaim, Origin)], AuthenticationIdentity.Scheme)), Origin, new string('A', 64));
        var operation = new StepUpOperation("OP-composite-receipt-fixture", "fixture-composite-receipt", PasskeyApplication.RevocationScope, actor.PasskeyReference.ToString("N"), 1, new string('B', 64));
        await using var transaction = await context.Database.BeginTransactionAsync(); Assert.NotNull(await authentication.LockForOperationAsync(caller, operation.Scope.Operation, CancellationToken.None));
        var credential = await context.Set<PasskeyCredentialRow>().SingleAsync(); var passkeyPolicy = scope.ServiceProvider.GetRequiredService<IPasskeyPolicySource>().RequireEffective(host.Clock.GetUtcNow());
        credential.State = "REVOKED"; credential.RevokedAt = host.Clock.GetUtcNow(); credential.Version++; credential.RevocationSessionEffect = passkeyPolicy.RevocationSessionEffect.ToString(); credential.RevocationPolicyVersion = passkeyPolicy.Version; credential.RevocationOperationId = operation.OperationId; credential.RevocationCorrelationId = operation.CorrelationId;
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUserAdapter>>(); var user = await context.Users.SingleAsync(); Assert.True((await manager.RemovePasskeyAsync(user, credential.CredentialId)).Succeeded);
        var receipt = await authentication.EndCredentialActorSessionAsync(caller, operation, credential, passkeyPolicy, CancellationToken.None); Assert.NotNull(receipt);
        var unrelated = await context.Set<TotpAuthenticatorRow>().SingleAsync(); unrelated.State = "DISABLED"; unrelated.DisabledAt = host.Clock.GetUtcNow(); unrelated.Version++; unrelated.DisableSessionEffect = nameof(TotpSessionEffect.RevokeFactorSessions); unrelated.DisablePolicyVersion = actor.Totp.Policy.Version; unrelated.DisableOperationId = "OP-unrelated-fixture"; unrelated.DisableCorrelationId = "fixture-unrelated";
        await context.SaveChangesAsync();
        var verified = await authentication.VerifyTerminationAsync(caller, operation, receipt, receipt.PriorSessionVersion, host.Policies.Policy, host.Clock.GetUtcNow(), CancellationToken.None); Assert.Null(verified);
        await transaction.RollbackAsync();
        await using var read = db.Context(); Assert.Equal("ACTIVE", (await read.Set<TotpAuthenticatorRow>().SingleAsync()).State); Assert.Equal("ACTIVE", (await read.Set<PasskeyCredentialRow>().SingleAsync()).State);
    }
}
