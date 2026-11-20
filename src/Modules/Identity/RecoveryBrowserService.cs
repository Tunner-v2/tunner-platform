using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace Tunner.Identity.Persistence;

/// <summary>Anonymous private User BFF facade. Protected original browser facts never come from JSON owner selectors.</summary>
public sealed class RecoveryBrowserService
{
    private const string PointerCookie = "__Host-Tunner.Recovery.Original";
    private readonly RecoveryCaseApplication application;
    private readonly IRecoveryPasswordPolicySource policies;
    private readonly IRecoveryPasswordPresentationSource presentations;
    private readonly IAuthenticationPolicySource authenticationPolicies;
    private readonly IDataProtectionProvider protection;
    private readonly AuthenticationPartitionKey partitions;
    private readonly string origin;
    private readonly TimeProvider clock;
    internal RecoveryBrowserService(RecoveryCaseApplication application, IRecoveryPasswordPolicySource policies,
        IRecoveryPasswordPresentationSource presentations, IAuthenticationPolicySource authenticationPolicies, IDataProtectionProvider protection,
        AuthenticationPartitionKey partitions, string origin, TimeProvider clock)
    {
        this.application = application; this.policies = policies; this.presentations = presentations; this.authenticationPolicies = authenticationPolicies;
        this.protection = protection; this.partitions = partitions; this.origin = origin; this.clock = clock;
    }
    private sealed record Pointer(Guid RequestId, string BrowserDigest, string PreviousCookieDigest, string Origin, string PolicyVersion, DateTimeOffset ExpiresAt);
    internal async ValueTask<bool> AvailableAsync(CancellationToken cancellationToken)
    {
        try { _ = await CurrentAsync(cancellationToken).ConfigureAwait(false); return application.PasswordAvailable; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.Data.Common.DbException or HttpRequestException or TimeoutException) { return false; }
    }
    private async ValueTask<(RecoveryPasswordPolicyVersion Policy, RecoveryPasswordPresentation Publication)> CurrentAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow(); var policy = policies.RequireEffective(now); policy.Require(origin, now);
        if (presentations is IRecoveryPasswordAsyncPresentationSource asynchronous)
        {
            var publication = await asynchronous.RequirePublishedAsync(policy.Values, now, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Current Recovery publication is unavailable.");
            var current = policies.RequireEffective(clock.GetUtcNow()); current.Require(origin, clock.GetUtcNow());
            if (current.Snapshot != policy.Snapshot) throw new InvalidOperationException("Recovery policy changed during publication preparation.");
            publication.Require(current.Values, clock.GetUtcNow()); return (current, publication);
        }
        var legacy = presentations.RequirePublished(policy.Values, now); legacy.Require(policy.Values, now); return (policy, legacy);
    }
    private RecoveryEntryCaller Caller(HttpContext context)
    {
        var now = clock.GetUtcNow(); var auth = authenticationPolicies.RequireEffective(now); auth.Validate();
        if (!AuthenticationEndpoints.TryBrowser(context, AuthenticationEndpoints.Protector(protection), now, out var browser) || browser!.PolicyVersion != auth.Version ||
            browser.PreviousCookieDigest != AuthenticationEndpoints.CookieDigest(context) || context.Connection.RemoteIpAddress is null)
            throw new RecoveryBrowserContextException();
        return new(AuthenticationIdentity.Digest(browser.Nonce), browser.PreviousCookieDigest, origin, partitions.Network(context.Connection.RemoteIpAddress));
    }
    internal async Task<IResult> OptionsAsync(HttpContext context)
    {
        try
        {
            if (context.Request.QueryString.HasValue) return Problem(400, "VALIDATION_ERROR");
            var (policy, publication) = await CurrentAsync(context.RequestAborted).ConfigureAwait(false); var caller = Caller(context); OperationView? operation = null;
            var selected = Selected(context, caller, policy.Values.Version);
            if (selected is not null)
            {
                var result = await application.ReadPasswordEntryAsync(caller, selected.RequestId, context.RequestAborted).ConfigureAwait(false);
                if (result.ErrorCode is null && result.Entry is not null) operation = View(result, publication);
            }
            return Results.Json(new OptionsView(true, policy.Values.Version, publication.Version, publication.Locale, publication.Content,
                [new(policy.Values.GoalReference, publication.GoalLabelKey, publication.GoalDescriptionKey)],
                new(true, policy.Values.PasswordMinimumLength, policy.Values.PasswordMaximumLength, policy.Values.SessionEffect.ToString(), publication.EffectLabelKey, publication.EffectValueKey), operation,
                policy.Values.PasskeyRecoveryProfile is null ? null : application.PasswordPasskeyCapability(origin, clock.GetUtcNow()),
                policy.Values.ContactRecoveryProfile is { } contact ? new ContactCapability(application.ContactAvailable(origin, clock.GetUtcNow()), contact.ProofDigits) : null,
                policy.Values.GovernedRecoveryProfile is null ? null : new GovernedCapability(application.GovernedAvailable(origin, clock.GetUtcNow())),
                new CodeCapability(application.PasswordCodeAvailable)));
        }
        catch (RecoveryBrowserContextException) { return Problem(403, "BROWSER_CONTEXT_REQUIRED"); }
        catch (InvalidOperationException) { return Problem(503, "RECOVERY_UNAVAILABLE"); }
        catch (ArgumentException) { return Problem(503, "RECOVERY_UNAVAILABLE"); }
    }
    internal async Task<IResult> CommandAsync(HttpContext context, string kind)
    {
        try
        {
            var (policy, publication) = await CurrentAsync(context.RequestAborted).ConfigureAwait(false); var caller = Caller(context);
            if (context.Request.QueryString.HasValue || !context.Request.HasJsonContentType()) return Problem(400, "VALIDATION_ERROR");
            RecoveryEntryResult result;
            if (kind == "start")
            {
                var command = await IdentityJsonCommand.ReadAsync<RecoveryEntryStartCommand>(context, 4096).ConfigureAwait(false);
                result = await application.StartPasswordEntryAsync(caller, command.RequestId, command.Email, command.GoalReference, context.RequestAborted).ConfigureAwait(false);
            }
            else if (kind == "review/repair")
            {
                if (!application.GovernedAvailable(origin, clock.GetUtcNow())) return Problem(503, "RECOVERY_UNAVAILABLE");
                var command = await IdentityJsonCommand.ReadAsync<RecoveryPasswordGovernedCommand>(context,
                    checked((long)policy.Values.PasswordMaximumLength * 12 + 4096)).ConfigureAwait(false);
                if (Selected(context, caller, policy.Values.Version)?.RequestId != command.RequestId) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
                try { result = await application.RepairPasswordWithGovernedDispositionAsync(caller, command.RequestId, command.ExpectedVersion,
                    command.Password, command.PasswordConfirmation, context.RequestAborted).ConfigureAwait(false); }
                catch (InvalidOperationException) { return Problem(400, "RECOVERY_PROOF_REJECTED"); }
            }
            else if (kind == "contact/options")
            {
                var command = await IdentityJsonCommand.ReadAsync<RecoveryEntryCommand>(context, 4096).ConfigureAwait(false);
                if (Selected(context, caller, policy.Values.Version)?.RequestId != command.RequestId) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
                var options = await application.StartPasswordContactAsync(caller, command.RequestId, context.RequestAborted).ConfigureAwait(false);
                if (options.ErrorCode is { } error) return Failure(error);
                if (options.Entry is not { } contactEntry || options.Presentation is not { } presentation) return Problem(400, "RECOVERY_PROOF_REJECTED");
                if ((await CurrentAsync(context.RequestAborted).ConfigureAwait(false)).Policy.Snapshot != policy.Snapshot) return Problem(503, "RECOVERY_UNAVAILABLE");
                Select(context, caller, contactEntry);
                return Results.Json(new { requestId = contactEntry.RequestId, challengeId = presentation.ChallengeId, expiresAt = presentation.ExpiresAt });
            }
            else if (kind == "contact/repair")
            {
                if (!application.ContactAvailable(origin, clock.GetUtcNow())) return Problem(503, "RECOVERY_UNAVAILABLE");
                var command = await IdentityJsonCommand.ReadAsync<RecoveryPasswordContactRepairCommand>(context,
                    checked((long)policy.Values.PasswordMaximumLength * 12 + 4096)).ConfigureAwait(false);
                if (Selected(context, caller, policy.Values.Version)?.RequestId != command.RequestId) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
                try
                {
                    result = await application.RepairPasswordWithContactAsync(caller, command.RequestId, command.ExpectedVersion, command.ChallengeId,
                        command.Code, command.Password, command.PasswordConfirmation, context.RequestAborted).ConfigureAwait(false);
                }
                catch (Exception error) when (error is InvalidOperationException or CryptographicException or FormatException)
                {
                    return Problem(400, "RECOVERY_PROOF_REJECTED");
                }
            }
            else if (kind == "passkey/options")
            {
                var command = await IdentityJsonCommand.ReadAsync<RecoveryEntryCommand>(context, 4096).ConfigureAwait(false);
                var selected = Selected(context, caller, policy.Values.Version);
                if (selected?.RequestId != command.RequestId) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
                if (!application.PasswordPasskeyCapability(origin, clock.GetUtcNow()).Available) return Problem(503, "RECOVERY_UNAVAILABLE");
                var options = await application.StartPasswordPasskeyAsync(caller, command.RequestId, context, context.RequestAborted).ConfigureAwait(false);
                if (options.ErrorCode is { } error) return Failure(error);
                if (options.Entry is not { } optionsEntry || options.Presentation is not { } presentation) return Problem(400, "RECOVERY_PROOF_REJECTED");
                if ((await CurrentAsync(context.RequestAborted).ConfigureAwait(false)).Policy.Snapshot != policy.Snapshot) return Problem(503, "RECOVERY_UNAVAILABLE");
                Select(context, caller, optionsEntry);
                return Results.Json(new PasskeyOptionsView(optionsEntry.RequestId, presentation.CeremonyId,
                    JsonSerializer.Deserialize<JsonElement>(presentation.OptionsJson), presentation.ExpiresAt));
            }
            else if (kind == "passkey/repair")
            {
                var capability = application.PasswordPasskeyCapability(origin, clock.GetUtcNow());
                if (!capability.Available) return Problem(503, "RECOVERY_UNAVAILABLE");
                var command = await IdentityJsonCommand.ReadAsync<RecoveryPasswordPasskeyRepairCommand>(context,
                    checked((long)policy.Values.PasswordMaximumLength * 12 + capability.MaximumCredentialJsonBytes!.Value + 4096)).ConfigureAwait(false);
                var selected = Selected(context, caller, policy.Values.Version);
                if (selected?.RequestId != command.RequestId) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
                result = await application.RepairPasswordWithPasskeyAsync(caller, command.RequestId, command.ExpectedVersion, command.CeremonyId,
                    command.Credential, command.Password, command.PasswordConfirmation, context, context.RequestAborted).ConfigureAwait(false);
            }
            else if (kind == "repair")
            {
                var command = await IdentityJsonCommand.ReadAsync<RecoveryPasswordRepairCommand>(context, checked((long)policy.Values.PasswordMaximumLength * 12 + 4096)).ConfigureAwait(false);
                var selected = Selected(context, caller, policy.Values.Version);
                if (selected?.RequestId != command.RequestId) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
                result = await application.RepairPasswordAsync(caller, command.RequestId, command.ExpectedVersion, command.Code,
                    command.Password, command.PasswordConfirmation, context.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                var command = await IdentityJsonCommand.ReadAsync<RecoveryEntryCommand>(context, 4096).ConfigureAwait(false);
                // Exact request+actual browser can restore a lost response selector. Never look up a latest operation.
                result = kind == "cancel" ? await application.CancelPasswordEntryAsync(caller, command.RequestId, context.RequestAborted).ConfigureAwait(false)
                    : await application.ReadPasswordEntryAsync(caller, command.RequestId, context.RequestAborted).ConfigureAwait(false);
            }
            if (result.ErrorCode is { } failure) return Failure(failure);
            if (result.Entry is not { } entry) return Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
            Select(context, caller, entry);
            return Results.Json(new { operation = View(result, publication) });
        }
        catch (RecoveryBrowserContextException) { return Problem(403, "BROWSER_CONTEXT_REQUIRED"); }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (BadHttpRequestException error) { return Problem(error.StatusCode == 413 ? 413 : 400, "VALIDATION_ERROR"); }
        catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (InvalidOperationException) { return Problem(503, "RECOVERY_UNAVAILABLE"); }
        catch (CryptographicException) { return Problem(503, "RECOVERY_UNAVAILABLE"); }
    }
    internal async Task<bool> ScreenAllowedAsync(HttpContext context)
    {
        if (!await AvailableAsync(context.RequestAborted).ConfigureAwait(false) || context.Request.QueryString.HasValue) return false;
        if (context.Request.Path == "/recovery") return true;
        try { var (policy, _) = await CurrentAsync(context.RequestAborted).ConfigureAwait(false); return Selected(context, Caller(context), policy.Values.Version) is not null; }
        catch (RecoveryBrowserContextException) { return false; } catch (InvalidOperationException) { return false; }
    }
    private IDataProtector PointerProtector() => protection.CreateProtector("Tunner.User.Recovery.Original.v1");
    private void Select(HttpContext context, RecoveryEntryCaller caller, RecoveryEntryRow entry)
    {
        var now = clock.GetUtcNow();
        var pointer = new Pointer(entry.RequestId, caller.BrowserDigest, caller.PreviousCookieDigest, origin, entry.PolicyVersion, entry.RecoveryExpiresAt);
        context.Response.Cookies.Append(PointerCookie, PointerProtector().Protect(JsonSerializer.Serialize(pointer)), new CookieOptions
            { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, IsEssential = true, MaxAge = entry.RecoveryExpiresAt - now });
    }
    private Pointer? Selected(HttpContext context, RecoveryEntryCaller caller, string version)
    {
        if (!context.Request.Cookies.TryGetValue(PointerCookie, out var cookie)) return null;
        Pointer? selected;
        try { selected = JsonSerializer.Deserialize<Pointer>(PointerProtector().Unprotect(cookie)); }
        catch (CryptographicException) { return null; } catch (JsonException) { return null; }
        return selected is not null && selected.RequestId != Guid.Empty && selected.BrowserDigest == caller.BrowserDigest &&
            selected.PreviousCookieDigest == caller.PreviousCookieDigest && selected.Origin == caller.Origin && selected.PolicyVersion == version &&
            selected.ExpiresAt > clock.GetUtcNow() ? selected : null;
    }
    private OperationView View(RecoveryEntryResult result, RecoveryPasswordPresentation publication)
    {
        var entry = result.Entry!; var open = entry.State == "OPEN" && clock.GetUtcNow() < entry.ExpiresAt;
        var state = entry.State == "OPEN" ? open ? "AWAITING_PROOF" : "EXPIRED" : entry.State == "REPAIRED" ? "PASSWORD_REPAIRED_NO_SESSION" : "CANCELLED";
        ResultView? completed = result.Result is { Outcome: "PASSWORD_REPAIRED_NO_SESSION" } ? new("PASSWORD_REPAIRED_NO_SESSION",
            [new("TUNNER_SESSION_INVALIDATION", publication.EffectLabelKey, publication.EffectValueKey)], publication.FollowUps) : null;
        return new(entry.RequestId, state, entry.Version, entry.GoalReference, open, open, "ATOMIC_REPAIR_ONLY", false, completed);
    }
    private static IResult Problem(int status, string code) => Results.Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["error_code"] = code });
    private static IResult Failure(string error) => Problem(error switch { "NOT_FOUND_OR_NOT_DISCLOSABLE" => 404, "RATE_OR_ABUSE_LIMITED" => 429,
        "VERSION_CONFLICT" => 409, "RECOVERY_UNAVAILABLE" => 503, _ => 400 }, error);
    private sealed class RecoveryBrowserContextException : Exception { }
    private sealed record GoalView(string Reference, string LabelKey, string DescriptionKey);
    private sealed record RepairView(bool Available, int PasswordMinimumLength, int PasswordMaximumLength, string SessionEffect, string EffectLabelKey, string EffectValueKey);
    private sealed record EffectView(string Kind, string LabelKey, string ValueKey);
    private sealed record ResultView(string Outcome, IReadOnlyList<EffectView> Effects, IReadOnlyList<RecoveryPasswordFollowUp> FollowUps);
    private sealed record OperationView(Guid RequestId, string Status, long Version, string GoalReference, bool CanSubmitRepair, bool CanCancel,
        string CodeSubmission, bool Authenticated, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ResultView? Result);
    private sealed record OptionsView(bool Available, string ProfileVersion, string PublicationVersion, string Locale,
        IReadOnlyDictionary<string, RegistrationPublishedText> Content, IReadOnlyList<GoalView> Goals, RepairView Repair,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] OperationView? Operation,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RecoveryPasskeyCapability? Passkey,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ContactCapability? Contact,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] GovernedCapability? GovernedReview, CodeCapability Code);
    private sealed record CodeCapability(bool Available);
    private sealed record GovernedCapability(bool Available);
    private sealed record ContactCapability(bool Available, int ProofDigits);
    private sealed record PasskeyOptionsView(Guid RequestId, Guid CeremonyId, JsonElement PublicKey, DateTimeOffset ExpiresAt);
}
