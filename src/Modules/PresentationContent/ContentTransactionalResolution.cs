using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Tunner.Platform.Policies;

namespace Tunner.PresentationContent;

/// <summary>Explicit server-owned read admission. No grants, deployment values or timeouts are selected by the reader.</summary>
public sealed record ContentTransactionReadQualification(PolicyVersionCandidate<string> Metadata, string EvidenceReference,
    string DatabaseName, string DatabaseRole, int MaximumRows, int MaximumLockKeys, int CommandTimeoutSeconds, TimeSpan MaximumReadDuration)
{
    internal void Require(string scope, DateTimeOffset at)
    {
        if (Metadata is null || Metadata.Scope != scope || Metadata.Status != "ACTIVE" || !Metadata.EffectiveInterval.Contains(at) ||
            Metadata.ApprovalEvidenceReferences.Count == 0 || new[] { EvidenceReference, DatabaseName, DatabaseRole }.Any(string.IsNullOrWhiteSpace) ||
            MaximumRows is < 1 or int.MaxValue || MaximumLockKeys is < 1 or int.MaxValue || CommandTimeoutSeconds < 1 ||
            MaximumReadDuration <= TimeSpan.Zero || MaximumReadDuration.TotalMilliseconds > uint.MaxValue - 1)
            throw new ContentUnavailableException();
    }
}
public sealed record ContentTransactionResolution(ContentResolution Resolution, EffectivePolicyInterval Availability,
    string ProfileVersion, string ProfileDigest);

public sealed partial class PresentationContentApplication
{
    /// <summary>Read only, on the caller's exact open PostgreSQL connection/transaction. Neither is owned, committed or disposed here.</summary>
    public async Task<ContentTransactionResolution> ResolveInTransactionAsync(ContentResolutionRequest request,
        DbConnection connection, DbTransaction transaction, ContentTransactionReadQualification qualification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(qualification);
        if (connection is not NpgsqlConnection || transaction is not NpgsqlTransaction || connection.State != ConnectionState.Open ||
            !ReferenceEquals(transaction.Connection, connection) || transaction.IsolationLevel != IsolationLevel.ReadCommitted || connection.Database != qualification.DatabaseName)
            throw new ContentUnavailableException();
        qualification.Require(request.Scope, clock.GetUtcNow());
        using var timeout = new CancellationTokenSource(qualification.MaximumReadDuration, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var ct = deadline.Token;
        try
        {
            var profile = ResolutionProfile(request); var profileSnapshot = profile.Snapshot;
            var chain = profile.Fallbacks.Single(x => x.RequestedVariantReference == request.VariantReference).OrderedVariantReferences;
            if ((long)profile.Definitions.Count * chain.Count > qualification.MaximumLockKeys) throw new ContentUnavailableException();
            var lockKeys = profile.Definitions.SelectMany(definition => chain.Select(variant => "presentation-content:publication:" +
                ContentDigest.Of(new { Scope = request.Scope, definition.Key, VariantReference = variant }))).Order(StringComparer.Ordinal).ToArray();
            var options = new DbContextOptionsBuilder<PresentationContentDbContext>().UseNpgsql(connection, contextOwnsConnection: false).Options;
            await using var reader = new PresentationContentDbContext(options); reader.Database.SetCommandTimeout(qualification.CommandTimeoutSeconds);
            await using var participation = await reader.Database.UseTransactionAsync(transaction, ct).ConfigureAwait(false);
            // Verify actual identity and read privileges; the evidence reference alone is not a database grant.
            var role = await reader.Database.SqlQuery<string>($"SELECT current_user::text AS \"Value\"").SingleAsync(ct).ConfigureAwait(false);
            if (role != qualification.DatabaseRole) throw new ContentUnavailableException();
            var allowed = await reader.Database.SqlQuery<bool>($"""
                SELECT (has_schema_privilege(current_user,'presentation_content','USAGE') AND
                  has_table_privilege(current_user,'presentation_content.profiles','SELECT') AND
                  has_table_privilege(current_user,'presentation_content.definitions','SELECT') AND
                  has_table_privilege(current_user,'presentation_content.variants','SELECT') AND
                  has_table_privilege(current_user,'presentation_content.proposals','SELECT') AND
                  has_table_privilege(current_user,'presentation_content.versions','SELECT') AND
                  has_table_privilege(current_user,'presentation_content.publications','SELECT') AND
                  has_table_privilege(current_user,'audit.events','SELECT') AND
                  has_table_privilege(current_user,'platform.outbox_messages','SELECT') AND
                  has_function_privilege(current_user,'presentation_content.valid_publication(uuid)','EXECUTE') AND
                  has_function_privilege(current_user,'pg_catalog.pg_advisory_xact_lock_shared(bigint)','EXECUTE')) AS "Value"
                """).SingleAsync(ct).ConfigureAwait(false);
            if (!allowed) throw new ContentUnavailableException();
            // Writers use the exclusive form of these exact existing keys. Canonical shared locks pin all possible fallback publications.
            foreach (var key in lockKeys)
                await reader.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared(hashtextextended({key},0))", ct).ConfigureAwait(false);
            var stored = await reader.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.Scope == request.Scope && x.Version == profile.Metadata.VersionId, ct).ConfigureAwait(false);
            if (stored?.Snapshot != profileSnapshot) throw new ContentUnavailableException();
            var publications = await reader.Publications.AsNoTracking().Where(x => x.Scope == request.Scope && x.ProfileVersion == profile.Metadata.VersionId)
                .Take(qualification.MaximumRows + 1).ToArrayAsync(ct).ConfigureAwait(false);
            var versions = await reader.Versions.AsNoTracking().Where(x => x.Scope == request.Scope && x.ProfileVersion == profile.Metadata.VersionId)
                .Take(qualification.MaximumRows + 1).ToArrayAsync(ct).ConfigureAwait(false);
            if (publications.Length > qualification.MaximumRows || versions.Length > qualification.MaximumRows) throw new ContentUnavailableException();
            if (await reader.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM presentation_content.publications WHERE \"Scope\"={request.Scope} AND \"ProfileVersion\"={profile.Metadata.VersionId} AND NOT presentation_content.valid_publication(\"Id\")").SingleAsync(ct).ConfigureAwait(false) != 0)
                throw new ContentUnavailableException();
            var bundle = ResolveBundle(request, profile, publications, versions);
            var availability = SelectionAvailability(request, profile, publications, bundle.Content.Values, qualification.Metadata.EffectiveInterval);
            var finalAt = clock.GetUtcNow(); qualification.Require(request.Scope, finalAt);
            var currentProfile = profiles.RequireEffective(request.Scope, finalAt); currentProfile.Require(request.Scope, finalAt);
            if (currentProfile.Snapshot != profileSnapshot || !availability.Contains(clock.GetUtcNow()) || !ReferenceEquals(transaction.Connection, connection))
                throw new ContentUnavailableException();
            var result = new ContentTransactionResolution(bundle, availability, profile.Metadata.VersionId, ContentDigest.Of(profile));
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new ContentUnavailableException(); }
        catch (Exception error) when (error is DbException or TimeoutException or ArgumentException or InvalidOperationException)
        { throw new ContentUnavailableException(); }
    }
}
