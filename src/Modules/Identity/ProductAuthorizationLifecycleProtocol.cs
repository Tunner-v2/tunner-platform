using System.Security.Claims;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tunner.Identity.Persistence;

internal sealed partial class ProductAuthorizationProtocol :
    IOpenIddictServerHandler<OpenIddictServerEvents.HandleIntrospectionRequestContext>,
    IOpenIddictServerHandler<OpenIddictServerEvents.HandleRevocationRequestContext>,
    IOpenIddictServerHandler<OpenIddictServerEvents.ProcessSignInContext>,
    IOpenIddictServerHandler<OpenIddictServerEvents.ApplyIntrospectionResponseContext>,
    IOpenIddictServerHandler<OpenIddictServerEvents.ApplyRevocationResponseContext>
{
    private ProductAuthorizationLifecycleEvaluation? lifecycleEvaluation;
    private ClaimsPrincipal? lifecyclePrincipal;

    public ValueTask HandleAsync(OpenIddictServerEvents.ProcessSignInContext context)
    {
        if (lifecycleEvaluation is { Request.Operation: ProductAuthorizationLifecycleOperation.Refresh } evaluation)
        {
            // Preserve exact policy-selected instants rather than extending them by native
            // processing time between setting a relative lifetime and generating each artifact.
            context.AccessTokenPrincipal?.SetExpirationDate(evaluation.Decision.AccessTokenExpiresAt);
            context.IdentityTokenPrincipal?.SetExpirationDate(evaluation.Decision.IdentityTokenExpiresAt);
            context.RefreshTokenPrincipal?.SetExpirationDate(evaluation.Decision.RefreshTokenExpiresAt);
        }
        return ValueTask.CompletedTask;
    }

    private async ValueTask RefreshAsync(OpenIddictServerEvents.HandleTokenRequestContext context)
    {
        var principal = context.RefreshTokenPrincipal;
        var evaluation = await EvaluateLifecycleAsync(ProductAuthorizationLifecycleOperation.Refresh, principal, context.Request).ConfigureAwait(false);
        if (evaluation is null) { context.Reject(Errors.InvalidGrant); return; }
        if (!evaluation.Decision.ArtifactActive)
        {
            lifecycleEvaluation = evaluation; lifecyclePrincipal = principal;
            context.Reject(Errors.InvalidGrant); return;
        }
        if (profile.Lifecycle is not { } settings ||
            evaluation.Decision.RefreshRotates != settings.RotateRefreshTokens ||
            !settings.SlidingRefreshExpiration && evaluation.Decision.RefreshTokenExpiresAt > evaluation.Request.NativeState.ExpiresAt)
        { context.Reject(Errors.InvalidGrant); return; }
        var now = clock.GetUtcNow(); var decision = evaluation.Decision;
        var requiresIdentity = evaluation.Request.RequestedScopes.Contains(Scopes.OpenId, StringComparer.Ordinal);
        if (decision.AccessTokenExpiresAt is not { } access || access <= now ||
            requiresIdentity && (decision.IdentityTokenExpiresAt is not { } identityEnd || identityEnd <= now) ||
            decision.RefreshTokenExpiresAt is not { } refresh || refresh <= now)
        { context.Reject(Errors.InvalidGrant); return; }
        var identity = new ClaimsIdentity("Tunner.ProductAuthorization");
        identity.SetClaim(Claims.Subject, evaluation.Subject);
        identity.SetDestinations(claim => claim.Type == Claims.Subject ? [Destinations.AccessToken, Destinations.IdentityToken] : []);
        context.Principal = new ClaimsPrincipal(identity).SetAuthorizationId(evaluation.Request.NativeState.AuthorizationId)
            .SetScopes(evaluation.Request.RequestedScopes).SetResources(evaluation.Request.RequestedResources)
            .SetAccessTokenLifetime(access - now).SetRefreshTokenLifetime(refresh - now);
        if (decision.IdentityTokenExpiresAt is { } idExpiry) context.Principal.SetIdentityTokenLifetime(idExpiry - now);
        lifecycleEvaluation = evaluation; lifecyclePrincipal = principal;
    }

    public async ValueTask HandleAsync(OpenIddictServerEvents.HandleIntrospectionRequestContext context)
    {
        RequireTransaction();
        var evaluation = await EvaluateLifecycleAsync(ProductAuthorizationLifecycleOperation.Introspection,
            context.GenericTokenPrincipal, context.Request).ConfigureAwait(false);
        if (evaluation is null) { context.Reject(Errors.InvalidToken); return; }
        lifecycleEvaluation = evaluation; lifecyclePrincipal = context.GenericTokenPrincipal;
        if (!evaluation.Decision.ArtifactActive) context.Reject(Errors.InvalidToken);
    }

    public async ValueTask HandleAsync(OpenIddictServerEvents.HandleRevocationRequestContext context)
    {
        RequireTransaction();
        var evaluation = await EvaluateLifecycleAsync(ProductAuthorizationLifecycleOperation.Revocation,
            context.GenericTokenPrincipal, context.Request).ConfigureAwait(false);
        if (evaluation is null || evaluation.Decision.Effect == ProductAuthorizationArtifactEffect.None)
        { context.Reject(Errors.UnauthorizedClient); return; }
        lifecycleEvaluation = evaluation; lifecyclePrincipal = context.GenericTokenPrincipal;
        // The next native handler performs presented-token revocation. An explicitly selected
        // chain effect is applied only at the same pre-output commit boundary below.
    }

    public ValueTask HandleAsync(OpenIddictServerEvents.ApplyIntrospectionResponseContext context) => FinalizeLifecycleAsync(context.Response);
    public ValueTask HandleAsync(OpenIddictServerEvents.ApplyRevocationResponseContext context) => FinalizeLifecycleAsync(context.Response);

    private async Task<ProductAuthorizationLifecycleEvaluation?> EvaluateLifecycleAsync(ProductAuthorizationLifecycleOperation operation,
        ClaimsPrincipal? principal, OpenIddictRequest request)
    {
        RequireTransaction();
        if (profile.Lifecycle is null || lifecycleApplication is null || principal is null || request.ClientId is not { Length: > 0 } clientId)
            return null;
        var ct = Http.RequestAborted;
        var state = await NativeStateAsync(principal).ConfigureAwait(false);
        if (state is null) return null;
        var authorization = await authorizations.FindByIdAsync(state.AuthorizationId, ct).ConfigureAwait(false);
        if (authorization is null) return null;
        var properties = await authorizations.GetPropertiesAsync(authorization, ct).ConfigureAwait(false);
        if (!properties.TryGetValue(ProductAuthorizationApplication.LineageProperty, out var raw) ||
            ProductAuthorizationApplication.ReadLineage(raw) is not { } original) return null;
        var client = await clients.FindByClientIdAsync(clientId, ct).ConfigureAwait(false);
        if (client is null || await clients.GetIdAsync(client, ct).ConfigureAwait(false) is not { Length: > 0 } clientNativeId) return null;
        var scopes = request.GetScopes(); var resources = request.GetResources();
        var lifecycleRequest = new ProductAuthorizationLifecycleRequest(operation, clientId, clientNativeId, state,
            Canonical(scopes.Length == 0 ? state.Scopes : scopes), Canonical(resources.Length == 0 ? state.Resources : resources));
        try { return await lifecycleApplication.EvaluateAsync(original, lifecycleRequest, ct).ConfigureAwait(false); }
        catch (ArgumentException) { return null; }
    }

    private async Task<ProductAuthorizationNativeState?> NativeStateAsync(ClaimsPrincipal principal)
    {
        var ct = Http.RequestAborted;
        if (principal.GetAuthorizationId() is not { Length: > 0 } authId || principal.GetTokenId() is not { Length: > 0 } tokenId) return null;
        var authorization = await authorizations.FindByIdAsync(authId, ct).ConfigureAwait(false);
        var token = await tokens.FindByIdAsync(tokenId, ct).ConfigureAwait(false);
        if (authorization is null || token is null || await tokens.GetAuthorizationIdAsync(token, ct).ConfigureAwait(false) != authId ||
            await authorizations.GetApplicationIdAsync(authorization, ct).ConfigureAwait(false) is not { Length: > 0 } applicationId ||
            await tokens.GetApplicationIdAsync(token, ct).ConfigureAwait(false) != applicationId ||
            await authorizations.GetSubjectAsync(authorization, ct).ConfigureAwait(false) is not { Length: > 0 } subject ||
            await tokens.GetSubjectAsync(token, ct).ConfigureAwait(false) != subject || principal.GetClaim(Claims.Subject) != subject ||
            await tokens.GetCreationDateAsync(token, ct).ConfigureAwait(false) is not { } created ||
            await tokens.GetExpirationDateAsync(token, ct).ConfigureAwait(false) is not { } expires ||
            await tokens.GetTypeAsync(token, ct).ConfigureAwait(false) is not { Length: > 0 } type || type != principal.GetTokenType() ||
            await tokens.GetStatusAsync(token, ct).ConfigureAwait(false) is not { Length: > 0 } status ||
            await authorizations.GetStatusAsync(authorization, ct).ConfigureAwait(false) is not { Length: > 0 } authorizationStatus) return null;
        var resources = principal.GetResources();
        // Native access-token preparation projects validated resources into audiences and
        // omits the private resource claim. ID-token audiences must never be used here.
        if (resources.Length == 0 && type == TokenTypeIdentifiers.AccessToken) resources = principal.GetAudiences();
        return new(authId, authorizationStatus, applicationId, subject, tokenId, type, status, created, expires,
            Canonical(principal.GetScopes()), Canonical(resources));
    }

    private async ValueTask FinalizeLifecycleAsync(OpenIddictResponse response)
    {
        if (transaction is null) return;
        var ct = Http.RequestAborted;
        if (lifecycleEvaluation is { } evaluation && lifecyclePrincipal is not null && lifecycleApplication is not null &&
            (response.Error is null || !evaluation.Decision.ArtifactActive && evaluation.Request.Operation != ProductAuthorizationLifecycleOperation.Revocation))
        {
            if (evaluation.Decision.Effect != ProductAuthorizationArtifactEffect.None)
            {
                var token = await tokens.FindByIdAsync(evaluation.Request.NativeState.TokenId, ct).ConfigureAwait(false);
                if (token is null || !await tokens.TryRevokeAsync(token, ct).ConfigureAwait(false))
                    throw new InvalidOperationException("Qualified native token revocation failed.");
                if (evaluation.Decision.Effect == ProductAuthorizationArtifactEffect.AuthorizationChain)
                {
                    var authorization = await authorizations.FindByIdAsync(evaluation.Request.NativeState.AuthorizationId, ct).ConfigureAwait(false);
                    if (authorization is null || !await authorizations.TryRevokeAsync(authorization, ct).ConfigureAwait(false))
                        throw new InvalidOperationException("Qualified native authorization revocation failed.");
                    _ = await tokens.RevokeByAuthorizationIdAsync(evaluation.Request.NativeState.AuthorizationId, ct).ConfigureAwait(false);
                }
            }
            var current = await NativeStateAsync(lifecyclePrincipal).ConfigureAwait(false);
            if (current is null || !await lifecycleApplication.VerifyCurrentAsync(evaluation, current, ct).ConfigureAwait(false))
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                foreach (var parameter in response.GetParameters().ToArray()) response.RemoveParameter(parameter.Key);
                response.Error = evaluation.Request.Operation switch
                {
                    ProductAuthorizationLifecycleOperation.Refresh => Errors.InvalidGrant,
                    ProductAuthorizationLifecycleOperation.Introspection => Errors.InvalidToken,
                    _ => Errors.UnauthorizedClient
                };
                await transaction.DisposeAsync().ConfigureAwait(false); transaction = null; return;
            }
            if (evaluation.Request.Operation == ProductAuthorizationLifecycleOperation.Introspection && response.Error is null)
            {
                // Native processing remains the upper disclosure bound; qualification can only
                // remove fields, never manufacture claims the native handler would not disclose.
                foreach (var parameter in response.GetParameters().ToArray())
                    if (parameter.Key != Claims.Active && !evaluation.Decision.IntrospectionClaims.Contains(parameter.Key, StringComparer.Ordinal))
                        response.RemoveParameter(parameter.Key);
            }
            lifecycleApplication.StageNativeEffect(evaluation, current, "OP-" + Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
        }
        await database.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        await transaction.DisposeAsync().ConfigureAwait(false); transaction = null;
    }
}
