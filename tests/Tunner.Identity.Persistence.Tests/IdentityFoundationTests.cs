using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenIddict.Abstractions;
using Tunner.Accounts;
using Tunner.Database.Outbox;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class IdentityFoundationTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MigrationRoundTripOwnsAccountStateAndExcludesRawDob()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        await using var context = db.Context();
        var account = Row();
        account.AgeEligibilityStatus = "ELIGIBLE";
        account.AgeEvaluatedAtUtc = RecordedAt;
        account.AgePolicyVersion = "age-policy-v1";
        account.AgeEvidenceReference = "test-age-evidence";
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        var snapshot = await context.ReadAccountAsync(account.AccountId);
        Assert.NotNull(snapshot);
        Assert.Equal(account.AccountId, snapshot.AccountId);
        Assert.Equal(AccountState.VerificationPending, snapshot.State);
        Assert.Equal(AgeEligibilityStatus.Eligible, snapshot.AgeEligibilityStatus);
        Assert.Equal(1, await context.AccountStateVersions.CountAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.DoesNotContain(context.Model.GetEntityTypes().SelectMany(t => t.GetProperties()), p =>
            p.Name.Contains("birth", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("dob", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.Model.GetEntityTypes(), t => t.GetTableName() == "AspNetUserPasskeys");
    }

    [Fact]
    public async Task VerifiedEmailUniquenessIsAtomicWhileUnverifiedCandidatesDoNotReplaceIt()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        var a = Row(); var b = Row();
        await using (var seed = db.Context()) { seed.Accounts.AddRange(a,b); await seed.SaveChangesAsync(); }
        await using var first = db.Context(); await using var second = db.Context();
        first.VerifiedContacts.Add(Contact(a.AccountId, ContactVerificationState.Verified));
        second.VerifiedContacts.Add(Contact(b.AccountId, ContactVerificationState.Verified));
        var results = await Task.WhenAll(TrySave(first), TrySave(second));
        Assert.Single(results, x => x);
        await using var check = db.Context();
        Assert.Equal(1, await check.VerifiedContacts.CountAsync(x => x.State == ContactVerificationState.Verified));
        var candidate = Contact(b.AccountId, ContactVerificationState.Unverified);
        check.VerifiedContacts.Add(candidate);
        await check.SaveChangesAsync();
        candidate.State = ContactVerificationState.Verified; candidate.Version++;
        await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
    }

    [Fact]
    public async Task ExternalIdentityUsesProviderAndImmutableSubjectRatherThanEmail()
    {
        await using var db = await fixture.CreateDatabaseAsync();
        var a=Row(); var b=Row();
        await using (var context=db.Context())
        {
            context.Accounts.AddRange(a,b);
            context.ExternalIdentityLinks.AddRange(Link(a.AccountId,"Google"),Link(a.AccountId,"Microsoft"));
            await context.SaveChangesAsync();
        }
        await using var duplicate=db.Context();
        duplicate.ExternalIdentityLinks.Add(Link(b.AccountId,"Google"));
        await Assert.ThrowsAsync<DbUpdateException>(()=>duplicate.SaveChangesAsync());
        await using var mutate=db.Context();
        var link=await mutate.ExternalIdentityLinks.FirstAsync(); link.Subject="replacement";link.Version++;
        await Assert.ThrowsAsync<DbUpdateException>(()=>mutate.SaveChangesAsync());
        Assert.DoesNotContain(typeof(ExternalIdentityLinkRow).GetProperties(),p=>p.Name.Contains("email",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AccountConcurrencyAdvancesOneVersionAndPreservesHistory()
    {
        await using var db=await fixture.CreateDatabaseAsync();var account=Row();
        await using(var seed=db.Context()){seed.Accounts.Add(account);await seed.SaveChangesAsync();}
        await using var first=db.Context();await using var second=db.Context();
        var a=await first.Accounts.SingleAsync();var b=await second.Accounts.SingleAsync();
        a.State=AccountState.Restricted;a.Version++;a.EvidenceReference="restriction-evidence";
        b.State=AccountState.Closing;b.Version++;b.EvidenceReference="closing-evidence";
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>second.SaveChangesAsync());
        await using var check=db.Context();
        Assert.Equal(2,await check.AccountStateVersions.CountAsync());
        Assert.Equal(AccountState.Restricted,(await check.ReadAccountAsync(account.AccountId))!.State);
    }

    [Theory]
    [InlineData("UPDATE accounts.account_state_versions SET state='ACTIVE'")]
    [InlineData("DELETE FROM accounts.account_state_versions")]
    [InlineData("TRUNCATE accounts.account_state_versions")]
    [InlineData("DELETE FROM accounts.accounts")]
    [InlineData("UPDATE accounts.accounts SET account_id='ACC-replacement',version=version+1")]
    [InlineData("UPDATE accounts.accounts SET state='RESTRICTED'")]
    public async Task DirectSqlCannotRewriteIdentityOrBypassVersionEvidence(string sql)
    {
        await using var db=await fixture.CreateDatabaseAsync();await using var context=db.Context();
        var account=Row();context.Accounts.Add(account);await context.SaveChangesAsync();
        await Assert.ThrowsAsync<PostgresException>(()=>context.Database.ExecuteSqlRawAsync(sql));
        Assert.NotNull(await context.ReadAccountAsync(account.AccountId));
        Assert.Equal(1,await context.AccountStateVersions.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveAccountCannotOmitEligibleAgeAndItsProvenance(bool partial)
    {
        await using var db=await fixture.CreateDatabaseAsync();await using var context=db.Context();
        var account=Row();account.State=AccountState.Active;
        if(partial){account.AgeEligibilityStatus="ELIGIBLE";account.AgeEvaluatedAtUtc=RecordedAt;}
        context.Accounts.Add(account);
        await Assert.ThrowsAsync<DbUpdateException>(()=>context.SaveChangesAsync());
    }

    [Fact]
    public async Task EmptyDowngradeReappliesAndPopulatedDowngradeRefusesDataLoss()
    {
        await using var db=await fixture.CreateDatabaseAsync();await using var context=db.Context();
        await context.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase);
        Assert.Empty(await context.Database.GetAppliedMigrationsAsync());
        await context.Database.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        context.Accounts.Add(Row());await context.SaveChangesAsync();
        await Assert.ThrowsAsync<PostgresException>(()=>context.GetService<IMigrator>().MigrateAsync(Migration.InitialDatabase));
        Assert.Equal(1,await context.Accounts.CountAsync());
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        Assert.False(await context.Set<OutboxMessage>().AnyAsync());
    }

    [Fact]
    public async Task FrameworkStoresRoundTripWithoutActivatingAuthenticationOrTokenServer()
    {
        await using var db=await fixture.CreateDatabaseAsync();var account=Row();
        await using(var seed=db.Context()){seed.Accounts.Add(account);await seed.SaveChangesAsync();}
        await using var scope=db.Services.CreateAsyncScope();
        var store=scope.ServiceProvider.GetRequiredService<IUserStore<IdentityUserAdapter>>();
        var user=new IdentityUserAdapter{AccountId=account.AccountId,UserName="synthetic-user",NormalizedUserName="SYNTHETIC-USER"};
        Assert.True((await store.CreateAsync(user,CancellationToken.None)).Succeeded);
        Assert.NotEqual(account.AccountId,user.Id);
        Assert.Equal(account.AccountId,(await store.FindByIdAsync(user.Id,CancellationToken.None))!.AccountId);
        var manager=scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application=await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId="synthetic-client", ClientType=OpenIddictConstants.ClientTypes.Public,
            ConsentType=OpenIddictConstants.ConsentTypes.Explicit
        });
        Assert.NotNull(await manager.FindByClientIdAsync("synthetic-client"));
        Assert.NotNull(application);
        Assert.Null(scope.ServiceProvider.GetService<UserManager<IdentityUserAdapter>>());
        Assert.Null(scope.ServiceProvider.GetService<IAuthenticationSchemeProvider>());
    }

    [Fact]
    public async Task AccountAndSharedOutboxCommitOrRollbackTogether()
    {
        await using var db=await fixture.CreateDatabaseAsync();var coordinator=new EfTransactionalOutbox();
        var eventId=Guid.NewGuid();var first=Row();
        await using(var context=db.Context())
            await coordinator.PersistAsync(context,async (owner,ct)=>{owner.Accounts.Add(first);await owner.SaveChangesAsync(ct);},Message(eventId));
        var rejected=Row();
        await using(var context=db.Context())
            await Assert.ThrowsAsync<DbUpdateException>(()=>coordinator.PersistAsync(context,async(owner,ct)=>{owner.Accounts.Add(rejected);await owner.SaveChangesAsync(ct);},Message(eventId)));
        await using var check=db.Context();
        Assert.NotNull(await check.ReadAccountAsync(first.AccountId));
        Assert.Null(await check.ReadAccountAsync(rejected.AccountId));
        Assert.Equal(1,await check.Set<OutboxMessage>().CountAsync());
        Assert.Equal(1,await check.AccountStateVersions.CountAsync());
    }

    [Fact]
    public void PersistenceRequiresAnExplicitConnection() => Assert.Throws<ArgumentException>(()=>
        new ServiceCollection().AddTunnerIdentityPersistence(" "));

    private static AccountRow Row()=>new()
    {
        AccountId="ACC-"+Guid.NewGuid().ToString("N"),State=AccountState.VerificationPending,Version=1,
        RecordedAtUtc=RecordedAt,OperationId="synthetic-operation",CorrelationId="synthetic-correlation",EvidenceReference="synthetic-evidence"
    };
    private static VerifiedContactRow Contact(string accountId,ContactVerificationState state)=>new()
    {
        ContactId=Guid.NewGuid(),AccountId=accountId,NormalizedEmail="PERSON@EXAMPLE.TEST",State=state,Version=1,EvidenceReference="synthetic-contact-evidence"
    };
    private static ExternalIdentityLinkRow Link(string accountId,string provider)=>new()
    {
        LinkId=Guid.NewGuid(),AccountId=accountId,Provider=provider,Subject="immutable-subject",Version=1,EvidenceReference="synthetic-link-evidence"
    };
    private static OutboxMessage Message(Guid eventId)=>new(eventId,"1","Technical.IdentityStorageProbe","test-only-v1",RecordedAt,
        "urn:tunner:technical-test","test-resource","INTERNAL","{}",operationId:"synthetic-operation",correlationId:"synthetic-correlation");
    private static async Task<bool> TrySave(IdentityPersistenceDbContext context)
    {
        try{await context.SaveChangesAsync();return true;}
        catch(DbUpdateException e) when(e.InnerException is PostgresException{SqlState:"23505"}){return false;}
    }
}
