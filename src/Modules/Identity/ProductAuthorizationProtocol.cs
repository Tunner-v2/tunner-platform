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
    ProductAuthorizationLifecycleApplication? lifecycleApplication = null, ProductAuthorizationContinuation? continuation = null) :
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
        // Essential claims and review/account-selection prompts need their separately
        // qualified paths. Never silently accept an unsupported interaction request.
        if (context.Request.GetParameter("claims") is not null ||
            context.Request.GetPromptValues().Any(value => value is not (PromptValues.None or PromptValues.Login)))
        { context.Reject(Errors.InteractionRequired); return; }
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
                context.Request.State, context.Request.Nonce)
            { AuthenticationContext = ReadAuthenticationContext(context.Request) };
            var authenticated = await Http.AuthenticateAsync(AuthenticationIdentity.Scheme).ConfigureAwait(false);
            var caller = authenticated.Succeeded && authenticated.Principal is not null
                ? StepUpCaller.FromPrincipal(authenticated.Principal, profile.Origin, partitions.Network(Http.Connection.RemoteIpAddress)) : null;
            var interaction = restoredContinuation?.Interaction;
            if (request.AuthenticationContext is not null && restoredContinuation is null)
                interaction = await application.CaptureInteractionAsync(request, profile.Origin,
                    caller?.SessionId, ct).ConfigureAwait(false);
            var status = request.AuthenticationContext is null
                ? caller is null ? ProductAuthorizationInteractionStatus.RequiresSignIn : ProductAuthorizationInteractionStatus.Satisfied
                : (await application.EvaluateInteractionAsync(caller, request, profile.Origin, interaction, ct).ConfigureAwait(false)).Status;
            if (status == ProductAuthorizationInteractionStatus.Denied) { context.Reject(Errors.AccessDenied); return; }
            if (status == ProductAuthorizationInteractionStatus.RequiresFreshProof) { context.Reject(Errors.InteractionRequired); return; }
            if (status == ProductAuthorizationInteractionStatus.RequiresSignIn)
            {
                if (continuation is null || context.Request.GetPromptValues().Contains(PromptValues.None, StringComparer.Ordinal))
                { context.Reject(Errors.LoginRequired); return; }
                var registration = await application.RequireRegistrationAsync(request, profile.Origin, ct).ConfigureAwait(false);
                if (registration is null) { context.Reject(Errors.AccessDenied); return; }
                var registeredClient = await clients.FindByClientIdAsync(request.ClientId, ct).ConfigureAwait(false);
                if (registeredClient is null || await clients.GetIdAsync(registeredClient, ct).ConfigureAwait(false) != registration.NativeApplicationId ||
                    await clients.GetClientTypeAsync(registeredClient, ct).ConfigureAwait(false) != registration.ClientType)
                { context.Reject(Errors.InvalidClient); return; }
                if (restoredContinuation is null)
                    _ = await continuation.BeginAsync(Http, request, registration.NativeApplicationId, ct, interaction).ConfigureAwait(false);
                else if (!await continuation.ReturnToSignInAsync(restoredContinuation, ct).ConfigureAwait(false))
                { context.Reject(Errors.LoginRequired); return; }
                await CommitTransactionAsync(ct).ConfigureAwait(false);
                Http.Response.Headers.CacheControl = "no-store"; Http.Response.Headers["Referrer-Policy"] = "no-referrer";
                Http.Response.Redirect("/login"); context.HandleRequest(); return;
            }
            if (restoredContinuation is not null && restoredContinuation.Request.Digest != request.Digest)
            { context.Reject(Errors.InvalidRequest); return; }
            if (caller is null) { context.Reject(Errors.LoginRequired); return; }
            grant = await application.AuthorizeAsync(caller, request, restoredContinuation?.OperationId ?? "OP-" + Guid.NewGuid().ToString("N"),
                restoredContinuation?.CorrelationId ?? Guid.NewGuid().ToString("N"), ct, interaction).ConfigureAwait(false);
        }
        catch (ArgumentException) { context.Reject(Errors.InvalidRequest); return; }
        if (grant is null) { context.Reject(Errors.AccessDenied); return; }
        var client = await clients.FindByClientIdAsync(request.ClientId, ct).ConfigureAwait(false);
        if (client is null || await clients.GetIdAsync(client, ct).ConfigureAwait(false) != grant.Lineage.Registration.NativeApplicationId ||
            await clients.GetClientTypeAsync(client, ct).ConfigureAwait(false) != grant.Lineage.Registration.ClientType)
        { grant = null; context.Reject(Errors.InvalidClient); return; }
        if (restoredContinuation is not null)
        {
            if (continuation is null || !await continuation.ConsumeAsync(restoredContinuation, grant.Lineage.Registration.NativeApplicationId, ct).ConfigureAwait(false))
            { grant = null; context.Reject(Errors.InvalidRequest); return; }
            consumedContinuation = true;
        }
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
        await CommitTransactionAsync(ct).ConfigureAwait(false);
        if (consumedContinuation && restoredContinuation is not null && continuation is not null)
            await continuation.ClearAsync(Http, restoredContinuation, ct).ConfigureAwait(false);
    }

    private async Task CommitTransactionAsync(CancellationToken ct)
    {
        RequireTransaction();
        await database.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction!.CommitAsync(ct).ConfigureAwait(false);
        await transaction.DisposeAsync().ConfigureAwait(false); transaction = null;
    }

    private ClaimsPrincipal Principal(ProductAuthorizationGrant value, string id)
    {
        var now = clock.GetUtcNow(); var remainingCode = value.Lineage.CodeExpiresAt - now;
        if (remainingCode <= TimeSpan.Zero) throw new InvalidOperationException("Original authorization has expired.");
        var identity = new ClaimsIdentity("Tunner.ProductAuthorization");
        identity.SetClaim(Claims.Subject, value.Subject);
        ApplyAuthenticationClaims(identity, value.Lineage.AuthenticationClaims);
        var principal = new ClaimsPrincipal(identity);
        principal.SetAuthorizationId(id).SetScopes(value.Lineage.Request.Scopes).SetResources(value.Lineage.Request.Resources);
        principal.SetAuthorizationCodeLifetime(remainingCode).SetAccessTokenLifetime(value.Policy.AccessTokenLifetime)
            .SetIdentityTokenLifetime(value.Policy.IdentityTokenLifetime);
        if (value.Policy.AllowRefreshTokens && profile.Lifecycle is not null)
            principal.SetRefreshTokenLifetime(value.Policy.RefreshTokenLifetime);
        return principal;
    }
    private static ImmutableArray<string> Canonical(IEnumerable<string> values) => values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
    private static ProductAuthorizationAuthenticationContext? ReadAuthenticationContext(OpenIddictRequest request)
    {
        var prompt = (string?)request.GetParameter("prompt");
        var acr = (string?)request.GetParameter("acr_values");
        return prompt is null && acr is null && request.MaxAge is null ? null :
            new(prompt, request.MaxAge, acr?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToImmutableArray() ?? []);
    }
    private static void ApplyAuthenticationClaims(ClaimsIdentity identity, ProductAuthorizationAuthenticationClaims? claims)
    {
        if (claims?.AuthenticationTime is { } at) identity.SetClaim("auth_time", at.ToUnixTimeSeconds());
        if (claims?.Acr is { Length: > 0 } acr) identity.SetClaim("acr", acr);
        if (claims is { Amr.Length: > 0 }) identity.SetClaims("amr", claims.Amr);
        identity.SetDestinations(claim => claim.Type switch
        {
            Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
            "auth_time" or "acr" or "amr" => [Destinations.IdentityToken],
            _ => []
        });
    }
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
