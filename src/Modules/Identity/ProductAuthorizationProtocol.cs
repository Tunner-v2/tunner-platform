using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tunner.Identity.Persistence;

/// <summary>One scoped native request coordinator; commit precedes native response publication.</summary>
internal sealed partial class ProductAuthorizationProtocol(IdentityPersistenceDbContext database, ProductAuthorizationApplication application,
    ProductAuthorizationServerProfile profile, IOpenIddictApplicationManager clients, IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens, IHttpContextAccessor accessor, AuthenticationPartitionKey partitions, TimeProvider clock,
    ProductAuthorizationLifecycleApplication? lifecycleApplication = null) :
    IOpenIddictServerHandler<OpenIddictServerEvents.ProcessRequestContext>,
    IOpenIddictServerHandler<OpenIddictServerEvents.HandleAuthorizationRequestContext>,
    IOpenIddictServerHandler<OpenIddictServerEvents.HandleTokenRequestContext>,
    IOpenIddictServerHandler<OpenIddictServerEvents.ApplyAuthorizationResponseContext>,
    IOpenIddictServerHandler<OpenIddictServerEvents.ApplyTokenResponseContext>, IAsyncDisposable
{
    private IDbContextTransaction? transaction;
    private ProductAuthorizationGrant? grant;
    private string? authorizationId;
    private HttpContext Http => accessor.HttpContext ?? throw new InvalidOperationException("A native HTTP request is required.");

    public async ValueTask HandleAsync(OpenIddictServerEvents.ProcessRequestContext context)
    {
        var request = Http.Request;
        var isAuthorization = request.Path == profile.AuthorizationPath;
        if (!isAuthorization && request.Path != profile.TokenPath &&
            (profile.Lifecycle is null || request.Path != profile.Lifecycle.IntrospectionPath && request.Path != profile.Lifecycle.RevocationPath)) return;
        var expectedPath = isAuthorization ? profile.AuthorizationPath : request.Path == profile.TokenPath ? profile.TokenPath :
            request.Path == profile.Lifecycle?.IntrospectionPath ? profile.Lifecycle!.IntrospectionPath : profile.Lifecycle!.RevocationPath;
        var rawPath = Http.Features.Get<IHttpRequestFeature>()?.RawTarget?.Split('?', 2)[0];
        if (!string.Equals(rawPath, expectedPath, StringComparison.Ordinal) || request.PathBase.HasValue ||
            isAuthorization && !HttpMethods.IsGet(request.Method) ||
            !isAuthorization && !HttpMethods.IsPost(request.Method))
        { context.Reject(Errors.InvalidRequest); return; }
        if (database.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("A native request must own its Identity transaction.");
        transaction = await database.Database.BeginTransactionAsync(Http.RequestAborted).ConfigureAwait(false);
    }

    public async ValueTask HandleAsync(OpenIddictServerEvents.HandleAuthorizationRequestContext context)
    {
        RequireTransaction(); var ct = Http.RequestAborted;
        if (!HttpMethods.IsGet(Http.Request.Method) || context.Request.ResponseMode != ResponseModes.FormPost)
        { context.Reject(Errors.InvalidRequest); return; }
        // This first admitted transport profile must never silently ignore stronger requested
        // assurance, freshness or essential-claim/interaction controls. Their qualified continuation
        // handling remains part of P2-014 before feature acceptance.
        if (context.Request.GetParameter("acr_values") is not null || context.Request.MaxAge is not null ||
            context.Request.GetParameter("claims") is not null ||
            context.Request.GetPromptValues().Any(value => value != PromptValues.None))
        { context.Reject(Errors.InteractionRequired); return; }
        var authenticated = await Http.AuthenticateAsync(AuthenticationIdentity.Scheme).ConfigureAwait(false);
        if (!authenticated.Succeeded || authenticated.Principal is null)
        { context.Reject(Errors.LoginRequired); return; }
        if (Http.Connection.RemoteIpAddress is null) { context.Reject(Errors.InvalidRequest); return; }
        ProductAuthorizationRequest request;
        try
        {
            request = new((string?)context.Request.GetParameter("product_id") ?? string.Empty,
                (string?)context.Request.GetParameter("environment") ?? string.Empty,
                context.Request.ClientId ?? string.Empty, context.Request.RedirectUri ?? string.Empty,
                Canonical(context.Request.GetScopes()), Canonical(context.Request.GetResources()),
                context.Request.CodeChallenge ?? string.Empty, context.Request.CodeChallengeMethod ?? string.Empty,
                context.Request.ResponseType ?? string.Empty, context.Request.ResponseMode ?? string.Empty,
                context.Request.State, context.Request.Nonce);
            var caller = StepUpCaller.FromPrincipal(authenticated.Principal, profile.Origin, partitions.Network(Http.Connection.RemoteIpAddress));
            grant = await application.AuthorizeAsync(caller, request, "OP-" + Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), ct).ConfigureAwait(false);
        }
        catch (ArgumentException) { context.Reject(Errors.InvalidRequest); return; }
        if (grant is null) { context.Reject(Errors.AccessDenied); return; }
        var client = await clients.FindByClientIdAsync(request.ClientId, ct).ConfigureAwait(false);
        if (client is null || await clients.GetIdAsync(client, ct).ConfigureAwait(false) != grant.Lineage.Registration.NativeApplicationId ||
            await clients.GetClientTypeAsync(client, ct).ConfigureAwait(false) != grant.Lineage.Registration.ClientType)
        { grant = null; context.Reject(Errors.InvalidClient); return; }
        var descriptor = new OpenIddictAuthorizationDescriptor
        {
            ApplicationId = grant.Lineage.Registration.NativeApplicationId, Subject = grant.Subject,
            CreationDate = clock.GetUtcNow(), Status = Statuses.Valid, Type = AuthorizationTypes.AdHoc
        };
        descriptor.Scopes.UnionWith(request.Scopes);
        descriptor.Properties.Add(ProductAuthorizationApplication.LineageProperty, ProductAuthorizationApplication.WriteLineage(grant));
        var authorization = await authorizations.CreateAsync(descriptor, ct).ConfigureAwait(false);
        authorizationId = await authorizations.GetIdAsync(authorization, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Native authorization identity is unavailable.");
        context.Principal = Principal(grant, authorizationId);
    }

    public async ValueTask HandleAsync(OpenIddictServerEvents.HandleTokenRequestContext context)
    {
        RequireTransaction(); var ct = Http.RequestAborted;
        if (context.Request.IsRefreshTokenGrantType())
        { await RefreshAsync(context).ConfigureAwait(false); return; }
        if (!context.Request.IsAuthorizationCodeGrantType() || context.AuthorizationCodePrincipal is not { } principal ||
            principal.GetAuthorizationId() is not { Length: > 0 } id)
        { context.Reject(Errors.InvalidGrant); return; }
        var authorization = await authorizations.FindByIdAsync(id, ct).ConfigureAwait(false);
        if (authorization is null || await authorizations.GetStatusAsync(authorization, ct).ConfigureAwait(false) != Statuses.Valid)
        { context.Reject(Errors.InvalidGrant); return; }
        var properties = await authorizations.GetPropertiesAsync(authorization, ct).ConfigureAwait(false);
        if (!properties.TryGetValue(ProductAuthorizationApplication.LineageProperty, out var raw) ||
            ProductAuthorizationApplication.ReadLineage(raw) is not { } lineage ||
            context.Request.ClientId != lineage.Request.ClientId || context.Request.RedirectUri != lineage.Request.RedirectUri ||
            principal.GetClaim(Claims.Subject) != lineage.Relationship.Subject ||
            !Canonical(principal.GetScopes()).SequenceEqual(lineage.Request.Scopes) ||
            !Canonical(principal.GetResources()).SequenceEqual(lineage.Request.Resources) ||
            await authorizations.GetApplicationIdAsync(authorization, ct).ConfigureAwait(false) != lineage.Registration.NativeApplicationId ||
            await authorizations.GetSubjectAsync(authorization, ct).ConfigureAwait(false) != lineage.Relationship.Subject)
        { context.Reject(Errors.InvalidGrant); return; }
        grant = await application.RedeemAsync(lineage, ct).ConfigureAwait(false);
        if (grant is null) { context.Reject(Errors.InvalidGrant); return; }
        authorizationId = id; context.Principal = Principal(grant, id);
    }

    public ValueTask HandleAsync(OpenIddictServerEvents.ApplyAuthorizationResponseContext context) => FinalizeAsync(context.Response, false);
    public ValueTask HandleAsync(OpenIddictServerEvents.ApplyTokenResponseContext context) => FinalizeAsync(context.Response, true);

    private async ValueTask FinalizeAsync(OpenIddictResponse response, bool redemption)
    {
        if (transaction is null) return;
        if (lifecycleEvaluation is not null)
        { await FinalizeLifecycleAsync(response).ConfigureAwait(false); return; }
        var ct = Http.RequestAborted;
        if (grant is not null && authorizationId is not null)
        {
            if (response.Error is null && await application.VerifyCurrentAsync(grant, ct).ConfigureAwait(false))
                application.StageNativeEffect(grant, authorizationId, redemption);
            else
            {
                var authorization = await authorizations.FindByIdAsync(authorizationId, ct).ConfigureAwait(false);
                if (authorization is not null) _ = await authorizations.TryRevokeAsync(authorization, ct).ConfigureAwait(false);
                _ = await tokens.RevokeByAuthorizationIdAsync(authorizationId, ct).ConfigureAwait(false);
                response.Code = null; response.AccessToken = null; response.IdToken = null;
                response.RefreshToken = null; response.TokenType = null; response.ExpiresIn = null; response.Scope = null;
                response.Error = redemption ? Errors.InvalidGrant : Errors.AccessDenied;
                response.ErrorDescription = null; response.ErrorUri = null;
            }
        }
        // Native rejection/replay may revoke artifacts, and Identity denial may end a session.
        // Preserve those security transitions; never roll back every protocol error indiscriminately.
        await database.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        await transaction.DisposeAsync().ConfigureAwait(false); transaction = null;
    }

    private ClaimsPrincipal Principal(ProductAuthorizationGrant value, string id)
    {
        var now = clock.GetUtcNow(); var remainingCode = value.Lineage.CodeExpiresAt - now;
        if (remainingCode <= TimeSpan.Zero) throw new InvalidOperationException("Original authorization has expired.");
        var identity = new ClaimsIdentity("Tunner.ProductAuthorization");
        identity.SetClaim(Claims.Subject, value.Subject);
        identity.SetDestinations(claim => claim.Type == Claims.Subject ? [Destinations.AccessToken, Destinations.IdentityToken] : []);
        var principal = new ClaimsPrincipal(identity);
        principal.SetAuthorizationId(id).SetScopes(value.Lineage.Request.Scopes).SetResources(value.Lineage.Request.Resources);
        principal.SetAuthorizationCodeLifetime(remainingCode).SetAccessTokenLifetime(value.Policy.AccessTokenLifetime)
            .SetIdentityTokenLifetime(value.Policy.IdentityTokenLifetime);
        if (value.Policy.AllowRefreshTokens && profile.Lifecycle is not null)
            principal.SetRefreshTokenLifetime(value.Policy.RefreshTokenLifetime);
        return principal;
    }
    private static ImmutableArray<string> Canonical(IEnumerable<string> values) => values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
    private void RequireTransaction()
    {
        if (transaction is null || database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Native owner handling requires its request transaction.");
    }
    public async ValueTask DisposeAsync()
    {
        if (transaction is not null) { await transaction.DisposeAsync().ConfigureAwait(false); transaction = null; }
    }
}
