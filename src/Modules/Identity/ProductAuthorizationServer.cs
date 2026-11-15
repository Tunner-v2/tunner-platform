using System.Collections.Immutable;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Tunner.Platform.Bff;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit native transport/key configuration. This does not select Product or token policy.</summary>
public sealed class ProductAuthorizationServerProfile
{
    public ProductAuthorizationServerProfile(string origin, string authorizationPath, string tokenPath,
        SecurityKey signingKey, SecurityKey encryptionKey, ImmutableArray<string> supportedScopes,
        int maximumBodyBytes, int maximumFields, int maximumKeyLength, int maximumValueLength)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            origin != uri.GetLeftPart(UriPartial.Authority) || uri.UserInfo.Length != 0)
            throw new ArgumentException("An exact HTTPS User origin is required.", nameof(origin));
        ArgumentNullException.ThrowIfNull(signingKey); ArgumentNullException.ThrowIfNull(encryptionKey);
        if (!ProductAuthorizationRequest.Canonical(supportedScopes) || supportedScopes.Length == 0 ||
            supportedScopes.Contains(OpenIddictConstants.Scopes.OfflineAccess, StringComparer.Ordinal))
            throw new ArgumentException("Explicit scopes for the admitted code-only profile are required.", nameof(supportedScopes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBodyBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFields);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumKeyLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumValueLength);
        // Reuse the existing exact, bounded protocol-path validator without activating a callback here.
        _ = new BffProtocolCallback(authorizationPath, maximumBodyBytes, maximumFields, maximumKeyLength, maximumValueLength, _ => Task.CompletedTask);
        _ = new BffProtocolCallback(tokenPath, maximumBodyBytes, maximumFields, maximumKeyLength, maximumValueLength, _ => Task.CompletedTask);
        if (string.Equals(authorizationPath, tokenPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Distinct native authorization and token paths are required.");
        Origin = origin; AuthorizationPath = authorizationPath; TokenPath = tokenPath;
        SigningKey = signingKey; EncryptionKey = encryptionKey; SupportedScopes = supportedScopes;
        MaximumBodyBytes = maximumBodyBytes; MaximumFields = maximumFields;
        MaximumKeyLength = maximumKeyLength; MaximumValueLength = maximumValueLength;
    }
    public string Origin { get; }
    public string AuthorizationPath { get; }
    public string TokenPath { get; }
    internal SecurityKey SigningKey { get; }
    internal SecurityKey EncryptionKey { get; }
    public ImmutableArray<string> SupportedScopes { get; }
    internal int MaximumBodyBytes { get; }
    internal int MaximumFields { get; }
    internal int MaximumKeyLength { get; }
    internal int MaximumValueLength { get; }
    public override string ToString() => nameof(ProductAuthorizationServerProfile);
}

public static class ProductAuthorizationServer
{
    public static IServiceCollection AddTunnerProductAuthorization(this IServiceCollection services, ProductAuthorizationServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(profile);
        Type[] required = [typeof(IdentityPersistenceDbContext), typeof(IProductAuthorizationPolicySource),
            typeof(IProductAuthorizationRegistrySource), typeof(IProductAuthorizationRelationshipSource), typeof(AuthenticationPartitionKey)];
        if (required.Any(type => !services.Any(item => item.ServiceType == type)))
            throw new InvalidOperationException("Product authorization requires explicit persistence and qualified owner providers.");
        if (services.Any(item => item.ServiceType == typeof(ProductAuthorizationServerProfile)))
            throw new InvalidOperationException("Product authorization may be composed only once.");
        services.AddSingleton(profile); services.AddHttpContextAccessor();
        services.AddScoped<ProductAuthorizationApplication>();
        services.AddSingleton(new BffProtocolCallback(profile.TokenPath, profile.MaximumBodyBytes, profile.MaximumFields,
            profile.MaximumKeyLength, profile.MaximumValueLength, async context =>
            {
                var handler = await context.RequestServices.GetRequiredService<IAuthenticationHandlerProvider>()
                    .GetHandlerAsync(context, OpenIddict.Server.AspNetCore.OpenIddictServerAspNetCoreDefaults.AuthenticationScheme).ConfigureAwait(false);
                if (handler is not IAuthenticationRequestHandler requestHandler || !await requestHandler.HandleRequestAsync().ConfigureAwait(false))
                    throw new InvalidOperationException("The exact native token callback must be handled terminally.");
            }));
        services.AddOpenIddict().AddCore(options => options.DisableEntityCaching()).AddServer(options =>
        {
            options.SetIssuer(new Uri(profile.Origin));
            options.SetAuthorizationEndpointUris(profile.AuthorizationPath).SetTokenEndpointUris(profile.TokenPath);
            options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
            options.RegisterScopes(profile.SupportedScopes.ToArray());
            options.AddSigningKey(profile.SigningKey).AddEncryptionKey(profile.EncryptionKey);
            options.Configure(value =>
            {
                value.CodeChallengeMethods.Clear(); value.CodeChallengeMethods.Add(OpenIddictConstants.CodeChallengeMethods.Sha256);
                value.ResponseModes.Clear(); value.ResponseModes.Add(OpenIddictConstants.ResponseModes.FormPost);
            });
            options.UseAspNetCore(); // No passthrough and no transport/permission/storage bypass.
            options.AddEventHandler<OpenIddictServerEvents.ProcessRequestContext>(builder =>
                builder.UseScopedHandler<ProductAuthorizationProtocol>().SetOrder(int.MinValue + 100_000));
            options.AddEventHandler<OpenIddictServerEvents.HandleAuthorizationRequestContext>(builder =>
                builder.UseScopedHandler<ProductAuthorizationProtocol>().SetOrder(OpenIddictServerHandlers.Authentication.AttachPrincipal.Descriptor.Order + 1_000));
            options.AddEventHandler<OpenIddictServerEvents.HandleTokenRequestContext>(builder =>
                builder.UseScopedHandler<ProductAuthorizationProtocol>().SetOrder(OpenIddictServerHandlers.Exchange.AttachPrincipal.Descriptor.Order + 1_000));
            options.AddEventHandler<OpenIddictServerEvents.ApplyAuthorizationResponseContext>(builder =>
                builder.UseScopedHandler<ProductAuthorizationProtocol>().SetOrder(int.MinValue + 100_000));
            options.AddEventHandler<OpenIddictServerEvents.ApplyTokenResponseContext>(builder =>
                builder.UseScopedHandler<ProductAuthorizationProtocol>().SetOrder(int.MinValue + 100_000));
        });
        return services;
    }
}
