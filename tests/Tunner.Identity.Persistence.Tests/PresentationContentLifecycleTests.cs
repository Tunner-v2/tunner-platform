using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Text.Json;
using Npgsql;
using Tunner.Database;
using Tunner.Database.Audit;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class PresentationContentLifecycleTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task ValidatedApprovedPublicationResolvesWithImmutableProvenanceAndReferenceOnlyEffects()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        var publication = await env.PublishTextAsync("login.heading", "Fixture original login text");
        await using var scope = env.Open(); var resolved = await scope.Application.ResolveAsync(new(env.Scope, env.Variant, env.Clock.GetUtcNow()));
        var text = Assert.Single(resolved.Content).Value; Assert.Equal("Fixture original login text", text.Text);
        Assert.Equal(publication.PublicationReference.ToString("N"), text.PublicationReference); Assert.Equal(publication.VersionReference.ToString("N"), text.VersionReference);
        Assert.Equal(resolved.Locale, text.Locale); Assert.Equal(resolved.EffectiveVariantReference, text.VariantReference);
        await using var platform = env.Platform(); var audits = await platform.AuditEvents.ToArrayAsync(); var events = await platform.OutboxMessages.ToArrayAsync();
        Assert.Equal(4, audits.Length); Assert.Equal(4, events.Length); Assert.All(audits, x => Assert.True(x.VerifyIntegrity()));
        Assert.All(audits, x => Assert.DoesNotContain(text.Text, x.Envelope, StringComparison.Ordinal));
        Assert.All(events, x => Assert.DoesNotContain(text.Text, x.Payload, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DraftAndValidationCannotBypassRequiredApprovalOrBecomePublic()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        await using var s = env.Open(); var proposal = await s.Application.CreateDraftAsync(env.Draft("Fixture candidate"));
        Assert.Throws<ContentUnavailableException>(() => s.Application.Resolve(new(env.Scope, env.Variant, env.Clock.GetUtcNow())));
        proposal = await s.Application.ValidateAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-publisher";
        await Assert.ThrowsAsync<ContentConflictException>(() => s.Application.PublishAsync(env.Publish(proposal)));
    }

    [Theory]
    [InlineData(ContentOperation.CreateDraft)]
    [InlineData(ContentOperation.Validate)]
    [InlineData(ContentOperation.Approve)]
    [InlineData(ContentOperation.Publish)]
    public async Task ActualServerAuthorizationIsRequiredAtEachWriteBoundary(ContentOperation denied)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        await using var s = env.Open(); env.Authorization.Denied = denied;
        if (denied == ContentOperation.CreateDraft) { await Assert.ThrowsAsync<ContentForbiddenException>(() => s.Application.CreateDraftAsync(env.Draft("fixture"))); return; }
        var p = await s.Application.CreateDraftAsync(env.Draft("fixture"));
        if (denied == ContentOperation.Validate) { await Assert.ThrowsAsync<ContentForbiddenException>(() => s.Application.ValidateAsync(env.Command(p))); return; }
        p = await s.Application.ValidateAsync(env.Command(p)); env.Authorization.Actor = "fixture-approver";
        if (denied == ContentOperation.Approve) { await Assert.ThrowsAsync<ContentForbiddenException>(() => s.Application.ApproveAsync(env.Command(p))); return; }
        p = await s.Application.ApproveAsync(env.Command(p)); env.Authorization.Actor = "fixture-publisher";
        await Assert.ThrowsAsync<ContentForbiddenException>(() => s.Application.PublishAsync(env.Publish(p)));
    }

    [Theory]
    [InlineData("author-approves")]
    [InlineData("author-publishes")]
    [InlineData("approver-publishes")]
    public async Task QualifiedSeparationOfDutiesIncludesActualContributors(string violation)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        await using var s = env.Open(); var p = await s.Application.CreateDraftAsync(env.Draft("fixture")); p = await s.Application.ValidateAsync(env.Command(p));
        if (violation == "author-approves") { await Assert.ThrowsAsync<ContentForbiddenException>(() => s.Application.ApproveAsync(env.Command(p))); return; }
        env.Authorization.Actor = "fixture-approver"; p = await s.Application.ApproveAsync(env.Command(p));
        env.Authorization.Actor = violation == "author-publishes" ? "fixture-author" : "fixture-approver";
        await Assert.ThrowsAsync<ContentForbiddenException>(() => s.Application.PublishAsync(env.Publish(p)));
    }

    [Fact]
    public async Task ModelCanProposeOnlyAndSpoofedAuthorizationDigestCannotWrite()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        await using var s = env.Open(); env.Authorization.Kind = ContentActorKind.Model;
        var p = await s.Application.CreateDraftAsync(env.Draft("fixture model proposal"));
        Assert.True(p.ModelOrigin); Assert.Equal("DRAFT", p.State);
        await Assert.ThrowsAsync<ContentForbiddenException>(() => s.Application.ValidateAsync(env.Command(p)));
        env.Authorization.Kind = ContentActorKind.Human; env.Authorization.WrongDigest = true;
        await Assert.ThrowsAsync<ContentForbiddenException>(() => s.Application.UpdateDraftAsync(new(p.ProposalReference, p.Revision, "spoofed", "OP-fixture", "fixture-correlation")));
    }

    [Fact]
    public async Task RejectedFinalAuthorizationRollsBackContentAndAuditTogether()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        env.Authorization.OnCall = (_, call) => { if (call == 2) env.Authorization.Available = false; };
        await using (var s = env.Open()) await Assert.ThrowsAsync<ContentForbiddenException>(() => s.Application.CreateDraftAsync(env.Draft("fixture rollback")));
        await using var context = env.Context(); Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM presentation_content.proposals").SingleAsync());
        await using var platform = env.Platform(); Assert.Empty(await platform.AuditEvents.ToArrayAsync()); Assert.Empty(await platform.OutboxMessages.ToArrayAsync());
    }

    [Fact]
    public async Task StaleRevisionAndEditedApprovalCannotPublish()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        ContentProposalView original; await using (var s = env.Open()) original = await s.Application.CreateDraftAsync(env.Draft("old"));
        ContentProposalView changed; await using (var s = env.Open()) changed = await s.Application.UpdateDraftAsync(new(original.ProposalReference, original.Revision, "new", "OP-change", "fixture-correlation"));
        await using (var s = env.Open()) await Assert.ThrowsAsync<ContentConflictException>(() => s.Application.ValidateAsync(env.Command(original)));
        await using (var s = env.Open())
        {
            var p = await s.Application.ValidateAsync(env.Command(changed)); env.Authorization.Actor = "fixture-approver"; p = await s.Application.ApproveAsync(env.Command(p));
            env.Authorization.Actor = "fixture-second-author"; var draft = await s.Application.UpdateDraftAsync(new(p.ProposalReference, p.Revision, "edited after approval", "OP-edit", "fixture-correlation"));
            env.Authorization.Actor = "fixture-publisher"; await Assert.ThrowsAsync<ContentConflictException>(() => s.Application.PublishAsync(env.Publish(draft)));
        }
    }

    [Fact]
    public async Task TwoProposalsInOneApplicationKeepEachApprovalBoundToItsExactVersion()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        await using var scope = env.Open();
        var first = await scope.Application.CreateDraftAsync(env.Draft("first candidate")); first = await scope.Application.ValidateAsync(env.Command(first));
        var second = await scope.Application.CreateDraftAsync(env.Draft("second candidate")); second = await scope.Application.ValidateAsync(env.Command(second));
        Assert.NotEqual(first.VersionReference, second.VersionReference);
        env.Authorization.Actor = "fixture-approver"; second = await scope.Application.ApproveAsync(env.Command(second));
        env.Authorization.Actor = "fixture-publisher"; var published = await scope.Application.PublishAsync(env.Publish(second));
        Assert.Equal(second.VersionReference, published.VersionReference);
        Assert.Equal("second candidate", scope.Application.Resolve(new(env.Scope, env.Variant, env.Clock.GetUtcNow())).Content["login.heading"].Text);
        await using var platform = env.Platform();
        var approvals = await platform.OutboxMessages.Where(x => x.EventType == "fixture.content.Approve" && x.Subject == second.ProposalReference.ToString("N")).ToArrayAsync();
        using var payload = JsonDocument.Parse(Assert.Single(approvals).Payload);
        Assert.Equal(second.VersionReference!.Value.ToString("N"), payload.RootElement.GetProperty("versionReference").GetString());
    }

    [Fact]
    public async Task ConcurrentDraftEditsHaveOneRevisionWinner()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        ContentProposalView proposal; await using (var initial = env.Open()) proposal = await initial.Application.CreateDraftAsync(env.Draft("initial"));
        async Task<bool> Change(string text)
        {
            await using var s = env.Open();
            try { await s.Application.UpdateDraftAsync(new(proposal.ProposalReference, proposal.Revision, text, "OP-" + text, "fixture-correlation")); return true; }
            catch (ContentConflictException) { return false; }
        }
        var results = await Task.WhenAll(Change("first"), Change("second")); Assert.Single(results, x => x); Assert.Single(results, x => !x);
    }

    [Fact]
    public async Task QualifiedProfileDriftAtFinalBoundaryCannotPublishAnUnreviewedVersion()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        env.Authorization.OnCall = (_, call) => { if (call == 2) env.Profile = env.Profile with { EvidenceReference = "fixture-changed-approval" }; };
        await using (var s = env.Open()) await Assert.ThrowsAsync<ContentConflictException>(() => s.Application.CreateDraftAsync(env.Draft("drifting profile")));
        await using var platform = env.Platform(); Assert.Empty(await platform.AuditEvents.ToArrayAsync()); Assert.Empty(await platform.OutboxMessages.ToArrayAsync());
    }

    [Fact]
    public async Task ExplicitWholeBundleFallbackAndMissingSensitiveContentFailClosed()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        await env.PublishTextAsync("login.heading", "fixture English");
        await using var s = env.Open(); var result = s.Application.Resolve(new(env.Scope, "fixture-login-fr", env.Clock.GetUtcNow()));
        Assert.Equal(env.Variant, result.EffectiveVariantReference); Assert.Equal("en", result.Locale);
        Assert.All(result.Content.Values, text => Assert.Equal("en", text.Locale));
        env.Profile = env.Profile with { Metadata = new("fixture-strict-profile-v2", "ACTIVE", env.Scope, env.Interval, null, ["fixture-approval"]),
            Definitions = [new("login.heading", "fixture-sensitive", true, false, true, [], []) { Purpose = "Synthetic Login heading purpose" }] };
        await env.PublishTextAsync("login.heading", "fixture strict English");
        Assert.Throws<ContentUnavailableException>(() => s.Application.Resolve(new(env.Scope, "fixture-login-fr", env.Clock.GetUtcNow())));
    }

    [Fact]
    public async Task ProspectivePublicationRollbackAndExpiryRetainHistoricalVersionsWithoutImplicitResurrection()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var clock = new TestClock(); var env = new PresentationContentTestEnvironment(db, clock); await env.InitializeAsync();
        var first = await env.PublishTextAsync("login.heading", "first"); clock.Advance(TimeSpan.FromSeconds(1));
        var second = await env.PublishTextAsync("login.heading", "second", supersedes: first.PublicationReference);
        await using (var s = env.Open()) Assert.Equal("first", s.Application.Resolve(new(env.Scope, env.Variant, first.EffectiveInterval.From)).Content["login.heading"].Text);
        clock.Advance(TimeSpan.FromSeconds(1)); ContentHistoryEntry rollback;
        await using (var s = env.Open())
        {
            rollback = await s.Application.RollbackAsync(new(first.VersionReference, second.PublicationReference,
                new(clock.GetUtcNow(), true, clock.GetUtcNow().AddSeconds(1), false), "OP-rollback", "fixture-correlation"));
            Assert.Equal(first.VersionReference, rollback.VersionReference);
            Assert.Equal("first", s.Application.Resolve(new(env.Scope, env.Variant, clock.GetUtcNow())).Content["login.heading"].Text);
        }
        clock.Advance(TimeSpan.FromSeconds(1)); await using var expired = env.Open();
        Assert.Throws<ContentUnavailableException>(() => expired.Application.Resolve(new(env.Scope, env.Variant, clock.GetUtcNow())));
        var history = await expired.Application.HistoryAsync(env.Scope, "login.heading", env.Variant, "OP-history", "fixture-correlation"); Assert.Equal(3, history.Count);
    }

    [Theory]
    [InlineData("UPDATE presentation_content.versions SET \"Text\"='bypass'")]
    [InlineData("DELETE FROM presentation_content.publications")]
    [InlineData("TRUNCATE presentation_content.definitions CASCADE")]
    public async Task DatabaseRejectsDirectHistoryMutation(string sql)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        await env.PublishTextAsync("login.heading", "fixture immutable"); await using var context = env.Context();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql)); Assert.Equal("55000", error.SqlState);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{")]
    public async Task IntegritySnapshotReadRejectsMissingNullIncompleteOrMalformedSelectedProfile(string? snapshot)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        var publication = await env.PublishTextAsync("login.heading", "fixture integrity control"); await using var context = env.Context();
        Assert.True(await context.Database.SqlQuery<bool>($"SELECT presentation_content.valid_publication({publication.PublicationReference}) AS \"Value\"").SingleAsync());
        var digest = await context.Database.SqlQuery<string>($"SELECT \"TextDigest\" AS \"Value\" FROM presentation_content.versions WHERE \"Id\"={publication.VersionReference}").SingleAsync();
        var target = publication.PublicationReference.ToString("N"); const string actor = "fixture-publisher";
        const string controlVersion = "fixture-control-snapshot";
        const string rejectedVersion = "fixture-invalid-snapshot";
        var controlProfile = env.Profile with { Metadata = new(controlVersion, "ACTIVE", env.Scope, env.Interval, null, ["fixture-profile-approval"]) };
        var controlEvent = await StageEffectAsync(controlVersion, JsonSerializer.Serialize(controlProfile));
        Assert.True(await HasEffectAsync(controlEvent, controlVersion));
        // Construct a complete matching causal pair for this exact selected profile, rather than
        // relying on the original publication's differently bound profileVersion to reject it.
        var rejectedEvent = await StageEffectAsync(rejectedVersion, snapshot);
        try
        {
            Assert.False(await HasEffectAsync(rejectedEvent, rejectedVersion));
        }
        catch (PostgresException error) when (snapshot == "{" && error.SqlState == "22P02")
        {
            // Invalid JSON is an allowed denial; no other database failure is accepted.
        }
        Assert.False(await context.Database.SqlQueryRaw<bool>("SELECT presentation_content.valid_publication(NULL::uuid) AS \"Value\"").SingleAsync());
        Assert.False(await context.Database.SqlQuery<bool>($"SELECT presentation_content.valid_publication({Guid.NewGuid()}) AS \"Value\"").SingleAsync());
        Assert.True(await HasEffectAsync(controlEvent, controlVersion));
        Assert.True(await context.Database.SqlQuery<bool>($"SELECT presentation_content.valid_publication({publication.PublicationReference}) AS \"Value\"").SingleAsync());
        Assert.All(await context.Set<AuditEventRecord>().AsNoTracking().ToArrayAsync(), audit => Assert.True(audit.VerifyIntegrity()));

        Task<bool> HasEffectAsync(Guid eventReference, string profileVersion) => context.Database.SqlQuery<bool>(
            $"SELECT presentation_content.has_effect({eventReference},{env.Scope},{profileVersion},{nameof(ContentOperation.Publish)},{target},{digest},{actor}) AS \"Value\"").SingleAsync();

        async Task<Guid> StageEffectAsync(string profileVersion, string? selectedSnapshot)
        {
            if (selectedSnapshot is not null)
                await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO presentation_content.profiles (\"Scope\",\"Version\",\"Snapshot\") VALUES ({env.Scope},{profileVersion},{selectedSnapshot})");
            var contract = env.Profile.Effects[ContentOperation.Publish]; var eventId = Guid.NewGuid(); var at = env.Clock.GetUtcNow();
            const string operation = "OP-fixture-snapshot", correlation = "fixture-snapshot-correlation", evidence = "fixture-authorization-evidence";
            EfAuditAppender.StageAppend(context, new(eventId, at, new(actor, "fixture-content-actor", [evidence]), contract.AuditAction,
                operation, correlation, [new(target, "1")], contract.AuditResult, env.Profile.Classification, env.Profile.AuditRetentionClassReference,
                policyVersionReferences: [profileVersion], evidenceReferences: [evidence]));
            context.Set<OutboxMessage>().Add(new(eventId, contract.EnvelopeVersion, contract.EventType!, contract.EventSchemaVersion!, at, contract.Source,
                target, env.Profile.Classification, JsonSerializer.Serialize(new { reference = target, scope = env.Scope, profileVersion,
                    revision = 1, contentDigest = digest, authorizationEvidenceReference = evidence }), "1", operation, correlation));
            await context.SaveChangesAsync(); return eventId;
        }
    }

    [Fact]
    public async Task PopulatedSnapshotReadMigrationDowngradeAndReapplyPreserveFunctionsExecutionAclAndAllOwnerData()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        var publication = await env.PublishTextAsync("login.heading", "fixture migration control"); await using var context = env.Context();
        var migrator = context.GetService<IMigrator>(); var migrations = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal("20261118175300_P2ContentIntegritySnapshotRead", migrations[^1]);
        const string definitionsSql = """
            SELECT jsonb_object_agg(p.proname,pg_get_functiondef(p.oid))::text AS "Value"
            FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
            WHERE n.nspname='presentation_content' AND p.proname IN ('has_effect','valid_publication')
            """;
        const string otherFunctionsSql = """
            SELECT jsonb_object_agg(p.proname,pg_get_functiondef(p.oid))::text AS "Value"
            FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
            WHERE n.nspname='presentation_content' AND p.proname NOT IN ('has_effect','valid_publication')
            """;
        const string aclSql = """
            SELECT jsonb_object_agg(p.proname,p.proacl::text)::text AS "Value"
            FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
            WHERE n.nspname='presentation_content' AND p.proname IN ('has_effect','valid_publication')
            """;
        const string dataSql = """
            SELECT jsonb_build_object(
              'profiles',(SELECT jsonb_agg(to_jsonb(t) ORDER BY t."Scope",t."Version") FROM presentation_content.profiles t),
              'definitions',(SELECT jsonb_agg(to_jsonb(t) ORDER BY t."Scope",t."ProfileVersion",t."Key") FROM presentation_content.definitions t),
              'variants',(SELECT jsonb_agg(to_jsonb(t) ORDER BY t."Scope",t."ProfileVersion",t."Reference") FROM presentation_content.variants t),
              'proposals',(SELECT jsonb_agg(to_jsonb(t) ORDER BY t."Id") FROM presentation_content.proposals t),
              'versions',(SELECT jsonb_agg(to_jsonb(t) ORDER BY t."Id") FROM presentation_content.versions t),
              'publications',(SELECT jsonb_agg(to_jsonb(t) ORDER BY t."Id") FROM presentation_content.publications t),
              'audit',(SELECT jsonb_agg(to_jsonb(t) ORDER BY t.event_id) FROM audit.events t),
              'outbox',(SELECT jsonb_agg(to_jsonb(t) ORDER BY t.event_id) FROM platform.outbox_messages t))::text AS "Value"
            """;
        var originalAcl = await context.Database.SqlQueryRaw<string>(aclSql).SingleAsync();
        await context.Database.ExecuteSqlRawAsync("""
            REVOKE EXECUTE ON FUNCTION presentation_content.has_effect(uuid,text,text,text,text,text,text),
              presentation_content.valid_publication(uuid) FROM PUBLIC;
            """);
        var executionAcl = await context.Database.SqlQueryRaw<string>(aclSql).SingleAsync(); Assert.NotEqual(originalAcl, executionAcl);
        var upgraded = await context.Database.SqlQueryRaw<string>(definitionsSql).SingleAsync();
        var otherFunctions = await context.Database.SqlQueryRaw<string>(otherFunctionsSql).SingleAsync();
        var ownerData = await context.Database.SqlQueryRaw<string>(dataSql).SingleAsync();
        Assert.Contains("WITH profile_value AS MATERIALIZED", upgraded, StringComparison.Ordinal);
        await migrator.MigrateAsync("20261116075423_P2ContentChangeReason");
        Assert.Equal(migrations[..^1], (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        var restored = await context.Database.SqlQueryRaw<string>(definitionsSql).SingleAsync();
        Assert.NotEqual(upgraded, restored); Assert.DoesNotContain("profile_value", restored, StringComparison.Ordinal);
        Assert.Equal(executionAcl, await context.Database.SqlQueryRaw<string>(aclSql).SingleAsync());
        Assert.Equal(otherFunctions, await context.Database.SqlQueryRaw<string>(otherFunctionsSql).SingleAsync());
        Assert.Equal(ownerData, await context.Database.SqlQueryRaw<string>(dataSql).SingleAsync());
        Assert.True(await context.Database.SqlQuery<bool>($"SELECT presentation_content.valid_publication({publication.PublicationReference}) AS \"Value\"").SingleAsync());
        await migrator.MigrateAsync();
        Assert.Equal(migrations, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(upgraded, await context.Database.SqlQueryRaw<string>(definitionsSql).SingleAsync());
        Assert.Equal(executionAcl, await context.Database.SqlQueryRaw<string>(aclSql).SingleAsync());
        Assert.Equal(otherFunctions, await context.Database.SqlQueryRaw<string>(otherFunctionsSql).SingleAsync());
        Assert.Equal(ownerData, await context.Database.SqlQueryRaw<string>(dataSql).SingleAsync());
        Assert.True(await context.Database.SqlQuery<bool>($"SELECT presentation_content.valid_publication({publication.PublicationReference}) AS \"Value\"").SingleAsync());
        await using var scope = env.Open(); var resolved = await scope.Application.ResolveAsync(new(env.Scope, env.Variant, env.Clock.GetUtcNow()));
        Assert.Equal(publication.PublicationReference.ToString("N"), Assert.Single(resolved.Content).Value.PublicationReference);
        Assert.Equal("fixture migration control", Assert.Single(resolved.Content).Value.Text);
    }

    [Fact]
    public async Task UnknownSlotsScopeVariantAndUnqualifiedPublicationContractAreRejected()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        await using var s = env.Open(); var p = await s.Application.CreateDraftAsync(env.Draft("undeclared {accountSecret}"));
        await Assert.ThrowsAsync<ArgumentException>(() => s.Application.ValidateAsync(env.Command(p)));
        await Assert.ThrowsAsync<ContentUnavailableException>(() => s.Application.CreateDraftAsync(env.Draft("fixture") with { Scope = "other-owner" }));
        await Assert.ThrowsAsync<ContentUnavailableException>(() => s.Application.CreateDraftAsync(env.Draft("fixture") with { VariantReference = "unapproved" }));
        env.Profile = env.Profile with { Effects = new Dictionary<ContentOperation, ContentEffectContract>() };
        await Assert.ThrowsAsync<ContentUnavailableException>(() => s.Application.CreateDraftAsync(env.Draft("fixture")));
    }

    [Fact]
    public async Task RawInsertedVersionAndForgedPublicationAgainstDraftCannotCommitOrResolve()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        var original = await env.PublishTextAsync("login.heading", "approved original"); ContentProposalView draft;
        env.Authorization.Actor = "fixture-author";
        await using (var s = env.Open()) draft = await s.Application.CreateDraftAsync(env.Draft("unvalidated draft"));
        var version = Guid.NewGuid(); var publication = Guid.NewGuid();
        await using (var context = env.Context())
        await using (var tx = await context.Database.BeginTransactionAsync())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO presentation_content.versions ("Id","ProposalReference","Scope","ProfileVersion","Key","VariantReference","Locale","Text",
                  "TextDigest","ContributorsJson","SourceEvidenceReference","ModelOrigin","ValidationEventReference")
                SELECT {version},{draft.ProposalReference},"Scope","ProfileVersion","Key","VariantReference",'fr','substituted unapproved wording',
                  "TextDigest","ContributorsJson","SourceEvidenceReference","ModelOrigin","ValidationEventReference"
                FROM presentation_content.versions WHERE "Id"={original.VersionReference}
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO presentation_content.publications ("Id","VersionReference","Scope","ProfileVersion","Key","VariantReference","From","IncludesFrom","Until","IncludesUntil",
                  "SupersedesPublicationReference","PublisherReference","ApproverReference","ApprovalEvidenceReference","PublisherEvidenceReference","PublisherAuthorizationVersion","PublicationEventReference","Operation")
                SELECT {publication},{version},"Scope","ProfileVersion","Key","VariantReference","From","IncludesFrom","Until","IncludesUntil",NULL,
                  'fabricated-publisher','fabricated-approver','fabricated-approval',"PublisherEvidenceReference","PublisherAuthorizationVersion","PublicationEventReference",'Publish'
                FROM presentation_content.publications WHERE "Id"={original.PublicationReference}
                """);
            var error = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync()); Assert.Equal("23514", error.SqlState);
        }
        await using var read = env.Open(); Assert.Equal("approved original", read.Application.Resolve(new(env.Scope, env.Variant, env.Clock.GetUtcNow())).Content["login.heading"].Text);
    }

    [Theory]
    [InlineData(false, "authorization")]
    [InlineData(true, "authorization")]
    [InlineData(false, "profile")]
    [InlineData(true, "profile")]
    [InlineData(false, "publication")]
    [InlineData(true, "publication")]
    public async Task SavingAndSavedTimeDriftCannotCommitExpiredAuthorityOrBackdatedPublication(bool afterSave, string frontier)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var clock = new TestClock(); var env = new PresentationContentTestEnvironment(db, clock);
        if (frontier == "profile") env.Profile = env.Profile with { Metadata = new(env.Profile.Metadata.VersionId, "ACTIVE", env.Scope,
            new(clock.GetUtcNow().AddDays(-1), true, clock.GetUtcNow().AddSeconds(1), false), null, ["fixture-profile-approval"]) };
        await env.InitializeAsync(); ContentProposalView p;
        await using (var s = env.Open())
        {
            p = await s.Application.CreateDraftAsync(env.Draft("frontier candidate")); p = await s.Application.ValidateAsync(env.Command(p));
            env.Authorization.Actor = "fixture-approver"; p = await s.Application.ApproveAsync(env.Command(p));
        }
        env.Authorization.Actor = "fixture-publisher";
        if (frontier == "authorization") env.Authorization.OverrideInterval = new(clock.GetUtcNow().AddDays(-1), true, clock.GetUtcNow().AddSeconds(1), false);
        env.Interceptors = [new AdvanceContentClock(afterSave, () => clock.Advance(TimeSpan.FromSeconds(2)))];
        var interval = new EffectivePolicyInterval(clock.GetUtcNow().AddMilliseconds(500), true, frontier == "profile" ? clock.GetUtcNow().AddSeconds(1) : null, false);
        await using (var s = env.Open())
        {
            var command = new PublishContent(p.ProposalReference, p.Revision, interval, null, "OP-frontier", "fixture-correlation");
            if (frontier == "authorization") await Assert.ThrowsAsync<ContentForbiddenException>(() => s.Application.PublishAsync(command));
            else if (frontier == "profile") await Assert.ThrowsAsync<ContentUnavailableException>(() => s.Application.PublishAsync(command));
            else await Assert.ThrowsAsync<ContentConflictException>(() => s.Application.PublishAsync(command));
        }
        await using var context = env.Context();
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM presentation_content.publications").SingleAsync());
        await using var platform = env.Platform(); Assert.Equal(3, await platform.AuditEvents.CountAsync()); Assert.Equal(3, await platform.OutboxMessages.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolverStorageFailureHasSafeUnavailableBoundary(bool asynchronous)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock());
        var connection = new NpgsqlConnectionStringBuilder(db.ConnectionString) { Database = "absent_content_" + Guid.NewGuid().ToString("N") };
        await using var context = new PresentationContentDbContext(new DbContextOptionsBuilder<PresentationContentDbContext>().UseNpgsql(connection.ConnectionString).Options);
        var app = new PresentationContentApplication(context, env, env.Authorization, env.Clock); var request = new ContentResolutionRequest(env.Scope, env.Variant, env.Clock.GetUtcNow());
        var error = asynchronous ? await Assert.ThrowsAsync<ContentUnavailableException>(() => app.ResolveAsync(request)) : Assert.Throws<ContentUnavailableException>(() => app.Resolve(request));
        Assert.Null(error.InnerException); Assert.DoesNotContain(connection.Database, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsyncResolverPreservesRequestedCancellation()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new TestClock()); await env.InitializeAsync();
        using var cancellation = new CancellationTokenSource(); await cancellation.CancelAsync(); await using var scope = env.Open();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Application.ResolveAsync(new(env.Scope, env.Variant, env.Clock.GetUtcNow()), cancellation.Token));
    }

    private sealed class AdvanceContentClock(bool afterSave, Action advance) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (!afterSave) advance(); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { if (afterSave) advance(); return ValueTask.FromResult(result); }
    }
}

/// <summary>Shared fixture authority for this owner and the real Login adapter test. Every value is explicitly synthetic and isolated.</summary>
internal sealed class PresentationContentTestEnvironment : IPresentationContentProfileSource
{
    private readonly IdentityTestDatabase database;
    internal PresentationContentTestEnvironment(IdentityTestDatabase database, TimeProvider clock, IReadOnlyDictionary<string, string>? content = null)
    {
        this.database = database; Clock = clock; Interval = new(clock.GetUtcNow().AddDays(-1), true, null, false);
        var keys = content?.Keys.ToArray() ?? ["login.heading"];
        Profile = new(new("fixture-content-profile-v1", "ACTIVE", Scope, Interval, null, ["fixture-profile-approval"]), "fixture-login", "fixture-content-evidence",
            "fixture-processing", "fixture-retention", "fixture-audit-retention", "RESTRICTED_SECURITY", 4096, true, true, true,
            keys.Select(key => new ContentDefinition(key, "fixture-sensitive", true, true, true, [], []) { Purpose = "Synthetic purpose for " + key }).ToArray(),
            [new(Variant, "en", new Dictionary<string, string> { ["page"] = "login" }), new("fixture-login-fr", "fr", new Dictionary<string, string> { ["page"] = "login" })],
            [new(Variant, [Variant]), new("fixture-login-fr", ["fixture-login-fr", Variant])],
            Enum.GetValues<ContentOperation>().ToDictionary(operation => operation, operation => new ContentEffectContract("fixture-content-" + operation,
                "fixture-completed", "fixture/presentation-content", "1.0", operation is ContentOperation.History or ContentOperation.Preview ? null : "fixture.content." + operation,
                operation is ContentOperation.History or ContentOperation.Preview ? null : "1")));
        Authorization = new(this);
    }
    internal string Scope { get; } = "fixture.identity.login";
    internal string Variant { get; } = "fixture-login-en";
    internal TimeProvider Clock { get; }
    internal EffectivePolicyInterval Interval { get; }
    internal ContentGovernanceProfile Profile { get; set; }
    internal FixtureContentAuthorization Authorization { get; }
    internal IInterceptor[] Interceptors { get; set; } = [];
    public ContentGovernanceProfile RequireEffective(string scope, DateTimeOffset effectiveAt) => scope == Scope ? Profile : throw new ContentUnavailableException();
    internal PresentationContentDbContext Context() => new(new DbContextOptionsBuilder<PresentationContentDbContext>().UseNpgsql(database.ConnectionString,
        postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "presentation_content")).AddInterceptors(Interceptors).Options);
    internal TunnerMigrationsDbContext Platform() => new(new DbContextOptionsBuilder<TunnerMigrationsDbContext>().UseNpgsql(database.ConnectionString).Options);
    internal async Task InitializeAsync() { await using var context = Context(); await context.Database.MigrateAsync(); }
    internal ContentScope Open() => new(this);
    internal CreateContentDraft Draft(string text, string key = "login.heading", string? variant = null) => new(Scope, key, variant ?? Variant, text, "OP-fixture-content", "fixture-correlation");
    internal ContentProposalCommand Command(ContentProposalView view) => new(view.ProposalReference, view.Revision, "OP-fixture-content-" + Scope, "fixture-correlation");
    internal PublishContent Publish(ContentProposalView view, Guid? supersedes = null) => new(view.ProposalReference, view.Revision,
        new(Clock.GetUtcNow(), true, null, false), supersedes, "OP-fixture-publish", "fixture-correlation");
    internal async Task<ContentHistoryEntry> PublishTextAsync(string key, string text, string? variant = null, Guid? supersedes = null)
    {
        await using var scope = Open(); Authorization.Actor = "fixture-author";
        var proposal = await scope.Application.CreateDraftAsync(Draft(text, key, variant)); proposal = await scope.Application.ValidateAsync(Command(proposal));
        Authorization.Actor = "fixture-approver"; proposal = await scope.Application.ApproveAsync(Command(proposal));
        Authorization.Actor = "fixture-publisher"; return await scope.Application.PublishAsync(Publish(proposal, supersedes));
    }
    internal sealed class ContentScope : IAsyncDisposable
    {
        internal PresentationContentDbContext Context { get; }
        internal PresentationContentApplication Application { get; }
        internal ContentScope(PresentationContentTestEnvironment env) { Context = env.Context(); Application = new(Context, env, env.Authorization, env.Clock); }
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
    internal sealed class FixtureContentAuthorization(PresentationContentTestEnvironment env) : IPresentationContentAuthorizationSource
    {
        internal string Actor = "fixture-author";
        internal ContentActorKind Kind = ContentActorKind.Human;
        internal ContentOperation? Denied;
        internal bool Available = true, WrongDigest;
        internal int Calls;
        internal Action<ContentAuthorizationRequest, int>? OnCall;
        internal EffectivePolicyInterval? OverrideInterval;
        public ValueTask<ContentAuthorizationDecision> AuthorizeAsync(ContentAuthorizationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); OnCall?.Invoke(request, ++Calls);
            return ValueTask.FromResult(new ContentAuthorizationDecision(WrongDigest ? "unrelated" : request.Digest, Available && request.Scope == env.Scope && request.Operation != Denied,
                Actor, Kind, "fixture-content-actor", "fixture-authorization-v1", "fixture-authorization-evidence", OverrideInterval ?? env.Interval));
        }
    }
}
