using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class ContentTransactionalResolutionTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    [Fact]
    public async Task ReaderUsesCallerTransactionAndLeavesCommitRollbackAndConnectionOwnershipWithCaller()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var clock = new AuthenticationTestHost.TestClock(); var env = new PresentationContentTestEnvironment(db, clock);
        await env.InitializeAsync(); _ = await env.PublishTextAsync("login.heading", "Synthetic published heading");
        await using var contentBefore = env.Context(); await using var effectsBefore = env.Platform();
        var before = new[] { await contentBefore.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM presentation_content.proposals").SingleAsync(), await contentBefore.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM presentation_content.versions").SingleAsync(), await contentBefore.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM presentation_content.publications").SingleAsync(), await effectsBefore.AuditEvents.CountAsync(), await effectsBefore.OutboxMessages.CountAsync() };
        await using var identity = db.Context(); await using var tx = await identity.Database.BeginTransactionAsync(); var connection = identity.Database.GetDbConnection();
        await identity.Database.ExecuteSqlRawAsync("CREATE TEMP TABLE content_reader_owner_marker(value integer) ON COMMIT DROP; INSERT INTO content_reader_owner_marker VALUES(1)");
        await using (var owner = env.Open())
        {
            var result = await owner.Application.ResolveInTransactionAsync(new(env.Scope, "fixture-login-fr", clock.GetUtcNow()), connection, tx.GetDbTransaction(), await Admission(env, identity));
            Assert.Equal(env.Variant, result.Resolution.EffectiveVariantReference); Assert.Equal("Synthetic published heading", result.Resolution.Content["login.heading"].Text); Assert.True(result.Availability.Contains(clock.GetUtcNow()));
        }
        await using var contentAfter = env.Context(); await using var effectsAfter = env.Platform();
        Assert.Equal(before, new[] { await contentAfter.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM presentation_content.proposals").SingleAsync(), await contentAfter.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM presentation_content.versions").SingleAsync(), await contentAfter.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM presentation_content.publications").SingleAsync(), await effectsAfter.AuditEvents.CountAsync(), await effectsAfter.OutboxMessages.CountAsync() });
        Assert.Same(tx, identity.Database.CurrentTransaction); Assert.Equal(1, await identity.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM content_reader_owner_marker").SingleAsync());
        Assert.Equal(System.Data.ConnectionState.Open, connection.State); await tx.RollbackAsync();
        await using var retained = env.Open(); Assert.Equal("Synthetic published heading", retained.Application.Resolve(new(env.Scope, env.Variant, clock.GetUtcNow())).Content["login.heading"].Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduledPublicationClipsBorrowedReadWitnessWithExactBoundaryInclusion(bool includesFrom)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var clock = new AuthenticationTestHost.TestClock(); var env = new PresentationContentTestEnvironment(db, clock);
        await env.InitializeAsync(); var first = await env.PublishTextAsync("login.heading", "Original synthetic heading"); var transition = clock.GetUtcNow().AddSeconds(1);
        await using (var writer = env.Open())
        {
            env.Authorization.Actor = "fixture-author"; var p = await writer.Application.CreateDraftAsync(env.Draft("Scheduled synthetic heading")); p = await writer.Application.ValidateAsync(env.Command(p));
            env.Authorization.Actor = "fixture-approver"; p = await writer.Application.ApproveAsync(env.Command(p)); env.Authorization.Actor = "fixture-publisher";
            _ = await writer.Application.PublishAsync(env.Publish(p, first.PublicationReference) with { EffectiveInterval = new(transition, includesFrom, null, false) });
        }
        await using var identity = db.Context(); await using var tx = await identity.Database.BeginTransactionAsync(); await using var owner = env.Open();
        var witness = await owner.Application.ResolveInTransactionAsync(new(env.Scope, env.Variant, clock.GetUtcNow()), identity.Database.GetDbConnection(), tx.GetDbTransaction(), await Admission(env, identity));
        Assert.Equal(transition, witness.Availability.Until); Assert.Equal(!includesFrom, witness.Availability.IncludesUntil); Assert.Equal("Original synthetic heading", witness.Resolution.Content["login.heading"].Text);
        await tx.RollbackAsync();
    }

    [Theory]
    [InlineData(System.Data.IsolationLevel.RepeatableRead)]
    [InlineData(System.Data.IsolationLevel.Serializable)]
    public async Task SnapshotIsolationCannotBorrowThePublicationReadBoundary(System.Data.IsolationLevel isolation)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new AuthenticationTestHost.TestClock()); await env.InitializeAsync();
        await using var identity = db.Context(); await using var tx = await identity.Database.BeginTransactionAsync(isolation); await using var owner = env.Open(); var admission = await Admission(env, identity);
        await Assert.ThrowsAsync<ContentUnavailableException>(() => owner.Application.ResolveInTransactionAsync(new(env.Scope, env.Variant, env.Clock.GetUtcNow()), identity.Database.GetDbConnection(), tx.GetDbTransaction(), admission));
        Assert.Same(tx, identity.Database.CurrentTransaction); Assert.Equal(1, await identity.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"").SingleAsync()); await tx.RollbackAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringFinalSynchronousProfileReadCannotReturnAWitness(bool readDeadline)
    {
        await using var db = await fixture.CreateDatabaseAsync(); using var clock = new ReadDeadlineClock(new AuthenticationTestHost.TestClock()); var env = new PresentationContentTestEnvironment(db, clock);
        await env.InitializeAsync(); _ = await env.PublishTextAsync("login.heading", "Synthetic published heading"); using var cancellation = new CancellationTokenSource();
        var profiles = new FinalProfile(env, () => { if (readDeadline) clock.ElapseDeadline(); else cancellation.Cancel(); });
        await using var owner = env.Open(); var application = new PresentationContentApplication(owner.Context, profiles, env.Authorization, clock);
        await using var identity = db.Context(); await using var tx = await identity.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted); var admission = await Admission(env, identity);
        if (readDeadline)
            await Assert.ThrowsAsync<ContentUnavailableException>(() => application.ResolveInTransactionAsync(new(env.Scope, env.Variant, clock.GetUtcNow()), identity.Database.GetDbConnection(), tx.GetDbTransaction(), admission, cancellation.Token));
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => application.ResolveInTransactionAsync(new(env.Scope, env.Variant, clock.GetUtcNow()), identity.Database.GetDbConnection(), tx.GetDbTransaction(), admission, cancellation.Token));
        Assert.Equal(2, profiles.Reads); Assert.Equal(admission.MaximumReadDuration, clock.ArmedDuration); Assert.Same(tx, identity.Database.CurrentTransaction); await tx.RollbackAsync();
    }

    [Fact]
    public async Task DifferentPhysicalConnectionCannotBorrowCallerTransaction()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new AuthenticationTestHost.TestClock()); await env.InitializeAsync();
        await using var identity = db.Context(); await using var tx = await identity.Database.BeginTransactionAsync(); await using var other = new NpgsqlConnection(db.ConnectionString); await other.OpenAsync(); await using var owner = env.Open();
        await Assert.ThrowsAsync<ContentUnavailableException>(() => owner.Application.ResolveInTransactionAsync(new(env.Scope, env.Variant, env.Clock.GetUtcNow()), other, tx.GetDbTransaction(), AdmissionValue(env, other.Database, "fixture-role")));
        Assert.Equal(1, await identity.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"").SingleAsync()); await tx.RollbackAsync();
    }

    [Fact]
    public async Task UnqualifiedDatabaseRoleFailsClosedWithoutTakingCallerTransactionOwnership()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new AuthenticationTestHost.TestClock()); await env.InitializeAsync();
        await using var identity = db.Context(); await using var tx = await identity.Database.BeginTransactionAsync(); await using var owner = env.Open(); var admission = await Admission(env, identity);
        await Assert.ThrowsAsync<ContentUnavailableException>(() => owner.Application.ResolveInTransactionAsync(new(env.Scope, env.Variant, env.Clock.GetUtcNow()), identity.Database.GetDbConnection(), tx.GetDbTransaction(), admission with { DatabaseRole = "unqualified-role" }));
        Assert.Same(tx, identity.Database.CurrentTransaction); Assert.Equal(1, await identity.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"").SingleAsync()); await tx.RollbackAsync();
    }

    [Fact]
    public async Task CancelledReadPreservesCancellationAndLeavesCallerResponsibleForRollback()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new AuthenticationTestHost.TestClock()); await env.InitializeAsync();
        await using var identity = db.Context(); await using var tx = await identity.Database.BeginTransactionAsync(); await using var owner = env.Open(); var admission = await Admission(env, identity); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.Application.ResolveInTransactionAsync(new(env.Scope, env.Variant, env.Clock.GetUtcNow()), identity.Database.GetDbConnection(), tx.GetDbTransaction(), admission, cancelled.Token));
        Assert.Same(tx, identity.Database.CurrentTransaction); await tx.RollbackAsync();
    }

    [Fact]
    public async Task ExplicitRowBudgetCannotBeExceededByPublicationHistory()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new PresentationContentTestEnvironment(db, new AuthenticationTestHost.TestClock()); await env.InitializeAsync(); var first = await env.PublishTextAsync("login.heading", "First synthetic heading");
        ((AuthenticationTestHost.TestClock)env.Clock).Advance(TimeSpan.FromSeconds(1)); _ = await env.PublishTextAsync("login.heading", "Second synthetic heading", supersedes: first.PublicationReference);
        await using var identity = db.Context(); await using var tx = await identity.Database.BeginTransactionAsync(); await using var owner = env.Open(); var admission = await Admission(env, identity);
        await Assert.ThrowsAsync<ContentUnavailableException>(() => owner.Application.ResolveInTransactionAsync(new(env.Scope, env.Variant, env.Clock.GetUtcNow()), identity.Database.GetDbConnection(), tx.GetDbTransaction(), admission with { MaximumRows = 1 }));
        Assert.Equal(1, await identity.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"").SingleAsync()); await tx.RollbackAsync();
    }

    private sealed class FinalProfile(IPresentationContentProfileSource source, Action finalRead) : IPresentationContentProfileSource
    {
        internal int Reads;
        public ContentGovernanceProfile RequireEffective(string scope, DateTimeOffset effectiveAt)
        { Reads++; var value = source.RequireEffective(scope, effectiveAt); if (Reads == 2) finalRead(); return value; }
    }
    // Narrow deterministic TimeProvider timer seam; no real sleeps or new test runner.
    private sealed class ReadDeadlineClock(TimeProvider source) : TimeProvider, IDisposable
    {
        private ControlledTimer? timer;
        internal TimeSpan ArmedDuration;
        public override DateTimeOffset GetUtcNow() => source.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { ArmedDuration = dueTime; timer = new(callback, state); return timer; }
        internal void ElapseDeadline() => timer!.Fire();
        public void Dispose() => timer?.Dispose();
        private sealed class ControlledTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool disposed;
            internal void Fire() { if (!disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;
            public void Dispose() => disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private static async Task<ContentTransactionReadQualification> Admission(PresentationContentTestEnvironment env, IdentityPersistenceDbContext identity)
    { var role = await identity.Database.SqlQueryRaw<string>("SELECT current_user::text AS \"Value\"").SingleAsync(); return AdmissionValue(env, identity.Database.GetDbConnection().Database, role); }
    private static ContentTransactionReadQualification AdmissionValue(PresentationContentTestEnvironment env, string database, string role) => new(
        new("fixture-transaction-read-v1", "ACTIVE", env.Scope, env.Interval, null, ["SYNTHETIC-ONLY"]), "fixture-read-role-qualification", database, role, 16, 16, 10, TimeSpan.FromSeconds(10));
}
