using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class DeviceTrustModelTests
{
    [Fact]
    public void DeviceEvidenceIsAccountOwnedVersionedAndIndependentFromCredentialAndAuthenticationBrowserState()
    {
        using var database = new IdentityPersistenceDbContext(new DbContextOptionsBuilder<IdentityPersistenceDbContext>()
            .UseNpgsql("Host=localhost;Database=never_opened;Username=synthetic").Options);
        var model = database.GetService<IDesignTimeModel>().Model;
        var record = model.FindEntityType(typeof(DeviceRecordRow))!; var trust = model.FindEntityType(typeof(DeviceTrustRow))!;
        var session = model.FindEntityType(typeof(DeviceSessionEvidenceRow))!; var operation = model.FindEntityType(typeof(DeviceOperationRow))!;
        Assert.True(record.GetTableName() == "device_records" && trust.GetTableName() == "device_trust_versions" && session.GetTableName() == "device_session_evidence");
        Assert.True(record.FindProperty("Version")!.IsConcurrencyToken && operation.FindProperty("Version")!.IsConcurrencyToken);
        Assert.Contains(record.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["AccountId", "Origin", "CorrelationDigest"]));
        Assert.Equal(["DeviceId", "Version"], trust.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Equal(["SessionId"], session.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.Contains(session.GetForeignKeys(), x => x.Properties.Select(p => p.Name).SequenceEqual(["AccountId", "Origin", "DeviceId", "TrustVersion"]));
        Assert.Contains(operation.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["SessionId", "RequestId"]));
        foreach (var entity in new[] { record, trust, session, operation })
        {
            Assert.True(entity.GetForeignKeys().All(x => x.DeleteBehavior == DeleteBehavior.Restrict));
            Assert.True(entity.GetProperties().All(x => x.Name is not ("IpAddress" or "Fingerprint" or "UserAgent" or "Token" or "Cookie" or "BrowserDigest" or "Password")));
        }
        Assert.Contains("result_trust_version=expected_trust_version+1", operation.GetCheckConstraints().Single(x => x.Name == "ck_device_operation").Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionalCollectionRequiresEveryQualifiedOwnerSource()
    {
        Assert.Throws<InvalidOperationException>(() => DeviceBrowserService.AddQualifiedEvidence(new ServiceCollection()));
        Assert.Equal(nameof(DeviceTrustRemovalRequest), new DeviceTrustRemovalRequest(Guid.NewGuid(), Guid.NewGuid(), 1, 1, 1, "synthetic").ToString());
        Assert.Throws<ArgumentException>(() => new DeviceTrustRemovalRequest(Guid.Empty, Guid.NewGuid(), 1, 1, 1, "synthetic").Validate());
    }

    [Fact]
    public void DeviceEvidenceDisclosureOmitsInternalCorrelationPrivacyAndAuthenticationFields()
    {
        var names = typeof(DeviceListingItem).GetProperties().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        Assert.True(names.SetEquals(["DeviceReference", "Version", "TrustVersion", "ObservedForCurrentSession", "FirstObservedAt", "LastObservedAt",
            "Characteristics", "RecordedTrustState", "EffectiveTrustState", "EvaluatedAt"]));
        Assert.True(typeof(DeviceEvidencePolicy).IsNotPublic && typeof(DeviceTrustRemovalRequest).IsNotPublic);
        Assert.True(typeof(DeviceListingItem).IsNotPublic && typeof(DeviceExecutionValidity).IsNotPublic);
    }
}
