using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class RecoveryCodeModelTests
{
    [Fact]
    public void RecoveryStorageContainsVerifiersAndSafeLineageWithOwnerAndTransitionConstraints()
    {
        // Model construction only. This synthetic connection is never opened.
        using var db = new IdentityPersistenceDbContext(new DbContextOptionsBuilder<IdentityPersistenceDbContext>()
            .UseNpgsql("Host=localhost;Database=not_opened;Username=synthetic").Options);
        var model = db.GetService<IDesignTimeModel>().Model;
        var entry = model.FindEntityType(typeof(RecoveryCodeEntryRow))!;
        var set = model.FindEntityType(typeof(RecoveryCodeSetRow))!;
        var operation = model.FindEntityType(typeof(RecoveryCodeOperationRow))!;
        Assert.True(entry.GetTableName() == "recovery_code_entries" && set.GetTableName() == "recovery_code_sets" && operation.GetTableName() == "recovery_code_operations");
        Assert.True(entry.GetProperties().All(x => x.Name is not ("Code" or "Plaintext" or "ProtectedCode")));
        Assert.Contains(entry.GetForeignKeys(), x => x.Properties.Select(p => p.Name).SequenceEqual(["AccountId", "SetReference"]));
        Assert.Contains(set.GetForeignKeys(), x => x.Properties.Select(p => p.Name).SequenceEqual(["AccountId", "IssuanceOperationId", "SetReference"]) &&
            x.PrincipalKey.Properties.Select(p => p.Name).SequenceEqual(["AccountId", "OperationId", "SetReference"]));
        Assert.Contains(set.GetForeignKeys(), x => x.Properties.Select(p => p.Name).SequenceEqual(["AccountId", "RetiredByOperationId"]) &&
            x.PrincipalKey.Properties.Select(p => p.Name).SequenceEqual(["AccountId", "OperationId"]));
        Assert.Contains(set.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["IssuanceOperationId"]));
        Assert.True(set.GetProperties().Where(x => x.IsConcurrencyToken).Select(x => x.Name).Order().SequenceEqual(["LifecycleVersion", "UsageVersion"]));
        Assert.True(entry.FindProperty("Version")!.IsConcurrencyToken && operation.FindProperty("Version")!.IsConcurrencyToken);
        Assert.Contains(operation.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["SessionId", "RequestId"]));
        var consumed = entry.GetCheckConstraints().Single(x => x.Name == "ck_recovery_consumption").Sql;
        Assert.True(consumed.Contains("consumed_by_operation_id IS NOT NULL", StringComparison.Ordinal) && consumed.Contains("consumption_correlation_id IS NOT NULL", StringComparison.Ordinal));
        Assert.True(set.GetCheckConstraints().Single(x => x.Name == "ck_recovery_set_retirement").Sql.Contains("retired_at IS NOT NULL", StringComparison.Ordinal));
        Assert.True(operation.GetCheckConstraints().Single(x => x.Name == "ck_recovery_first_response").Sql.Contains("first_response_committed_at IS NOT NULL", StringComparison.Ordinal));
        Assert.True(model.GetEntityTypes().All(x => !x.Name.Contains("RecoveryCase", StringComparison.Ordinal)));
    }
}
