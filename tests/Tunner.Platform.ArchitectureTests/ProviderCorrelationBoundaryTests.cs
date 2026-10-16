using Tunner.Platform.Operations;
using Tunner.Platform.Providers;
using Xunit;

namespace Tunner.Platform.ArchitectureTests;

public sealed class ProviderCorrelationBoundaryTests
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 5, 18, 0, 0, TimeSpan.FromHours(-4));

    private static ProviderBindingReference Binding(string instance = "stripe-instance", string account = "account-ref",
        string environment = "test-environment", string contract = "ADR-0014") => new(instance, account, environment, contract);

    private static TechnicalCommandEnvelope<string> Command(string operation = "operation-1", string correlation = "correlation-1",
        string? key = "stable-key", DateTimeOffset? requestedAt = null) => new(operation, key, correlation, "resource", 4,
            requestedAt ?? Instant, new Dictionary<string, string> { ["policy"] = "owner-version" }, "owner-payload");

    private static ProviderOperationContext Context(string scope = "owner-scope", ProviderBindingReference? binding = null,
        TechnicalCommandEnvelope<string>? command = null) => ProviderOperationContext.FromCommand(command ?? Command(), new IdempotencyScope(scope), binding ?? Binding());

    private static ProviderIdempotencyBinding Identity(ProviderOperationContext? context = null, string fingerprint = "owner-fingerprint", string providerKey = "provider-stable-key") => new(context ?? Context(), fingerprint, providerKey);

    private static ProviderObjectReference Reference(ProviderBindingReference? binding = null) => new(binding ?? Binding(), "payment_intent", "pi_reference", "evt_reference", Instant);

    [Fact]
    public void ContextPreservesOwnerIdentityWithoutCanonicalizingOrGeneratingValues()
    {
        var context = Context(" Owner:1 ", Binding("Instance", "Account", "Environment", "Contract"), Command(" Operation ", "Correlation", " Key "));
        Assert.Equal(" Operation ", context.OperationId);
        Assert.Equal("Correlation", context.CorrelationId);
        Assert.Equal(" Key ", context.IdempotencyKey);
        Assert.Equal(" Owner:1 ", context.OwnerScope.Value);
        Assert.Equal(Binding("Instance", "Account", "Environment", "Contract"), context.Binding);
        Assert.Equal(Instant.ToUniversalTime(), context.RequestedAt);
        Assert.Equal(TimeSpan.Zero, context.RequestedAt.Offset);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("correlation")]
    [InlineData("key")]
    [InlineData("scope")]
    [InlineData("time")]
    public void InvalidCommandIdentityIsRejected(string field)
    {
        var command = field switch
        {
            "operation" => Command(operation: " "),
            "correlation" => Command(correlation: ""),
            "key" => Command(key: null),
            "time" => Command(requestedAt: default(DateTimeOffset)),
            _ => Command()
        };
        Assert.ThrowsAny<ArgumentException>(() => Context(field == "scope" ? " " : "owner-scope", command: command));
    }

    [Theory]
    [InlineData("instance")]
    [InlineData("account")]
    [InlineData("environment")]
    [InlineData("contract")]
    public void MissingBindingInputsAreRejected(string field)
    {
        Assert.ThrowsAny<ArgumentException>(() => Binding(field == "instance" ? " " : "instance",
            field == "account" ? "" : "account", field == "environment" ? "" : "env", field == "contract" ? "" : "contract"));
    }

    [Fact]
    public void EquivalentOwnerRequestsRemainEquivalentAcrossObservationTimes()
    {
        var previous = Identity();
        var later = Identity(Context(command: Command(requestedAt: Instant.AddHours(1))));
        Assert.Equal(ProviderIdempotencyComparison.EquivalentRequest, ProviderIdempotencyGuard.Compare(previous, later));
        Assert.Equal(ProviderIdempotencyComparison.EquivalentRequest, ProviderIdempotencyGuard.Compare(previous, Identity()));
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("key")]
    [InlineData("instance")]
    [InlineData("account")]
    [InlineData("environment")]
    public void DistinctProviderOrOwnerIdentityIsSeparated(string field)
    {
        var other = Context(field == "scope" ? "other-scope" : "owner-scope",
            Binding(field == "instance" ? "other-instance" : "stripe-instance", field == "account" ? "other-account" : "account-ref",
                field == "environment" ? "other-environment" : "test-environment"), Command(key: field == "key" ? "other-key" : "stable-key"));
        Assert.Equal(ProviderIdempotencyComparison.DifferentIdentity, ProviderIdempotencyGuard.Compare(Identity(), Identity(other, providerKey: field is "scope" or "key" ? "other-provider-key" : "provider-stable-key")));
    }

    [Theory]
    [InlineData("fingerprint")]
    [InlineData("operation")]
    [InlineData("correlation")]
    [InlineData("contract")]
    public void SameKeyWithChangedRequestOrTraceIsAConflict(string field)
    {
        var other = Context(binding: Binding(contract: field == "contract" ? "contract-v2" : "ADR-0014"),
            command: Command(field == "operation" ? "operation-2" : "operation-1", field == "correlation" ? "correlation-2" : "correlation-1"));
        Assert.Equal(ProviderIdempotencyComparison.ConflictingRequest,
            ProviderIdempotencyGuard.Compare(Identity(), Identity(other, field == "fingerprint" ? "changed-request" : "owner-fingerprint")));
    }

    [Fact]
    public void IdentityComparisonIsOrdinalAndDoesNotImposeStripeLimits()
    {
        var longKey = new string('k', 300);
        Assert.Equal(longKey, Context(command: Command(key: longKey)).IdempotencyKey);
        Assert.Equal(ProviderIdempotencyComparison.DifferentIdentity,
            ProviderIdempotencyGuard.Compare(Identity(Context(command: Command(key: "KEY"))), Identity(Context(command: Command(key: "key")), providerKey: "other-provider-key")));
        Assert.Equal(ProviderIdempotencyComparison.ConflictingRequest,
            ProviderIdempotencyGuard.Compare(Identity(fingerprint: "Fingerprint"), Identity(fingerprint: "fingerprint")));
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("key")]
    public void SameProviderKeyCannotBeReassignedToAnotherOwnerIdentity(string field)
    {
        var other = Context(field == "scope" ? "other-owner" : "owner-scope", command: Command(key: field == "key" ? "other-owner-key" : "stable-key"));
        Assert.Equal(ProviderIdempotencyComparison.ConflictingRequest, ProviderIdempotencyGuard.Compare(Identity(), Identity(other)));
    }

    [Fact]
    public void OwnerRetryCannotSilentlySwitchItsProviderKey()
    {
        Assert.Equal(ProviderIdempotencyComparison.ConflictingRequest,
            ProviderIdempotencyGuard.Compare(Identity(), Identity(providerKey: "replacement-provider-key")));
    }

    [Fact]
    public void ProviderKeyIsExplicitPreservedAndHasNoGenericVendorLimit()
    {
        var key = " " + new string('p', 300) + " ";
        Assert.Equal(key, Identity(providerKey: key).ProviderIdempotencyKey);
        Assert.ThrowsAny<ArgumentException>(() => Identity(providerKey: " "));
    }

    [Fact]
    public void ProviderKeyComparisonIsOrdinal()
    {
        Assert.Equal(ProviderIdempotencyComparison.ConflictingRequest,
            ProviderIdempotencyGuard.Compare(Identity(providerKey: "KEY"), Identity(providerKey: "key")));
    }

    [Fact]
    public void MetadataAndEvidenceAreImmutableSnapshots()
    {
        var metadata = new Dictionary<string, string> { ["evidence"] = "safe-ref" };
        var reference = new ProviderObjectReference(Binding(), "object-type", "object-id", null, Instant, metadata);
        var input = new List<ProviderObjectReference> { reference };
        var evidence = new ProviderCorrelationEvidence(Context(), input);
        metadata["evidence"] = "changed";
        metadata["new"] = "new";
        input.Clear();
        Assert.Equal("safe-ref", reference.SafeEvidenceMetadataReferences["evidence"]);
        Assert.Single(reference.SafeEvidenceMetadataReferences);
        Assert.Same(reference, Assert.Single(evidence.References));
        Assert.Equal(TimeSpan.Zero, reference.ObservedAt.Offset);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)reference.SafeEvidenceMetadataReferences).Add("x", "y"));
        Assert.Throws<NotSupportedException>(() => ((IList<ProviderObjectReference>)evidence.References).Clear());
    }

    [Theory]
    [InlineData("instance")]
    [InlineData("account")]
    [InlineData("environment")]
    [InlineData("contract")]
    public void EvidenceFromAnotherExactBindingIsRejected(string field)
    {
        var other = Binding(field == "instance" ? "other" : "stripe-instance", field == "account" ? "other" : "account-ref",
            field == "environment" ? "other" : "test-environment", field == "contract" ? "other" : "ADR-0014");
        Assert.Throws<ArgumentException>(() => new ProviderCorrelationEvidence(Context(), [Reference(other)]));
    }

    [Fact]
    public void ObjectEvidencePreservesExactObjectAndEventReferencesAndOriginalTrace()
    {
        var reference = new ProviderObjectReference(Binding(), " payment_intent ", " PI_id ", " EVT_id ", Instant);
        var operation = Context();
        var evidence = new ProviderCorrelationEvidence(operation, [reference]);
        Assert.Equal(" payment_intent ", reference.ProviderObjectType);
        Assert.Equal(" PI_id ", reference.ProviderObjectId);
        Assert.Equal(" EVT_id ", reference.ProviderEventId);
        Assert.Equal(Instant.ToUniversalTime(), reference.ObservedAt);
        Assert.Same(operation, evidence.Operation);
    }

    [Fact]
    public void NoObservedObjectCarriesNoInventedOutcomeAndEventIdIsOptional()
    {
        Assert.Empty(new ProviderCorrelationEvidence(Context(), []).References);
        var reference = new ProviderObjectReference(Binding(), "object", "id", null, Instant);
        Assert.Null(reference.ProviderEventId);
        Assert.Empty(reference.SafeEvidenceMetadataReferences);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("id")]
    [InlineData("event")]
    [InlineData("time")]
    [InlineData("metadata-name")]
    [InlineData("metadata-reference")]
    public void MalformedObjectEvidenceIsRejected(string field)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ProviderObjectReference(Binding(), field == "type" ? "" : "object",
            field == "id" ? " " : "id", field == "event" ? " " : null, field == "time" ? default : Instant,
            new Dictionary<string, string> { [field == "metadata-name" ? "" : "evidence"] = field == "metadata-reference" ? " " : "ref" }));
    }

    [Fact]
    public void NullBoundaryInputsAndMissingFingerprintsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => ProviderOperationContext.FromCommand<string>(null!, new IdempotencyScope("owner"), Binding()));
        Assert.Throws<ArgumentNullException>(() => ProviderOperationContext.FromCommand(Command(), null!, Binding()));
        Assert.Throws<ArgumentNullException>(() => ProviderOperationContext.FromCommand(Command(), new IdempotencyScope("owner"), null!));
        Assert.Throws<ArgumentNullException>(() => new ProviderIdempotencyBinding(null!, "fingerprint", "provider-key"));
        Assert.ThrowsAny<ArgumentException>(() => Identity(fingerprint: " "));
        Assert.Throws<ArgumentNullException>(() => ProviderIdempotencyGuard.Compare(null!, Identity()));
        Assert.Throws<ArgumentNullException>(() => ProviderIdempotencyGuard.Compare(Identity(), null!));
        Assert.Throws<ArgumentNullException>(() => new ProviderObjectReference(null!, "type", "id", null, Instant));
        Assert.Throws<ArgumentNullException>(() => new ProviderCorrelationEvidence(null!, []));
        Assert.Throws<ArgumentNullException>(() => new ProviderCorrelationEvidence(Context(), null!));
        Assert.Throws<ArgumentNullException>(() => new ProviderCorrelationEvidence(Context(), [null!]));
    }
}
