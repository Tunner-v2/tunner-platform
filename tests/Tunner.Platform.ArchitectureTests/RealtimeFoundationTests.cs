using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Realtime;
using Xunit;

namespace Tunner.Platform.ArchitectureTests;

public sealed class RealtimeFoundationTests
{
    [Fact]
    public void RealtimeFoundationMapsOnlyAnAuthenticatedBrowserProtectedEndpoint()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddTunnerRealtimeFoundation();
        using var application = builder.Build();

        application.MapTunnerRealtimeFoundation(new TunnerRealtimeEndpointOptions(
            "/realtime",
            "tunner.browser.same-origin",
            "tunner.realtime.connection"));

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/realtime");

        Assert.Contains(endpoint.Metadata.OfType<IAuthorizeData>(), metadata => metadata.Policy == TunnerRealtimePolicies.AuthenticatedConnect);
        Assert.Contains(endpoint.Metadata.OfType<IEnableCorsAttribute>(), metadata => metadata.PolicyName == "tunner.browser.same-origin");
        Assert.Contains(endpoint.Metadata, metadata => metadata.GetType().Name == "EnableRateLimitingAttribute" && metadata.GetType().GetProperty("PolicyName")?.GetValue(metadata) as string == "tunner.realtime.connection");
    }

    [Fact]
    public void RealtimeFoundationHasNoClientSuppliedSubscriptionOrDomainPayload()
    {
        var clientCallableMethods = typeof(TunnerRealtimeHub)
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ToArray();

        Assert.Equal([nameof(TunnerRealtimeHub.OnConnectedAsync)], clientCallableMethods);
        Assert.Empty(typeof(RealtimeResynchronizationHint).GetProperties(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public async Task RealtimeFoundationDeniesScopeUntilAnAuthoritativeResolverIsProvided()
    {
        var resolver = new DenyAllRealtimeAudienceResolver();
        var result = await resolver.ResolveAsync(
            new RealtimeConnectionAuthorizationContext(
                new System.Security.Claims.ClaimsPrincipal(),
                new AuthorizationServiceStub(),
                "connection-001"),
            CancellationToken.None);

        Assert.False(result.IsAuthorized);
        Assert.Empty(result.Audiences);
    }

    [Theory]
    [InlineData("tunner:scope-001")]
    [InlineData("tunner_scope_001")]
    public void RealtimeAudienceAcceptsOnlyOpaqueTechnicalKeys(string groupName)
    {
        var audience = new RealtimeAudience(groupName);

        Assert.Equal(groupName, audience.GroupName);
    }

    [Theory]
    [InlineData("product scope")]
    [InlineData("customer@example.com")]
    public void RealtimeAudienceRejectsUnsafeKeys(string groupName)
    {
        Assert.Throws<ArgumentException>(() => new RealtimeAudience(groupName));
    }

    private sealed class AuthorizationServiceStub : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(System.Security.Claims.ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Failed());
    }
}