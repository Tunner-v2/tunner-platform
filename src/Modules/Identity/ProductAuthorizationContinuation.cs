using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Tunner.Platform.Operations;

namespace Tunner.Identity.Persistence;

/// <summary>Purpose-separated browser pointer backed by a single-use native request-token row; never an authentication credential.</summary>
internal sealed class ProductAuthorizationContinuation(IdentityPersistenceDbContext database, IOpenIddictTokenManager tokens,
    IDataProtectionProvider protection, IProductAuthorizationContinuationPolicySource policies,
    ProductAuthorizationServerProfile profile, TimeProvider clock)
{
    internal const string CookieName = "__Host-Tunner.ProductAuthorization.Continuation";
    private const string MetadataKey = "tunner.product.continuation.v1";
    private const string ConsumedKey = "tunner.product.continuation.consumed.v1";
    private const string ReviewKey = "tunner.product.continuation.review.v1";
    private readonly IDataProtector cookieProtection = protection.CreateProtector("Tunner.User.ProductAuthorization.Continuation.Cookie.v1");
    private readonly IDataProtector materialProtection = protection.CreateProtector("Tunner.Identity.ProductAuthorization.Continuation.Material.v1");
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private sealed record Pointer(string TokenId, string Origin, string Secret, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
        string PolicySnapshot, bool Ready, string? AccountId, Guid? SessionId)
    {
        public int ReviewGeneration { get; init; }
        public override string ToString() => nameof(Pointer);
    }
    private sealed record Material(ProductAuthorizationRequest Request, string NativeApplicationId, string OperationId,
        string CorrelationId, string Origin, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string PolicySnapshot, string BindingDigest)
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ProductAuthorizationInteractionContext? Interaction { get; init; }
        public override string ToString() => nameof(Material);
    }
    private sealed record Metadata(string Origin, string MaterialDigest, string BindingDigest, string OperationId, string CorrelationId)
    { public override string ToString() => nameof(Metadata); }
    private sealed record Consumed(string AccountId, Guid SessionId, string OperationId, string RequestDigest)
    { public override string ToString() => nameof(Consumed); }
    private sealed record ReviewState(int Generation, string? AccountId, Guid? SessionId, string? ApprovalDigest, bool Denied)
    { public string? DenialDigest { get; init; } }
    private sealed record Read(Pointer Pointer, Material Material, object Token, ReviewState? Review);

    internal async Task<ProductAuthorizationRestoredContinuation> BeginAsync(HttpContext http, ProductAuthorizationRequest request,
        string qualifiedNativeApplicationId, CancellationToken ct, ProductAuthorizationInteractionContext? interaction = null,
        bool bindAuthenticatedActor = false)
    {
        RequireTransaction(); RequireOrigin(http); ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(qualifiedNativeApplicationId);
        var at = IdentityPersistenceTime.Normalize(clock.GetUtcNow()); var policy = Policy(clock.GetUtcNow());
        var expires = at + policy.Lifetime;
        if (policy.Metadata.EffectiveInterval.Until is { } until && until < expires) expires = until;
        expires = IdentityPersistenceTime.Normalize(expires);
        if (expires <= at) throw new InvalidOperationException("A current continuation lifetime is required.");
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        if (interaction is not null && (interaction.RequestDigest != request.Digest || interaction.Origin != profile.Origin))
            throw new ArgumentException("The original authentication context must bind this exact Product request.");
        var material = new Material(request, qualifiedNativeApplicationId, "OP-" + Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"), profile.Origin, at, expires, policy.Snapshot, AuthenticationIdentity.Digest(secret))
        { Interaction = interaction };
        var serialized = JsonSerializer.Serialize(material, Json);
        if (Encoding.UTF8.GetByteCount(serialized) > policy.MaximumRequestBytes)
            throw new ArgumentException("The original Product authorization request exceeds its qualified budget.");
        var descriptor = new OpenIddictTokenDescriptor
        {
            ApplicationId = qualifiedNativeApplicationId, CreationDate = at, ExpirationDate = expires,
            Status = OpenIddictConstants.Statuses.Valid, Type = OpenIddictConstants.TokenTypeIdentifiers.Private.RequestToken,
            Payload = materialProtection.Protect(serialized)
        };
        descriptor.Properties.Add(MetadataKey, JsonSerializer.SerializeToElement(new Metadata(profile.Origin,
            AuthenticationIdentity.Digest(serialized), material.BindingDigest, material.OperationId, material.CorrelationId), Json));
        var token = await tokens.CreateAsync(descriptor, ct).ConfigureAwait(false);
        var id = await tokens.GetIdAsync(token, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Native continuation identity is unavailable.");
        var pointer = new Pointer(id, profile.Origin, secret, at, expires, policy.Snapshot, false, null, null);
        if (bindAuthenticatedActor)
        {
            var actual = await http.AuthenticateAsync(AuthenticationIdentity.Scheme).ConfigureAwait(false);
            if (!actual.Succeeded || actual.Principal is null || !Actor(actual.Principal, out var account, out var session))
                throw new InvalidOperationException("An actual authenticated session must own the Product review.");
            pointer = pointer with { Ready = true, AccountId = account, SessionId = session };
        }
        Write(http, pointer, policy); return Saved(http, pointer, material);
    }

    internal async Task<bool> PendingAsync(HttpContext http, CancellationToken ct)
    {
        if (!http.Request.Cookies.ContainsKey(CookieName)) return false;
        if (!Actor(http.User, out var account, out var session)) return false;
        var read = await ReadAsync(http, ct).ConfigureAwait(false);
        return read is not null && (!read.Pointer.Ready || read.Pointer.AccountId == account && read.Pointer.SessionId == session);
    }

    internal async Task<string?> PrepareResumeAsync(HttpContext http, CancellationToken ct)
    {
        RequireOrigin(http);
        if (!Actor(http.User, out var account, out var session)) return null;
        var read = await ReadAsync(http, ct).ConfigureAwait(false);
        if (read is null || read.Pointer.Ready && (read.Pointer.AccountId != account || read.Pointer.SessionId != session)) return null;
        var pointer = read.Pointer with { Ready = true, AccountId = account, SessionId = session };
        Write(http, pointer, Policy(clock.GetUtcNow()));
        return profile.RequireAuthorizationReview && read.Review?.ApprovalDigest is null && read.Review?.Denied != true
            ? ProductAuthorizationReviewEndpoints.ScreenPath : profile.AuthorizationPath;
    }

    internal async Task<ProductAuthorizationRestoredContinuation?> TryRestoreAsync(HttpContext http, CancellationToken ct)
    {
        RequireOrigin(http);
        var read = await ReadAsync(http, ct).ConfigureAwait(false);
        if (read is null || !read.Pointer.Ready) return null;
        var actual = await http.AuthenticateAsync(AuthenticationIdentity.Scheme).ConfigureAwait(false);
        return actual.Succeeded && actual.Principal is not null && Actor(actual.Principal, out var account, out var session) &&
            read.Pointer.AccountId == account && read.Pointer.SessionId == session ? Saved(http, read.Pointer, read.Material, read.Review) : null;
    }

    internal async Task<ProductAuthorizationRestoredContinuation?> ReadForReviewAsync(HttpContext http, CancellationToken ct)
    {
        var saved = await TryRestoreAsync(http, ct).ConfigureAwait(false);
        if (saved is not null)
        {
            // Native extraction runs before the default cookie middleware. Never use an
            // unrelated principal in the account-bound presentation owner.
            var actual = await http.AuthenticateAsync(AuthenticationIdentity.Scheme).ConfigureAwait(false);
            http.User = actual.Principal!;
        }
        return saved;
    }

    internal async Task<ProductAuthorizationRestoredContinuation?> ReadForLoginAsync(HttpContext http, CancellationToken ct)
    {
        if (!http.Request.Cookies.ContainsKey(CookieName)) return null;
        var read = await ReadAsync(http, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original Product sign-in request is unavailable.");
        if (read.Review?.ApprovalDigest is not null || read.Review?.Denied == true)
            throw new InvalidOperationException("The original Product request already has a terminal decision.");
        return Saved(http, read.Pointer, read.Material, read.Review);
    }

    internal Task<bool> ApproveReviewAsync(ProductAuthorizationRestoredContinuation saved, string digest, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);
        return RecordReviewAsync(saved, digest, false, false, ct);
    }
    internal Task<bool> DenyReviewAsync(ProductAuthorizationRestoredContinuation saved, string digest, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(digest);
        return RecordReviewAsync(saved, null, true, false, ct, digest);
    }
    internal Task<bool> SwitchReviewAccountAsync(ProductAuthorizationRestoredContinuation saved, CancellationToken ct) =>
        RecordReviewAsync(saved, null, false, true, ct);

    private async Task<bool> RecordReviewAsync(ProductAuthorizationRestoredContinuation saved, string? digest,
        bool denied, bool switching, CancellationToken ct, string? denialDigest = null)
    {
        RequireTransaction();
        var read = await ReadLockedAsync(saved.Http, saved.TokenId, ct).ConfigureAwait(false);
        if (read is null || !read.Pointer.Ready || !SameSaved(saved, read)) return false;
        var actual = await saved.Http.AuthenticateAsync(AuthenticationIdentity.Scheme).ConfigureAwait(false);
        if (!actual.Succeeded || actual.Principal is null || !Actor(actual.Principal, out var account, out var session) ||
            account != saved.AccountId || session != saved.SessionId) return false;
        if (read.Review is { } prior && (prior.ApprovalDigest is not null || prior.Denied))
            return !switching && prior.ApprovalDigest == digest && prior.Denied == denied && prior.DenialDigest == denialDigest;
        var generation = switching ? checked(read.Pointer.ReviewGeneration + 1) : read.Pointer.ReviewGeneration;
        var state = new ReviewState(generation, switching ? null : account, switching ? null : session, digest, denied)
        { DenialDigest = denialDigest };
        var descriptor = new OpenIddictTokenDescriptor();
        await tokens.PopulateAsync(descriptor, read.Token, ct).ConfigureAwait(false);
        descriptor.Properties[ReviewKey] = JsonSerializer.SerializeToElement(state, Json);
        await tokens.UpdateAsync(read.Token, descriptor, ct).ConfigureAwait(false);
        if (switching) Write(saved.Http, read.Pointer with
        { Ready = false, AccountId = null, SessionId = null, ReviewGeneration = generation }, Policy(clock.GetUtcNow()));
        return true;
    }

    private async Task<Read?> ReadLockedAsync(HttpContext http, string tokenId, CancellationToken ct)
    {
        RequireTransaction();
        await database.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"product-continuation:" + tokenId},0))", ct).ConfigureAwait(false);
        var token = await tokens.FindByIdAsync(tokenId, ct).ConfigureAwait(false);
        if (token is null) return null;
        // DisableEntityCaching does not disable EF tracking. A waiter must see the
        // committed decision, not the pre-lock tracked token snapshot.
        await database.Entry(token).ReloadAsync(ct).ConfigureAwait(false);
        return await ReadAsync(http, ct).ConfigureAwait(false);
    }

    internal async Task<bool> ReturnToSignInAsync(ProductAuthorizationRestoredContinuation saved, CancellationToken ct)
    {
        RequireTransaction();
        var read = await ReadAsync(saved.Http, ct).ConfigureAwait(false);
        if (read is null || !SameSaved(saved, read) || read.Review?.ApprovalDigest is not null || read.Review?.Denied == true) return false;
        var actual = await saved.Http.AuthenticateAsync(AuthenticationIdentity.Scheme).ConfigureAwait(false);
        if (!actual.Succeeded || actual.Principal is null || !Actor(actual.Principal, out var account, out var session) ||
            account != saved.AccountId || session != saved.SessionId) return false;
        // Reset only the completed-login binding, preserving the immutable original request
        // and freshness baseline across the required new sign-in.
        Write(saved.Http, read.Pointer with { Ready = false, AccountId = null, SessionId = null }, Policy(clock.GetUtcNow()));
        return true;
    }

    internal async Task<bool> ConsumeAsync(ProductAuthorizationRestoredContinuation saved, string actualNativeApplicationId, CancellationToken ct,
        bool denied = false)
    {
        RequireTransaction(); ArgumentNullException.ThrowIfNull(saved);
        var read = profile.RequireAuthorizationReview
            ? await ReadLockedAsync(saved.Http, saved.TokenId, ct).ConfigureAwait(false)
            : await ReadAsync(saved.Http, ct).ConfigureAwait(false);
        if (read is null || !read.Pointer.Ready || read.Pointer.TokenId != saved.TokenId ||
            read.Material.NativeApplicationId != actualNativeApplicationId || !SameSaved(saved, read)) return false;
        if (profile.RequireAuthorizationReview && (denied
            ? read.Review?.Denied != true || read.Review.ApprovalDigest is not null || !saved.ReviewDenied ||
                string.IsNullOrWhiteSpace(saved.ReviewDenialDigest) || read.Review.DenialDigest != saved.ReviewDenialDigest
            : read.Review?.Denied == true || string.IsNullOrWhiteSpace(saved.ReviewApprovalDigest) ||
                read.Review?.ApprovalDigest != saved.ReviewApprovalDigest)) return false;
        var actual = await saved.Http.AuthenticateAsync(AuthenticationIdentity.Scheme).ConfigureAwait(false);
        if (!actual.Succeeded || actual.Principal is null || !Actor(actual.Principal, out var account, out var session) ||
            account != saved.AccountId || session != saved.SessionId) return false;
        var at = clock.GetUtcNow();
        if (at < saved.CreatedAt || at >= saved.ExpiresAt || Policy(at).Snapshot != saved.PolicySnapshot) return false;
        // The native optimistic concurrency token owns single-use, including concurrent browser resumptions.
        if (!await tokens.TryRedeemAsync(read.Token, ct).ConfigureAwait(false)) return false;
        var descriptor = new OpenIddictTokenDescriptor();
        await tokens.PopulateAsync(descriptor, read.Token, ct).ConfigureAwait(false);
        descriptor.Properties[ConsumedKey] = JsonSerializer.SerializeToElement(new Consumed(account!, session!.Value,
            saved.OperationId, saved.Request.Digest), Json);
        await tokens.UpdateAsync(read.Token, descriptor, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Only committed native consumption qualifies clearing. A rolled-back or uncertain result keeps its pointer.</summary>
    internal async Task ClearAsync(HttpContext http, ProductAuthorizationRestoredContinuation saved, CancellationToken ct)
    {
        if (database.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("A continuation can be cleared only after the native transaction has ended.");
        var pointer = ReadPointer(http, Policy(clock.GetUtcNow()));
        if (pointer is null || pointer.TokenId != saved.TokenId || pointer.AccountId != saved.AccountId || pointer.SessionId != saved.SessionId) return;
        var token = await tokens.FindByIdAsync(saved.TokenId, ct).ConfigureAwait(false);
        if (token is null) return;
        // Manager queries may return an already tracked instance after an outer rollback.
        // Reload from PostgreSQL before treating consumption as a committed terminal fact.
        await database.Entry(token).ReloadAsync(ct).ConfigureAwait(false);
        if (database.Entry(token).State == EntityState.Detached ||
            await tokens.GetStatusAsync(token, ct).ConfigureAwait(false) != OpenIddictConstants.Statuses.Redeemed ||
            await tokens.GetTypeAsync(token, ct).ConfigureAwait(false) != OpenIddictConstants.TokenTypeIdentifiers.Private.RequestToken ||
            await tokens.GetApplicationIdAsync(token, ct).ConfigureAwait(false) != saved.NativeApplicationId) return;
        var properties = await tokens.GetPropertiesAsync(token, ct).ConfigureAwait(false);
        if (!properties.TryGetValue(ConsumedKey, out var raw) || raw.Deserialize<Consumed>(Json) is not { } consumed ||
            consumed.AccountId != saved.AccountId || consumed.SessionId != saved.SessionId || consumed.OperationId != saved.OperationId ||
            consumed.RequestDigest != saved.Request.Digest) return;
        http.Response.Cookies.Delete(CookieName, Cookie(TimeSpan.Zero));
    }

    internal ProductAuthorizationContinuationPolicy CurrentPolicy() => Policy(clock.GetUtcNow());
    private ProductAuthorizationContinuationPolicy Policy(DateTimeOffset at)
    {
        var policy = policies.RequireCurrent(profile.Origin, at); policy.Require(profile.Origin, at); return policy;
    }
    private async Task<Read?> ReadAsync(HttpContext http, CancellationToken ct)
    {
        RequireOrigin(http); var policy = Policy(clock.GetUtcNow()); var pointer = ReadPointer(http, policy);
        if (pointer is null) return null;
        var token = await tokens.FindByIdAsync(pointer.TokenId, ct).ConfigureAwait(false);
        if (token is null || await tokens.GetTypeAsync(token, ct).ConfigureAwait(false) != OpenIddictConstants.TokenTypeIdentifiers.Private.RequestToken ||
            await tokens.GetStatusAsync(token, ct).ConfigureAwait(false) != OpenIddictConstants.Statuses.Valid ||
            await tokens.GetCreationDateAsync(token, ct).ConfigureAwait(false) != pointer.CreatedAt ||
            await tokens.GetExpirationDateAsync(token, ct).ConfigureAwait(false) != pointer.ExpiresAt) return null;
        var payload = await tokens.GetPayloadAsync(token, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(payload)) return null;
        try
        {
            var serialized = materialProtection.Unprotect(payload);
            if (Encoding.UTF8.GetByteCount(serialized) > policy.MaximumRequestBytes) return null;
            var material = JsonSerializer.Deserialize<Material>(serialized, Json);
            var properties = await tokens.GetPropertiesAsync(token, ct).ConfigureAwait(false);
            if (material is null || material.Request is null || !properties.TryGetValue(MetadataKey, out var raw) ||
                raw.Deserialize<Metadata>(Json) is not { } metadata || metadata.Origin != profile.Origin ||
                metadata.MaterialDigest != AuthenticationIdentity.Digest(serialized) || metadata.BindingDigest != AuthenticationIdentity.Digest(pointer.Secret) ||
                material.BindingDigest != metadata.BindingDigest || metadata.OperationId != material.OperationId || metadata.CorrelationId != material.CorrelationId ||
                material.Origin != profile.Origin || material.CreatedAt != pointer.CreatedAt || material.ExpiresAt != pointer.ExpiresAt ||
                material.PolicySnapshot != pointer.PolicySnapshot ||
                await tokens.GetApplicationIdAsync(token, ct).ConfigureAwait(false) != material.NativeApplicationId) return null;
            var at = clock.GetUtcNow(); var current = Policy(at);
            ReviewState? review = null;
            if (properties.TryGetValue(ReviewKey, out var reviewRaw))
            {
                review = reviewRaw.Deserialize<ReviewState>(Json);
                if (!profile.RequireAuthorizationReview || review is null || review.Generation != pointer.ReviewGeneration ||
                    review.Generation < 0 || (review.Denied
                        ? review.ApprovalDigest is not null || string.IsNullOrWhiteSpace(review.DenialDigest)
                        : review.DenialDigest is not null) ||
                    ((review.ApprovalDigest is not null || review.Denied)
                        ? !pointer.Ready || review.AccountId != pointer.AccountId || review.SessionId != pointer.SessionId
                        : review.AccountId is not null || review.SessionId is not null)) return null;
            }
            else if (pointer.ReviewGeneration != 0) return null;
            return current.Snapshot == pointer.PolicySnapshot && at >= pointer.CreatedAt && at < pointer.ExpiresAt
                ? new(pointer, material, token, review) : null;
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }

    private Pointer? ReadPointer(HttpContext http, ProductAuthorizationContinuationPolicy policy)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName, out var value) ||
            Encoding.UTF8.GetByteCount(CookieName + "=" + value) > policy.MaximumCookieBytes) return null;
        try
        {
            var pointer = JsonSerializer.Deserialize<Pointer>(cookieProtection.Unprotect(value), Json);
            var at = clock.GetUtcNow();
            return pointer is not null && !string.IsNullOrWhiteSpace(pointer.TokenId) && pointer.Origin == profile.Origin &&
                pointer.Secret is { Length: 64 } && pointer.Secret.All(Uri.IsHexDigit) && pointer.CreatedAt <= at && at < pointer.ExpiresAt &&
                pointer.ExpiresAt > pointer.CreatedAt && pointer.PolicySnapshot == policy.Snapshot &&
                (pointer.Ready ? !string.IsNullOrWhiteSpace(pointer.AccountId) && pointer.SessionId is { } id && id != Guid.Empty : pointer.AccountId is null && pointer.SessionId is null)
                ? pointer : null;
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }
    private void Write(HttpContext http, Pointer pointer, ProductAuthorizationContinuationPolicy policy)
    {
        var at = clock.GetUtcNow(); var current = Policy(at);
        if (current.Snapshot != pointer.PolicySnapshot || policy.Snapshot != current.Snapshot || at >= pointer.ExpiresAt)
            throw new InvalidOperationException("Continuation qualification changed before publication.");
        var value = cookieProtection.Protect(JsonSerializer.Serialize(pointer, Json)); var options = Cookie(pointer.ExpiresAt - at);
        if (!SessionRevocationOriginalSelection.Fits(CookieName, value, options, policy.MaximumCookieBytes))
            throw new InvalidOperationException("The protected continuation exceeds its qualified cookie budget.");
        http.Response.Cookies.Append(CookieName, value, options);
    }
    private void RequireOrigin(HttpContext http)
    {
        if (!http.Request.IsHttps || http.Request.PathBase.HasValue ||
            http.Request.Scheme + "://" + http.Request.Host.ToUriComponent() != profile.Origin)
            throw new ArgumentException("The exact qualified User origin is required.");
    }
    private bool Actor(ClaimsPrincipal principal, out string? account, out Guid? session)
    {
        account = principal.FindFirstValue(ClaimTypes.NameIdentifier); session = AuthenticationIdentity.SessionId(principal);
        return principal.Identity is { IsAuthenticated: true, AuthenticationType: AuthenticationIdentity.Scheme } &&
            !string.IsNullOrWhiteSpace(account) && session is { } id && id != Guid.Empty &&
            principal.FindFirstValue(AuthenticationIdentity.OriginClaim) == profile.Origin;
    }
    private static ProductAuthorizationRestoredContinuation Saved(HttpContext http, Pointer pointer, Material material, ReviewState? review = null) =>
        new(http, pointer.TokenId, material.Request, material.NativeApplicationId, material.OperationId, material.CorrelationId,
            material.Origin, material.CreatedAt, material.ExpiresAt, material.PolicySnapshot, material.BindingDigest, pointer.AccountId, pointer.SessionId)
        { Interaction = material.Interaction, ReviewGeneration = pointer.ReviewGeneration,
            ReviewApprovalDigest = review?.ApprovalDigest, ReviewDenied = review?.Denied == true,
            ReviewDenialDigest = review?.DenialDigest };
    private static bool SameSaved(ProductAuthorizationRestoredContinuation saved, Read read) =>
        saved.Request.Digest == read.Material.Request.Digest && saved.NativeApplicationId == read.Material.NativeApplicationId &&
        saved.OperationId == read.Material.OperationId && saved.CorrelationId == read.Material.CorrelationId && saved.Origin == read.Material.Origin &&
        saved.CreatedAt == read.Material.CreatedAt && saved.ExpiresAt == read.Material.ExpiresAt && saved.PolicySnapshot == read.Material.PolicySnapshot &&
        saved.BindingDigest == read.Material.BindingDigest && saved.AccountId == read.Pointer.AccountId && saved.SessionId == read.Pointer.SessionId &&
        saved.ReviewGeneration == read.Pointer.ReviewGeneration &&
        JsonSerializer.Serialize(saved.Interaction, Json) == JsonSerializer.Serialize(read.Material.Interaction, Json);
    private static CookieOptions Cookie(TimeSpan remaining) => new() { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, MaxAge = remaining };
    private void RequireTransaction()
    {
        if (database.Database.CurrentTransaction is null) throw new InvalidOperationException("Native continuation mutation requires the caller-owned Identity transaction.");
    }
}

public static class ProductAuthorizationContinuationEndpoints
{
    internal static void MapIfConfigured(IEndpointRouteBuilder endpoints)
    {
        if (endpoints.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(ProductAuthorizationContinuation)) != true) return;
        var user = new AuthorizationPolicyBuilder(AuthenticationIdentity.Scheme).RequireAuthenticatedUser().Build();
        endpoints.MapPost("/product-authorization/continuation/resume", async (HttpContext http, ProductAuthorizationContinuation service) =>
        {
            try
            {
                if (http.Request.QueryString.HasValue || !http.Request.HasJsonContentType()) return Problem(400, "VALIDATION_ERROR");
                _ = await IdentityJsonCommand.ReadAsync<ResumeCommand>(http, service.CurrentPolicy().MaximumRequestBytes).ConfigureAwait(false);
                var path = await service.PrepareResumeAsync(http, http.RequestAborted).ConfigureAwait(false);
                return path is null ? Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE") : Results.Json(new { resumePath = path });
            }
            catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
            catch (BadHttpRequestException error) { return Problem(error.StatusCode == 413 ? 413 : 400, "VALIDATION_ERROR"); }
            catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
            catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
            catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        }).RequireAuthorization(user);
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)] private sealed class ResumeCommand;
    private static IResult Problem(int status, string code) => TechnicalProblemDetails.HttpResult(status, "about:blank",
        "The request could not be completed.", new TechnicalProblemMetadata(code, Guid.NewGuid().ToString("N"), "UNSPECIFIED"));
}
