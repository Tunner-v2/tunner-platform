using System.Collections.Immutable;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Tunner.Platform.Bff;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit issuer-wide native refresh behavior; owner decisions must agree with this qualification.</summary>
public sealed record ProductAuthorizationLifecycleServerProfile(string IntrospectionPath, string RevocationPath,
    bool RotateRefreshTokens, bool SlidingRefreshExpiration, TimeSpan RefreshReuseLeeway);

/// <summary>Explicit native transport/key configuration. This does not select Product or token policy.</summary>
public sealed class ProductAuthorizationServerProfile
{
    public ProductAuthorizationServerProfile(string origin, string authorizationPath, string tokenPath,
        SecurityKey signingKey, SecurityKey encryptionKey, ImmutableArray<string> supportedScopes, ImmutableArray<string> supportedResources,
        int maximumBodyBytes, int maximumFields, int maximumKeyLength, int maximumValueLength,
        ProductAuthorizationLifecycleServerProfile? lifecycle = null, bool enableContinuation = false,
        bool requireAuthorizationReview = false)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            origin != uri.GetLeftPart(UriPartial.Authority) || uri.UserInfo.Length != 0)
            throw new ArgumentException("An exact HTTPS User origin is required.", nameof(origin));
        ArgumentNullException.ThrowIfNull(signingKey); ArgumentNullException.ThrowIfNull(encryptionKey);
        if (!ProductAuthorizationRequest.Canonical(supportedScopes) || supportedScopes.Length == 0 ||
            lifecycle is null && supportedScopes.Contains(OpenIddictConstants.Scopes.OfflineAccess, StringComparer.Ordinal))
            throw new ArgumentException("Explicit scopes and qualified lifecycle support for offline access are required.", nameof(supportedScopes));
        if (!ProductAuthorizationRequest.Canonical(supportedResources) || supportedResources.Length == 0 ||
            supportedResources.Any(resource => !Uri.TryCreate(resource, UriKind.Absolute, out var resourceUri) ||
                resourceUri.AbsoluteUri != resource || resourceUri.Fragment.Length != 0 || resourceUri.UserInfo.Length != 0))
            throw new ArgumentException("Explicit canonical resource URIs are required.", nameof(supportedResources));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBodyBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFields);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumKeyLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumValueLength);
        // Reuse the existing exact, bounded protocol-path validator without activating a callback here.
        _ = new BffProtocolCallback(authorizationPath, maximumBodyBytes, maximumFields, maximumKeyLength, maximumValueLength, _ => Task.CompletedTask);
        _ = new BffProtocolCallback(tokenPath, maximumBodyBytes, maximumFields, maximumKeyLength, maximumValueLength, _ => Task.CompletedTask);
        if (string.Equals(authorizationPath, tokenPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Distinct native authorization and token paths are required.");
        if (lifecycle is not null)
        {
            _ = new BffProtocolCallback(lifecycle.IntrospectionPath, maximumBodyBytes, maximumFields, maximumKeyLength, maximumValueLength, _ => Task.CompletedTask);
            _ = new BffProtocolCallback(lifecycle.RevocationPath, maximumBodyBytes, maximumFields, maximumKeyLength, maximumValueLength, _ => Task.CompletedTask);
            string[] paths = [authorizationPath, tokenPath, lifecycle.IntrospectionPath, lifecycle.RevocationPath];
            if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length || lifecycle.RefreshReuseLeeway < TimeSpan.Zero)
                throw new ArgumentException("Distinct lifecycle paths and explicit nonnegative refresh reuse leeway are required.", nameof(lifecycle));
        }
        Origin = origin; AuthorizationPath = authorizationPath; TokenPath = tokenPath;
        SigningKey = signingKey; EncryptionKey = encryptionKey; SupportedScopes = supportedScopes; SupportedResources = supportedResources;
        MaximumBodyBytes = maximumBodyBytes; MaximumFields = maximumFields;
        MaximumKeyLength = maximumKeyLength; MaximumValueLength = maximumValueLength;
        Lifecycle = lifecycle;
        EnableContinuation = enableContinuation;
        if (requireAuthorizationReview && !enableContinuation)
            throw new ArgumentException("Explicit Product review requires protected native continuation.", nameof(requireAuthorizationReview));
        RequireAuthorizationReview = requireAuthorizationReview;
    }
    public string Origin { get; }
    public string AuthorizationPath { get; }
    public string TokenPath { get; }
    internal SecurityKey SigningKey { get; }
    internal SecurityKey EncryptionKey { get; }
    public ImmutableArray<string> SupportedScopes { get; }
    public ImmutableArray<string> SupportedResources { get; }
    public ProductAuthorizationLifecycleServerProfile? Lifecycle { get; }
    public bool EnableContinuation { get; }
    public bool RequireAuthorizationReview { get; }
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
        if (profile.Lifecycle is not null && !services.Any(item => item.ServiceType == typeof(IProductAuthorizationLifecycleSource)))
            throw new InvalidOperationException("Product lifecycle endpoints require an explicit qualified lifecycle provider.");
        if (profile.EnableContinuation && !services.Any(item => item.ServiceType == typeof(IProductAuthorizationContinuationPolicySource)))
            throw new InvalidOperationException("Product continuation requires an explicit qualified policy provider.");
        if (profile.RequireAuthorizationReview && !services.Any(item => item.ServiceType == typeof(IProductAuthorizationPresentationSource)))
            throw new InvalidOperationException("Product review requires an explicit qualified presentation provider.");
        if (services.Any(item => item.ServiceType == typeof(ProductAuthorizationServerProfile)))
            throw new InvalidOperationException("Product authorization may be composed only once.");
        services.AddSingleton(profile); services.AddHttpContextAccessor();
        services.AddScoped<ProductAuthorizationApplication>();
        if (profile.EnableContinuation) services.AddScoped<ProductAuthorizationContinuation>();
        if (profile.RequireAuthorizationReview) services.AddScoped<ProductAuthorizationReview>();
        if (profile.Lifecycle is not null) services.AddScoped<ProductAuthorizationLifecycleApplication>();
        string[] callbackPaths = profile.Lifecycle is { } lifecycle
            ? [profile.TokenPath, lifecycle.IntrospectionPath, lifecycle.RevocationPath] : [profile.TokenPath];
        foreach (var path in callbackPaths) services.AddSingleton(new BffProtocolCallback(path, profile.MaximumBodyBytes, profile.MaximumFields,
            profile.MaximumKeyLength, profile.MaximumValueLength, async context =>
            {
                var handler = await context.RequestServices.GetRequiredService<IAuthenticationHandlerProvider>()
                    .GetHandlerAsync(context, OpenIddict.Server.AspNetCore.OpenIddictServerAspNetCoreDefaults.AuthenticationScheme).ConfigureAwait(false);
                if (handler is not IAuthenticationRequestHandler requestHandler || !await requestHandler.HandleRequestAsync().ConfigureAwait(false))
                    throw new InvalidOperationException("The exact native protocol callback must be handled terminally.");
            }));
        services.AddOpenIddict().AddCore(options => options.DisableEntityCaching()).AddServer(options =>
        {
            options.SetIssuer(new Uri(profile.Origin));
            options.SetAuthorizationEndpointUris(profile.AuthorizationPath).SetTokenEndpointUris(profile.TokenPath);
            options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
            if (profile.Lifecycle is { } lifecycleOptions)
            {
                options.AllowRefreshTokenFlow().SetIntrospectionEndpointUris(lifecycleOptions.IntrospectionPath)
                    .SetRevocationEndpointUris(lifecycleOptions.RevocationPath);
                options.Configure(value =>
                {
                    value.DisableRollingRefreshTokens = !lifecycleOptions.RotateRefreshTokens;
                    value.DisableSlidingRefreshTokenExpiration = !lifecycleOptions.SlidingRefreshExpiration;
                    value.RefreshTokenReuseLeeway = lifecycleOptions.RefreshReuseLeeway;
                });
                options.AddEventHandler<OpenIddictServerEvents.HandleIntrospectionRequestContext>(builder =>
                    builder.UseScopedHandler<ProductAuthorizationProtocol>().SetOrder(OpenIddictServerHandlers.Introspection.AttachApplicationClaims.Descriptor.Order + 1_000));
                options.AddEventHandler<OpenIddictServerEvents.HandleRevocationRequestContext>(builder =>
                    builder.UseScopedHandler<ProductAuthorizationProtocol>().SetOrder(OpenIddictServerHandlers.Revocation.AttachPrincipal.Descriptor.Order + 500));
                options.AddEventHandler<OpenIddictServerEvents.ProcessSignInContext>(builder =>
                    builder.UseScopedHandler<ProductAuthorizationProtocol>().SetOrder(OpenIddictServerHandlers.PrepareIdentityTokenPrincipal.Descriptor.Order + 500));
                options.AddEventHandler<OpenIddictServerEvents.ApplyIntrospectionResponseContext>(builder =>
                    builder.UseScopedHandler<ProductAuthorizationProtocol>().SetOrder(int.MinValue + 50_000));
                options.AddEventHandler<OpenIddictServerEvents.ApplyRevocationResponseContext>(builder =>
                    builder.UseScopedHandler<ProductAuthorizationProtocol>().SetOrder(int.MinValue + 50_000));
            }
            options.RegisterScopes(profile.SupportedScopes.ToArray());
            options.AddSigningKey(profile.SigningKey).AddEncryptionKey(profile.EncryptionKey);
            options.Configure(value =>
            {
                value.Scopes.Clear(); value.Scopes.UnionWith(profile.SupportedScopes);
                value.Resources.Clear(); value.Resources.UnionWith(profile.SupportedResources.Select(resource => new Uri(resource)));
                value.CodeChallengeMethods.Clear(); value.CodeChallengeMethods.Add(OpenIddictConstants.CodeChallengeMethods.Sha256);
                value.ResponseModes.Clear(); value.ResponseModes.Add(OpenIddictConstants.ResponseModes.FormPost);
                value.PromptValues.Clear();
                value.PromptValues.Add(OpenIddictConstants.PromptValues.None);
                value.PromptValues.Add(OpenIddictConstants.PromptValues.Login);
            });
            options.UseAspNetCore(); // No passthrough and no transport/permission/storage bypass.
            if (profile.EnableContinuation)
                options.AddEventHandler<OpenIddictServerEvents.ExtractAuthorizationRequestContext>(builder => builder
                    .UseScopedHandler<ProductAuthorizationProtocol>().SetOrder(OpenIddict.Server.AspNetCore.OpenIddictServerAspNetCoreHandlers
                        .ExtractGetOrPostRequest<OpenIddictServerEvents.ExtractAuthorizationRequestContext>.Descriptor.Order + 500));
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
