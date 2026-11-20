using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Fixture = Tunner.Identity.Persistence.Tests.PublishedRecoveryPasswordPresentationTests.Fixture;

namespace Tunner.Identity.Persistence.Tests;

// Public composition/guard tests only. No Recovery code, credential, wire repair or private native fixture.
public sealed class RecoveryPasswordBrowserCompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepairScreenHasSeparateGetAndPostAndRejectsUnavailablePolicyOrPublication(bool policyAvailable)
    {
        var f = new Fixture(); var inputs = new UnavailableBrowserInputs(f, policyAvailable);
        // These ports cannot be reached: current policy/publication must reject before private browser or owner access.
        var browser = new RecoveryBrowserService(null!, inputs, inputs, null!, null!, null!, AuthenticationTestHost.Origin, f.Clock);
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IdentityWebAssets>(); builder.Services.AddSingleton(browser);
        await using var app = builder.Build();
        var directory = Path.Combine(AppContext.BaseDirectory, "wwwroot", "registration"); Directory.CreateDirectory(directory);
        var entry = Path.Combine(directory, "recovery.html"); var created = !File.Exists(entry);
        if (created) await File.WriteAllTextAsync(entry, "<!doctype html><title>Public Recovery route guard fixture</title>");
        try
        {
            app.MapTunnerRecoveryPassword();
            var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.RoutePattern.RawText == "/recovery/repair").ToArray();
            Assert.Equal(2, routes.Length);
            var get = Assert.Single(routes, endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(HttpMethods.Get, StringComparer.Ordinal));
            var post = Assert.Single(routes, endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(HttpMethods.Post, StringComparer.Ordinal));
            Assert.NotSame(get, post);
            Assert.NotNull(post.Metadata.GetMetadata<AuthenticationEndpoints.CredentialAuthenticationMetadata>());
            var context = new DefaultHttpContext { RequestServices = app.Services };
            context.Request.Method = HttpMethods.Get; context.Request.Path = "/recovery/repair";
            context.Response.Body = Stream.Null;
            await get.RequestDelegate!(context);
            Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
            Assert.Equal("/login", context.Response.Headers.Location.ToString());
            Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
            Assert.Equal(1, inputs.PolicyReads); Assert.Equal(policyAvailable ? 1 : 0, inputs.PublicationReads);
        }
        finally { if (created) File.Delete(entry); }
    }

    [Fact]
    public void CodeCapabilityRequiresBothPasswordCompositionAndAnOptionalCodeOwner()
    {
        var f = new Fixture(); var inputs = new CompositionInputs();
        var policies = new RecoveryPasswordPolicyCatalog([f.Binding.Policy]);
        using var partitions = new AuthenticationPartitionKey(new byte[32]);
        // Inert owner references only: no persistence, native crypto, proof, code or browser method is invoked.
        var codes = new RecoveryCodeApplication(null!, null!, null!, null!, null!, null!, null!, null!, f.Clock);
        RecoveryCaseApplication Owner(RecoveryCodeApplication? codeOwner, bool passwordOwner = true) =>
            new(null!, codeOwner, null!, f.Clock, hierarchy: inputs, passwordPolicies: passwordOwner ? policies : null,
                passwordBlocklist: inputs, authenticationPolicies: new AuthenticationTestHost.TestPolicies(), partitions: partitions);
        Assert.False(Owner(null).PasswordCodeAvailable);
        Assert.True(Owner(codes).PasswordCodeAvailable);
        Assert.False(Owner(codes, passwordOwner: false).PasswordCodeAvailable);
    }

    private sealed class UnavailableBrowserInputs(Fixture fixture, bool policyAvailable) : IRecoveryPasswordPolicySource, IRecoveryPasswordPresentationSource
    {
        internal int PolicyReads; internal int PublicationReads;
        public RecoveryPasswordPolicyVersion RequireEffective(DateTimeOffset at)
        {
            PolicyReads++;
            return policyAvailable ? fixture.Binding.Policy : throw new InvalidOperationException("Synthetic unavailable Recovery policy.");
        }
        public RecoveryPasswordPresentation RequirePublished(RecoveryPasswordPolicy policy, DateTimeOffset at)
        { PublicationReads++; throw new InvalidOperationException("Synthetic unavailable Recovery publication."); }
    }
    private sealed class CompositionInputs : IRecoveryCaseHierarchySource, IRegistrationPasswordBlocklist
    {
        public RecoveryCaseHierarchyDecision RequireCurrent(RecoveryCaseHierarchyRequest request, DateTimeOffset at) =>
            throw new InvalidOperationException("No proof evaluation in the public composition test.");
        public ValueTask<bool> IsBlockedAsync(string password, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No password evaluation in the public composition test.");
    }

}
