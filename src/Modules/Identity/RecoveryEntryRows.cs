using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

/// <summary>Neutral original browser request. No identifier plaintext, existence flag, fake Account or public owner selector.</summary>
internal sealed class RecoveryEntryRow
{
    public Guid EntryId { get; set; }
    public Guid RequestId { get; set; }
    public string BrowserDigest { get; set; } = string.Empty;
    public string PreviousCookieDigest { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string NetworkDigest { get; set; } = string.Empty;
    public string IdentifierDigest { get; set; } = string.Empty;
    public string GoalReference { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public string PolicySnapshot { get; set; } = string.Empty;
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset RecoveryExpiresAt { get; set; }
    public string State { get; set; } = "OPEN";
    public long Version { get; set; } = 1;
    public DateTimeOffset? EndedAt { get; set; }
    public override string ToString() => nameof(RecoveryEntryRow);
}
internal static class RecoveryEntryModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<RecoveryEntryRow>(b =>
        {
            b.ToTable("recovery_entries", "identity", t =>
            {
                t.HasCheckConstraint("ck_recovery_entry_binding", "length(browser_digest)=64 AND length(previous_cookie_digest)=64 AND length(network_digest)=64 AND length(identifier_digest)=64 AND length(goal_reference)>0 AND length(policy_version)>0 AND length(processing_reference)>0 AND length(retention_reference)>0 AND jsonb_typeof(policy_snapshot::jsonb)='object'");
                t.HasCheckConstraint("ck_recovery_entry_state", "expires_at>created_at AND recovery_expires_at>expires_at AND ((state='OPEN' AND version=1 AND ended_at IS NULL) OR (state IN ('CANCELLED','REPAIRED') AND version=2 AND ended_at>=created_at AND ended_at<expires_at))");
            });
            b.HasKey(x => x.EntryId); b.Property(x => x.EntryId).ValueGeneratedNever();
            b.HasIndex(x => new { x.BrowserDigest, x.RequestId }).IsUnique(); b.HasIndex(x => x.ExpiresAt);
            b.Property(x => x.Version).IsConcurrencyToken();
        });
        model.Entity<RecoveryCaseRow>().HasIndex(x => x.EntryId).IsUnique();
        IdentityColumnNames.UseSnakeCase(model, typeof(RecoveryEntryRow));
    }
}
