using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Operations;

namespace Tunner.Identity.Persistence;

public static class StepUpBrowserEndpoints
{
    public const string CookieName = "__Host-Tunner.StepUp";
    public static IServiceCollection AddTunnerOperationStepUp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] required = [typeof(AuthenticationEndpoints.Surface), typeof(IAssurancePolicySource), typeof(IUserStepUpOperationSource), typeof(IStepUpPresentationSource)];
        if (required.Any(type => !services.Any(x => x.ServiceType == type)))
            throw new InvalidOperationException("Step-up requires explicit User authentication, operation-owner, assurance and presentation providers.");
        if (services.Any(x => x.ServiceType == typeof(StepUpBrowserService))) throw new InvalidOperationException("Step-up may be activated only once.");
        services.AddScoped<StepUpApplication>(); services.AddScoped<StepUpBrowserService>(); return services;
    }
    public static IEndpointRouteBuilder MapTunnerOperationStepUp(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var authorization = new AuthorizeAttribute { AuthenticationSchemes = AuthenticationIdentity.Scheme };
        endpoints.MapGet("/authentication/step-up/options", (HttpContext context, StepUpBrowserService service) => service.OptionsAsync(context)).RequireAuthorization(authorization);
        endpoints.MapPost("/authentication/step-up/passkey/options", (HttpContext context, StepUpBrowserService service) => service.PasskeyOptionsAsync(context)).RequireAuthorization(authorization);
        endpoints.MapPost("/authentication/step-up/passkey", (HttpContext context, StepUpBrowserService service) => service.PasskeyAsync(context)).RequireAuthorization(authorization);
        endpoints.MapPost("/authentication/step-up/passkey/continue", (HttpContext context, StepUpBrowserService service) => service.PasskeyContinueAsync(context)).RequireAuthorization(authorization);
        endpoints.MapPost("/authentication/step-up/password", (HttpContext context, StepUpBrowserService service) => service.PasswordAsync(context)).RequireAuthorization(authorization);
        endpoints.MapPost("/authentication/step-up/totp", (HttpContext context, StepUpBrowserService service) => service.TotpAsync(context)).RequireAuthorization(authorization);
        endpoints.MapPost("/authentication/step-up/totp/continue", (HttpContext context, StepUpBrowserService service) => service.TotpContinueAsync(context)).RequireAuthorization(authorization);
        return endpoints;
    }
}

internal sealed partial class StepUpBrowserService(IUserStepUpOperationSource owner, IAssurancePolicySource assurancePolicies,
    IAuthenticationPolicySource authenticationPolicies, IStepUpPresentationSource presentations,
    AuthenticationPartitionKey partitions, AuthenticationEndpoints.Surface surface, IDataProtectionProvider protection,
    IAntiforgery antiforgery, StepUpApplication application, IdentityPersistenceDbContext database, TimeProvider clock, IEnumerable<PasskeyApplication> passkeyApplications,
    IEnumerable<TotpApplication>? totpApplications = null)
{
    private readonly IDataProtector protector = protection.CreateProtector("Tunner.User.StepUp.Browser.v1");
    internal sealed record Browser(Guid RequestId, string BindingDigest, string AuthenticationPolicyVersion, DateTimeOffset ExpiresAt);
    internal Task<IResult> OptionsAsync(HttpContext context) => GuardAsync(context, async () =>
    {
        var caller = Caller(context); var actor = new UserStepUpActor(caller); var operation = await owner.ResolveAsync(actor, context.RequestAborted).ConfigureAwait(false);
        if (!MatchesActor(operation, actor)) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var csrf = antiforgery.GetAndStoreTokens(context).RequestToken;
        var now = clock.GetUtcNow();
        if (operation!.Completed)
        {
            // A current owner result is not proof that an earlier dialog completed its own action.
            Guid? requestId = TryBrowser(context, now, out var completedView) && completedView!.BindingDigest == Digest(operation)
                ? completedView.RequestId : null;
            var completedTotp = requestId is not null && totpApplications?.SingleOrDefault() is { } completedNative
                ? await ReadTotpRecoveryAsync(context, new(caller, actor, operation, completedView!), completedNative).ConfigureAwait(false) : null;
            return Results.Json(new { status = "COMPLETED", requestId, operationReference = operation.Binding.CorrelationId, csrfToken = csrf, totpRecovery = completedTotp });
        }
        var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        var policy = assurancePolicies.RequireEffective(operation.Binding.Scope, now, operation.ExpectedPolicyVersion);
        StepUpPresentation presentation;
        try { presentation = presentations.RequirePublished(policy); }
        catch (ArgumentException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        var binding = Digest(operation);
        if (!TryBrowser(context, now, out var browser) || browser!.BindingDigest != binding || browser.AuthenticationPolicyVersion != auth.Version)
        {
            var expires = now + auth.BrowserContextLifetime;
            if (policy.Metadata.EffectiveInterval.Until is { } until && until < expires) expires = until;
            browser = new(Guid.NewGuid(), binding, auth.Version, expires);
            context.Response.Cookies.Append(StepUpBrowserEndpoints.CookieName, protector.Protect(JsonSerializer.Serialize(browser)),
                new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = expires - now });
        }
        var native = passkeyApplications.SingleOrDefault();
        var capability = native is null ? new PasskeyCapability(false, null) : await native.ProofCapabilityAsync(caller, policy, context.RequestAborted).ConfigureAwait(false);
        var passkeyRecovery = native is null ? null : await ReadPasskeyRecoveryAsync(context, new(caller, actor, operation, browser!), native).ConfigureAwait(false);
        var hasPassword = await database.Users.AnyAsync(x => x.AccountId == caller.AccountId && x.PasswordHash != null, context.RequestAborted).ConfigureAwait(false);
        var totp = totpApplications?.SingleOrDefault(); var totpAvailable = totp is not null && await totp.ProofCapabilityAsync(caller, policy, context.RequestAborted).ConfigureAwait(false);
        if (totpAvailable) presentation.RequireTotpPublished();
        var totpAuthenticators = totpAvailable ? await totp!.ActiveFactorViewsAsync(caller.AccountId, context.RequestAborted).ConfigureAwait(false) : [];
        var totpRecovery = totp is null ? null : await ReadTotpRecoveryAsync(context, new(caller, actor, operation, browser!), totp).ConfigureAwait(false);
        return Results.Json(new { status = "PENDING", requestId = browser!.RequestId, operationReference = operation.Binding.CorrelationId, csrfToken = csrf, passwordMaximumLength = auth.PasswordMaximumLength,
            passkeyAvailable = capability.Available, maximumCredentialJsonBytes = capability.MaximumCredentialJsonBytes, passkeyRecovery,
            totpAvailable, totpAuthenticators, totpRecovery,
            passwordAvailable = hasPassword && policy.MethodAssurances.TryGetValue("pwd", out var mapped) && mapped == policy.RequiredAssurance,
            presentation.Locale, presentation.Content });
    });

    internal Task<IResult> TotpAsync(HttpContext context) => GuardAsync(context, async () =>
    {
        var native = totpApplications?.SingleOrDefault(); if (native is null) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        if (!context.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var command = await IdentityJsonCommand.ReadAsync<TotpCodeCommand>(context, 4096).ConfigureAwait(false);
        var frame = await CurrentProofOperationAsync(context, command.RequestId).ConfigureAwait(false); if (frame is null) return Problem(409, "VERSION_CONFLICT");
        if (frame.Operation.Completed) return await CompletedTotpAsync(context, frame, native, command.AttemptId, command.AuthenticatorReference).ConfigureAwait(false);
        var issued = await native.IssueOperationProofAsync(frame.Caller, frame.Operation, command, context.RequestAborted).ConfigureAwait(false);
        if (issued.ErrorCode is { } error)
        {
            if (await CompletedAsync(frame.Actor, frame.Browser.BindingDigest, context.RequestAborted).ConfigureAwait(false)) return await CompletedTotpAsync(context, frame, native, command.AttemptId, command.AuthenticatorReference).ConfigureAwait(false);
            return Failure(error);
        }
        SelectTotpProof(context, frame, native, command.AttemptId, command.AuthenticatorReference, issued.Grant!.ExpiresAt);
        return await ExecuteProofAsync(context, frame, issued.Grant!.Key).ConfigureAwait(false);
    });

    internal Task<IResult> PasswordAsync(HttpContext context) => GuardAsync(context, async () =>
    {
        if (!context.Request.HasJsonContentType()) return Problem(415, "VALIDATION_ERROR");
        var caller = Caller(context); var actor = new UserStepUpActor(caller);
        var now = clock.GetUtcNow(); var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        if (!TryBrowser(context, now, out var browser)) return Problem(403, TechnicalProblemCodes.ForbiddenScope);
        var command = await IdentityJsonCommand.ReadAsync<PasswordStepUpCommand>(context, checked((long)auth.PasswordMaximumLength * 6 + 4096)).ConfigureAwait(false);
        if (command.RequestId == Guid.Empty || command.RequestId != browser!.RequestId) return Problem(409, TechnicalProblemCodes.VersionConflict);
        var operation = await owner.ResolveAsync(actor, context.RequestAborted).ConfigureAwait(false);
        if (!MatchesActor(operation, actor) || Digest(operation!) != browser.BindingDigest || browser.AuthenticationPolicyVersion != auth.Version)
            return Problem(409, TechnicalProblemCodes.VersionConflict);
        if (operation!.Completed) return Results.Json(new { status = "COMPLETED", operationReference = operation.Binding.CorrelationId }); // Reconcile, never execute again.
        var issued = await application.IssuePasswordAsync(caller, operation.Binding, operation.ExpectedPolicyVersion, command.Password, context.RequestAborted).ConfigureAwait(false);
        if (issued.ErrorCode is { } issue)
        {
            if (await CompletedAsync(actor, browser.BindingDigest, context.RequestAborted).ConfigureAwait(false)) return Results.Json(new { status = "COMPLETED", operationReference = operation.Binding.CorrelationId });
            return Failure(issue);
        }
        return await ExecuteProofAsync(context, new(caller, actor, operation, browser), issued.Grant!.Key).ConfigureAwait(false);
    });

    private async Task<IResult> ExecuteProofAsync(HttpContext context, StepUpBrowserOperation frame, string proofKey, NativeProofCompletion? nativeCompletion = null)
    {
        var caller = frame.Caller; var actor = frame.Actor; var operation = frame.Operation; var browser = frame.Browser;
        var execution = await application.ExecuteOutcomeAsync(caller, operation.Binding, proofKey, async ct =>
        {
            var current = await owner.ResolveAsync(actor, ct).ConfigureAwait(false);
            if (!MatchesActor(current, actor) || current!.Completed || Digest(current) != browser.BindingDigest) return UserStepUpOperationApplyOutcome.Conflict();
            if (owner is IUserStepUpOperationOutcomeSource typed) return await typed.ApplyWithOutcomeAsync(actor, current, database, ct).ConfigureAwait(false);
            return await owner.ApplyAsync(actor, current, database, ct).ConfigureAwait(false) ? UserStepUpOperationApplyOutcome.SessionContinues() : UserStepUpOperationApplyOutcome.Conflict();
        }, context.RequestAborted).ConfigureAwait(false);
        if (execution.Applied)
        {
            if (owner is IUserStepUpBrowserCompletionSource browserOwner) await browserOwner.CompleteBrowserAsync(operation, execution.SignInRequired, context.RequestAborted).ConfigureAwait(false);
            if (execution.SignInRequired) await context.SignOutAsync(AuthenticationIdentity.Scheme).ConfigureAwait(false);
            return ProofCompleted(operation, execution.SignInRequired, nativeCompletion);
        }
        if (await CompletedAsync(actor, browser.BindingDigest, context.RequestAborted).ConfigureAwait(false))
            return ProofCompleted(operation, false, nativeCompletion);
        return Failure(execution.ErrorCode ?? "STEP_UP_REQUIRED");
    }

    private async Task<bool> CompletedAsync(UserStepUpActor actor, string binding, CancellationToken ct)
    {
        var current = await owner.ResolveAsync(actor, ct).ConfigureAwait(false);
        return MatchesActor(current, actor) && current!.Completed && Digest(current) == binding;
    }
    private StepUpCaller Caller(HttpContext context)
    {
        if (context.Connection.RemoteIpAddress is null) throw new ArgumentException("A current peer is required.");
        return StepUpCaller.FromPrincipal(context.User, surface.Origin, partitions.Network(context.Connection.RemoteIpAddress));
    }
    private bool TryBrowser(HttpContext context, DateTimeOffset now, out Browser? browser)
    {
        browser = null;
        if (!context.Request.Cookies.TryGetValue(StepUpBrowserEndpoints.CookieName, out var value)) return false;
        try { browser = JsonSerializer.Deserialize<Browser>(protector.Unprotect(value)); }
        catch (CryptographicException) { return false; }
        catch (JsonException) { return false; }
        return browser is { RequestId: var id, BindingDigest.Length: 64 } && id != Guid.Empty && browser.ExpiresAt > now;
    }
    private static bool MatchesActor(UserStepUpOperationContext? operation, UserStepUpActor actor) =>
        operation is not null && operation.Actor.AccountId == actor.AccountId && operation.Actor.SessionId == actor.SessionId && operation.Actor.Origin == actor.Origin;
    private static string Digest(UserStepUpOperationContext operation)
    {
        var b = operation.Binding;
        return AuthenticationIdentity.Digest(JsonSerializer.Serialize(new[] { operation.Actor.AccountId, operation.Actor.SessionId.ToString("N"), operation.Actor.Origin,
            b.OperationId, b.CorrelationId, b.Scope.Operation, b.Scope.Context, b.ResourceId, b.ExpectedVersion.ToString(CultureInfo.InvariantCulture), b.ContextDigest, operation.ExpectedPolicyVersion }));
    }
    private static IResult Failure(string code) => Problem(code switch { "AUTHENTICATION_FAILED" => 401, "RATE_OR_ABUSE_LIMITED" => 429,
        "VERSION_CONFLICT" => 409, "STEP_UP_REQUIRED" => 403, _ => 400 }, code);
    private static IResult Problem(int status, string code) => TechnicalProblemDetails.HttpResult(status, "about:blank", "The request could not be completed.",
        new TechnicalProblemMetadata(code, Guid.NewGuid().ToString("N"), "UNSPECIFIED"));
    private static async Task<IResult> GuardAsync(HttpContext context, Func<Task<IResult>> action)
    {
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (context.Request.QueryString.HasValue) return Problem(400, "VALIDATION_ERROR");
        try { return await action().ConfigureAwait(false); }
        catch (AssurancePolicyVersionConflictException) { return Problem(409, TechnicalProblemCodes.VersionConflict); }
        catch (DbUpdateConcurrencyException) { return Problem(409, TechnicalProblemCodes.VersionConflict); }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (BadHttpRequestException e) { return Problem(e.StatusCode == 413 ? 413 : 400, "VALIDATION_ERROR"); }
        catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
    }
}
