using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Operations;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit optional User-only owner. No production provider/default, consume endpoint or disclosure read.</summary>
public static class RecoveryCodeEndpoints
{
    public static IServiceCollection AddTunnerRecoveryCodes(this IServiceCollection services, RecoveryCodeCryptoProfile admittedCryptoProfile)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(admittedCryptoProfile);
        Type[] required = [typeof(AuthenticationEndpoints.Surface), typeof(IRecoveryCodePolicySource), typeof(IRecoveryCodeMutationAdmissionSource),
            typeof(IRecoveryCodePresentationSource), typeof(IAssurancePolicySource), typeof(IStepUpPresentationSource)];
        if (required.Any(t => !services.Any(x => x.ServiceType == t))) throw new InvalidOperationException("Recovery codes require explicit approved policy, mutation admission, publication and shared proof owners.");
        if (services.Any(x => x.ServiceType == typeof(RecoveryCodeApplication))) throw new InvalidOperationException("Recovery codes may be activated only once.");
        // One admitted native instance shares its work gate across requests; no per-request unlimited replacement.
        services.AddSingleton(_ => new RecoveryCodeNative(admittedCryptoProfile)); services.AddScoped<RecoveryCodeApplication>(); services.AddScoped<RecoveryCodeBrowserService>();
        var fallback = services.LastOrDefault(x => x.ServiceType == typeof(IUserStepUpOperationSource));
        if (fallback is not null)
        {
            services.Remove(fallback);
            services.Add(new ServiceDescriptor(typeof(RecoveryCodeFallbackOwner), p => new RecoveryCodeFallbackOwner(fallback.ImplementationInstance as IUserStepUpOperationSource ??
                fallback.ImplementationFactory?.Invoke(p) as IUserStepUpOperationSource ?? (IUserStepUpOperationSource)ActivatorUtilities.GetServiceOrCreateInstance(p, fallback.ImplementationType!)), fallback.Lifetime));
        }
        services.AddScoped<IUserStepUpOperationSource, RecoveryCodeStepUpOwner>(); return services;
    }
    public static IEndpointRouteBuilder MapTunnerRecoveryCodes(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var user = new AuthorizeAttribute { AuthenticationSchemes = AuthenticationIdentity.Scheme };
        endpoints.MapGet("/account/recovery-codes/options", (HttpContext c, RecoveryCodeBrowserService s) => s.OptionsAsync(c)).RequireAuthorization(user);
        endpoints.MapPost("/account/recovery-codes/generate/start", (HttpContext c, RecoveryCodeBrowserService s) => s.StartAsync(c, "GENERATE")).RequireAuthorization(user);
        endpoints.MapPost("/account/recovery-codes/replace/start", (HttpContext c, RecoveryCodeBrowserService s) => s.StartAsync(c, "REPLACE")).RequireAuthorization(user);
        endpoints.MapPost("/account/recovery-codes/start-result", (HttpContext c, RecoveryCodeBrowserService s) => s.StartResultAsync(c)).RequireAuthorization(user);
        endpoints.MapPost("/account/recovery-codes/result", (HttpContext c, RecoveryCodeBrowserService s) => s.ResultAsync(c)).RequireAuthorization(user);
        endpoints.MapPost("/account/recovery-codes/cancel", (HttpContext c, RecoveryCodeBrowserService s) => s.CancelAsync(c)).RequireAuthorization(user);
        return endpoints;
    }
}
internal sealed class RecoveryCodeFallbackOwner(IUserStepUpOperationSource owner) { internal IUserStepUpOperationSource Owner { get; } = owner; }
internal sealed record RecoveryCodeSelection(Guid OperationId, Guid OwnerRequestId, string AccountId, Guid SessionId, string Origin, DateTimeOffset ExpiresAt);
internal sealed class RecoveryCodeStepUpOwner(RecoveryCodeApplication application, IdentityPersistenceDbContext database, IHttpContextAccessor accessor,
    IDataProtectionProvider protection, AuthenticationPartitionKey partitions, AuthenticationEndpoints.Surface surface, IEnumerable<RecoveryCodeFallbackOwner> fallback, TimeProvider clock)
    : IUserStepUpOperationOutcomeSource, IUserStepUpBrowserCompletionSource
{
    internal const string CookieName = "__Host-Tunner.RecoveryCode.Operation";
    internal const string ResultCookieName = "__Host-Tunner.RecoveryCode.Result";
    private static IDataProtector Protector(IDataProtectionProvider provider) => provider.CreateProtector("Tunner.User.RecoveryCode.Operation.v1");
    internal static RecoveryCodeSelection? Selection(HttpContext http, IDataProtectionProvider provider, string name = CookieName)
    {
        if (!http.Request.Cookies.TryGetValue(name, out var value)) return null;
        try { return JsonSerializer.Deserialize<RecoveryCodeSelection>(Protector(provider).Unprotect(value)); }
        catch (CryptographicException) { return null; } catch (JsonException) { return null; }
    }
    internal static void Select(HttpContext http, RecoveryCodeOperationRow row, IDataProtectionProvider provider, DateTimeOffset at)
    {
        FederationStepUpOwner.ClearFederationSelection(http);
        var selection = new RecoveryCodeSelection(row.OperationId, row.RequestId, row.AccountId, row.SessionId, row.Origin, IdentityPersistenceTime.Normalize(row.ResultRecoveryExpiresAt));
        var value = Protector(provider).Protect(JsonSerializer.Serialize(selection));
        if (row.State == "PENDING_PROOF")
        {
            http.Response.Cookies.Append(CookieName, value, Cookie(row.ResultRecoveryExpiresAt - at));
            http.Response.Cookies.Delete(PasskeyStepUpOwner.CookieName, Cookie()); http.Response.Cookies.Delete(TotpStepUpOwner.CookieName, Cookie());
        }
        else http.Response.Cookies.Delete(CookieName, Cookie());
        http.Response.Cookies.Append(ResultCookieName, value, Cookie(row.ResultRecoveryExpiresAt - at));
    }
    internal static CookieOptions Cookie(TimeSpan? age = null) => new() { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = age };
    private StepUpCaller Caller(HttpContext http) => StepUpCaller.FromPrincipal(http.User, surface.Origin, partitions.Network(http.Connection.RemoteIpAddress ?? throw new ArgumentException("Current peer required.")));
    public async Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken ct)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("Current BFF context required.");
        if (!http.Request.Cookies.ContainsKey(CookieName)) return fallback.SingleOrDefault() is { } prior ? await prior.Owner.ResolveAsync(actor, ct).ConfigureAwait(false) : null;
        var selected = Selection(http, protection);
        return selected is not null && selected.AccountId == actor.AccountId && selected.SessionId == actor.SessionId && selected.Origin == actor.Origin
            ? await application.ResolveAsync(Caller(http), selected.OperationId, selected.OwnerRequestId, ct).ConfigureAwait(false) : null;
    }
    public async Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext context, CancellationToken ct)
    {
        if (RecoveryCodeApplication.Owns(operation.Binding.Scope)) throw new InvalidOperationException("Recovery issuance requires its typed winning outcome path.");
        using var outcome = await ApplyWithOutcomeAsync(actor, operation, context, ct).ConfigureAwait(false); return outcome.Applied;
    }
    public async Task<UserStepUpOperationApplyOutcome> ApplyWithOutcomeAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext context, CancellationToken ct)
    {
        if (!ReferenceEquals(context, database)) throw new InvalidOperationException("Shared Identity owner transaction required.");
        var http = accessor.HttpContext ?? throw new InvalidOperationException("Current BFF context required.");
        if (!http.Request.Cookies.ContainsKey(CookieName))
        {
            var prior = fallback.SingleOrDefault()?.Owner;
            if (prior is not IUserStepUpOperationOutcomeSource && RecoveryCodeApplication.Owns(operation.Binding.Scope)) throw new InvalidOperationException("Recovery issuance requires its typed winning outcome path.");
            return prior is IUserStepUpOperationOutcomeSource typed ? await typed.ApplyWithOutcomeAsync(actor, operation, context, ct).ConfigureAwait(false) :
                prior is not null && await prior.ApplyAsync(actor, operation, context, ct).ConfigureAwait(false) ? UserStepUpOperationApplyOutcome.SessionContinues() : UserStepUpOperationApplyOutcome.Conflict();
        }
        var selected = Selection(http, protection);
        return selected is not null && selected.AccountId == actor.AccountId && selected.SessionId == actor.SessionId && selected.Origin == actor.Origin && selected.OperationId.ToString("N") == operation.Binding.CorrelationId
            ? await application.ApplyAsync(Caller(http), operation, ct).ConfigureAwait(false) : UserStepUpOperationApplyOutcome.Conflict();
    }
    public async Task CompleteBrowserAsync(UserStepUpOperationContext operation, bool signInRequired, CancellationToken ct)
    {
        if (!RecoveryCodeApplication.Owns(operation.Binding.Scope))
        {
            if (fallback.SingleOrDefault()?.Owner is IUserStepUpBrowserCompletionSource prior) await prior.CompleteBrowserAsync(operation, signInRequired, ct).ConfigureAwait(false); return;
        }
        var http = accessor.HttpContext ?? throw new InvalidOperationException("Current BFF context required."); var selected = Selection(http, protection);
        if (signInRequired || selected is null || selected.AccountId != operation.Actor.AccountId || selected.SessionId != operation.Actor.SessionId || selected.Origin != operation.Actor.Origin ||
            selected.OperationId.ToString("N") != operation.Binding.CorrelationId) throw new InvalidOperationException("Recovery completion does not match its selected owner.");
        var row = await application.ReadAsync(Caller(http), selected.OperationId, selected.OwnerRequestId, ct).ConfigureAwait(false);
        if (row?.State != "COMPLETED" || row.ContextDigest != operation.Binding.ContextDigest) throw new InvalidOperationException("Recovery completion is not current.");
        Select(http, row, protection, clock.GetUtcNow());
    }
}

internal sealed class RecoveryCodeBrowserService(RecoveryCodeApplication application, AuthenticationPartitionKey partitions, AuthenticationEndpoints.Surface surface,
    IDataProtectionProvider protection, IAntiforgery antiforgery, TimeProvider clock)
{
    internal Task<IResult> OptionsAsync(HttpContext http) => Guard(http, async () =>
    {
        var selected = RecoveryCodeStepUpOwner.Selection(http, protection);
        if (http.Request.Cookies.ContainsKey(RecoveryCodeStepUpOwner.CookieName) && selected is null)
        {
            http.Response.Cookies.Delete(RecoveryCodeStepUpOwner.CookieName, RecoveryCodeStepUpOwner.Cookie()); return Failure("VERSION_CONFLICT");
        }
        selected ??= RecoveryCodeStepUpOwner.Selection(http, protection, RecoveryCodeStepUpOwner.ResultCookieName);
        var view = await application.AccountViewAsync(Caller(http), selected?.OperationId, selected?.OwnerRequestId, http.RequestAborted).ConfigureAwait(false);
        if (view is null) return Failure("AUTHENTICATION_FAILED");
        return Results.Json(new { csrfToken = antiforgery.GetAndStoreTokens(http).RequestToken, ownerRequestId = Guid.NewGuid(), view.ExpectedVersion,
            policyVersion = view.Policy.Version, view.Policy.FormatProfileVersion, view.Policy.DeliveryProfileVersion, view.Policy.Crypto.CodesPerSet, view.Policy.Crypto.MaximumCodesPerSet,
            view.CanGenerate, view.CanReplace, view.ActiveSetCount, view.RemainingCodeCount, view.Presentation.Locale, view.Presentation.Content,
            recovery = view.Recovery is null ? null : View(view.Recovery, view.RecoveryCanContinue) });
    });
    internal Task<IResult> StartAsync(HttpContext http, string kind) => Guard(http, async () =>
    {
        var command = await Read<RecoveryCodeStartCommand>(http).ConfigureAwait(false); var result = await application.StartAsync(Caller(http), kind, command, http.RequestAborted).ConfigureAwait(false);
        if (result.ErrorCode is { } error) return Failure(error);
        RecoveryCodeStepUpOwner.Select(http, result.Operation!, protection, clock.GetUtcNow()); return Results.Json(View(result.Operation!, false));
    });
    internal Task<IResult> StartResultAsync(HttpContext http) => Guard(http, async () =>
    {
        var command = await Read<RecoveryCodeStartResultCommand>(http).ConfigureAwait(false);
        if (command.OwnerRequestId == Guid.Empty || command.Kind is not ("GENERATE" or "REPLACE") || command.ExpectedVersion <= 0) return Failure("VALIDATION_ERROR");
        var row = await application.ReadStartAsync(Caller(http), command, http.RequestAborted).ConfigureAwait(false); if (row is null) return Failure("NOT_FOUND_OR_NOT_DISCLOSABLE");
        RecoveryCodeStepUpOwner.Select(http, row, protection, clock.GetUtcNow()); return Results.Json(View(row, false));
    });
    internal Task<IResult> ResultAsync(HttpContext http) => Guard(http, async () =>
    {
        var command = await Read<RecoveryCodeOperationCommand>(http).ConfigureAwait(false);
        if (command.OwnerRequestId == Guid.Empty || command.OperationId == Guid.Empty) return Failure("VALIDATION_ERROR");
        var row = await application.ReadAsync(Caller(http), command.OperationId, command.OwnerRequestId, http.RequestAborted).ConfigureAwait(false);
        return row is null ? Failure("NOT_FOUND_OR_NOT_DISCLOSABLE") : Results.Json(View(row, false));
    });
    internal Task<IResult> CancelAsync(HttpContext http) => Guard(http, async () =>
    {
        var command = await Read<RecoveryCodeOperationCommand>(http).ConfigureAwait(false);
        if (command.OwnerRequestId == Guid.Empty || command.OperationId == Guid.Empty) return Failure("VALIDATION_ERROR");
        var result = await application.CancelAsync(Caller(http), command, http.RequestAborted).ConfigureAwait(false);
        if (result.ErrorCode is { } error) return Failure(error);
        RecoveryCodeStepUpOwner.Select(http, result.Operation!, protection, clock.GetUtcNow()); return Results.Json(View(result.Operation!, false));
    });
    private object View(RecoveryCodeOperationRow row, bool canContinue) => new
    {
        status = row.State == "PENDING_PROOF" && row.ExpiresAt <= clock.GetUtcNow() ? "EXPIRED" : row.State,
        ownerRequestId = row.RequestId, operationReference = row.OperationId.ToString("N"), row.SetReference, ownerContextDigest = row.ContextDigest,
        row.DeliveryProfileVersion, row.FormatProfileVersion, row.CodesPerSet, row.MaximumCodesPerSet, row.ExpiresAt, row.ExpectedVersion, row.Kind, codesUnavailable = row.State == "COMPLETED",
        impact = new { impactKey = row.PublishedImpactKey, factsDigest = row.ImpactFactsDigest, canContinue = canContinue && row.State == "PENDING_PROOF",
            priorSetCount = row.PriorSetCount, remainingCodeCount = row.PriorRemainingCodeCount, sessionEffect = "NO_SESSION_MUTATION", replacementMechanism = "ATOMIC_RETIRE_EXACT_EXPECTED_SETS_AT_ISSUE_COMMIT_V1",
            row.PublicationVersion, row.PublicationDigest, publicationValidity = new { from = row.PublicationFrom, includesFrom = row.PublicationIncludesFrom, until = row.PublicationUntil, includesUntil = row.PublicationIncludesUntil } }
    };
    private StepUpCaller Caller(HttpContext http) => StepUpCaller.FromPrincipal(http.User, surface.Origin, partitions.Network(http.Connection.RemoteIpAddress ?? throw new ArgumentException("Current peer required.")));
    private static async Task<T> Read<T>(HttpContext http) where T : class
    {
        if (!http.Request.HasJsonContentType()) throw new BadHttpRequestException("JSON required.", 415);
        return await IdentityJsonCommand.ReadAsync<T>(http, 4096).ConfigureAwait(false);
    }
    private static IResult Failure(string error) => TechnicalProblemDetails.HttpResult(error switch { "AUTHENTICATION_FAILED" => 401, "RATE_OR_ABUSE_LIMITED" => 429,
        "NOT_FOUND_OR_NOT_DISCLOSABLE" => 404, "VERSION_CONFLICT" => 409, "RECOVERY_CODES_UNAVAILABLE" => 403, _ => 400 }, "about:blank", "The request could not be completed.", new TechnicalProblemMetadata(error, Guid.NewGuid().ToString("N"), "UNSPECIFIED"));
    private static async Task<IResult> Guard(HttpContext http, Func<Task<IResult>> action)
    {
        http.Response.Headers.CacheControl = "no-store"; http.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (http.Request.QueryString.HasValue) return Failure("VALIDATION_ERROR");
        try { return await action().ConfigureAwait(false); }
        catch (AssurancePolicyVersionConflictException) { return Failure("VERSION_CONFLICT"); }
        catch (DbUpdateConcurrencyException) { return Failure("VERSION_CONFLICT"); }
        catch (JsonException) { return Failure("VALIDATION_ERROR"); }
        catch (ArgumentException) { return Failure("VALIDATION_ERROR"); }
        catch (BadHttpRequestException error) { return TechnicalProblemDetails.HttpResult(error.StatusCode, "about:blank", "The request could not be completed.", new TechnicalProblemMetadata("VALIDATION_ERROR", Guid.NewGuid().ToString("N"), "UNSPECIFIED")); }
        catch (InvalidOperationException) { return TechnicalProblemDetails.HttpResult(503, "about:blank", "The request could not be completed.", new TechnicalProblemMetadata("DEPENDENCY_UNAVAILABLE", Guid.NewGuid().ToString("N"), "UNSPECIFIED")); }
        catch (CryptographicException) { return TechnicalProblemDetails.HttpResult(503, "about:blank", "The request could not be completed.", new TechnicalProblemMetadata("DEPENDENCY_UNAVAILABLE", Guid.NewGuid().ToString("N"), "UNSPECIFIED")); }
    }
}
