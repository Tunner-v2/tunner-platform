using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tunner.Database;
using Tunner.Database.Audit;
using Xunit;

namespace Tunner.Database.IntegrationTests;

internal static class AuditTestData
{
    internal static AuditEvent Event(Guid? id = null, DateTimeOffset? time = null) => new(
        id ?? Guid.NewGuid(), time ?? DateTimeOffset.Parse("2026-10-05T12:00:00.1234567+00:00", System.Globalization.CultureInfo.InvariantCulture),
        new AuditActor("test-principal", "test-actor-type", ["test-identity-ref"]),
        "test-only.record", "test-operation", "test-correlation", [new("test-resource", "v2")],
        "test-only.success", "test-classification", "test-retention-class-ref",
        causationEventId: "test-cause", reason: "test-reason",
        permissionVersionReferences: ["permission-v1"], scopeVersionReferences: ["scope-v1"],
        policyVersionReferences: ["policy-v1"], stepUpReferences: ["step-up-ref"], approvalReferences: ["approval-ref"],
        beforeVersions: [new("test-resource", "v1")], afterVersions: [new("test-resource", "v2")],
        providerReferences: ["provider-ref"], evidenceReferences: ["evidence-ref"]);
}

public sealed class AuditEventTests
{
    [Fact]
    public void EnvelopeCarriesApprovedIdentityVersionAndEvidenceReferences()
    {
        var entry = AuditTestData.Event();
        using var document = JsonDocument.Parse(entry.ToEnvelope());
        var root = document.RootElement;
        Assert.Equal(entry.EventId, root.GetProperty("event_id").GetGuid());
        Assert.Equal("test-principal", root.GetProperty("actor").GetProperty("principal_id").GetString());
        Assert.Equal("test-identity-ref", root.GetProperty("actor").GetProperty("identity_refs")[0].GetString());
        Assert.Equal("test-operation", root.GetProperty("operation_id").GetString());
        Assert.Equal("test-correlation", root.GetProperty("correlation_id").GetString());
        Assert.Equal("test-cause", root.GetProperty("causation_event_id").GetString());
        Assert.Equal("v2", root.GetProperty("targets")[0].GetProperty("version").GetString());
        foreach (var property in new[] { "permission_version_refs", "scope_version_refs", "policy_version_refs", "step_up_refs", "approval_refs", "before_versions", "after_versions", "provider_refs", "evidence_refs" })
            Assert.Equal(1, root.GetProperty(property).GetArrayLength());
        Assert.Equal("test-only.success", root.GetProperty("result").GetString());
        Assert.Equal("test-classification", root.GetProperty("data_classification").GetString());
        Assert.Equal("test-retention-class-ref", root.GetProperty("retention_class_ref").GetString());
        Assert.False(root.TryGetProperty("payload", out _));
    }

    [Fact]
    public void CallerCollectionsCannotRewriteAnEventOrItsIdentity()
    {
        string[] identities = ["original-identity"];
        string[] permissions = ["original-permission"];
        AuditTargetVersion[] targets = [new("resource", "v1")];
        var actor = new AuditActor("principal", "owner-type", identities);
        var entry = new AuditEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, actor, "action", "operation", "correlation", targets, "owner-result", "classification", "retention", permissionVersionReferences: permissions);
        var before = entry.ToEnvelope();
        identities[0] = "changed"; permissions[0] = "changed"; targets[0] = new("other", "v2");
        Assert.Equal(before, entry.ToEnvelope());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)entry.PermissionVersionReferences)[0] = "changed");
    }

    [Theory]
    [InlineData("id")]
    [InlineData("action")]
    [InlineData("operation")]
    [InlineData("correlation")]
    [InlineData("result")]
    [InlineData("classification")]
    [InlineData("retention")]
    public void RequiredMetadataCannotBeEmpty(string missing)
    {
        Assert.Throws<ArgumentException>(() => new AuditEvent(
            missing == "id" ? Guid.Empty : Guid.NewGuid(), DateTimeOffset.UtcNow,
            new AuditActor("principal", "type", []), missing == "action" ? " " : "action",
            missing == "operation" ? " " : "operation", missing == "correlation" ? " " : "correlation", [],
            missing == "result" ? " " : "result", missing == "classification" ? " " : "classification", missing == "retention" ? " " : "retention"));
    }

    [Fact]
    public void InvalidActorTargetsAndOptionalReferencesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new AuditActor(" ", "type", []));
        Assert.Throws<ArgumentException>(() => new AuditActor("principal", " ", []));
        Assert.Throws<ArgumentException>(() => new AuditActor("principal", "type", [" "]));
        Assert.Throws<ArgumentException>(() => new AuditTargetVersion("resource", " "));
        var actor = new AuditActor("principal", "type", []);
        Assert.Throws<ArgumentNullException>(() => new AuditEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, actor, "action", "operation", "correlation", null!, "result", "class", "retention"));
        Assert.Throws<ArgumentException>(() => new AuditEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, actor, "action", "operation", "correlation", [null!], "result", "class", "retention"));
        Assert.Throws<ArgumentException>(() => new AuditEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, actor, "action", "operation", "correlation", [], "result", "class", "retention", evidenceReferences: [" "]));
        Assert.Throws<ArgumentException>(() => new AuditEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, actor, "action", "operation", "correlation", [], "result", "class", "retention", reason: " "));
    }

    [Fact]
    public void EquivalentInstantsHaveTheSameEnvelopeAndExactTimeIsPreserved()
    {
        var id = Guid.NewGuid();
        var instant = DateTimeOffset.Parse("2026-10-05T12:00:00.1234567Z", System.Globalization.CultureInfo.InvariantCulture);
        var utc = AuditTestData.Event(id, instant);
        Assert.Equal(utc.ToEnvelope(), AuditTestData.Event(id, instant.ToOffset(TimeSpan.FromHours(-4))).ToEnvelope());
        using var document = JsonDocument.Parse(utc.ToEnvelope());
        Assert.Equal(instant, document.RootElement.GetProperty("occurred_at_utc").GetDateTimeOffset());
        var record = new AuditEventRecord(utc);
        Assert.Equal(0, record.OccurredAtUtc.Ticks % 10);
        Assert.True(record.VerifyIntegrity());
    }

    [Fact]
    public void ExactBytesAndIntegrityVersionAreRequired()
    {
        var record = new AuditEventRecord(AuditTestData.Event());
        Assert.True(record.VerifyIntegrity());
        record.RequireIntegrity();
        Assert.False(AuditEventIntegrity.VerifyEnvelope(record.Envelope.Replace("test-only.success", "test-only.succesx", StringComparison.Ordinal), record.IntegrityFormat, record.EnvelopeSha256));
        Assert.False(AuditEventIntegrity.VerifyEnvelope(record.Envelope + " ", record.IntegrityFormat, record.EnvelopeSha256));
        Assert.False(AuditEventIntegrity.VerifyEnvelope(record.Envelope, "unknown-format", record.EnvelopeSha256));
        Assert.False(AuditEventIntegrity.VerifyEnvelope(record.Envelope, record.IntegrityFormat, new string('0', 64)));
    }

    [Fact]
    public void OptionalOwnerReferencesRemainOptionalWithoutInventedDefaults()
    {
        var entry = new AuditEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, new AuditActor("principal", "owner-type", []), "action", "operation", "correlation", [], "owner-result", "classification", "retention");
        Assert.Empty(entry.Targets);
        Assert.Empty(entry.ApprovalReferences);
        Assert.Null(entry.Reason);
        Assert.Null(entry.CausationEventId);
        Assert.True(new AuditEventRecord(entry).VerifyIntegrity());
    }

    [Fact]
    public void StagingAddsOnlyANewAuditRecordWithoutClaimingDurableSuccess()
    {
        using var context = new TunnerMigrationsDbContext(new DbContextOptionsBuilder<TunnerMigrationsDbContext>().UseNpgsql("Host=unused.invalid;Database=unused").Options);
        EfAuditAppender.StageAppend(context, AuditTestData.Event());
        var entry = Assert.Single(context.ChangeTracker.Entries());
        Assert.IsType<AuditEventRecord>(entry.Entity);
        Assert.Equal(EntityState.Added, entry.State);
    }
}
