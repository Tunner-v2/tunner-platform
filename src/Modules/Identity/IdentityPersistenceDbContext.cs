using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tunner.Accounts;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

public sealed class IdentityPersistenceDbContext(DbContextOptions<IdentityPersistenceDbContext> options)
    : IdentityUserContext<IdentityUserAdapter>(options), ITransactionalOutboxContext
{
    internal DbSet<AccountRow> Accounts => Set<AccountRow>();
    internal DbSet<AccountStateRow> AccountStateVersions => Set<AccountStateRow>();
    internal DbSet<VerifiedContactRow> VerifiedContacts => Set<VerifiedContactRow>();
    internal DbSet<ExternalIdentityLinkRow> ExternalIdentityLinks => Set<ExternalIdentityLinkRow>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        IdentityModelConfiguration.Configure(builder);
        RegistrationModelConfiguration.Configure(builder);
        AccountPersonalInformationModel.Configure(builder);
    }

    public async Task<AccountSnapshot?> ReadAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        var row = await Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId, cancellationToken).ConfigureAwait(false);
        return row is null ? null : new AccountSnapshot(row.AccountId, row.State, row.Version,
            row.AgeEligibilityStatus is null ? null : Enum.Parse<AgeEligibilityStatus>(row.AgeEligibilityStatus, true),
            row.AgeEvaluatedAtUtc, row.AgePolicyVersion, row.AgeEvidenceReference);
    }

    Task<IDbContextTransaction> ITransactionalOutboxContext.BeginOutboxTransactionAsync(CancellationToken cancellationToken)
        => Database.BeginTransactionAsync(cancellationToken);
    void ITransactionalOutboxContext.AddOutboxMessage(OutboxMessage message) => Set<OutboxMessage>().Add(message);
    Task<int> ITransactionalOutboxContext.SaveChangesAsync(CancellationToken cancellationToken)
        => base.SaveChangesAsync(cancellationToken);
}
