using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class FederationAttemptBindingTests
{
    // Explicit synthetic owner/profile/clock values only; no configured provider, persisted attempt or session.
    private static readonly Guid Attempt = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Session = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Link = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Created = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expires = Created.AddMinutes(3);
    private const string Origin = "https://user.example.test";
    private static string Digest(char value = 'A') => new(value, 64);

    private static FederationProviderRegistration Profile(string change = "") => new(
        change == "provider" ? FederationProviderKind.Microsoft : FederationProviderKind.Google,
        change == "namespace" ? "other-namespace" : "synthetic-namespace",
        change == "issuer-case" ? "https://ISSUER.example.test" : "https://issuer.example.test",
        change == "client" ? "other-client" : "synthetic-client", change == "issuers" ? [] : ["https://alias.example.test"],
        change == "version" ? "synthetic-v2" : "synthetic-v1", change == "mapping" ? "other-map" : "synthetic-map", 128, 128);

    private static FederationAttemptOwner Owner(string change = "", bool withLink = true)
    {
        var origin = change == "origin" ? "https://other.example.test" : Origin;
        Claim[] claims = [new(ClaimTypes.NameIdentifier, change == "account" ? "ACC-other" : "ACC-synthetic"),
            new(AuthenticationIdentity.SessionClaim, (change == "session" ? Guid.NewGuid() : Session).ToString()),
            new(AuthenticationIdentity.OriginClaim, origin)];
        var caller = StepUpCaller.FromPrincipal(new(new ClaimsIdentity(claims, AuthenticationIdentity.Scheme)), origin, Digest());
        var operation = new StepUpOperation(change == "operation" ? "other-operation" : "synthetic-operation",
            change == "owner-correlation" ? "other-correlation" : "synthetic-owner-correlation",
            new(change == "scope-operation" ? "other-action" : "synthetic-action", change == "scope-context" ? "other-context" : "synthetic-context"),
            change == "resource" ? "other-resource" : "synthetic-resource", change == "resource-version" ? 2 : 1,
            change == "context" ? Digest('B') : Digest());
        return new(caller, operation, change == "account-version" ? 2 : 1, change == "session-version" ? 2 : 1,
            withLink ? change == "source-link" ? Guid.NewGuid() : Link : null, withLink ? change == "source-version" ? 2 : 1 : null);
    }

    private static FederationAttemptBinding Bind(FederationAttemptPurpose purpose = FederationAttemptPurpose.SignIn,
        FederationAttemptOwner? owner = null, FederationProviderRegistration? profile = null, string browser = "synthetic-browser") =>
        new(Attempt, "synthetic-request", "synthetic-correlation", purpose, profile ?? Profile(), browser, "/synthetic/return",
            ["/synthetic/return"], Created, Expires, owner);

    private static bool Matches(FederationAttemptBinding attempt, FederationAttemptPurpose purpose = FederationAttemptPurpose.SignIn,
        FederationAttemptOwner? owner = null, FederationProviderRegistration? profile = null, DateTimeOffset? at = null) =>
        attempt.Matches(Attempt, "synthetic-request", "synthetic-correlation", purpose, profile ?? Profile(), "synthetic-browser",
            "/synthetic/return", at ?? Created.AddSeconds(1), owner);

    [Theory]
    [InlineData("SignIn")]
    [InlineData("Registration")]
    [InlineData("Link")]
    [InlineData("OperationProof")]
    public void ReconstructedExactProfileAndOwnerPreserveTheOriginalAttempt(string purposeName)
    {
        var purpose = Enum.Parse<FederationAttemptPurpose>(purposeName);
        var needsOwner = purpose is FederationAttemptPurpose.Link or FederationAttemptPurpose.OperationProof;
        var attempt = Bind(purpose, needsOwner ? Owner() : null);
        Assert.True(Matches(attempt, purpose, needsOwner ? Owner() : null));
    }

    [Fact]
    public void IssuerSetComparisonIsOrdinalOrderIndependentAndDoesNotInventAliases()
    {
        var original = new FederationProviderRegistration(FederationProviderKind.Google, "synthetic-namespace", "https://issuer.example.test",
            "synthetic-client", ["https://alias.example.test", "https://second.example.test"], "synthetic-v1", "synthetic-map", 128, 128);
        var equivalent = new FederationProviderRegistration(FederationProviderKind.Google, "synthetic-namespace", "https://second.example.test",
            "synthetic-client", ["https://alias.example.test", "https://issuer.example.test", "https://alias.example.test"], "synthetic-v1", "synthetic-map", 128, 128);
        Assert.True(Matches(Bind(profile: original), profile: equivalent));
        Assert.False(Matches(Bind(profile: original))); // An admitted issuer was removed, despite unchanged profile labels.
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("namespace")]
    [InlineData("client")]
    [InlineData("version")]
    [InlineData("mapping")]
    [InlineData("issuers")]
    [InlineData("issuer-case")]
    public void ChangedRegistrationIdentityOrMappingRejectsTheAttempt(string change)
        => Assert.False(Matches(Bind(), profile: Profile(change)));

    [Theory]
    [InlineData("attempt")]
    [InlineData("request")]
    [InlineData("correlation")]
    [InlineData("purpose")]
    [InlineData("browser")]
    [InlineData("continuation")]
    public void CallbackCannotSelectAnotherOriginalContext(string change)
    {
        var attempt = Bind();
        Assert.False(attempt.Matches(change == "attempt" ? Guid.NewGuid() : Attempt,
            change == "request" ? "other-request" : "synthetic-request", change == "correlation" ? "other-correlation" : "synthetic-correlation",
            change == "purpose" ? FederationAttemptPurpose.Registration : FederationAttemptPurpose.SignIn, Profile(),
            change == "browser" ? "other-browser" : "synthetic-browser", change == "continuation" ? "/other" : "/synthetic/return", Created));
    }

    [Theory]
    [InlineData("account")]
    [InlineData("session")]
    [InlineData("origin")]
    [InlineData("account-version")]
    [InlineData("session-version")]
    [InlineData("operation")]
    [InlineData("owner-correlation")]
    [InlineData("scope-operation")]
    [InlineData("scope-context")]
    [InlineData("resource")]
    [InlineData("resource-version")]
    [InlineData("context")]
    [InlineData("source-link")]
    [InlineData("source-version")]
    public void ProofOwnerDriftCannotBorrowTheOriginalAttempt(string change)
    {
        var attempt = Bind(FederationAttemptPurpose.OperationProof, Owner());
        Assert.False(Matches(attempt, FederationAttemptPurpose.OperationProof, Owner(change)));
        Assert.False(Matches(attempt, FederationAttemptPurpose.OperationProof));
    }

    [Fact]
    public void PurposeRequiresItsOwnerAndProofRequiresAnExistingSourceLink()
    {
        Assert.Throws<ArgumentException>(() => Bind(FederationAttemptPurpose.Link));
        Assert.Throws<ArgumentException>(() => Bind(FederationAttemptPurpose.OperationProof));
        Assert.Throws<ArgumentException>(() => Bind(FederationAttemptPurpose.OperationProof, Owner(withLink: false)));
        Assert.Throws<ArgumentException>(() => Bind(owner: Owner()));
        Assert.Throws<ArgumentException>(() => Bind(FederationAttemptPurpose.Registration, Owner()));
        Assert.True(Matches(Bind(FederationAttemptPurpose.Link, Owner(withLink: false)), FederationAttemptPurpose.Link, Owner(withLink: false)));
    }

    [Fact]
    public void DecisionTimeCannotUseFutureExpiredOrExtendedAttempt()
    {
        var attempt = Bind();
        Assert.False(Matches(attempt, at: Created.AddTicks(-1)));
        Assert.True(Matches(attempt, at: Created));
        Assert.True(Matches(attempt, at: Expires.AddTicks(-1)));
        Assert.False(Matches(attempt, at: Expires));
        Assert.False(Matches(attempt, at: Expires.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => new FederationAttemptBinding(Attempt, "request", "correlation", FederationAttemptPurpose.SignIn,
            Profile(), "browser", "/synthetic/return", ["/synthetic/return"], Created, Created));
    }

    [Theory]
    [InlineData("https://external.example.test")]
    [InlineData("//external.example.test")]
    [InlineData("/synthetic?code=value")]
    [InlineData("/synthetic#value")]
    [InlineData("/synthetic\\other")]
    [InlineData("/synthetic/%2fother")]
    [InlineData("/synthetic/../other")]
    [InlineData("/synthetic\r\nother")]
    public void EvenAnAllowlistCannotAdmitAnUncleanOrExternalContinuation(string continuation)
        => Assert.Throws<ArgumentException>(() => new FederationAttemptBinding(Attempt, "request", "correlation", FederationAttemptPurpose.SignIn,
            Profile(), "browser", continuation, [continuation], Created, Expires));

    [Fact]
    public void OnlyTheExplicitAllowedContinuationIsAdmitted()
        => Assert.Throws<ArgumentException>(() => new FederationAttemptBinding(Attempt, "request", "correlation", FederationAttemptPurpose.SignIn,
            Profile(), "browser", "/synthetic/other", ["/synthetic/return"], Created, Expires));

    [Fact]
    public void IncompleteIdentityPurposeAndOwnerVersionShapesFailClosed()
    {
        Assert.Throws<ArgumentException>(() => new FederationAttemptBinding(Guid.Empty, "request", "correlation", FederationAttemptPurpose.SignIn,
            Profile(), "browser", "/synthetic/return", ["/synthetic/return"], Created, Expires));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bind((FederationAttemptPurpose)99));
        Assert.Throws<ArgumentException>(() => Bind(browser: "browser\0other"));
        Assert.False(Bind().Matches(Attempt, "synthetic-request", "synthetic-correlation", FederationAttemptPurpose.SignIn,
            null!, "synthetic-browser", "/synthetic/return", Created));
        var owner = Owner();
        Assert.Throws<ArgumentOutOfRangeException>(() => new FederationAttemptOwner(owner.Caller, owner.Operation, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FederationAttemptOwner(owner.Caller, owner.Operation, 1, -1));
        Assert.Throws<ArgumentException>(() => new FederationAttemptOwner(owner.Caller, owner.Operation, 1, 1, Link));
        Assert.Throws<ArgumentException>(() => new FederationAttemptOwner(owner.Caller, owner.Operation, 1, 1, null, 1));
        Assert.Throws<ArgumentException>(() => new FederationAttemptOwner(owner.Caller, owner.Operation, 1, 1, Guid.Empty, 1));
        Assert.Throws<ArgumentException>(() => new FederationAttemptOwner(owner.Caller, owner.Operation, 1, 1, Link, -1));
    }

    [Fact]
    public void OpaqueOriginalValuesRemainExactWithoutTrimmingOrUnicodeNormalization()
    {
        var attempt = new FederationAttemptBinding(Attempt, " request ", "correlation", FederationAttemptPurpose.SignIn, Profile(), "é",
            "/synthetic/return", ["/synthetic/return"], Created, Expires);
        Assert.True(attempt.Matches(Attempt, " request ", "correlation", FederationAttemptPurpose.SignIn, Profile(), "é", "/synthetic/return", Created));
        Assert.False(attempt.Matches(Attempt, "request", "correlation", FederationAttemptPurpose.SignIn, Profile(), "é", "/synthetic/return", Created));
        Assert.False(attempt.Matches(Attempt, " request ", "correlation", FederationAttemptPurpose.SignIn, Profile(), "e\u0301", "/synthetic/return", Created));
    }

    [Fact]
    public void ExactRepeatedMatchIsNotAClaimOfTransactionalReplayConsumption()
    {
        var attempt = Bind();
        Assert.True(Matches(attempt)); Assert.True(Matches(attempt));
        Assert.False(attempt.Matches(Guid.NewGuid(), "synthetic-request", "synthetic-correlation", FederationAttemptPurpose.SignIn,
            Profile(), "synthetic-browser", "/synthetic/return", Created));
    }

    [Fact]
    public void OrdinarySerializationAndDiagnosticsDoNotDiscloseBindings()
    {
        var owner = Owner(); var attempt = Bind(FederationAttemptPurpose.Link, owner);
        Assert.Equal("{}", JsonSerializer.Serialize(attempt)); Assert.Equal("{}", JsonSerializer.Serialize(owner));
        Assert.Equal(nameof(FederationAttemptBinding), attempt.ToString()); Assert.Equal(nameof(FederationAttemptOwner), owner.ToString());
        Assert.Equal("{}", JsonSerializer.Serialize(Profile()));
    }
}
