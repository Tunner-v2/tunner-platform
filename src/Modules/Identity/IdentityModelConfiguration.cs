using Microsoft.EntityFrameworkCore;
using Tunner.Accounts;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

internal static class IdentityModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.HasDefaultSchema("identity");
        model.Entity<AccountRow>(b =>
        {
            b.ToTable("accounts", "accounts", t =>
            {
                t.HasCheckConstraint("ck_account_id", "left(account_id, 4) = 'ACC-' AND length(account_id) > 4");
                t.HasCheckConstraint("ck_account_state", "state IN ('VERIFICATION_PENDING','ACTIVE','RESTRICTED','CLOSING','CLOSED')");
                t.HasCheckConstraint("ck_account_version", "version >= 1");
                t.HasCheckConstraint("ck_account_provenance", "length(operation_id) > 0 AND length(correlation_id) > 0 AND length(evidence_reference) > 0");
                t.HasCheckConstraint("ck_account_age", "(age_eligibility_status IS NULL AND age_evaluated_at_utc IS NULL AND age_policy_version IS NULL AND age_evidence_reference IS NULL) OR (age_eligibility_status IN ('ELIGIBLE','INELIGIBLE') AND age_evaluated_at_utc IS NOT NULL AND age_policy_version IS NOT NULL AND age_evidence_reference IS NOT NULL AND length(age_policy_version) > 0 AND length(age_evidence_reference) > 0)");
            });
            b.HasKey(x => x.AccountId);
            b.Property(x => x.AccountId).HasColumnName("account_id");
            b.Property(x => x.State).HasColumnName("state").HasConversion(x => WriteState(x), x => ReadState(x));
            b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            b.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc");
            b.Property(x => x.OperationId).HasColumnName("operation_id").IsRequired();
            b.Property(x => x.CorrelationId).HasColumnName("correlation_id").IsRequired();
            b.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").IsRequired();
            b.Property(x => x.AgeEligibilityStatus).HasColumnName("age_eligibility_status");
            b.Property(x => x.AgeEvaluatedAtUtc).HasColumnName("age_evaluated_at_utc");
            b.Property(x => x.AgePolicyVersion).HasColumnName("age_policy_version");
            b.Property(x => x.AgeEvidenceReference).HasColumnName("age_evidence_reference");
        });
        model.Entity<AccountStateRow>(b =>
        {
            b.ToTable("account_state_versions", "accounts");
            b.HasKey(x => new { x.AccountId, x.Version });
            b.Property(x => x.AccountId).HasColumnName("account_id");
            b.Property(x => x.Version).HasColumnName("version");
            b.Property(x => x.State).HasColumnName("state").HasConversion(x => WriteState(x), x => ReadState(x));
            b.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc");
            b.Property(x => x.OperationId).HasColumnName("operation_id").IsRequired();
            b.Property(x => x.CorrelationId).HasColumnName("correlation_id").IsRequired();
            b.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").IsRequired();
            b.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<VerifiedContactRow>(b =>
        {
            b.ToTable("verified_contacts", "accounts", t =>
            {
                t.HasCheckConstraint("ck_contact_state", "state IN ('UNVERIFIED','VERIFIED','SUPERSEDED','REVOKED')");
                t.HasCheckConstraint("ck_contact_version", "version >= 1");
                t.HasCheckConstraint("ck_contact_anonymization", "(anonymized_at_utc IS NULL AND normalized_email IS NOT NULL AND length(normalized_email) > 0) OR (anonymized_at_utc IS NOT NULL AND normalized_email IS NULL)");
            });
            b.HasKey(x => x.ContactId);
            b.Property(x => x.ContactId).HasColumnName("contact_id").ValueGeneratedNever();
            b.Property(x => x.AccountId).HasColumnName("account_id").IsRequired();
            b.Property(x => x.NormalizedEmail).HasColumnName("normalized_email");
            b.Property(x => x.State).HasColumnName("state").HasConversion(x => x.ToString().ToUpperInvariant(), x => Enum.Parse<ContactVerificationState>(x, true));
            b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            b.Property(x => x.AnonymizedAtUtc).HasColumnName("anonymized_at_utc");
            b.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").IsRequired();
            b.HasIndex(x => x.NormalizedEmail).IsUnique().HasFilter("state = 'VERIFIED' AND normalized_email IS NOT NULL");
            b.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ExternalIdentityLinkRow>(b =>
        {
            b.ToTable("external_identity_links", "identity", t =>
            {
                t.HasCheckConstraint("ck_external_identity_key", "length(provider) > 0 AND length(subject) > 0");
                t.HasCheckConstraint("ck_external_version", "version >= 1");
            });
            b.HasKey(x => x.LinkId);
            b.Property(x => x.LinkId).HasColumnName("link_id").ValueGeneratedNever();
            b.Property(x => x.AccountId).HasColumnName("account_id").IsRequired();
            b.Property(x => x.Provider).HasColumnName("provider").IsRequired();
            b.Property(x => x.Subject).HasColumnName("subject").IsRequired();
            b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            b.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").IsRequired();
            b.HasIndex(x => new { x.Provider, x.Subject }).IsUnique();
            b.HasOne<AccountRow>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<IdentityUserAdapter>(b =>
        {
            b.Property(x => x.AccountId).IsRequired();
            b.HasIndex(x => x.AccountId).IsUnique();
            b.HasOne<AccountRow>().WithOne().HasForeignKey<IdentityUserAdapter>(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });
        OutboxModelConfiguration.Configure(model);
        model.Entity<OutboxMessage>().ToTable("outbox_messages", "platform", t => t.ExcludeFromMigrations());
    }

    private static string WriteState(AccountState state) => state == AccountState.VerificationPending
        ? "VERIFICATION_PENDING" : state.ToString().ToUpperInvariant();
    private static AccountState ReadState(string state) => state == "VERIFICATION_PENDING"
        ? AccountState.VerificationPending : Enum.Parse<AccountState>(state, true);
}
