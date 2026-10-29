using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed class AuthenticationSessionRow
{
    public Guid SessionId { get; set; }
    public string KeyDigest { get; set; } = string.Empty;
    public string ProtectedKey { get; set; } = string.Empty;
    public string ProtectedTicket { get; set; } = string.Empty;
    public string BrowserDigest { get; set; } = string.Empty;
    public Guid IdempotencyKey { get; set; }
    public string IdentifierDigest { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string SecurityStampDigest { get; set; } = string.Empty;
    public string Scheme { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string AuthenticationMethods { get; set; } = string.Empty;
    public string Assurance { get; set; } = string.Empty;
    public DateTimeOffset AuthenticatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public long IdleTicks { get; set; }
    public string State { get; set; } = "ACTIVE";
    public DateTimeOffset? RevokedAt { get; set; }
    public string PolicyVersion { get; set; } = string.Empty;
    public string AccessPolicyVersion { get; set; } = string.Empty;
    public string AccessEvidenceReference { get; set; } = string.Empty;
    public string ProcessingReference { get; set; } = string.Empty;
    public string RetentionReference { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public long Version { get; set; }
}
internal sealed class AuthenticationAbuseRow
{
    public string Partition { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = string.Empty;
    public long Window { get; set; }
    public int Count { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
internal static class AuthenticationModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<AuthenticationSessionRow>(b =>
        {
            b.ToTable("tunner_sessions", "identity", t =>
            {
                t.HasCheckConstraint("ck_session_state", "state IN ('ACTIVE','REVOKED','EXPIRED')");
                t.HasCheckConstraint("ck_session_bounds", "expires_at > authenticated_at AND last_seen_at >= authenticated_at AND idle_ticks > 0 AND version >= 1");
                t.HasCheckConstraint("ck_session_key", "length(key_digest) = 64 AND length(browser_digest) = 64");
            });
            b.HasKey(x => x.SessionId); b.Property(x => x.SessionId).ValueGeneratedNever();
            b.HasIndex(x => x.KeyDigest).IsUnique();
            b.HasIndex(x => new { x.BrowserDigest, x.IdempotencyKey }).IsUnique();
            b.HasIndex(x => x.AccountId);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AuthenticationAbuseRow>(b =>
        {
            b.ToTable("authentication_abuse", "identity");
            b.HasKey(x => new { x.Partition, x.PolicyVersion, x.Window });
        });
        foreach (var type in new[] { typeof(AuthenticationSessionRow), typeof(AuthenticationAbuseRow) })
            foreach (var property in model.Entity(type).Metadata.GetProperties())
                property.SetColumnName(string.Concat(property.Name.Select((c, i) => char.IsUpper(c) ? (i > 0 ? "_" : "") + char.ToLowerInvariant(c) : c.ToString())));
    }
}
