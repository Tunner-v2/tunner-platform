using System.Text.Json;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class FederationFreshAuthenticationTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static FederationFreshAuthenticationProfile Profile() => new("synthetic-freshness", "synthetic-capability",
        "synthetic-assurance-map", TimeSpan.FromMinutes(2), "/account/security",
        [new(["pwd", "mfa"], "synthetic-qualified-assurance")]);

    [Fact]
    public void NewTokenReceiptCannotRefreshTheActualUpstreamAuthenticationTime()
    {
        var profile = Profile(); var authenticated = At.AddMinutes(-1);
        var first = profile.Bind(authenticated, ["mfa", "pwd"], At);
        var later = profile.Bind(authenticated, ["pwd", "mfa"], At.AddSeconds(30));
        Assert.NotNull(first); Assert.NotNull(later);
        Assert.Equal(authenticated, first.AuthenticatedAt); Assert.Equal(first.AuthenticatedAt, later.AuthenticatedAt);
        Assert.Equal(authenticated.AddMinutes(2), first.ExpiresAt); Assert.Equal(first.ExpiresAt, later.ExpiresAt);
        Assert.Equal("synthetic-qualified-assurance", first.Assurance);
        Assert.Null(profile.Bind(authenticated, ["pwd", "mfa"], first.ExpiresAt));
        Assert.Equal("{}", JsonSerializer.Serialize(first));
        Assert.Equal(nameof(FederationVerifiedAuthentication), first.ToString());
    }

    [Theory]
    [InlineData(-121, false)]
    [InlineData(-120, false)]
    [InlineData(-119, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    public void NativeEvidenceMustBePastOrPresentAndWithinTheExplicitAgeBound(int seconds, bool eligible)
    { Assert.Equal(eligible, Profile().Bind(At.AddSeconds(seconds), ["pwd", "mfa"], At) is not null); }

    [Fact]
    public void MissingExtraDuplicateAndUnqualifiedMethodsCannotBorrowAnotherMapping()
    {
        var profile = Profile();
        Assert.Null(profile.Bind(At, [], At)); Assert.Null(profile.Bind(At, ["pwd"], At));
        Assert.Null(profile.Bind(At, ["pwd", "mfa", "unqualified"], At));
        Assert.Null(profile.Bind(At, ["pwd", "mfa", "mfa"], At));
        Assert.Null(profile.Bind(At, ["PWD", "mfa"], At));
        Assert.Null(profile.Bind(At, ["pwd", "mfa\0"], At));
    }

    [Fact]
    public void QualifiedMethodAndProfileCollectionsDoNotFollowCallerMutation()
    {
        var methods = new[] { "pwd", "mfa" };
        var mapping = new FederationQualifiedAuthenticationMethods(methods, "synthetic-qualified-assurance");
        var mappings = new List<FederationQualifiedAuthenticationMethods> { mapping };
        var profile = new FederationFreshAuthenticationProfile("synthetic-freshness", "synthetic-capability",
            "synthetic-assurance-map", TimeSpan.FromMinutes(2), "/account/security", mappings);
        methods[0] = "unqualified"; mappings.Clear();
        Assert.NotNull(profile.Bind(At, ["pwd", "mfa"], At));
        Assert.Null(profile.Bind(At, ["unqualified", "mfa"], At));
        Assert.Throws<ArgumentException>(() => new FederationFreshAuthenticationProfile("synthetic-freshness", "synthetic-capability",
            "synthetic-assurance-map", TimeSpan.FromMinutes(2), "/account/security", [mapping, mapping]));
    }

    [Fact]
    public void UnrepresentableExpiryFailsClosedWithoutTimestampOverflow()
    {
        var profile = new FederationFreshAuthenticationProfile("synthetic-freshness", "synthetic-capability",
            "synthetic-assurance-map", TimeSpan.MaxValue, "/account/security", [new(["pwd"], "synthetic-assurance")]);
        Assert.Null(profile.Bind(At, ["pwd"], At));
        Assert.Null(Profile().Bind(DateTimeOffset.MaxValue, ["pwd", "mfa"], DateTimeOffset.MaxValue));
    }
}
