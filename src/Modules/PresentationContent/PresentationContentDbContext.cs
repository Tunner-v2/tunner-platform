using Microsoft.EntityFrameworkCore;
using Tunner.Database.Audit;
using Tunner.Database.Outbox;

namespace Tunner.PresentationContent;

public sealed class PresentationContentDbContext(DbContextOptions<PresentationContentDbContext> options) : DbContext(options)
{
    internal DbSet<ContentProfileRow> Profiles => Set<ContentProfileRow>();
    internal DbSet<ContentDefinitionRow> Definitions => Set<ContentDefinitionRow>();
    internal DbSet<ContentVariantRow> Variants => Set<ContentVariantRow>();
    internal DbSet<ContentProposalRow> Proposals => Set<ContentProposalRow>();
    internal DbSet<ContentVersionRow> Versions => Set<ContentVersionRow>();
    internal DbSet<ContentPublicationRow> Publications => Set<ContentPublicationRow>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ContentProfileRow>(b => { b.ToTable("profiles", "presentation_content"); b.HasKey(x => new { x.Scope, x.Version }); });
        modelBuilder.Entity<ContentDefinitionRow>(b => { b.ToTable("definitions", "presentation_content"); b.HasKey(x => new { x.Scope, x.ProfileVersion, x.Key });
            b.HasOne<ContentProfileRow>().WithMany().HasForeignKey(x => new { x.Scope, Version = x.ProfileVersion }).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<ContentVariantRow>(b => { b.ToTable("variants", "presentation_content"); b.HasKey(x => new { x.Scope, x.ProfileVersion, x.Reference });
            b.HasOne<ContentProfileRow>().WithMany().HasForeignKey(x => new { x.Scope, Version = x.ProfileVersion }).OnDelete(DeleteBehavior.Restrict); });
        modelBuilder.Entity<ContentProposalRow>(b =>
        {
            b.ToTable("proposals", "presentation_content", t => t.HasCheckConstraint("ck_content_proposal", "\"Revision\">=1 AND \"State\" IN ('DRAFT','VALIDATED','APPROVED','PUBLISHED')"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne<ContentDefinitionRow>().WithMany().HasForeignKey(x => new { x.Scope, x.ProfileVersion, x.Key }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ContentVariantRow>().WithMany().HasForeignKey(x => new { x.Scope, x.ProfileVersion, Reference = x.VariantReference }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ContentVersionRow>(b =>
        {
            b.ToTable("versions", "presentation_content"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.HasOne<ContentProposalRow>().WithMany().HasForeignKey(x => x.ProposalReference).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.Scope, x.ProfileVersion, x.Key, x.VariantReference });
        });
        modelBuilder.Entity<ContentPublicationRow>(b =>
        {
            b.ToTable("publications", "presentation_content", t => t.HasCheckConstraint("ck_content_publication_interval", "\"Until\" IS NULL OR \"Until\">\"From\" OR (\"Until\"=\"From\" AND \"IncludesFrom\" AND \"IncludesUntil\")"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.HasOne<ContentVersionRow>().WithMany().HasForeignKey(x => x.VersionReference).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ContentPublicationRow>().WithMany().HasForeignKey(x => x.SupersedesPublicationReference).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.Scope, x.ProfileVersion, x.Key, x.VariantReference }); b.HasIndex(x => x.SupersedesPublicationReference).IsUnique();
        });
        AuditModelConfiguration.Configure(modelBuilder); OutboxModelConfiguration.Configure(modelBuilder);
        modelBuilder.Entity<AuditEventRecord>().ToTable("events", "audit", t => t.ExcludeFromMigrations());
        modelBuilder.Entity<OutboxMessage>().ToTable("outbox_messages", "platform", t => t.ExcludeFromMigrations());
    }
    private void RequireImmutableHistory()
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries().Any(x => (x.State is EntityState.Modified or EntityState.Deleted) &&
            x.Entity is ContentProfileRow or ContentDefinitionRow or ContentVariantRow or ContentVersionRow or ContentPublicationRow))
            throw new InvalidOperationException("Content definitions, qualified profiles, versions and publications are immutable.");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess) { RequireImmutableHistory(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    { RequireImmutableHistory(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }
}
internal sealed class ContentProfileRow { public string Scope { get; set; } = ""; public string Version { get; set; } = ""; public string Snapshot { get; set; } = ""; }
internal sealed class ContentDefinitionRow { public string Scope { get; set; } = ""; public string ProfileVersion { get; set; } = ""; public string Key { get; set; } = ""; public string Snapshot { get; set; } = ""; }
internal sealed class ContentVariantRow { public string Scope { get; set; } = ""; public string ProfileVersion { get; set; } = ""; public string Reference { get; set; } = ""; public string Snapshot { get; set; } = ""; }
internal sealed class ContentProposalRow
{
    public Guid Id { get; set; } public string Scope { get; set; } = ""; public string ProfileVersion { get; set; } = ""; public string Key { get; set; } = "";
    public string VariantReference { get; set; } = ""; public string Text { get; set; } = ""; public long Revision { get; set; } public string State { get; set; } = "DRAFT";
    public string ContributorsJson { get; set; } = "[]"; public string SourceEvidenceReference { get; set; } = ""; public bool ModelOrigin { get; set; }
    public Guid? VersionReference { get; set; } public string? ApproverReference { get; set; } public string? ApprovalEvidenceReference { get; set; }
    public Guid LastEventReference { get; set; } public Guid LastTargetReference { get; set; } public string LastOperation { get; set; } = "";
    public string LastActorReference { get; set; } = "";
    public string LastDigest { get; set; } = "";
    public Guid? ApprovalEventReference { get; set; }
    public string? ChangeReason { get; set; }
}
internal sealed class ContentVersionRow
{
    public Guid Id { get; set; } public Guid ProposalReference { get; set; } public string Scope { get; set; } = ""; public string ProfileVersion { get; set; } = "";
    public string Key { get; set; } = ""; public string VariantReference { get; set; } = ""; public string Locale { get; set; } = ""; public string Text { get; set; } = "";
    public string TextDigest { get; set; } = ""; public string ContributorsJson { get; set; } = "[]"; public string SourceEvidenceReference { get; set; } = "";
    public bool ModelOrigin { get; set; }
    public Guid ValidationEventReference { get; set; }
    public string? ChangeReason { get; set; }
}
internal sealed class ContentPublicationRow
{
    public Guid Id { get; set; } public Guid VersionReference { get; set; } public string Scope { get; set; } = ""; public string ProfileVersion { get; set; } = "";
    public string Key { get; set; } = ""; public string VariantReference { get; set; } = ""; public DateTimeOffset From { get; set; } public bool IncludesFrom { get; set; }
    public DateTimeOffset? Until { get; set; } public bool IncludesUntil { get; set; } public Guid? SupersedesPublicationReference { get; set; }
    public string PublisherReference { get; set; } = ""; public string? ApproverReference { get; set; } public string? ApprovalEvidenceReference { get; set; }
    public string PublisherEvidenceReference { get; set; } = ""; public string PublisherAuthorizationVersion { get; set; } = "";
    public Guid PublicationEventReference { get; set; } public string Operation { get; set; } = "";
    public Guid? ApprovalEventReference { get; set; }
}
