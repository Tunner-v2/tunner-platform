using Tunner.Admin.Bff;
using Tunner.PresentationContent;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

public sealed class AdminAuthenticationContentPreviewTests
{
    [Theory]
    [InlineData("scope")]
    [InlineData("variant")]
    [InlineData("policy")]
    [InlineData("expired")]
    public void UnqualifiedDraftContextCannotReadASeparatePublishedBundle(string mismatch)
    {
        var clock = new TestClock(); var policies = new TestPolicies(); var original = new TestPresentation().RequirePublished(policies.Policy);
        var binding = new AuthenticationContentBinding(policies.Policy.Version, "fixture-configuration", "fixture-approval", "fixture-scope", "fixture-en",
            new(clock.GetUtcNow().AddDays(-1), true, null, false), original.TermsUri, original.PrivacyUri, original.SupportUri);
        binding = mismatch switch
        {
            "scope" => binding with { ContentScope = "fixture-other-scope" },
            "variant" => binding with { VariantReference = "fixture-other-variant" },
            "policy" => binding with { AuthenticationPolicyVersion = "fixture-other-policy" },
            _ => binding with { EffectiveInterval = new(clock.GetUtcNow().AddDays(-2), true, clock.GetUtcNow().AddDays(-1), false) }
        };
        var resolver = new UnusedResolver(); var source = new AdminAuthenticationContentPreviewSource(policies, new Bindings(binding), resolver, clock);
        var draft = new ContentPreview(new(Guid.NewGuid(), 1, "DRAFT", "fixture-scope", "login.heading", "fixture-en", null, "fixture-digest"),
            "Fixture draft", "fixture-sensitive", []);
        Assert.Throws<ContentUnavailableException>(() => source.RequirePublishedContext(draft)); Assert.Equal(0, resolver.Calls);
    }

    private sealed class Bindings(AuthenticationContentBinding binding) : IAuthenticationContentBindingSource
    { public AuthenticationContentBinding RequireQualified(AuthenticationPolicy policy, DateTimeOffset effectiveAt) => binding; }
    private sealed class UnusedResolver : IPresentationContentResolver
    {
        internal int Calls;
        public ContentResolution Resolve(ContentResolutionRequest request) { Calls++; throw new InvalidOperationException("A mismatched context must not be resolved."); }
        public Task<ContentResolution> ResolveAsync(ContentResolutionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Resolve(request));
    }
}
