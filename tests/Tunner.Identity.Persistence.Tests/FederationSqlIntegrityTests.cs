using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Tunner.Accounts;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

// Public, nonfunctional persistence metadata only; these rows are not protocol proof or authentication authority.
// No ordinary xUnit dispatch or default fixture: only a separately registered finite owner may invoke these methods.
internal sealed class FederationSqlIntegrityTests(IdentityPostgresFixture fixture)
{
    private static readonly DateTimeOffset At = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private const string Public = "PUBLIC-NONFUNCTIONAL-FEDERATION";
    private const string Migration = "20261112145042_P2FederationSignIn";
    private static readonly string[] GuardNames =
        ["external_qualification_guard", "federation_attempt_guard", "session_federation_source_guard"];

    // Same public Account/session seed shapes as the existing finite identity and recovery SQL tests.
    private static AccountRow Account() => new()
    {
        AccountId = "ACC-" + Guid.NewGuid().ToString("N"), State = AccountState.Active, Version = 1,
        RecordedAtUtc = At, OperationId = Public, CorrelationId = Public, EvidenceReference = Public
    };

    private static ExternalIdentityLinkRow Link(string account) => new()
    {
        LinkId = Guid.NewGuid(), AccountId = account, Provider = "synthetic-stable-namespace",
        Subject = "SyntheticOpaqueSubject", Version = 1, EvidenceReference = Public
    };

    private static void Qualify(ExternalIdentityLinkRow row)
    {
        row.LinkState = "ACTIVE"; row.QualifiedClientId = "synthetic-client";
        row.QualificationProfileVersion = "synthetic-profile";
        row.IssuerMappingEvidenceReference = "synthetic-issuer-map";
        row.QualificationEvidenceReference = "synthetic-qualification";
    }

    private static AuthenticationSessionRow Session(string account) => new()
    {
        SessionId = Guid.NewGuid(), AccountId = account, KeyDigest = AuthenticationIdentity.Digest(Guid.NewGuid().ToString("N")),
        BrowserDigest = new('A', 64), IdempotencyKey = Guid.NewGuid(), IdentifierDigest = new('A', 64),
        SecurityStampDigest = new('B', 64), ProtectedKey = Public, ProtectedTicket = Public,
        Scheme = AuthenticationIdentity.Scheme, Origin = "https://federation.example.test", AuthenticationMethods = "pwd", Assurance = Public,
        AuthenticatedAt = At, LastSeenAt = At, ExpiresAt = At.AddMinutes(30), IdleTicks = TimeSpan.FromMinutes(5).Ticks,
        Version = 1, PolicyVersion = Public, AccessPolicyVersion = Public, AccessEvidenceReference = Public,
        ProcessingReference = Public, RetentionReference = Public, OperationId = Public, CorrelationId = Public
    };

    private static FederationAttemptRow Attempt() => new()
    {
        AttemptId = Guid.NewGuid(), RequestId = Guid.NewGuid(), CorrelationId = Public, Provider = "Google", Purpose = "SignIn",
        BrowserKey = Guid.NewGuid(), BrowserDigest = new('A', 64), Origin = "https://federation.example.test",
        PreviousCookieDigest = new('B', 64), AuthenticationPolicyVersion = Public, ProviderNamespace = "synthetic-stable-namespace",
        ClientId = "synthetic-client", ProfileVersion = "synthetic-profile", MappingEvidenceReference = "synthetic-issuer-map",
        ProfileSnapshot = "{}", ContinuationPath = "/", ProcessingReference = Public, RetentionReference = Public,
        ResultRecoveryLifetimeTicks = TimeSpan.FromMinutes(2).Ticks, CreatedAt = At, ExpiresAt = At.AddMinutes(5)
    };

    private static void Verify(FederationAttemptRow row, ExternalIdentityLinkRow link)
    {
        row.State = "VERIFIED"; row.VerifiedAt = At.AddSeconds(1); row.VerificationExpiresAt = At.AddMinutes(2);
        row.ValidatedIssuer = "https://issuer.example.test"; row.LinkId = link.LinkId; row.LinkVersion = link.Version;
        row.LinkQualificationEvidenceReference = link.QualificationEvidenceReference;
        row.LinkQualificationProfileVersion = link.QualificationProfileVersion;
    }

    private static async Task RejectSave(IdentityPersistenceDbContext context, string state = "23514")
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(state, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    public async Task LegacyQualificationRequiresFreshEvidenceAndPreservesClientIdentity()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        var account = Account(); var link = Link(account.AccountId);
        await using (var seed = db.Context())
        {
            seed.Accounts.Add(account); seed.ExternalIdentityLinks.Add(link); await seed.SaveChangesAsync();
        }
        await using (var read = db.Context())
        {
            var legacy = await read.ExternalIdentityLinks.SingleAsync();
            Assert.Equal("LEGACY_UNQUALIFIED", legacy.LinkState); Assert.Null(legacy.QualifiedClientId);
            Assert.Null(legacy.QualificationProfileVersion); Assert.Null(legacy.IssuerMappingEvidenceReference);
            Assert.Null(legacy.QualificationEvidenceReference);
        }
        await using (var incomplete = db.Context())
        {
            var row = await incomplete.ExternalIdentityLinks.SingleAsync(); row.LinkState = "ACTIVE";
            row.EvidenceReference = "synthetic-promote"; row.Version++; await RejectSave(incomplete);
        }
        await using (var stale = db.Context())
        {
            var row = await stale.ExternalIdentityLinks.SingleAsync(); Qualify(row); row.Version++;
            await RejectSave(stale);
        }
        await using (var promote = db.Context())
        {
            var row = await promote.ExternalIdentityLinks.SingleAsync(); Qualify(row);
            row.EvidenceReference = "synthetic-promote"; row.Version++; await promote.SaveChangesAsync();
        }
        await using (var client = db.Context())
        {
            var row = await client.ExternalIdentityLinks.SingleAsync(); row.QualifiedClientId = "synthetic-other-client";
            row.EvidenceReference = "synthetic-client-rewrite"; row.Version++; await RejectSave(client);
        }
        await using (var staleUnlink = db.Context())
        {
            var row = await staleUnlink.ExternalIdentityLinks.SingleAsync(); row.LinkState = "UNLINKED"; row.Version++;
            await RejectSave(staleUnlink);
        }
        await using (var rewriteOnUnlink = db.Context())
        {
            var row = await rewriteOnUnlink.ExternalIdentityLinks.SingleAsync(); row.LinkState = "UNLINKED";
            row.QualificationEvidenceReference = "synthetic-rewritten-qualification";
            row.EvidenceReference = "synthetic-unlink"; row.Version++; await RejectSave(rewriteOnUnlink);
        }
        await using (var unlink = db.Context())
        {
            var row = await unlink.ExternalIdentityLinks.SingleAsync(); row.LinkState = "UNLINKED";
            row.EvidenceReference = "synthetic-unlink"; row.Version++; await unlink.SaveChangesAsync();
        }
        await using (var staleRelink = db.Context())
        {
            var row = await staleRelink.ExternalIdentityLinks.SingleAsync(); row.LinkState = "ACTIVE"; row.Version++;
            await RejectSave(staleRelink);
        }
        await using (var relink = db.Context())
        {
            var row = await relink.ExternalIdentityLinks.SingleAsync(); row.LinkState = "ACTIVE";
            row.QualificationProfileVersion = "synthetic-profile-v2"; row.QualificationEvidenceReference = "synthetic-requalification";
            row.EvidenceReference = "synthetic-relink"; row.Version++; await relink.SaveChangesAsync();
        }
        await using var final = db.Context(); var current = await final.ExternalIdentityLinks.SingleAsync();
        Assert.Equal("ACTIVE", current.LinkState); Assert.Equal(4, current.Version);
        Assert.Equal(account.AccountId, current.AccountId); Assert.Equal(link.Provider, current.Provider); Assert.Equal(link.Subject, current.Subject);
        Assert.Equal("synthetic-client", current.QualifiedClientId); Assert.Equal("synthetic-profile-v2", current.QualificationProfileVersion);
        Assert.Equal("synthetic-requalification", current.QualificationEvidenceReference);
    }

    public async Task AttemptsPreserveBindingVerifiedTupleAndOneUseTerminalResult()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        var account = Account(); var link = Link(account.AccountId); Qualify(link);
        var attempt = Attempt(); var result = Session(account.AccountId);
        await using (var seed = db.Context())
        {
            seed.Accounts.Add(account); seed.ExternalIdentityLinks.Add(link); seed.Add(attempt); seed.Add(result); await seed.SaveChangesAsync();
        }
        await using (var duplicate = db.Context())
        {
            var row = Attempt(); row.RequestId = attempt.RequestId; duplicate.Add(row); await RejectSave(duplicate, "23505");
        }
        await using (var insertedAdmitted = db.Context())
        {
            var row = Attempt(); row.State = "CHALLENGE_ADMITTED"; insertedAdmitted.Add(row); await RejectSave(insertedAdmitted);
        }
        foreach (var change in AttemptBindingChanges)
        {
            await using var context = db.Context(); var row = await context.Set<FederationAttemptRow>().SingleAsync();
            row.State = "CHALLENGE_ADMITTED"; row.Version++; change(row); await RejectSave(context);
        }
        await using (var skipped = db.Context())
        {
            var row = await skipped.Set<FederationAttemptRow>().SingleAsync(); Verify(row, link); row.Version++; await RejectSave(skipped);
        }
        await using (var admitted = db.Context())
        {
            var row = await admitted.Set<FederationAttemptRow>().SingleAsync(); row.State = "CHALLENGE_ADMITTED"; row.Version++;
            await admitted.SaveChangesAsync();
        }
        await using (var partial = db.Context())
        {
            var row = await partial.Set<FederationAttemptRow>().SingleAsync(); Verify(row, link);
            row.LinkQualificationEvidenceReference = null; row.Version++; await RejectSave(partial);
        }
        await using (var verified = db.Context())
        {
            var row = await verified.Set<FederationAttemptRow>().SingleAsync(); Verify(row, link); row.Version++; await verified.SaveChangesAsync();
        }
        await using (var missingResult = db.Context())
        {
            var row = await missingResult.Set<FederationAttemptRow>().SingleAsync(); Complete(row, result.SessionId);
            row.ResultSessionId = null; await RejectSave(missingResult);
        }
        foreach (var change in VerifiedTupleChanges)
        {
            await using var rewritten = db.Context(); var row = await rewritten.Set<FederationAttemptRow>().SingleAsync();
            Complete(row, result.SessionId); change(row); await RejectSave(rewritten);
        }
        await using (var complete = db.Context())
        {
            var row = await complete.Set<FederationAttemptRow>().SingleAsync(); Complete(row, result.SessionId); await complete.SaveChangesAsync();
        }
        await using (var reopen = db.Context())
        {
            var row = await reopen.Set<FederationAttemptRow>().SingleAsync(); row.State = "VERIFIED";
            row.EndedAt = null; row.ResultExpiresAt = null; row.ResultSessionId = null; row.Version++; await RejectSave(reopen);
        }
        await using (var replaceResult = db.Context())
        {
            var row = await replaceResult.Set<FederationAttemptRow>().SingleAsync(); row.ResultSessionId = Guid.NewGuid(); row.Version++;
            await RejectSave(replaceResult);
        }
        await using var final = db.Context(); var current = await final.Set<FederationAttemptRow>().SingleAsync();
        Assert.Equal("COMPLETED", current.State); Assert.Equal(4, current.Version); Assert.Equal(result.SessionId, current.ResultSessionId);
        Assert.Equal(attempt.RequestId, current.RequestId); Assert.Equal(attempt.BrowserDigest, current.BrowserDigest);
        Assert.Equal(link.LinkId, current.LinkId); Assert.Equal(link.Version, current.LinkVersion);
        Assert.Equal(At.AddMinutes(2), current.VerificationExpiresAt); Assert.Null(current.PendingMfaId); Assert.Null(current.ErrorCode);
    }

    private static void Complete(FederationAttemptRow row, Guid session)
    {
        row.State = "COMPLETED"; row.ResultSessionId = session; row.EndedAt = At.AddSeconds(2);
        row.ResultExpiresAt = At.AddMinutes(4); row.Version++;
    }

    private static readonly Action<FederationAttemptRow>[] AttemptBindingChanges =
    [
        row => row.RequestId = Guid.NewGuid(), row => row.BrowserKey = Guid.NewGuid(), row => row.BrowserDigest = new('C', 64),
        row => row.PreviousCookieDigest = new('C', 64), row => row.Origin = "https://other.example.test",
        row => row.ClientId = "synthetic-other-client", row => row.ProviderNamespace = "synthetic-other-namespace",
        row => row.ProfileVersion = "synthetic-other-profile", row => row.MappingEvidenceReference = "synthetic-other-map",
        row => row.ProfileSnapshot = "{\"changed\":true}", row => row.RememberMe = true, row => row.ExpiresAt = At.AddMinutes(6)
    ];

    private static readonly Action<FederationAttemptRow>[] VerifiedTupleChanges =
    [
        row => row.VerifiedAt = At.AddSeconds(2), row => row.VerificationExpiresAt = At.AddMinutes(3),
        row => row.ValidatedIssuer = "https://other.example.test", row => row.LinkId = Guid.NewGuid(), row => row.LinkVersion++,
        row => row.LinkQualificationEvidenceReference = "synthetic-other-qualification",
        row => row.LinkQualificationProfileVersion = "synthetic-other-qualification-profile"
    ];

    public async Task FederatedSessionSourceIsImmutableAndPasswordGuardsRemainActive()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        var account = Account(); var link = Link(account.AccountId); Qualify(link); var attempt = Attempt();
        var password = Session(account.AccountId); var federated = Session(account.AccountId);
        federated.AuthenticationMethods = "federation";
        new FederationAuthenticationSource(attempt.AttemptId, FederationProviderKind.Google, link.LinkId, link.Version,
            link.Provider, link.Subject, "synthetic-client", "https://issuer.example.test", "synthetic-profile", "synthetic-issuer-map",
            "synthetic-profile", "synthetic-qualification", Public, Public, Public, Public, At.AddMinutes(2)).Store(federated);
        await using (var seed = db.Context())
        {
            seed.Accounts.Add(account); seed.ExternalIdentityLinks.Add(link); seed.Add(attempt); seed.Add(password);
            await seed.SaveChangesAsync();
            attempt.State = "CHALLENGE_ADMITTED"; attempt.Version++; await seed.SaveChangesAsync();
            Verify(attempt, link); attempt.Version++; await seed.SaveChangesAsync();
            seed.Add(federated); await seed.SaveChangesAsync(); Complete(attempt, federated.SessionId); await seed.SaveChangesAsync();
        }
        foreach (var change in SessionSourceChanges)
        {
            await using var context = db.Context(); var row = await context.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == federated.SessionId);
            change(row); row.Version++; await RejectSave(context);
        }
        await using (var partial = db.Context())
        {
            var row = Session(account.AccountId); row.AuthenticationMethods = "federation"; partial.Add(row); await RejectSave(partial);
        }
        await using (var pollutedPassword = db.Context())
        {
            var row = Session(account.AccountId); row.FederationClientId = "synthetic-client"; pollutedPassword.Add(row); await RejectSave(pollutedPassword);
        }
        await using (var foreignOwner = db.Context())
        {
            var other = Account(); foreignOwner.Accounts.Add(other); await foreignOwner.SaveChangesAsync();
            var row = Session(other.AccountId); row.AuthenticationMethods = "federation";
            FederationAuthenticationSource.Read(federated, link)!.Store(row); foreignOwner.Add(row); await RejectSave(foreignOwner, "23503");
        }
        await using (var originalGuard = db.Context())
        {
            var row = await originalGuard.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == password.SessionId);
            row.KeyDigest = new('C', 64); row.Version++; await RejectSave(originalGuard);
        }
        await using (var touch = db.Context())
        {
            foreach (var row in await touch.Set<AuthenticationSessionRow>().ToArrayAsync()) { row.LastSeenAt = At.AddSeconds(1); row.Version++; }
            await touch.SaveChangesAsync();
        }
        await using var final = db.Context(); var savedFederated = await final.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == federated.SessionId);
        var savedPassword = await final.Set<AuthenticationSessionRow>().SingleAsync(x => x.SessionId == password.SessionId);
        Assert.Equal(2, savedFederated.Version); Assert.Equal(2, savedPassword.Version);
        Assert.Equal(federated.FederationAttemptId, savedFederated.FederationAttemptId);
        Assert.Equal(federated.FederationLinkId, savedFederated.FederationLinkId); Assert.Equal(federated.FederationClientId, savedFederated.FederationClientId);
        Assert.Equal("pwd", savedPassword.AuthenticationMethods); Assert.Null(savedPassword.FederationAttemptId);
        Assert.Null(savedPassword.FederationProvider); Assert.Null(savedPassword.FederationLinkId); Assert.Null(savedPassword.FederationClientId);
        Assert.Equal(password.KeyDigest, savedPassword.KeyDigest);
    }

    private static readonly Action<AuthenticationSessionRow>[] SessionSourceChanges =
    [
        row => row.FederationAttemptId = Guid.NewGuid(), row => row.FederationProvider = "Microsoft",
        row => row.FederationLinkId = Guid.NewGuid(), row => row.FederationLinkVersion++,
        row => row.FederationNamespace = "synthetic-other-namespace", row => row.FederationClientId = "synthetic-other-client",
        row => row.FederationIssuer = "https://other.example.test", row => row.FederationProfileVersion = "synthetic-other-profile",
        row => row.FederationIssuerMappingEvidenceReference = "synthetic-other-map",
        row => row.FederationQualificationProfileVersion = "synthetic-other-qualification-profile",
        row => row.FederationQualificationEvidenceReference = "synthetic-other-qualification",
        row => row.FederationSignInAdmissionPolicyVersion = "synthetic-other-admission",
        row => row.FederationAssuranceMappingEvidenceReference = "synthetic-other-assurance-map",
        row => row.FederationSessionUsePolicyVersion = "synthetic-other-session-use", row => row.FederationPrimaryAssurance = "synthetic-other-assurance"
    ];

    public async Task MigrationRoundTripInstallsGuardsAndRejectsPopulatedDown()
    {
        await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None);
        await using (var empty = db.Context())
        {
            var migrations = empty.Database.GetMigrations().ToArray(); var index = Array.IndexOf(migrations, Migration);
            Assert.True(index > 0); Assert.Equal(migrations, await empty.Database.GetAppliedMigrationsAsync());
            await empty.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
            Assert.DoesNotContain(Migration, await empty.Database.GetAppliedMigrationsAsync());
            await empty.GetService<IMigrator>().MigrateAsync();
            Assert.Equal(migrations, await empty.Database.GetAppliedMigrationsAsync()); Assert.False(empty.Database.HasPendingModelChanges());
            var guards = await empty.Database.SqlQueryRaw<string>("""
                SELECT t.tgname::text AS "Value" FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
                JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='identity' AND NOT t.tgisinternal AND t.tgenabled='O'
                AND ((c.relname='external_identity_links' AND t.tgname='external_qualification_guard')
                  OR (c.relname='federation_attempts' AND t.tgname='federation_attempt_guard')
                  OR (c.relname='tunner_sessions' AND t.tgname='session_federation_source_guard'))
                ORDER BY t.tgname
                """).ToArrayAsync();
            Assert.Equal(GuardNames, guards);
            empty.Accounts.Add(Account()); await empty.SaveChangesAsync();
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => empty.GetService<IMigrator>().MigrateAsync(migrations[index - 1]));
            Assert.Equal("23514", rejected.SqlState);
        }
        await using var final = db.Context();
        Assert.Contains(Migration, await final.Database.GetAppliedMigrationsAsync()); Assert.Equal(1, await final.Accounts.CountAsync());
        Assert.Equal(0, await final.Set<FederationAttemptRow>().CountAsync());
    }
}
