using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;
using Tunner.Accounts;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class RegistrationTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    private const string Password = "a very long unique test passphrase";
    private const string OtherPassword = "a different long unique test passphrase";

    [Fact]
    public async Task RegistrationWaitsForProofThenCommitsCredentialStateAndOutboxTogether()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var caller = Caller(); var registered = await h.Register(caller, Command());
        Assert.Equal("REQUIRES_ACTION", registered.Status); Assert.Null(registered.AccountId);
        await using (var check = h.Database.Context())
        {
            Assert.Equal(AccountState.VerificationPending, (await check.Accounts.SingleAsync()).State);
            var user = await check.Users.SingleAsync(); Assert.Null(user.PasswordHash); Assert.False(user.EmailConfirmed);
            Assert.Equal(2, await check.Set<OutboxMessage>().CountAsync());
        }
        var code = h.Notices.Messages.Single().Value.Code!;
        var verified = await h.Verify(caller, new(registered.OperationId, Guid.NewGuid(), registered.Version, code));
        Assert.Equal("SUCCEEDED", verified.Status); Assert.NotNull(verified.AccountId);
        await using var context = h.Database.Context();
        Assert.Equal(AccountState.Active, (await context.Accounts.SingleAsync()).State);
        Assert.Equal(2, await context.AccountStateVersions.CountAsync());
        Assert.Equal(ContactVerificationState.Verified, (await context.VerifiedContacts.SingleAsync()).State);
        var credential = await context.Users.SingleAsync(); Assert.True(credential.EmailConfirmed);
        Assert.Equal(PasswordVerificationResult.Success, Verifier(h).VerifyHashedPassword(credential, credential.PasswordHash!, Password));
        var row = await context.Set<RegistrationOperationRow>().SingleAsync(); Assert.Null(row.ProtectedCode); Assert.Null(row.ProtectedCredential);
        var publicEvidence = JsonSerializer.Serialize(verified) + string.Join("", await context.Set<OutboxMessage>().Select(x=>x.Payload).ToArrayAsync());
        Assert.DoesNotContain(code, publicEvidence); Assert.DoesNotContain(Password, publicEvidence); Assert.DoesNotContain("1990-01-01", publicEvidence);
        Assert.Equal(nameof(RegisterAccountCommand), Command().ToString());
    }
    [Fact]
    public async Task UnderageProducesDerivedFailureWithoutAccountContactCredentialOrNotification()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var result = await h.Register(Caller(), Command(birth: new DateOnly(2015,1,1)));
        Assert.Equal("AGE_INELIGIBLE", result.ErrorCode); Assert.Empty(h.Notices.Messages);
        await using var check = h.Database.Context();
        Assert.False(await check.Accounts.AnyAsync()); Assert.False(await check.Users.AnyAsync()); Assert.False(await check.VerifiedContacts.AnyAsync());
        Assert.False(await check.Set<OutboxMessage>().AnyAsync());
        Assert.Empty((await check.Set<RegistrationOperationRow>().SingleAsync()).NormalizedEmail);
        Assert.DoesNotContain(check.Model.GetEntityTypes().SelectMany(x=>x.GetProperties()), p=>p.Name.Contains("Birth",StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public async Task ConcurrentRegistrationUsesOneCanonicalAccountAndBindsEachProposedCredentialToItsOwnProof()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var callerA = Caller(); var callerB = Caller();
        var results = await Task.WhenAll(h.Register(callerA, Command()), h.Register(callerB, Command(candidatePassword:OtherPassword)));
        await using (var check = h.Database.Context()) { Assert.Equal(1, await check.Accounts.CountAsync()); Assert.Equal(1, await check.Users.CountAsync()); Assert.Equal(2, await check.Set<RegistrationOperationRow>().CountAsync()); }
        var codeB = await h.CodeFor(results[1].OperationId);
        Assert.Equal("SUCCEEDED", (await h.Verify(callerB, new(results[1].OperationId,Guid.NewGuid(),1,codeB))).Status);
        await using var confirmed = h.Database.Context(); var user = await confirmed.Users.SingleAsync();
        var hasher = Verifier(h);
        Assert.Equal(PasswordVerificationResult.Success,hasher.VerifyHashedPassword(user,user.PasswordHash!,OtherPassword));
        Assert.Equal(PasswordVerificationResult.Failed,hasher.VerifyHashedPassword(user,user.PasswordHash!,Password));
        var firstCode = await h.CodeFor(results[0].OperationId);
        Assert.NotEqual("SUCCEEDED",(await h.Verify(callerA,new(results[0].OperationId,Guid.NewGuid(),1,firstCode))).Status);
    }
    [Fact]
    public async Task ConcurrentProofsActivateExactlyOnceAndNeverOverwriteTheWinningCredential()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var a=Caller();var b=Caller();var ra=await h.Register(a,Command());var rb=await h.Register(b,Command(candidatePassword:OtherPassword));
        var ca=await h.CodeFor(ra.OperationId);var cb=await h.CodeFor(rb.OperationId);
        var results=await Task.WhenAll(h.Verify(a,new(ra.OperationId,Guid.NewGuid(),1,ca)),h.Verify(b,new(rb.OperationId,Guid.NewGuid(),1,cb)));
        Assert.Single(results,x=>x.Status=="SUCCEEDED");
        await using var check=h.Database.Context();Assert.Equal(2,await check.AccountStateVersions.CountAsync());
        Assert.Equal(1,await check.Set<OutboxMessage>().CountAsync(x=>x.EventType=="AccountActivated"));
        var user=await check.Users.SingleAsync();var winningPassword=results[0].Status=="SUCCEEDED"?Password:OtherPassword;
        Assert.Equal(PasswordVerificationResult.Success,Verifier(h).VerifyHashedPassword(user,user.PasswordHash!,winningPassword));
    }
    [Fact]
    public async Task RegistrationAndSuccessfulVerificationReplayWithoutNewCanonicalEffects()
    {
        await using var h=await Harness.CreateAsync(fixture);var caller=Caller();var command=Command();
        var first=await h.Register(caller,command);var duplicate=await h.Register(caller,command);
        Assert.Equal(first,duplicate);Assert.Single(h.Notices.Messages);
        var verify=new VerifyRegistrationCommand(first.OperationId,Guid.NewGuid(),1,await h.CodeFor(first.OperationId));
        var result=await h.Verify(caller,verify);Assert.Equal(result,await h.Verify(caller,verify));
        await using var check=h.Database.Context();Assert.Equal(1,await check.Accounts.CountAsync());Assert.Equal(2,await check.AccountStateVersions.CountAsync());
    }
    [Fact]
    public async Task ResendReplayPreservesIdentityAndOldProofCannotActivate()
    {
        await using var h=await Harness.CreateAsync(fixture);var caller=Caller();var first=await h.Register(caller,Command());var old=await h.CodeFor(first.OperationId);
        h.Clock.Advance(h.Policy.ResendCooldown);var resend=new ResendRegistrationCommand(first.OperationId,Guid.NewGuid(),1);
        var current=await h.Resend(caller,resend);Assert.Equal(current,await h.Resend(caller,resend));Assert.Equal(2,current.Version);
        var fresh=await h.CodeFor(first.OperationId);Assert.NotEqual(old,fresh);
        var stale=await h.Verify(caller,new(first.OperationId,Guid.NewGuid(),2,old));Assert.NotNull(stale.ErrorCode);
        Assert.Equal("SUCCEEDED",(await h.Verify(caller,new(first.OperationId,Guid.NewGuid(),stale.Version,fresh))).Status);
    }
    [Fact]
    public async Task ExpiredProofAndStaleVersionCannotActivate()
    {
        await using var h=await Harness.CreateAsync(fixture);var caller=Caller();var first=await h.Register(caller,Command());var code=await h.CodeFor(first.OperationId);
        Assert.Equal("VERSION_CONFLICT",(await h.Verify(caller,new(first.OperationId,Guid.NewGuid(),999,code))).ErrorCode);
        h.Clock.Advance(h.Policy.ProofLifetime);
        Assert.Equal("NOT_FOUND_OR_NOT_DISCLOSABLE",(await h.Verify(caller,new(first.OperationId,Guid.NewGuid(),1,code))).ErrorCode);
        await using var check=h.Database.Context();Assert.Equal(AccountState.VerificationPending,(await check.Accounts.SingleAsync()).State);
    }
    [Fact]
    public async Task WrongProofAttemptsAndResendCooldownAreEnforcedFromPolicy()
    {
        await using var h=await Harness.CreateAsync(fixture);var caller=Caller();var first=await h.Register(caller,Command());var code=await h.CodeFor(first.OperationId);
        Assert.Equal("RATE_OR_ABUSE_LIMITED",(await h.Resend(caller,new(first.OperationId,Guid.NewGuid(),1))).ErrorCode);
        var version=1L;
        for(var i=0;i<h.Policy.ProofAttempts;i++){var bad=await h.Verify(caller,new(first.OperationId,Guid.NewGuid(),version,"not-a-code"));version=bad.Version;}
        Assert.NotEqual("SUCCEEDED",(await h.Verify(caller,new(first.OperationId,Guid.NewGuid(),version,code))).Status);
    }
    [Fact]
    public async Task CallerBindingAndActiveAccountResponsesDoNotExposeOrResetAnAccount()
    {
        await using var h=await Harness.CreateAsync(fixture);var caller=Caller();var first=await h.Register(caller,Command());var code=await h.CodeFor(first.OperationId);
        Assert.Equal("NOT_FOUND_OR_NOT_DISCLOSABLE",(await h.Verify(Caller(),new(first.OperationId,Guid.NewGuid(),1,code))).ErrorCode);
        await h.Verify(caller,new(first.OperationId,Guid.NewGuid(),1,code));
        var next=await h.Register(Caller(),Command(candidatePassword:OtherPassword));Assert.Equal("REQUIRES_ACTION",next.Status);Assert.Null(next.AccountId);
        await using var check=h.Database.Context();Assert.Equal(1,await check.Accounts.CountAsync());var user=await check.Users.SingleAsync();
        Assert.Equal(PasswordVerificationResult.Success,Verifier(h).VerifyHashedPassword(user,user.PasswordHash!,Password));
    }
    [Fact]
    public async Task MissingAndAmbiguousPolicyFailBeforeAnyAuthoritativeMutation()
    {
        await using var h=await Harness.CreateAsync(fixture);
        await using var db=h.Database.Context();var noPolicy=h.Application(db,new RegistrationPolicyCatalog([]));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>noPolicy.RegisterAsync(Caller(),Command()));
        var metadata=new PolicyVersionCandidate<string>("other","ACTIVE","REGISTRATION",new EffectivePolicyInterval(h.Clock.GetUtcNow().AddDays(-1),true,null,false),null,["test-only-approval"]);
        var ambiguous=new RegistrationPolicyCatalog([h.Version,new RegistrationPolicyVersion(metadata,h.Policy with {Version="other"})]);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Application(db,ambiguous).RegisterAsync(Caller(),Command()));
        Assert.False(await db.Accounts.AnyAsync());
    }
    [Fact]
    public async Task NetworkAbuseBudgetAndPasswordBlocklistDoNotCreateAccounts()
    {
        await using var h=await Harness.CreateAsync(fixture,registerLimit:1);var caller=Caller();
        var blocked=Command(candidatePassword:"compromised password value");Assert.Equal("VALIDATION_ERROR",(await h.Register(caller,blocked)).ErrorCode);
        Assert.Equal("RATE_OR_ABUSE_LIMITED",(await h.Register(caller,Command())).ErrorCode);
        await using var check=h.Database.Context();Assert.False(await check.Accounts.AnyAsync());
    }
    [Fact]
    public async Task PostCommitNotificationFailurePreservesCanonicalStateAndReferenceOnlyOutbox()
    {
        await using var h=await Harness.CreateAsync(fixture);h.Notices.Unavailable=true;
        var result=await h.Register(Caller(),Command());Assert.Equal("REQUIRES_ACTION",result.Status);
        await using var check=h.Database.Context();Assert.Equal(1,await check.Accounts.CountAsync());Assert.Equal(2,await check.Set<OutboxMessage>().CountAsync());
        Assert.All(await check.Set<OutboxMessage>().Select(x=>x.Payload).ToArrayAsync(),p=>Assert.DoesNotContain("example.test",p));
    }
    [Fact]
    public async Task MaterialExpiryPurgesPendingSecretsAndPopulatedDowngradeIsRefused()
    {
        await using var h=await Harness.CreateAsync(fixture);await h.Register(Caller(),Command());
        await using(var check=h.Database.Context())
        { var rejected=await Assert.ThrowsAsync<PostgresException>(()=>check.GetService<IMigrator>().MigrateAsync("20261016200608_P2IdentityFoundation")); Assert.Equal("23514",rejected.SqlState); }
        h.Clock.Advance(h.Policy.PendingMaterialRetention);
        await using var context=h.Database.Context();Assert.Equal(1,await h.Application(context).PurgeExpiredMaterialAsync(100));
        var row=await context.Set<RegistrationOperationRow>().SingleAsync();Assert.Null(row.ProtectedCredential);Assert.Null(row.ProtectedCode);Assert.Empty(row.NormalizedEmail);
        Assert.Equal(1,await context.Accounts.CountAsync());
    }
    [Fact]
    public async Task RecoveryIsBrowserBoundAndNotificationRetriesUseCommittedStableReferences()
    {
        await using var h=await Harness.CreateAsync(fixture); var caller=Caller(); var command=Command();
        h.Notices.Unavailable=true; var pending=await h.Register(caller,command);
        await using var db=h.Database.Context(); var app=h.Application(db);
        Assert.Equal(pending,await app.FindAsync(caller,command.IdempotencyKey));
        Assert.Equal("NOT_FOUND_OR_NOT_DISCLOSABLE",(await app.FindAsync(Caller(),command.IdempotencyKey)).ErrorCode);
        var row=await db.Set<RegistrationOperationRow>().AsNoTracking().SingleAsync();
        h.Notices.Unavailable=false; Assert.True(await app.DispatchNotificationAsync(row.DeliveryId!.Value));
        Assert.True(await app.DispatchNotificationAsync(row.DeliveryId.Value)); Assert.Single(h.Notices.Messages);
        Assert.DoesNotContain("example.test",JsonSerializer.Serialize(h.Notices.Messages.Single().Value));
        await h.Verify(caller,new(pending.OperationId,Guid.NewGuid(),1,await h.CodeFor(pending.OperationId)));
        Assert.False(await app.DispatchNotificationAsync(row.DeliveryId.Value));
    }
    [Fact]
    public async Task PasswordPolicyCountsUnicodeCodePointsAndNoticeVersionsMustBeCurrent()
    {
        await using var h=await Harness.CreateAsync(fixture); var caller=Caller();
        var stale=new RegisterAccountCommand { IdempotencyKey=Guid.NewGuid(),Email="applicant@example.test",Password=Password,DateOfBirth=new(1990,1,1),TermsVersion="old",PrivacyVersion=h.Policy.PrivacyVersion,AcknowledgedNotices=true };
        Assert.Equal("VALIDATION_ERROR",(await h.Register(caller,stale)).ErrorCode);
        Assert.Equal("VALIDATION_ERROR",(await h.Register(caller,Command(candidatePassword:string.Concat(Enumerable.Repeat("🔐",14))))).ErrorCode);
        var unicode=string.Concat(Enumerable.Repeat("🔐",15)); var pending=await h.Register(caller,Command(candidatePassword:unicode));
        Assert.Equal("SUCCEEDED",(await h.Verify(caller,new(pending.OperationId,Guid.NewGuid(),1,await h.CodeFor(pending.OperationId)))).Status);
        await using var check=h.Database.Context();var user=await check.Users.SingleAsync();
        Assert.Equal(PasswordVerificationResult.Success,Verifier(h).VerifyHashedPassword(user,user.PasswordHash!,unicode));
    }
    [Fact]
    public async Task ResendsStopAtTheEffectivePolicyBudget()
    {
        await using var h=await Harness.CreateAsync(fixture);var caller=Caller();var current=await h.Register(caller,Command());
        for(var i=0;i<h.Policy.ResendLimit;i++){h.Clock.Advance(h.Policy.ResendCooldown);current=await h.Resend(caller,new(current.OperationId,Guid.NewGuid(),current.Version));Assert.Null(current.ErrorCode);}
        h.Clock.Advance(h.Policy.ResendCooldown);
        Assert.Equal("RATE_OR_ABUSE_LIMITED",(await h.Resend(caller,new(current.OperationId,Guid.NewGuid(),current.Version))).ErrorCode);
    }
    private static PasswordHasher<IdentityUserAdapter> Verifier(Harness h)=>new(Options.Create(new PasswordHasherOptions { IterationCount=h.Policy.PasswordHashIterations }));
    private static RegisterAccountCommand Command(string candidatePassword=Password,DateOnly? birth=null)=>new(){IdempotencyKey=Guid.NewGuid(),Email="applicant@example.test",Password=candidatePassword,DateOfBirth=birth??new(1990,1,1),TermsVersion="fixture-terms",PrivacyVersion="fixture-privacy",AcknowledgedNotices=true};
    private static RegistrationCaller Caller()=>new(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    private sealed class Harness : IAsyncDisposable
    {
        public required IdentityTestDatabase Database {get;init;}
        public TestClock Clock {get;}=new();
        public NoticeSink Notices {get;}=new();
        private readonly EphemeralDataProtectionProvider protection=new();
        public required RegistrationPolicy Policy {get;init;}
        public RegistrationPolicyVersion Version=>new(new(Policy.Version,"ACTIVE","REGISTRATION",new EffectivePolicyInterval(Clock.GetUtcNow().AddYears(-1),true,null,false),null,["fixture-only-approval"]),Policy);
        public static async Task<Harness> CreateAsync(IdentityPostgresFixture fixture,int registerLimit=1000)=>new(){Database=await fixture.CreateDatabaseAsync(),Policy=new("fixture-registration","fixture-age","fixture-processing","fixture-retention","fixture-terms","fixture-privacy","fixture-verification-template","fixture-existing-template","PASSWORD_AND_VERIFIED_EMAIL",15,128,10000,6,TimeSpan.FromMinutes(5),2,TimeSpan.FromSeconds(30),2,TimeSpan.FromMinutes(1),registerLimit,1000,1000,1000,TimeSpan.FromMinutes(30),TimeSpan.FromHours(1))};
        public RegistrationApplication Application(IdentityPersistenceDbContext context,IRegistrationPolicySource? source=null)=>new(context,source??new RegistrationPolicyCatalog([Version]),new Blocklist(),protection,Notices,Clock);
        public async Task<RegistrationResult> Register(RegistrationCaller caller,RegisterAccountCommand command){await using var db=Database.Context();return await Application(db).RegisterAsync(caller,command);}
        public async Task<RegistrationResult> Verify(RegistrationCaller caller,VerifyRegistrationCommand command){await using var db=Database.Context();return await Application(db).VerifyAsync(caller,command);}
        public async Task<RegistrationResult> Resend(RegistrationCaller caller,ResendRegistrationCommand command){await using var db=Database.Context();return await Application(db).ResendAsync(caller,command);}
        public async Task<string> CodeFor(Guid id){await using var db=Database.Context();var row=await db.Set<RegistrationOperationRow>().SingleAsync(x=>x.OperationId==id);return protection.CreateProtector("Tunner.Identity.Registration.Challenge.v1").Unprotect(row.ProtectedCode!);}
        public ValueTask DisposeAsync()=>Database.DisposeAsync();
    }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now=new(2026,10,16,12,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance(TimeSpan interval)=>now+=interval;
    }
    private sealed class NoticeSink : IRegistrationNotificationDispatcher
    {
        public ConcurrentDictionary<Guid,RegistrationNotification> Messages {get;}=new();
        public bool Unavailable {get;set;}
        public Task QueueAsync(RegistrationNotification notification,CancellationToken cancellationToken)
        {if(Unavailable)throw new RegistrationNotificationUnavailableException();Messages.TryAdd(notification.DeliveryId,notification);return Task.CompletedTask;}
    }
    private sealed class Blocklist : IRegistrationPasswordBlocklist
    {
        public ValueTask<bool> IsBlockedAsync(string password,CancellationToken cancellationToken)=>ValueTask.FromResult(password=="compromised password value");
    }
}
