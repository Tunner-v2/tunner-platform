using System.Collections.Immutable;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tunner.Identity.Persistence;

internal sealed partial class ProductAuthorizationProtocol : IOpenIddictServerHandler<OpenIddictServerEvents.ExtractAuthorizationRequestContext>
{
    private ProductAuthorizationRestoredContinuation? restoredContinuation;
    private bool consumedContinuation;

    public async ValueTask HandleAsync(OpenIddictServerEvents.ExtractAuthorizationRequestContext context)
    {
        if (continuation is null || Http.Request.Path != profile.AuthorizationPath ||
            !HttpMethods.IsGet(Http.Request.Method) || Http.Request.QueryString.HasValue) return;
        RequireTransaction();
        restoredContinuation = await continuation.TryRestoreAsync(Http, Http.RequestAborted).ConfigureAwait(false);
        if (restoredContinuation is null) return;
        var saved = restoredContinuation.Request;
        // This is the original protected request, not an arbitrary return URL. All standard
        // native extraction/validation stages after this handler still enforce client policy.
        context.Transaction.Request = new OpenIddictRequest
        {
            ClientId = saved.ClientId, RedirectUri = saved.RedirectUri, ResponseType = saved.ResponseType,
            ResponseMode = saved.ResponseMode, CodeChallenge = saved.CodeChallenge, CodeChallengeMethod = saved.CodeChallengeMethod,
            Scope = string.Join(' ', saved.Scopes), State = saved.State, Nonce = saved.Nonce,
            [Parameters.Resource] = new OpenIddictParameter(saved.Resources.Select(value => (string?)value).ToImmutableArray()),
            ["product_id"] = saved.ProductId, ["environment"] = saved.Environment
        };
        if (saved.AuthenticationContext is { } controls)
        {
            context.Transaction.Request.Prompt = controls.Prompt;
            context.Transaction.Request.MaxAge = controls.MaxAgeSeconds;
            if (controls.RequestedAcrValues.Length > 0)
                context.Transaction.Request.AcrValues = string.Join(' ', controls.RequestedAcrValues);
        }
    }
}
