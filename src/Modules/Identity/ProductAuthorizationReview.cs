using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Tunner.Database.Outbox;
using Tunner.Platform.Operations;

namespace Tunner.Identity.Persistence;

/// <summary>Private explicit review of a saved native request. This owner never creates a Relationship or grant.</summary>
internal sealed class ProductAuthorizationReview(IdentityPersistenceDbContext database, AuthenticationApplication authentication,
    ProductAuthorizationApplication application, ProductAuthorizationContinuation continuation, ProductAuthorizationServerProfile profile,
    IProductAuthorizationPolicySource policies, IProductAuthorizationPresentationSource presentations, IOpenIddictApplicationManager clients,
    AuthenticationPartitionKey partitions, IDataProtectionProvider protection, IAntiforgery antiforgery, TimeProvider clock)
{
    private readonly IDataProtector references = protection.CreateProtector("Tunner.User.ProductAuthorization.Review.v1");
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record Selection(string ContinuationDigest, string AccountId, long AccountVersion, Guid SessionId,
        string Origin, string ActorDigest, string RegistrationDigest, string PolicyDigest, string PresentationDigest,
        ProductAuthorizationInteractionStatus Interaction, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt)
    { public override string ToString() => nameof(Selection); }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class DecisionCommand
    {
        public string? Reference { get; init; }
        public string? Decision { get; init; }
        public override string ToString() => nameof(DecisionCommand);
    }

    private sealed record Observation(ProductAuthorizationRestoredContinuation Saved, ProductAuthorizationActor Actor,
        ProductAuthorizationRegistration Registration, ProductAuthorizationPolicy Policy,
        ProductAuthorizationPresentation Presentation, ProductAuthorizationPresentationFacts Facts,
        ProductAuthorizationInteractionEvaluation Interaction, Selection Selection)
    { public override string ToString() => nameof(Observation); }

    internal Task<IResult> OptionsAsync(HttpContext http) => ExecuteAsync(http, false);
    internal Task<IResult> DecideAsync(HttpContext http) => ExecuteAsync(http, true);

    /// <summary>Qualified login display, including forced login under a still-authenticated browser. No actor is inferred.</summary>
    internal async Task<ProductAuthorizationLoginContext?> LoginContextAsync(HttpContext http, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!http.Request.Cookies.ContainsKey(ProductAuthorizationContinuation.CookieName)) return null;
        if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Product login context requires a clean Identity transaction boundary.");
        await using var transaction = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var original = await ObserveLoginAsync(http, ct).ConfigureAwait(false);
            var current = await ObserveLoginAsync(http, ct).ConfigureAwait(false);
            if (ContinuationDigest(original.Saved) != ContinuationDigest(current.Saved) ||
                Digest(original.Registration) != Digest(current.Registration) || original.Policy.Snapshot != current.Policy.Snapshot ||
                original.Publication.Snapshot != current.Publication.Snapshot)
                throw new InvalidOperationException("Product login context qualification changed before publication.");
            var at = clock.GetUtcNow();
            current.Publication.RequireLogin(current.Facts, at);
            if (!current.Registration.EffectiveInterval.Contains(at) || current.Saved.ExpiresAt <= at || Policy().Snapshot != current.Policy.Snapshot)
                throw new InvalidOperationException("Current Product login context qualification is unavailable.");
            await database.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new(current.Publication.ProductName, current.Publication.Locale,
                current.Publication.Content.OrderBy(text => text.Key, StringComparer.Ordinal).ToDictionary(text => text.Key, StringComparer.Ordinal),
                current.Publication.ScopeDescriptions.OrderBy(text => text.Key, StringComparer.Ordinal).ToDictionary(text => text.Key, StringComparer.Ordinal));
        }
        catch (ArgumentException error) { throw new InvalidOperationException("Current Product login publication is unavailable.", error); }
    }

    private async Task<(ProductAuthorizationRestoredContinuation Saved, ProductAuthorizationRegistration Registration,
        ProductAuthorizationPolicy Policy, ProductAuthorizationPresentation Publication, ProductAuthorizationPresentationFacts Facts)>
        ObserveLoginAsync(HttpContext http, CancellationToken ct)
    {
        var saved = await continuation.ReadForLoginAsync(http, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The pending original Product login context is unavailable.");
        var registration = await application.RequireRegistrationAsync(saved.Request, profile.Origin, ct).ConfigureAwait(false);
        if (registration is null || saved.Origin != profile.Origin || registration.NativeApplicationId != saved.NativeApplicationId ||
            !await NativeClientCurrentAsync(saved.Request, registration, ct).ConfigureAwait(false))
            throw new InvalidOperationException("Current exact Product login registry qualification is unavailable.");
        var policy = Policy();
        var interaction = await application.EvaluateInteractionAsync(null, saved.Request, profile.Origin, saved.Interaction, ct).ConfigureAwait(false);
        if (interaction.Status != ProductAuthorizationInteractionStatus.RequiresSignIn)
            throw new InvalidOperationException("The original Product authentication request is not currently qualified.");
        var facts = new ProductAuthorizationPresentationFacts(saved.Request.Digest, saved.Request.ProductId, registration.ProductVersion,
            saved.Request.Environment, registration.EnvironmentVersion, saved.Request.ClientId, registration.ClientVersion,
            null, null, null, profile.Origin, new Uri(saved.Request.RedirectUri).GetLeftPart(UriPartial.Authority), saved.Request.Scopes,
            saved.Request.AuthenticationContext?.RequestedAcrValues ?? ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, policy.Metadata.VersionId) { LoginOnly = true };
        var publication = await presentations.RequirePublishedAsync(facts, clock.GetUtcNow(), ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("An explicit published Product login presentation is required.");
        publication.RequireLogin(facts, clock.GetUtcNow());
        return (saved, registration, policy, publication, facts);
    }

    /// <summary>Native authorization must call this before any grant or Relationship side effect.</summary>
    internal async Task<bool> ValidateApprovalAsync(ProductAuthorizationRestoredContinuation saved, CancellationToken ct,
        bool consumed = false)
    {
        RequireTransaction(); ct.ThrowIfCancellationRequested();
        if (saved.ReviewDenied || string.IsNullOrWhiteSpace(saved.ReviewApprovalDigest)) return false;
        var current = await ObserveAsync(saved.Http, consumed ? saved : null).ConfigureAwait(false);
        return current is not null && current.Interaction.Status == ProductAuthorizationInteractionStatus.Satisfied &&
            ContinuationDigest(saved) == current.Selection.ContinuationDigest && !current.Saved.ReviewDenied &&
            current.Saved.ReviewApprovalDigest == saved.ReviewApprovalDigest &&
            ApprovalDigest(current.Selection) == saved.ReviewApprovalDigest;
    }

    internal async Task<bool> ValidateDenialAsync(ProductAuthorizationRestoredContinuation saved, CancellationToken ct)
    {
        RequireTransaction(); ct.ThrowIfCancellationRequested();
        if (!saved.ReviewDenied || string.IsNullOrWhiteSpace(saved.ReviewDenialDigest)) return false;
        var current = await ObserveAsync(saved.Http).ConfigureAwait(false);
        return current is not null && ContinuationDigest(saved) == current.Selection.ContinuationDigest &&
            ApprovalDigest(current.Selection) == saved.ReviewDenialDigest;
    }

    /// <summary>Stage once after native denial consumption. Save/commit remain with the native transaction owner.</summary>
    internal async Task StageDeclined(ProductAuthorizationRestoredContinuation saved, CancellationToken ct)
    {
        RequireTransaction(); ct.ThrowIfCancellationRequested();
        if (!saved.ReviewDenied || saved.ReviewApprovalDigest is not null)
            throw new InvalidOperationException("An exact explicit Product denial is required.");
        // The native request token may already have been redeemed by the caller. Reuse the
        // authenticated original snapshot rather than attempting to restore a consumed pointer.
        var current = await ObserveAsync(saved.Http, saved).ConfigureAwait(false);
        if (current is null || ContinuationDigest(saved) != current.Selection.ContinuationDigest ||
            ApprovalDigest(current.Selection) != saved.ReviewDenialDigest)
            throw new InvalidOperationException("Current exact Product denial publication is unavailable.");
        database.Add(new OutboxMessage(Guid.NewGuid(), "1.0", "ProductAuthorizationDeclined", "1", clock.GetUtcNow(),
            "tunner/identity", saved.TokenId, current.Presentation.Classification,
            JsonSerializer.Serialize(new { reference = saved.TokenId, product = saved.Request.ProductId,
                environment = saved.Request.Environment, originalRequestDigest = saved.Request.Digest,
                presentationVersion = current.Presentation.Metadata.VersionId, current.Presentation.EvidenceReference,
                current.Presentation.ProcessingReference, current.Presentation.RetentionReference }),
            "1", saved.OperationId, saved.CorrelationId));
    }

    private async Task<IResult> ExecuteAsync(HttpContext http, bool deciding)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        try
        {
            RequireBrowser(http, deciding);
            if (deciding)
            {
                if (!http.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
                await antiforgery.ValidateRequestAsync(http).ConfigureAwait(false);
            }
            if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges())
                throw new InvalidOperationException("Product review requires a clean caller-owned transaction boundary.");
            await using var transaction = await database.Database.BeginTransactionAsync(http.RequestAborted).ConfigureAwait(false);
            var current = await ObserveAsync(http).ConfigureAwait(false);
            IResult result;
            if (current is null) result = Problem(409, "VERSION_CONFLICT");
            else result = deciding ? await DecisionAsync(http, current).ConfigureAwait(false) : await OptionsAsync(http, current).ConfigureAwait(false);
            // Preserve current authentication validation transitions, including a session denial.
            // Review decisions and native request-token transitions commit before a response is published.
            await database.SaveChangesAsync(http.RequestAborted).ConfigureAwait(false);
            await transaction.CommitAsync(http.RequestAborted).ConfigureAwait(false);
            return result;
        }
        catch (AntiforgeryValidationException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (BadHttpRequestException error) { return Problem(error.StatusCode == 413 ? 413 : 400, "VALIDATION_ERROR"); }
        catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (DbUpdateConcurrencyException) { return Problem(409, "VERSION_CONFLICT"); }
        catch (DbUpdateException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (System.Data.Common.DbException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (System.Net.Http.HttpRequestException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (TimeoutException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
    }

    private async Task<IResult> OptionsAsync(HttpContext http, Observation current)
    {
        var selection = current.Selection;
        var encoded = references.Protect(JsonSerializer.Serialize(selection, Json));
        if (Encoding.UTF8.GetByteCount(encoded) > current.Presentation.MaximumReferenceBytes ||
            Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(new { reference = encoded, decision = "SWITCH_ACCOUNT" })) > current.Presentation.MaximumDecisionBytes)
            return Problem(503, "TRANSPORT_CAPACITY_EXCEEDED");
        var final = await ObserveAsync(http).ConfigureAwait(false);
        if (final is null || !SameSelection(selection, final.Selection, clock.GetUtcNow())) return Problem(409, "VERSION_CONFLICT");
        var presentation = final.Presentation;
        return Results.Json(new
        {
            reference = encoded, csrfToken = antiforgery.GetAndStoreTokens(http).RequestToken,
            canAllow = final.Interaction.Status == ProductAuthorizationInteractionStatus.Satisfied,
            interactionStatus = final.Interaction.Status.ToString(), presentation.Locale,
            content = presentation.Content.ToDictionary(text => text.Key, StringComparer.Ordinal),
            presentation.ProductName, presentation.AccountLabel, presentation.RegisteredReturnOrigin,
            presentation.RequestedAssuranceLabel,
            issuedClaimNames = final.Facts.IssuedClaimNames,
            scopeDescriptions = presentation.ScopeDescriptions.ToDictionary(text => text.Key, StringComparer.Ordinal),
            presentation.TermsUri, presentation.PrivacyUri, presentation.SupportUri
        });
    }

    private async Task<IResult> DecisionAsync(HttpContext http, Observation current)
    {
        var command = await IdentityJsonCommand.ReadAsync<DecisionCommand>(http, current.Presentation.MaximumDecisionBytes).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(command.Reference) || Encoding.UTF8.GetByteCount(command.Reference) > current.Presentation.MaximumReferenceBytes ||
            command.Decision is not ("ALLOW" or "DENY" or "SWITCH_ACCOUNT")) return Problem(400, "VALIDATION_ERROR");
        Selection? selected;
        try { selected = JsonSerializer.Deserialize<Selection>(references.Unprotect(command.Reference), Json); }
        catch (CryptographicException) { return Problem(409, "VERSION_CONFLICT"); }
        catch (JsonException) { return Problem(409, "VERSION_CONFLICT"); }
        if (selected is null || !SameSelection(selected, current.Selection, clock.GetUtcNow())) return Problem(409, "VERSION_CONFLICT");
        var final = await ObserveAsync(http).ConfigureAwait(false);
        if (final is null || !SameSelection(selected, final.Selection, clock.GetUtcNow())) return Problem(409, "VERSION_CONFLICT");
        bool applied;
        switch (command.Decision)
        {
            case "ALLOW":
                if (final.Interaction.Status != ProductAuthorizationInteractionStatus.Satisfied) return Problem(409, "INTERACTION_REQUIRED");
                applied = await continuation.ApproveReviewAsync(final.Saved, ApprovalDigest(selected), http.RequestAborted).ConfigureAwait(false);
                break;
            case "DENY":
                applied = await continuation.DenyReviewAsync(final.Saved, ApprovalDigest(selected), http.RequestAborted).ConfigureAwait(false);
                break;
            default:
                applied = await continuation.SwitchReviewAccountAsync(final.Saved, http.RequestAborted).ConfigureAwait(false);
                break;
        }
        if (!applied) return Problem(409, "VERSION_CONFLICT");
        return Results.Json(new { resumePath = command.Decision == "SWITCH_ACCOUNT" ? "/login" : profile.AuthorizationPath });
    }

    private async Task<Observation?> ObserveAsync(HttpContext http, ProductAuthorizationRestoredContinuation? consumedDenial = null)
    {
        try { return await ObserveCoreAsync(http, consumedDenial).ConfigureAwait(false); }
        catch (ArgumentException error) { throw new InvalidOperationException("Current exact Product review qualification is unavailable.", error); }
    }

    private async Task<Observation?> ObserveCoreAsync(HttpContext http, ProductAuthorizationRestoredContinuation? consumedDenial)
    {
        var ct = http.RequestAborted;
        var saved = consumedDenial ?? await continuation.ReadForReviewAsync(http, ct).ConfigureAwait(false);
        if (saved is null || http.Connection.RemoteIpAddress is null) return null;
        var caller = StepUpCaller.FromPrincipal(http.User, profile.Origin, partitions.Network(http.Connection.RemoteIpAddress));
        if (saved.AccountId != caller.AccountId || saved.SessionId != caller.SessionId || saved.Origin != caller.Origin) return null;
        var registration = await application.RequireRegistrationAsync(saved.Request, profile.Origin, ct).ConfigureAwait(false);
        if (registration is null || registration.NativeApplicationId != saved.NativeApplicationId ||
            !await NativeClientCurrentAsync(saved.Request, registration, ct).ConfigureAwait(false)) return null;
        var actor = await authentication.LockProductAuthorizationAsync(caller, ProductAuthorizationApplication.AuthorizeOperation, ct).ConfigureAwait(false);
        if (actor is null) return null;
        var policy = Policy();
        var interaction = await application.EvaluateInteractionAsync(caller, saved.Request, profile.Origin, saved.Interaction, ct).ConfigureAwait(false);
        var facts = new ProductAuthorizationPresentationFacts(saved.Request.Digest, saved.Request.ProductId, registration.ProductVersion,
            saved.Request.Environment, registration.EnvironmentVersion, saved.Request.ClientId, registration.ClientVersion,
            caller.AccountId, actor.AccountVersion, caller.SessionId, caller.Origin,
            new Uri(saved.Request.RedirectUri).GetLeftPart(UriPartial.Authority), saved.Request.Scopes,
            saved.Request.AuthenticationContext?.RequestedAcrValues ?? ImmutableArray<string>.Empty,
            IssuedClaimNames(saved.Request, interaction), policy.Metadata.VersionId);
        var publication = await presentations.RequirePublishedAsync(facts, clock.GetUtcNow(), ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A current Product authorization publication is required.");
        publication.Require(facts, clock.GetUtcNow());
        if (!await authentication.ProductAuthorizationCurrentAsync(actor, ProductAuthorizationApplication.AuthorizeOperation, ct).ConfigureAwait(false) ||
            Policy().Snapshot != policy.Snapshot) return null;
        var at = clock.GetUtcNow();
        if (!registration.EffectiveInterval.Contains(at) || !publication.Metadata.EffectiveInterval.Contains(at)) return null;
        var expires = Earliest(saved.ExpiresAt, at + publication.ReviewLifetime, policy.Metadata.EffectiveInterval.Until,
            registration.EffectiveInterval.Until, publication.Metadata.EffectiveInterval.Until, actor.Session.ExpiresAt);
        if (expires <= at) return null;
        var actorDigest = Digest(new { actor.Session.AccountId, actor.AccountVersion, actor.Session.SessionId, actor.Session.Origin,
            actor.Session.AuthenticatedAt, actor.Session.AuthenticationMethods, actor.Session.Assurance, actor.Session.SecurityStampDigest,
            actor.AuthenticationPolicy, actor.SessionUse, actor.ProductAuthorization, actor.AuthenticationPerformedAt });
        var selection = new Selection(ContinuationDigest(saved), caller.AccountId, actor.AccountVersion, caller.SessionId, caller.Origin,
            actorDigest, Digest(registration), Digest(policy.Snapshot), Digest(publication.Snapshot), interaction.Status, at, expires);
        return new(saved, actor, registration, policy, publication, facts, interaction, selection);
    }

    private async Task<bool> NativeClientCurrentAsync(ProductAuthorizationRequest request, ProductAuthorizationRegistration registration, CancellationToken ct)
    {
        var client = await clients.FindByClientIdAsync(request.ClientId, ct).ConfigureAwait(false);
        if (client is null || await clients.GetIdAsync(client, ct).ConfigureAwait(false) != registration.NativeApplicationId ||
            await clients.GetClientTypeAsync(client, ct).ConfigureAwait(false) != registration.ClientType ||
            !await clients.ValidateRedirectUriAsync(client, request.RedirectUri, ct).ConfigureAwait(false)) return false;
        string[] required = [OpenIddictConstants.Permissions.Endpoints.Authorization, OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode, OpenIddictConstants.Permissions.ResponseTypes.Code];
        foreach (var permission in required)
            if (!await clients.HasPermissionAsync(client, permission, ct).ConfigureAwait(false)) return false;
        foreach (var scope in request.Scopes.Where(scope => scope is not ("openid" or "offline_access")))
            if (!await clients.HasPermissionAsync(client, OpenIddictConstants.Permissions.Prefixes.Scope + scope, ct).ConfigureAwait(false)) return false;
        foreach (var resource in request.Resources)
            if (!await clients.HasPermissionAsync(client, OpenIddictConstants.Permissions.Prefixes.Resource + resource, ct).ConfigureAwait(false)) return false;
        return !request.Scopes.Contains("offline_access", StringComparer.Ordinal) ||
            await clients.HasPermissionAsync(client, OpenIddictConstants.Permissions.GrantTypes.RefreshToken, ct).ConfigureAwait(false);
    }

    private ProductAuthorizationPolicy Policy()
    {
        var at = clock.GetUtcNow(); var policy = policies.RequireCurrent(profile.Origin, at); policy.Require(profile.Origin, at); return policy;
    }
    private void RequireBrowser(HttpContext http, bool deciding)
    {
        var expectedPath = deciding ? ProductAuthorizationReviewEndpoints.DecisionPath : ProductAuthorizationReviewEndpoints.OptionsPath;
        var origins = http.Request.Headers.Origin;
        if (!http.Request.IsHttps || http.Request.PathBase.HasValue || http.Request.QueryString.HasValue ||
            http.Request.Path.Value != expectedPath || http.Request.Scheme + "://" + http.Request.Host.ToUriComponent() != profile.Origin ||
            deciding && !HttpMethods.IsPost(http.Request.Method) || !deciding && !HttpMethods.IsGet(http.Request.Method) ||
            (deciding || origins.Count != 0) && (origins.Count != 1 || origins[0] != profile.Origin))
            throw new BadHttpRequestException("The exact same-origin private review endpoint is required.", 400);
    }
    private static bool SameSelection(Selection original, Selection current, DateTimeOffset at) =>
        at >= original.CreatedAt && at < original.ExpiresAt && at < current.ExpiresAt &&
        (original with { CreatedAt = current.CreatedAt, ExpiresAt = current.ExpiresAt }) == current;
    private static string ContinuationDigest(ProductAuthorizationRestoredContinuation saved) => Digest(new
    {
        saved.TokenId, requestDigest = saved.Request.Digest, saved.NativeApplicationId, saved.OperationId, saved.CorrelationId,
        saved.Origin, saved.CreatedAt, saved.ExpiresAt, saved.PolicySnapshot, saved.BindingDigest, saved.AccountId, saved.SessionId,
        saved.Interaction, saved.ReviewGeneration
    });
    private static string ApprovalDigest(Selection selection) => Digest(new
    {
        selection.ContinuationDigest, selection.AccountId, selection.AccountVersion, selection.SessionId, selection.Origin,
        selection.ActorDigest, selection.RegistrationDigest, selection.PolicyDigest, selection.PresentationDigest, selection.Interaction
    });
    private static ImmutableArray<string> IssuedClaimNames(ProductAuthorizationRequest request, ProductAuthorizationInteractionEvaluation interaction)
    {
        if (interaction.Status != ProductAuthorizationInteractionStatus.Satisfied) return ImmutableArray<string>.Empty;
        var claims = new List<string> { "sub" };
        if (request.Scopes.Contains("openid", StringComparer.Ordinal) && interaction.AuthenticationClaims is { } authenticationClaims)
        {
            if (authenticationClaims.AuthenticationTime is not null) claims.Add("auth_time");
            if (authenticationClaims.Acr is not null) claims.Add("acr");
            if (authenticationClaims.Amr.Length > 0) claims.Add("amr");
        }
        return claims.Order(StringComparer.Ordinal).ToImmutableArray();
    }
    private void RequireTransaction()
    {
        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Native review validation requires the caller-owned Identity transaction.");
    }
    private static string Digest<T>(T value) => AuthenticationIdentity.Digest(JsonSerializer.Serialize(value));
    private static DateTimeOffset Earliest(DateTimeOffset first, params DateTimeOffset?[] others) =>
        others.Where(value => value.HasValue).Aggregate(first, (minimum, candidate) => candidate!.Value < minimum ? candidate.Value : minimum);
    private static IResult Problem(int status, string code) => TechnicalProblemDetails.HttpResult(status, "about:blank",
        "The request could not be completed.", new TechnicalProblemMetadata(code, Guid.NewGuid().ToString("N"), "UNSPECIFIED"));
}

internal static class ProductAuthorizationReviewEndpoints
{
    internal const string ScreenPath = "/product-authorization";
    internal const string OptionsPath = "/product-authorization/options";
    internal const string DecisionPath = "/product-authorization/decision";
    internal static void MapIfConfigured(IEndpointRouteBuilder endpoints)
    {
        if (endpoints.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(ProductAuthorizationReview)) != true) return;
        var user = new AuthorizationPolicyBuilder(AuthenticationIdentity.Scheme).RequireAuthenticatedUser().Build();
        endpoints.MapGet(OptionsPath, (HttpContext http, ProductAuthorizationReview service) => service.OptionsAsync(http)).RequireAuthorization(user);
        endpoints.MapPost(DecisionPath, (HttpContext http, ProductAuthorizationReview service) => service.DecideAsync(http)).RequireAuthorization(user);
        var assets = endpoints.ServiceProvider.GetRequiredService<IdentityWebAssets>();
        assets.MapScreensAsync(endpoints, "product-authorization.html", [ScreenPath], async http =>
            http.User.Identity?.IsAuthenticated == true && !http.Request.QueryString.HasValue &&
            await http.RequestServices.GetRequiredService<ProductAuthorizationContinuation>()
                .ReadForReviewAsync(http, http.RequestAborted).ConfigureAwait(false) is not null);
    }
}
