using System.Collections.ObjectModel;
using Microsoft.Extensions.Configuration;

namespace Tunner.Platform.Configuration;

public enum EngineeringEnvironmentClass
{
    LocalDevelopment,
    EphemeralTest
}

/// <summary>Opaque vault references only. This value neither resolves a secret nor authorizes access.</summary>
public sealed class EngineeringSecretReference
{
    internal EngineeringSecretReference(string storeReference, string resourceReference)
    {
        StoreReference = storeReference;
        ResourceReference = resourceReference;
    }

    public string StoreReference { get; }
    public string ResourceReference { get; }
}

/// <summary>Validated deploy-time snapshot. Product environments, policy and runtime state have separate owners.</summary>
public sealed class EngineeringEnvironmentConfiguration
{
    public const string SectionName = "Tunner:EngineeringEnvironment";
    private const string InvalidShape = "Engineering configuration has an invalid shape.";
    private const string InvalidScalar = "Engineering configuration requires a nonempty scalar without control characters.";
    private const string InvalidEndpoint = "Engineering endpoints must be non-secret supported network URIs.";

    private EngineeringEnvironmentConfiguration(EngineeringEnvironmentClass environmentClass, string reference,
        Dictionary<string, Uri> endpoints, Dictionary<string, EngineeringSecretReference> secretReferences)
    {
        Class = environmentClass;
        Reference = reference;
        Endpoints = new ReadOnlyDictionary<string, Uri>(endpoints);
        SecretReferences = new ReadOnlyDictionary<string, EngineeringSecretReference>(secretReferences);
    }

    public EngineeringEnvironmentClass Class { get; }
    public string Reference { get; }
    public IReadOnlyDictionary<string, Uri> Endpoints { get; }
    public IReadOnlyDictionary<string, EngineeringSecretReference> SecretReferences { get; }

    public static EngineeringEnvironmentConfiguration Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(SectionName);
        RequireObject(section, "Class", "Reference", "Endpoints", "SecretReferences");
        var environmentClass = ReadScalar(section.GetSection("Class")) switch
        {
            "LOCAL_DEVELOPMENT" => EngineeringEnvironmentClass.LocalDevelopment,
            "EPHEMERAL_TEST" => EngineeringEnvironmentClass.EphemeralTest,
            _ => throw new InvalidOperationException("An explicit canonical engineering environment class is required.")
        };
        var reference = ReadScalar(section.GetSection("Reference"));
        var endpointsSection = section.GetSection("Endpoints");
        RequireContainer(endpointsSection);
        var endpoints = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in endpointsSection.GetChildren())
        {
            RequireName(endpoint.Key);
            var address = ReadScalar(endpoint);
            if (address.Any(char.IsWhiteSpace) || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
                || !IsSupportedScheme(uri.Scheme) || string.IsNullOrEmpty(uri.Host) || uri.Port == 0
                || uri.UserInfo.Length != 0 || uri.GetLeftPart(UriPartial.Authority).Contains('@')
                || uri.Query.Length != 0 || uri.Fragment.Length != 0
                || Uri.UnescapeDataString(address).Any(char.IsControl))
                throw new InvalidOperationException(InvalidEndpoint);
            endpoints.Add(endpoint.Key, uri);
        }

        var secretsSection = section.GetSection("SecretReferences");
        RequireContainer(secretsSection);
        var references = new Dictionary<string, EngineeringSecretReference>(StringComparer.OrdinalIgnoreCase);
        foreach (var secret in secretsSection.GetChildren())
        {
            RequireName(secret.Key);
            RequireObject(secret, "StoreReference", "ResourceReference");
            references.Add(secret.Key, new(ReadScalar(secret.GetSection("StoreReference")), ReadScalar(secret.GetSection("ResourceReference"))));
        }
        return new(environmentClass, reference, endpoints, references);
    }

    private static bool IsSupportedScheme(string scheme) => scheme is "http" or "https" or "amqp" or "amqps" or "redis" or "rediss" or "postgresql";

    private static void RequireContainer(IConfigurationSection section)
    {
        if (section.Value is not null) throw new InvalidOperationException(InvalidShape);
    }

    private static void RequireObject(IConfigurationSection section, params string[] allowedKeys)
    {
        RequireContainer(section);
        if (section.GetChildren().Any(child => !allowedKeys.Contains(child.Key, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException(InvalidShape);
    }

    private static string ReadScalar(IConfigurationSection section)
    {
        var value = section.Value;
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || section.GetChildren().Any())
            throw new InvalidOperationException(InvalidScalar);
        return value;
    }

    private static void RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl)) throw new InvalidOperationException(InvalidShape);
    }
}
