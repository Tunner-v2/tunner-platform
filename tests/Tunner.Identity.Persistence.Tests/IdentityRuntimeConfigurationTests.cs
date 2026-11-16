using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tunner.Database;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class IdentityRuntimeConfigurationTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Theory]
    [InlineData(IdentityConfigurationFamily.Authentication)]
    [InlineData(IdentityConfigurationFamily.Assurance)]
    [InlineData(IdentityConfigurationFamily.AuthenticationContent)]
    public async Task TypedOwnerPublicationSuppliesExistingRuntimePortsWithActualApprovalEvidence(IdentityConfigurationFamily family)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var publication = await env.PublishAsync(family, "fixture-value-v1");
        switch (family)
        {
            case IdentityConfigurationFamily.Authentication: Assert.Equal("fixture-value-v1", env.Source.RequireEffective(env.Clock.GetUtcNow()).Version); break;
            case IdentityConfigurationFamily.Assurance:
                var assurance = env.Source.RequireEffective(env.AssuranceScope, env.Clock.GetUtcNow());
                Assert.Equal("fixture-value-v1", assurance.Metadata.VersionId); Assert.Equal("fixture-assurance", assurance.RequireMethodAssurance("pwd"));
                Assert.Contains(publication.ApprovalEvidenceReference, assurance.Metadata.ApprovalEvidenceReferences);
                Assert.DoesNotContain("client-proposed-evidence-is-not-authority", assurance.Metadata.ApprovalEvidenceReferences); break;
            default:
                var binding = env.Source.RequireQualified(env.Authentication().Values, env.Clock.GetUtcNow());
                Assert.Equal("fixture-value-v1", binding.ConfigurationVersionReference);
                Assert.NotEqual("client-proposed-evidence-is-not-authority", binding.QualificationReference); break;
        }
        await using var platform = env.Platform(); var audit = await platform.AuditEvents.ToArrayAsync(); var events = await platform.OutboxMessages.ToArrayAsync();
        Assert.Equal(4, audit.Length); Assert.Equal(4, events.Length); Assert.All(audit, x => Assert.True(x.VerifyIntegrity()));
        Assert.All(events, x => Assert.DoesNotContain("PasswordHashIterations", x.Payload, StringComparison.Ordinal));
        Assert.All(audit, x => Assert.DoesNotContain("MethodAssurances", x.Envelope, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(IdentityConfigurationFamily.Authentication)]
    [InlineData(IdentityConfigurationFamily.Assurance)]
    [InlineData(IdentityConfigurationFamily.AuthenticationContent)]
    public async Task UnpublishedOrMerelyValidatedCandidateCannotBecomeRuntimeAuthority(IdentityConfigurationFamily family)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        env.AssertUnavailable(family);
        await using var scope = env.Open(); var proposal = await env.CreateAsync(scope.Application, family, "fixture-pending");
        proposal = await scope.Application.ValidateAsync(env.Command(proposal)); env.AssertUnavailable(family);
        env.Authorization.Actor = "fixture-security-publisher";
        await Assert.ThrowsAsync<IdentityConfigurationConflictException>(() => scope.Application.PublishAsync(env.Publication(proposal)));
    }

    [Theory]
    [InlineData(IdentityConfigurationOperation.CreateDraft)]
    [InlineData(IdentityConfigurationOperation.Validate)]
    [InlineData(IdentityConfigurationOperation.Approve)]
    [InlineData(IdentityConfigurationOperation.Publish)]
    public async Task IdentityCapabilityIsRequiredAndContentEditorClaimsDoNotGrantIt(IdentityConfigurationOperation denied)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); env.Authorization.Denied = denied;
        env.Authorization.Actor = "fixture-content-editor"; await using var scope = env.Open();
        if (denied == IdentityConfigurationOperation.CreateDraft)
        { await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => env.CreateAsync(scope.Application, IdentityConfigurationFamily.Authentication, "fixture-denied")); return; }
        var proposal = await env.CreateAsync(scope.Application, IdentityConfigurationFamily.Authentication, "fixture-denied");
        if (denied == IdentityConfigurationOperation.Validate)
        { await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => scope.Application.ValidateAsync(env.Command(proposal))); return; }
        proposal = await scope.Application.ValidateAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-approver";
        if (denied == IdentityConfigurationOperation.Approve)
        { await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => scope.Application.ApproveAsync(env.Command(proposal))); return; }
        proposal = await scope.Application.ApproveAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-publisher";
        await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => scope.Application.PublishAsync(env.Publication(proposal)));
    }

    [Theory]
    [InlineData("author-approves")]
    [InlineData("author-publishes")]
    [InlineData("approver-publishes")]
    public async Task SecurityApprovalAndConfiguredSeparationOfDutiesCannotBeBypassed(string violation)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); await using var scope = env.Open();
        var proposal = await env.CreateAsync(scope.Application, IdentityConfigurationFamily.Authentication, "fixture-sod");
        proposal = await scope.Application.ValidateAsync(env.Command(proposal));
        if (violation == "author-approves")
        { await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => scope.Application.ApproveAsync(env.Command(proposal))); return; }
        env.Authorization.Actor = "fixture-security-approver"; proposal = await scope.Application.ApproveAsync(env.Command(proposal));
        env.Authorization.Actor = violation == "author-publishes" ? "fixture-security-author" : "fixture-security-approver";
        await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => scope.Application.PublishAsync(env.Publication(proposal)));
    }

    [Fact]
    public async Task EditingAnApprovedCandidateInvalidatesApprovalAndRequiresANewImmutableVersionIdentity()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); await using var scope = env.Open();
        var proposal = await env.CreateAsync(scope.Application, IdentityConfigurationFamily.Authentication, "fixture-edit-v1");
        proposal = await scope.Application.ValidateAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-approver";
        proposal = await scope.Application.ApproveAsync(env.Command(proposal)); env.Authorization.Actor = "fixture-security-author";
        var edited = await scope.Application.UpdateAuthenticationDraftAsync(proposal.ProposalReference, proposal.Revision, env.Authentication("fixture-edit-v2"), "OP-edit", "fixture-correlation");
        Assert.Equal("DRAFT", edited.State); Assert.Null(edited.VersionReference);
        env.Authorization.Actor = "fixture-security-publisher";
        await Assert.ThrowsAsync<IdentityConfigurationConflictException>(() => scope.Application.PublishAsync(env.Publication(edited)));
        await Assert.ThrowsAsync<IdentityConfigurationConflictException>(() => scope.Application.ApproveAsync(env.Command(proposal)));
    }

    [Fact]
    public async Task EffectiveSelectionAndApprovedRollbackPreserveOldValuesAndNeverResurrectExpiredPublication()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var first = await env.PublishAsync(IdentityConfigurationFamily.Authentication, "fixture-old"); env.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await env.PublishAsync(IdentityConfigurationFamily.Authentication, "fixture-new", first.PublicationReference);
        Assert.Equal("fixture-old", env.Source.RequireEffective(first.EffectiveInterval.From).Version);
        Assert.Equal("fixture-new", env.Source.RequireEffective(env.Clock.GetUtcNow()).Version); env.Clock.Advance(TimeSpan.FromSeconds(1));
        await using (var scope = env.Open())
        {
            env.Authorization.Actor = "fixture-security-publisher";
            await scope.Application.RollbackAsync(new(first.VersionReference, second.PublicationReference,
                new(env.Clock.GetUtcNow(), true, env.Clock.GetUtcNow().AddSeconds(1), false), "OP-rollback", "fixture-correlation"));
        }
        Assert.Equal("fixture-old", env.Source.RequireEffective(env.Clock.GetUtcNow()).Version); env.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Throws<IdentityConfigurationUnavailableException>(() => env.Source.RequireEffective(env.Clock.GetUtcNow()));
    }

    [Fact]
    public async Task AssuranceScopeAndExpectedVersionAreAuthoritativeWithoutAssuranceRanking()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        await env.PublishAsync(IdentityConfigurationFamily.Assurance, "fixture-assurance-version");
        Assert.Throws<IdentityConfigurationUnavailableException>(() => env.Source.RequireEffective(new("other-operation", env.AssuranceScope.Context), env.Clock.GetUtcNow()));
        Assert.Throws<AssurancePolicyVersionConflictException>(() => env.Source.RequireEffective(env.AssuranceScope, env.Clock.GetUtcNow(), "other-version"));
    }

    [Fact]
    public async Task ModelCanDraftButCannotValidateOrApproveSecurityConfiguration()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); env.Authorization.Kind = IdentityConfigurationActorKind.Model;
        await using var scope = env.Open(); var proposal = await env.CreateAsync(scope.Application, IdentityConfigurationFamily.Authentication, "fixture-model");
        Assert.True(proposal.ModelOrigin); Assert.Equal("DRAFT", proposal.State);
        await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => scope.Application.ValidateAsync(env.Command(proposal)));
    }

    [Fact]
    public async Task TypedValidatorsRejectUnsupportedValuesAndInlineUriCredentialsWithoutEffects()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); await using var scope = env.Open();
        var auth = env.Authentication();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Application.CreateAuthenticationDraftAsync(auth with { Values = auth.Values with { PasswordMaximumLength = 1 } }, "OP-invalid", "fixture-correlation"));
        await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => scope.Application.CreateContentBindingDraftAsync(env.Binding() with { TermsUri = new("https://user:literal-secret@example.test/terms") }, "OP-secret", "fixture-correlation"));
        await using var platform = env.Platform(); Assert.Empty(await platform.AuditEvents.ToArrayAsync()); Assert.Empty(await platform.OutboxMessages.ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavingOrSavedAuthorityExpiryRollsBackConfigurationAuditAndOutbox(bool saved)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); IdentityConfigurationProposal proposal;
        await using (var scope = env.Open())
        {
            proposal = await env.CreateAsync(scope.Application, IdentityConfigurationFamily.Authentication, "fixture-frontier"); proposal = await scope.Application.ValidateAsync(env.Command(proposal));
            env.Authorization.Actor = "fixture-security-approver"; proposal = await scope.Application.ApproveAsync(env.Command(proposal));
        }
        env.Authorization.Actor = "fixture-security-publisher"; env.Authorization.Until = env.Clock.GetUtcNow().AddSeconds(1);
        env.Interceptors = [new AdvanceClock(saved, () => env.Clock.Advance(TimeSpan.FromSeconds(2)))];
        await using (var scope = env.Open()) await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => scope.Application.PublishAsync(env.Publication(proposal)));
        env.AssertUnavailable(IdentityConfigurationFamily.Authentication); await using var platform = env.Platform();
        Assert.Equal(3, await platform.AuditEvents.CountAsync()); Assert.Equal(3, await platform.OutboxMessages.CountAsync());
    }

    [Theory]
    [InlineData("UPDATE identity.runtime_configuration_versions SET payload='{}'")]
    [InlineData("DELETE FROM identity.runtime_configuration_publications")]
    [InlineData("TRUNCATE identity.runtime_configuration_governance CASCADE")]
    public async Task IdentityHistoryRejectsDirectMutation(string sql)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        await env.PublishAsync(IdentityConfigurationFamily.Authentication, "fixture-immutable"); await using var context = env.Context();
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(sql)); Assert.Equal("55000", error.SqlState);
    }

    [Fact]
    public async Task ConcurrentDraftEditsHaveOneExactRevisionWinner()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); IdentityConfigurationProposal proposal;
        await using (var scope = env.Open()) proposal = await env.CreateAsync(scope.Application, IdentityConfigurationFamily.Authentication, "fixture-race");
        async Task<bool> Edit(string version)
        {
            await using var scope = env.Open();
            try { await scope.Application.UpdateAuthenticationDraftAsync(proposal.ProposalReference, proposal.Revision, env.Authentication(version), "OP-" + version, "fixture-correlation"); return true; }
            catch (IdentityConfigurationConflictException) { return false; }
        }
        var results = await Task.WhenAll(Edit("first"), Edit("second")); Assert.Single(results, x => x); Assert.Single(results, x => !x);
    }

    [Fact]
    public async Task ForgedVersionAndPublicationAgainstUnvalidatedDraftCannotCommit()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db);
        var legitimate = await env.PublishAsync(IdentityConfigurationFamily.Authentication, "fixture-legitimate"); IdentityConfigurationProposal draft;
        env.Authorization.Actor = "fixture-security-author";
        await using (var scope = env.Open()) draft = await env.CreateAsync(scope.Application, IdentityConfigurationFamily.Authentication, "fixture-unvalidated");
        var version = Guid.NewGuid(); var publication = Guid.NewGuid(); await using var context = env.Context();
        await using (var tx = await context.Database.BeginTransactionAsync())
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity.runtime_configuration_versions(id,proposal_reference,scope_key,governance_version,policy_version_reference,payload,digest,contributors_json,model_origin,validation_event_reference)
                SELECT {version},p.id,p.scope_key,p.governance_version,p.policy_version_reference,p.payload,p.digest,p.contributors_json,p.model_origin,v.validation_event_reference
                FROM identity.runtime_configuration_proposals p CROSS JOIN identity.runtime_configuration_versions v
                WHERE p.id={draft.ProposalReference} AND v.id={legitimate.VersionReference}
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity.runtime_configuration_publications(id,version_reference,scope_key,effective_from,includes_from,effective_until,includes_until,
                  supersedes_publication_reference,approver_reference,approval_evidence_reference,approval_event_reference,publisher_reference,publisher_evidence_reference,publisher_authorization_version,publication_event_reference,operation)
                SELECT {publication},{version},scope_key,effective_from,includes_from,effective_until,includes_until,NULL,'fabricated-approver','fabricated-evidence',
                  approval_event_reference,'fabricated-publisher',publisher_evidence_reference,publisher_authorization_version,publication_event_reference,'Publish'
                FROM identity.runtime_configuration_publications WHERE id={legitimate.PublicationReference}
                """);
            var error = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync()); Assert.Equal("23514", error.SqlState);
        }
        Assert.Equal("fixture-legitimate", env.Source.RequireEffective(env.Clock.GetUtcNow()).Version);
    }

    private sealed class AdvanceClock(bool saved, Action advance) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (!saved) advance(); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { if (saved) advance(); return ValueTask.FromResult(result); }
    }
}

/// <summary>Explicit synthetic security authority for the isolated owner tests and later actual-host integration.</summary>
internal sealed class RuntimeConfigurationTestEnvironment : IIdentityConfigurationGovernanceSource
{
    private readonly IdentityTestDatabase database;
    internal RuntimeConfigurationTestEnvironment(IdentityTestDatabase database)
    {
        this.database = database; Interval = new(Clock.GetUtcNow().AddYears(-1), true, null, false);
        Authorization = new(this); Source = new(database.Services.GetRequiredService<IServiceScopeFactory>());
    }
    internal TestClock Clock { get; } = new();
    internal AssurancePolicyScope AssuranceScope { get; } = new("fixture-security-operation", "fixture-user-context");
    internal FixtureAuthorization Authorization { get; }
    internal PersistedIdentityRuntimeConfigurationSource Source { get; }
    internal IInterceptor[] Interceptors { get; set; } = [];
    internal EffectivePolicyInterval Interval { get; }
    public IdentityConfigurationGovernance RequireEffective(IdentityConfigurationScope scope, DateTimeOffset effectiveAt) => new(
        new("fixture-identity-governance-v1", "ACTIVE", scope, Interval, null, ["fixture-security-governance-approval"]), "fixture-security-governance-evidence",
        "fixture-processing", "fixture-retention", "fixture-audit-retention", "RESTRICTED_SECURITY", 32768, true, true, true,
        Enum.GetValues<IdentityConfigurationOperation>().ToDictionary(operation => operation, operation => new IdentityConfigurationEffect("fixture-identity-config-" + operation,
            "fixture-complete", "fixture/identity", "1.0", operation is IdentityConfigurationOperation.Preview or IdentityConfigurationOperation.History ? null : "fixture.identity.config." + operation,
            operation is IdentityConfigurationOperation.Preview or IdentityConfigurationOperation.History ? null : "1")));
    internal IdentityPersistenceDbContext Context()
    {
        var options = new DbContextOptionsBuilder<IdentityPersistenceDbContext>(database.Services.GetRequiredService<DbContextOptions<IdentityPersistenceDbContext>>());
        options.AddInterceptors(Interceptors); return new(options.Options);
    }
    internal TunnerMigrationsDbContext Platform() => new(new DbContextOptionsBuilder<TunnerMigrationsDbContext>().UseNpgsql(database.ConnectionString).Options);
    internal OwnerScope Open() => new(this);
    internal AuthenticationPolicyVersion Authentication(string version = "fixture-authentication-v1") => new(
        new(version, "ACTIVE", "AUTHENTICATION", Interval, null, ["client-proposed-evidence-is-not-authority"]), new TestPolicies().Policy with { Version = version });
    internal AssurancePolicyVersion Assurance(string version) => new(new(version, "ACTIVE", AssuranceScope, Interval, null, ["client-proposed-evidence-is-not-authority"]),
        "fixture-assurance", TimeSpan.FromSeconds(30), new Dictionary<string, string> { ["pwd"] = "fixture-assurance" });
    internal AuthenticationContentBinding Binding(string version = "fixture-binding-v1") => new(Authentication().Values.Version, version,
        "client-proposed-evidence-is-not-authority", "fixture.login.content", "fixture-login-en", Interval, new("https://fixture.example.test/terms"), new("https://fixture.example.test/privacy"), null);
    internal IdentityConfigurationCommand Command(IdentityConfigurationProposal proposal) => new(proposal.ProposalReference, proposal.Revision, "OP-fixture-" + proposal.Scope.Family, "fixture-correlation-" + AssuranceScope.Context);
    internal PublishIdentityConfiguration Publication(IdentityConfigurationProposal proposal, Guid? supersedes = null) => new(proposal.ProposalReference, proposal.Revision,
        new(Clock.GetUtcNow(), true, null, false), supersedes, "OP-fixture-publish", "fixture-correlation");
    internal Task<IdentityConfigurationProposal> CreateAsync(IdentityRuntimeConfigurationApplication app, IdentityConfigurationFamily family, string version) => family switch
    {
        IdentityConfigurationFamily.Authentication => app.CreateAuthenticationDraftAsync(Authentication(version), "OP-fixture-authentication", "fixture-correlation"),
        IdentityConfigurationFamily.Assurance => app.CreateAssuranceDraftAsync(Assurance(version), "OP-fixture-assurance", "fixture-correlation"),
        _ => app.CreateContentBindingDraftAsync(Binding(version), "OP-fixture-binding", "fixture-correlation")
    };
    internal async Task<IdentityConfigurationHistory> PublishAsync(IdentityConfigurationFamily family, string version, Guid? supersedes = null)
    {
        await using var scope = Open(); Authorization.Actor = "fixture-security-author"; var proposal = await CreateAsync(scope.Application, family, version);
        proposal = await scope.Application.ValidateAsync(Command(proposal)); Authorization.Actor = "fixture-security-approver"; proposal = await scope.Application.ApproveAsync(Command(proposal));
        Authorization.Actor = "fixture-security-publisher"; return await scope.Application.PublishAsync(Publication(proposal, supersedes));
    }
    internal void AssertUnavailable(IdentityConfigurationFamily family)
    {
        if (family == IdentityConfigurationFamily.Authentication) Assert.Throws<IdentityConfigurationUnavailableException>(() => Source.RequireEffective(Clock.GetUtcNow()));
        else if (family == IdentityConfigurationFamily.Assurance) Assert.Throws<IdentityConfigurationUnavailableException>(() => Source.RequireEffective(AssuranceScope, Clock.GetUtcNow()));
        else Assert.Throws<IdentityConfigurationUnavailableException>(() => Source.RequireQualified(Authentication().Values, Clock.GetUtcNow()));
    }
    internal sealed class OwnerScope : IAsyncDisposable
    {
        internal IdentityPersistenceDbContext Database { get; }
        internal IdentityRuntimeConfigurationApplication Application { get; }
        internal OwnerScope(RuntimeConfigurationTestEnvironment env) { Database = env.Context(); Application = new(Database, env, env.Authorization, env.Clock); }
        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
    internal sealed class FixtureAuthorization(RuntimeConfigurationTestEnvironment env) : IIdentityConfigurationAuthorizationSource
    {
        internal string Actor = "fixture-security-author";
        internal IdentityConfigurationActorKind Kind = IdentityConfigurationActorKind.Human;
        internal IdentityConfigurationOperation? Denied;
        internal DateTimeOffset? Until;
        public ValueTask<IdentityConfigurationAuthorizationDecision> AuthorizeAsync(IdentityConfigurationAuthorizationRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new IdentityConfigurationAuthorizationDecision(request.Digest, request.Operation != Denied, Actor, Kind, "fixture-identity-security-actor",
                "fixture-security-authorization-v1", "fixture-security-authorization-evidence", new(env.Interval.From, true, Until, false)));
        }
    }
}
