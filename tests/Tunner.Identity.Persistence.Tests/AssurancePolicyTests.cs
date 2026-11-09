using Tunner.Platform.Operations;
using Tunner.Platform.Policies;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class AssurancePolicyTests
{
    // Synthetic policy values exercise mechanics; they are not approved production settings.
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly AssurancePolicyScope Scope = new("fixture.change-contact", "fixture.user-bff");
    private static AssurancePolicyVersion Version(string id = "fixture-v1", string status = "ACTIVE",
        AssurancePolicyScope? scope = null, DateTimeOffset? from = null, DateTimeOffset? until = null) => new(
        new(id, status, scope ?? Scope, new EffectivePolicyInterval(from ?? Now.AddHours(-1), true, until ?? Now.AddHours(1), false),
            null, ["synthetic-test-approval-only"]), "fixture-strong", TimeSpan.FromSeconds(30),
        new Dictionary<string, string> { ["pwd"] = "fixture-password", ["passkey"] = "fixture-strong" });

    [Fact]
    public void ResolvesExactActiveOperationContextAndExplicitMethodMapping()
    {
        var expected = Version(); var source = new AssurancePolicyCatalog([expected]);
        var result = source.RequireEffective(Scope, Now);
        Assert.Same(expected, result);
        Assert.Equal("fixture-strong", result.RequiredAssurance);
        Assert.Equal("fixture-password", result.RequireMethodAssurance("pwd"));
        Assert.Equal(TimeSpan.FromSeconds(30), result.ProofLifetime);
    }

    [Theory]
    [InlineData("other-operation", "fixture.user-bff")]
    [InlineData("fixture.change-contact", "other-context")]
    [InlineData("FIXTURE.CHANGE-CONTACT", "fixture.user-bff")]
    public void CannotBorrowAnotherOperationOrContextPolicy(string operation, string context)
    {
        var source = new AssurancePolicyCatalog([Version()]);
        Assert.Throws<InvalidOperationException>(() => source.RequireEffective(new(operation, context), Now));
    }

    [Fact]
    public void MissingDraftAndAmbiguousPoliciesFailClosed()
    {
        Assert.Throws<InvalidOperationException>(() => new AssurancePolicyCatalog([]).RequireEffective(Scope, Now));
        Assert.Throws<InvalidOperationException>(() => new AssurancePolicyCatalog([Version(status: "DRAFT")]).RequireEffective(Scope, Now));
        Assert.Throws<InvalidOperationException>(() => new AssurancePolicyCatalog([Version("one"), Version("two")]).RequireEffective(Scope, Now));
    }

    [Fact]
    public void UsesExecutionTimeIncludingEffectiveBoundaryWithoutFallback()
    {
        var boundary = Now.AddMinutes(5);
        var source = new AssurancePolicyCatalog([Version("old", until: boundary), Version("new", from: boundary)]);
        Assert.Equal("old", source.RequireEffective(Scope, Now).Metadata.VersionId);
        Assert.Equal("new", source.RequireEffective(Scope, boundary).Metadata.VersionId);
        Assert.Throws<InvalidOperationException>(() => source.RequireEffective(Scope, Now.AddHours(1)));
    }

    [Fact]
    public void ExpectedVersionCannotSelectSupersededPolicy()
    {
        var source = new AssurancePolicyCatalog([Version("old", "SUPERSEDED"), Version("new")]);
        var error = Assert.Throws<AssurancePolicyVersionConflictException>(() => source.RequireEffective(Scope, Now, "old"));
        Assert.Equal(TechnicalProblemCodes.VersionConflict, error.ProblemCode);
        Assert.Equal("new", source.RequireEffective(Scope, Now, "new").Metadata.VersionId);
        Assert.Throws<ArgumentException>(() => source.RequireEffective(Scope, Now, " "));
    }

    [Fact]
    public void SnapshotsMappingsAndCatalogInputsSoCallersCannotMutateAnApprovedVersion()
    {
        var mappings = new Dictionary<string, string> { ["pwd"] = "fixture-password" };
        var version = new AssurancePolicyVersion(Version().Metadata, "fixture-strong", TimeSpan.FromSeconds(30), mappings);
        var inputs = new List<AssurancePolicyVersion> { version };
        var source = new AssurancePolicyCatalog(inputs);
        mappings["pwd"] = "forged-upgrade"; inputs.Clear();
        var result = source.RequireEffective(Scope, Now);
        Assert.Equal("fixture-password", result.RequireMethodAssurance("pwd"));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)result.MethodAssurances)["pwd"] = "forged");
    }

    [Fact]
    public void UnconfiguredAndDifferentlyCasedMethodsHaveNoImplicitAssurance()
    {
        var version = Version();
        Assert.Throws<InvalidOperationException>(() => version.RequireMethodAssurance("unknown"));
        Assert.Throws<InvalidOperationException>(() => version.RequireMethodAssurance("PWD"));
    }

    [Fact]
    public void RequiresExplicitCompletePolicyValues()
    {
        var metadata = Version().Metadata;
        Assert.Throws<ArgumentException>(() => new AssurancePolicyScope("", "context"));
        Assert.Throws<ArgumentException>(() => new AssurancePolicyScope("operation", " "));
        Assert.Throws<ArgumentException>(() => new AssurancePolicyVersion(metadata, " ", TimeSpan.FromSeconds(1), new Dictionary<string, string> { ["pwd"] = "level" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AssurancePolicyVersion(metadata, "level", TimeSpan.Zero, new Dictionary<string, string> { ["pwd"] = "level" }));
        Assert.Throws<ArgumentException>(() => new AssurancePolicyVersion(metadata, "level", TimeSpan.FromSeconds(1), new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => new AssurancePolicyVersion(metadata, "level", TimeSpan.FromSeconds(1), new Dictionary<string, string> { ["pwd"] = " " }));
    }

    [Fact]
    public void DuplicateVersionIdentitiesAreRejectedByTheSharedResolver()
    {
        var source = new AssurancePolicyCatalog([Version("same"), Version("same")]);
        Assert.Throws<ArgumentException>(() => source.RequireEffective(Scope, Now));
    }

    [Fact]
    public void DiagnosticStringsDoNotDumpPolicyContextOrMappings()
    {
        Assert.Equal(nameof(AssurancePolicyScope), Scope.ToString());
        Assert.Equal(nameof(AssurancePolicyVersion), Version().ToString());
    }
}
