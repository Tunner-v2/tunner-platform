using Tunner.Platform.Policies;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class AuthenticationPolicyTests
{
    [Fact]
    public void DraftMissingAndAmbiguousPoliciesCannotSilentlyActivateAuthentication()
    {
        var policy = new AuthenticationTestHost.TestPolicies().Policy; var now = DateTimeOffset.UtcNow;
        AuthenticationPolicyVersion Version(string id, string status, string? supersedes = null) => new(new(id, status, "AUTHENTICATION",
            new EffectivePolicyInterval(now.AddDays(-1), true, now.AddDays(1), false), supersedes, ["fixture-only-approval"]), policy with { Version = id });
        Assert.Throws<InvalidOperationException>(() => new AuthenticationPolicyCatalog([]).RequireEffective(now));
        Assert.Throws<InvalidOperationException>(() => new AuthenticationPolicyCatalog([Version("draft", "DRAFT")]).RequireEffective(now));
        Assert.Throws<InvalidOperationException>(() => new AuthenticationPolicyCatalog([Version("one", "ACTIVE"), Version("two", "ACTIVE")]).RequireEffective(now));
        Assert.Equal("two", new AuthenticationPolicyCatalog([Version("one", "SUPERSEDED"), Version("two", "ACTIVE", "one")]).RequireEffective(now).Version);
        Assert.Throws<InvalidOperationException>(() => new AuthenticationPolicyCatalog([Version("one", "ACTIVE")]).RequireEffective(now.AddDays(2)));
    }
}
