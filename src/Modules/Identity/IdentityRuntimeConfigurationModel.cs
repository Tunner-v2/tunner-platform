using Microsoft.EntityFrameworkCore;
using Tunner.Database.Audit;

namespace Tunner.Identity.Persistence;

internal static class IdentityRuntimeConfigurationModel
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<IdentityConfigurationGovernanceRow>(b => { b.ToTable("runtime_configuration_governance", "identity"); b.HasKey(x => new { x.ScopeKey, x.Version }); });
        model.Entity<IdentityConfigurationProposalRow>(b =>
        {
            b.ToTable("runtime_configuration_proposals", "identity", t => t.HasCheckConstraint("ck_identity_configuration_proposal", "revision>=1 AND state IN ('DRAFT','VALIDATED','APPROVED','PUBLISHED')"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne<IdentityConfigurationGovernanceRow>().WithMany().HasForeignKey(x => new { x.ScopeKey, Version = x.GovernanceVersion }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<IdentityConfigurationVersionRow>(b =>
        {
            b.ToTable("runtime_configuration_versions", "identity"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => new { x.ScopeKey, x.PolicyVersionReference }).IsUnique();
            b.HasOne<IdentityConfigurationProposalRow>().WithMany().HasForeignKey(x => x.ProposalReference).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<IdentityConfigurationPublicationRow>(b =>
        {
            b.ToTable("runtime_configuration_publications", "identity", t => t.HasCheckConstraint("ck_identity_configuration_interval", "effective_until IS NULL OR effective_until>effective_from OR (effective_until=effective_from AND includes_from AND includes_until)"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.HasOne<IdentityConfigurationVersionRow>().WithMany().HasForeignKey(x => x.VersionReference).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<IdentityConfigurationPublicationRow>().WithMany().HasForeignKey(x => x.SupersedesPublicationReference).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.ScopeKey); b.HasIndex(x => x.SupersedesPublicationReference).IsUnique();
        });
        IdentityColumnNames.UseSnakeCase(model, typeof(IdentityConfigurationGovernanceRow), typeof(IdentityConfigurationProposalRow),
            typeof(IdentityConfigurationVersionRow), typeof(IdentityConfigurationPublicationRow));
        AuditModelConfiguration.Configure(model);
        model.Entity<AuditEventRecord>().ToTable("events", "audit", t => t.ExcludeFromMigrations());
    }
}
internal sealed class IdentityConfigurationGovernanceRow { public string ScopeKey { get; set; } = ""; public string Version { get; set; } = ""; public string Snapshot { get; set; } = ""; }
internal sealed class IdentityConfigurationProposalRow
{
    public Guid Id { get; set; } public string ScopeKey { get; set; } = ""; public string GovernanceVersion { get; set; } = ""; public string Payload { get; set; } = "";
    public string Digest { get; set; } = ""; public string PolicyVersionReference { get; set; } = ""; public string ContributorsJson { get; set; } = "[]";
    public long Revision { get; set; } public string State { get; set; } = "DRAFT"; public bool ModelOrigin { get; set; } public Guid? VersionReference { get; set; }
    public string? ApproverReference { get; set; } public string? ApprovalEvidenceReference { get; set; } public Guid? ApprovalEventReference { get; set; }
    public Guid LastEventReference { get; set; } public string LastOperation { get; set; } = ""; public Guid LastTargetReference { get; set; }
    public string LastActorReference { get; set; } = "";
}
internal sealed class IdentityConfigurationVersionRow
{
    public Guid Id { get; set; } public Guid ProposalReference { get; set; } public string ScopeKey { get; set; } = ""; public string GovernanceVersion { get; set; } = "";
    public string PolicyVersionReference { get; set; } = ""; public string Payload { get; set; } = ""; public string Digest { get; set; } = "";
    public string ContributorsJson { get; set; } = "[]"; public bool ModelOrigin { get; set; } public Guid ValidationEventReference { get; set; }
}
internal sealed class IdentityConfigurationPublicationRow
{
    public Guid Id { get; set; } public Guid VersionReference { get; set; } public string ScopeKey { get; set; } = "";
    public DateTimeOffset EffectiveFrom { get; set; } public bool IncludesFrom { get; set; } public DateTimeOffset? EffectiveUntil { get; set; } public bool IncludesUntil { get; set; }
    public Guid? SupersedesPublicationReference { get; set; } public string ApproverReference { get; set; } = ""; public string ApprovalEvidenceReference { get; set; } = "";
    public Guid ApprovalEventReference { get; set; } public string PublisherReference { get; set; } = ""; public string PublisherEvidenceReference { get; set; } = "";
    public string PublisherAuthorizationVersion { get; set; } = ""; public Guid PublicationEventReference { get; set; } public string Operation { get; set; } = "";
}
