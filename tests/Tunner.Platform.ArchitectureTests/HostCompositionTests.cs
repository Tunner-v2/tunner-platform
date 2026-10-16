using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Api;
using Tunner.Platform;
using Tunner.Worker;
using Xunit;

namespace Tunner.Platform.ArchitectureTests;

public sealed class HostCompositionTests
{
    [Fact]
    public void ApiHostContainsOnlyTheTechnicalHealthEndpointAtThisFoundationSlice()
    {
        using var application = TunnerApiHost.CreateApplication([]);

        var routePatterns = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToArray();

        Assert.Contains("/health", routePatterns);
        Assert.Single(routePatterns);
    }

    [Fact]
    public void ApiAndWorkerReferenceTheSharedPlatformAndObservabilityFoundations()
    {
        AssertHostReferences(typeof(TunnerApiHost).Assembly);
        AssertHostReferences(typeof(TunnerWorkerHost).Assembly);
    }

    [Fact]
    public void PlatformFoundationDoesNotReferenceHostsOrDatabase()
    {
        var references = typeof(ITunnerModule).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        Assert.DoesNotContain("Tunner.Api", references);
        Assert.DoesNotContain("Tunner.Worker", references);
        Assert.DoesNotContain("Tunner.Database", references);
    }

    [Fact]
    public void ModuleRegistrationInvokesOnlyExplicitModules()
    {
        var services = new ServiceCollection();
        var module = new ProbeModule();

        services.AddTunnerModules(module);

        Assert.True(module.Registered);
        Assert.Single(services);
    }

    private static void AssertHostReferences(System.Reflection.Assembly hostAssembly)
    {
        var references = hostAssembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();

        Assert.Contains("Tunner.Platform", references);
        Assert.Contains("Tunner.Observability", references);
    }

    private sealed class ProbeModule : ITunnerModule
    {
        public bool Registered { get; private set; }

        public void Register(IServiceCollection services)
        {
            Registered = true;
            services.AddSingleton<ProbeModule>(this);
        }
    }
}