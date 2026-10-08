using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tunner.Platform.Configuration;
using Xunit;

namespace Tunner.Platform.ArchitectureTests;

public sealed class EngineeringEnvironmentConfigurationTests
{
    private const string Prefix = EngineeringEnvironmentConfiguration.SectionName + ":";

    private static IConfigurationRoot Configuration(params (string Key, string? Value)[] changes)
    {
        var values = new Dictionary<string, string?>
        {
            [Prefix + "Class"] = "LOCAL_DEVELOPMENT",
            [Prefix + "Reference"] = "test-environment",
            [Prefix + "Endpoints:api"] = "http://localhost:7100",
            [Prefix + "SecretReferences:database:StoreReference"] = "openbao-local",
            [Prefix + "SecretReferences:database:ResourceReference"] = "tunner/local/database"
        };
        foreach (var (key, value) in changes) values[Prefix + key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Theory]
    [InlineData("LOCAL_DEVELOPMENT", EngineeringEnvironmentClass.LocalDevelopment)]
    [InlineData("EPHEMERAL_TEST", EngineeringEnvironmentClass.EphemeralTest)]
    public void ExplicitEngineeringClassAndSeparateReferencesAreLoaded(string value, EngineeringEnvironmentClass expected)
    {
        var profile = EngineeringEnvironmentConfiguration.Load(Configuration(("Class", value)));
        Assert.Equal(expected, profile.Class);
        Assert.Equal("test-environment", profile.Reference);
        Assert.Equal("http://localhost:7100/", profile.Endpoints["api"].AbsoluteUri);
        Assert.Equal("openbao-local", profile.SecretReferences["database"].StoreReference);
        Assert.Equal("tunner/local/database", profile.SecretReferences["database"].ResourceReference);
    }

    [Theory]
    [InlineData("SANDBOX")]
    [InlineData("STAGING")]
    [InlineData("PRODUCTION")]
    [InlineData("Development")]
    [InlineData("local_development")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData(null)]
    public void ProductOrImplicitFrameworkEnvironmentCannotBecomeEngineeringIdentity(string? value)
    {
        Assert.Throws<InvalidOperationException>(() => EngineeringEnvironmentConfiguration.Load(Configuration(("Class", value))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("identity\n")]
    public void MissingOrControlBearingReferenceIsRejected(string? value)
    {
        Assert.Throws<InvalidOperationException>(() => EngineeringEnvironmentConfiguration.Load(Configuration(("Reference", value))));
    }

    [Theory]
    [InlineData("http://user:redacted@localhost:7100")]
    [InlineData("http://@localhost:7100")]
    [InlineData("http://localhost:7100?token=redacted")]
    [InlineData("http://localhost:7100#redacted")]
    [InlineData("http://localhost:0")]
    [InlineData("http://localhost:70000")]
    [InlineData("http://localhost:7100/bad%0Apath")]
    [InlineData("http://localhost:7100/bad path")]
    [InlineData("http://localhost:7100/\n")]
    [InlineData("file://localhost/config")]
    [InlineData("ftp://localhost/config")]
    [InlineData("relative-endpoint")]
    [InlineData("Host=localhost;Password=redacted")]
    [InlineData("")]
    [InlineData(null)]
    public void SecretBearingOrMalformedEndpointsFailWithRedactedDiagnostics(string? value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => EngineeringEnvironmentConfiguration.Load(Configuration(("Endpoints:api", value))));
        Assert.DoesNotContain("redacted", error.ToString(), StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(value)) Assert.DoesNotContain(value, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://localhost:7443")]
    [InlineData("amqp://localhost:5672")]
    [InlineData("amqps://localhost:5671")]
    [InlineData("redis://localhost:6379")]
    [InlineData("rediss://localhost:6380")]
    [InlineData("postgresql://localhost:5432")]
    [InlineData("http://[::1]:7100")]
    public void SelectedTransportEndpointsRemainNonSecretConfiguration(string address)
    {
        Assert.Equal(address, EngineeringEnvironmentConfiguration.Load(Configuration(("Endpoints:api", address))).Endpoints["api"].OriginalString);
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("BusinessPolicy")]
    [InlineData("RuntimeState")]
    [InlineData("Endpoints")]
    [InlineData("Endpoints:api:Password")]
    [InlineData("Reference:Nested")]
    [InlineData("Class:Nested")]
    [InlineData("SecretReferences")]
    [InlineData("SecretReferences:database")]
    [InlineData("SecretReferences:database:Value")]
    [InlineData("SecretReferences:database:Token")]
    [InlineData("SecretReferences:database:StoreReference:Nested")]
    [InlineData("SecretReferences:database:ResourceReference:Nested")]
    public void UnknownOrAmbiguousShapesAreRejectedWithoutEchoingValues(string key)
    {
        var error = Assert.Throws<InvalidOperationException>(() => EngineeringEnvironmentConfiguration.Load(Configuration((key, "must-not-echo"))));
        Assert.DoesNotContain("must-not-echo", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(key, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("StoreReference", null)]
    [InlineData("ResourceReference", null)]
    [InlineData("StoreReference", " ")]
    [InlineData("ResourceReference", "ref\n")]
    public void SecretReferenceComponentsAreRequired(string key, string? value)
    {
        Assert.Throws<InvalidOperationException>(() => EngineeringEnvironmentConfiguration.Load(Configuration(("SecretReferences:database:" + key, value))));
    }

    [Fact]
    public void MissingWholeSectionAndScalarRootFailClosed()
    {
        Assert.Throws<InvalidOperationException>(() => EngineeringEnvironmentConfiguration.Load(new ConfigurationBuilder().Build()));
        var root = Configuration();
        root[EngineeringEnvironmentConfiguration.SectionName] = "must-not-echo";
        var error = Assert.Throws<InvalidOperationException>(() => EngineeringEnvironmentConfiguration.Load(root));
        Assert.DoesNotContain("must-not-echo", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MinimalEngineeringIdentityDoesNotInventEndpointsOrResolveSecrets()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Prefix + "Class"] = "EPHEMERAL_TEST", [Prefix + "Reference"] = "test-only"
        }).Build();
        var profile = EngineeringEnvironmentConfiguration.Load(config);
        Assert.Empty(profile.Endpoints);
        Assert.Empty(profile.SecretReferences);
    }

    [Fact]
    public void SnapshotsStayImmutableWhenTheUnderlyingConfigurationChanges()
    {
        var config = Configuration();
        var profile = EngineeringEnvironmentConfiguration.Load(config);
        config[Prefix + "Reference"] = "changed-identity";
        config[Prefix + "Endpoints:api"] = "http://localhost:7200";
        config[Prefix + "SecretReferences:database:ResourceReference"] = "changed-ref";
        Assert.Equal("test-environment", profile.Reference);
        Assert.Equal(7100, profile.Endpoints["api"].Port);
        Assert.Equal("tunner/local/database", profile.SecretReferences["database"].ResourceReference);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, Uri>)profile.Endpoints).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, EngineeringSecretReference>)profile.SecretReferences).Clear());
    }

    [Fact]
    public void FrameworkKeysAreCaseInsensitiveAndOwnerReferencesArePreserved()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["tunner:engineeringenvironment:class"] = "LOCAL_DEVELOPMENT",
            ["tunner:engineeringenvironment:reference"] = " Exact-Owner-Reference ",
            ["tunner:engineeringenvironment:endpoints:API"] = "http://localhost:7100"
        }).Build();
        var profile = EngineeringEnvironmentConfiguration.Load(config);
        Assert.Equal(" Exact-Owner-Reference ", profile.Reference);
        Assert.Equal(7100, profile.Endpoints["api"].Port);
    }

    [Fact]
    public void ExistingModuleConventionComposesAValidatedSingletonInTheGenericHost()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddConfiguration(Configuration());
        builder.Services.AddTunnerModules(new EngineeringEnvironmentModule(builder.Configuration));
        using var host = builder.Build();
        var first = host.Services.GetRequiredService<EngineeringEnvironmentConfiguration>();
        Assert.Same(first, host.Services.GetRequiredService<EngineeringEnvironmentConfiguration>());
        Assert.Equal("test-environment", first.Reference);
    }

    [Fact]
    public void DuplicateRegistrationCannotReplaceTheEnvironmentSnapshot()
    {
        var services = new ServiceCollection();
        services.AddTunnerModules(new EngineeringEnvironmentModule(Configuration()));
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerModules(new EngineeringEnvironmentModule(Configuration(("Reference", "different")))));
        Assert.Single(services);
        using var provider = services.BuildServiceProvider();
        Assert.Equal("test-environment", provider.GetRequiredService<EngineeringEnvironmentConfiguration>().Reference);
    }

    [Fact]
    public void InvalidConfigurationDoesNotPartiallyRegisterAService()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerModules(new EngineeringEnvironmentModule(Configuration(("Password", "must-not-echo")))));
        Assert.Empty(services);
    }

    [Fact]
    public void JsonAndOrderedCommandLineProvidersOverrideEndpointsWithoutCodeChanges()
    {
        using var file = File.OpenRead(Path.Combine(RepositoryRoot(), "infra", "environments", "engineering.example.json"));
        var configuration = new ConfigurationBuilder().AddJsonStream(file)
            .AddCommandLine(["--Tunner:EngineeringEnvironment:Endpoints:api=http://localhost:7201"]).Build();
        var profile = EngineeringEnvironmentConfiguration.Load(configuration);
        Assert.Equal(EngineeringEnvironmentClass.LocalDevelopment, profile.Class);
        Assert.Equal(7201, profile.Endpoints["api"].Port);
        Assert.Equal(5432, profile.Endpoints["postgres"].Port);
        Assert.Equal("tunner/local/postgres", profile.SecretReferences["postgresCredential"].ResourceReference);
    }

    [Fact]
    public void EnvironmentProviderUsesTheFrameworkPrefixAndDoubleUnderscoreMapping()
    {
        var providerPrefix = "TUNNER_CFG_TEST_" + Guid.NewGuid().ToString("N") + "_";
        var variable = providerPrefix + "Tunner__EngineeringEnvironment__Endpoints__api";
        try
        {
            Environment.SetEnvironmentVariable(variable, "http://localhost:7202");
            var configuration = new ConfigurationBuilder().AddConfiguration(Configuration()).AddEnvironmentVariables(providerPrefix).Build();
            Assert.Equal(7202, EngineeringEnvironmentConfiguration.Load(configuration).Endpoints["api"].Port);
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
    }

    [Fact]
    public void JsonExampleCanBeLoadedFromAnyRuntimeFileLocation()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot(), "infra", "environments", "engineering.example.json"));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var profile = EngineeringEnvironmentConfiguration.Load(new ConfigurationBuilder().AddJsonStream(stream).Build());
        Assert.Equal(7100, profile.Endpoints["api"].Port);
        Assert.Equal(6, profile.Endpoints.Count);
    }

    [Fact]
    public void NullInputsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => EngineeringEnvironmentConfiguration.Load(null!));
        Assert.Throws<ArgumentNullException>(() => new EngineeringEnvironmentModule(null!));
        Assert.Throws<ArgumentNullException>(() => new EngineeringEnvironmentModule(Configuration()).Register(null!));
    }

    private static string RepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "Tunner.Platform.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
