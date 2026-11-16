using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Routing;
using Tunner.Identity.Persistence;
using Tunner.Platform.Operations;

namespace Tunner.Admin.Bff;

public static class AdminIdentityConfigurationEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<IdentityConfigurationFamily>(allowIntegerValues: false), new JsonStringEnumConverter<IdentityConfigurationCapabilityTarget>(allowIntegerValues: false) }
    };
    public static void MapTunnerAdminIdentityConfiguration(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if (endpoints.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(AdminIdentityConfigurationAuthorization.Scheme).GetAwaiter().GetResult() is null)
            throw new InvalidOperationException("Identity configuration requires the separate Admin browser foundation.");
        var routes = endpoints.MapGroup("/identity/configuration").RequireAuthorization(policy => policy.AddAuthenticationSchemes(AdminIdentityConfigurationAuthorization.Scheme).RequireAuthenticatedUser());
        routes.MapGet("/workspace", (HttpContext h, IdentityRuntimeConfigurationApplication app, IIdentityConfigurationBrowseBudgetSource budgets) => Guard(h, async (profile, _) =>
        {
            var workspaces = new List<IdentityConfigurationWorkspace>();
            foreach (var scope in profile.Scopes) { var op = Operation(); workspaces.Add(await app.WorkspaceAsync(scope, budgets, op.Id, op.Correlation, h.RequestAborted).ConfigureAwait(false)); }
            var presentation = profile.Presentation is { } binding
                ? await AdminPublishedPresentation.ResolveAsync(h, binding.Scope, binding.VariantReference, binding.EffectiveInterval).ConfigureAwait(false) : null;
            var clock = h.RequestServices.GetRequiredService<TimeProvider>();
            var current = h.RequestServices.GetRequiredService<IAdminIdentityConfigurationHttpProfileSource>().RequireEffective(clock.GetUtcNow()); current.Require(clock.GetUtcNow());
            if (JsonSerializer.Serialize(current) != JsonSerializer.Serialize(profile)) throw new IdentityConfigurationUnavailableException();
            return Result(new { workspaces, presentation, csrfToken = h.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(h).RequestToken });
        }));
        routes.MapPost("/proposals/search", (HttpContext h, IdentityRuntimeConfigurationApplication app, IIdentityConfigurationBrowseBudgetSource budgets) => Guard(h, async (profile, _) =>
        {
            var c = await Read<AdminIdentityConfigurationSearch>(h, profile).ConfigureAwait(false); var scope = Scope(profile, c.Scope); var op = Operation();
            return Result(await app.SearchAsync(new(scope, c.Search, c.PageSize, c.Offset, op.Id, op.Correlation), budgets, h.RequestAborted).ConfigureAwait(false));
        }));
        routes.MapPost("/authentication/draft", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminIdentityConfigurationDraft<AdminAuthenticationConfigurationCandidate>>(h, profile).ConfigureAwait(false); c.RequireTarget(); var candidate = c.Candidate.Value();
            _ = Scope(profile, new AdminIdentityConfigurationScope { Family = IdentityConfigurationFamily.Authentication });
            return await Write(h, app, session, c.IdempotencyKey, c, op => c.ProposalReference is { } id
                ? app.UpdateAuthenticationDraftAsync(id, c.ExpectedRevision!.Value, candidate, op.Id, op.Correlation, h.RequestAborted)
                : app.CreateAuthenticationDraftAsync(candidate, op.Id, op.Correlation, h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/assurance/draft", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminIdentityConfigurationDraft<AdminAssuranceConfigurationCandidate>>(h, profile).ConfigureAwait(false); c.RequireTarget(); var candidate = c.Candidate.Value();
            _ = Scope(profile, AdminIdentityConfigurationScope.From(new(IdentityConfigurationFamily.Assurance, candidate.Metadata.Scope)));
            return await Write(h, app, session, c.IdempotencyKey, c, op => c.ProposalReference is { } id
                ? app.UpdateAssuranceDraftAsync(id, c.ExpectedRevision!.Value, candidate, op.Id, op.Correlation, h.RequestAborted)
                : app.CreateAssuranceDraftAsync(candidate, op.Id, op.Correlation, h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/registration/draft", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminIdentityConfigurationDraft<AdminRegistrationConfigurationCandidate>>(h, profile).ConfigureAwait(false); c.RequireTarget(); var candidate = c.Candidate.Value();
            _ = Scope(profile, new AdminIdentityConfigurationScope { Family = IdentityConfigurationFamily.Registration });
            return await Write(h, app, session, c.IdempotencyKey, c, op => c.ProposalReference is { } id
                ? app.UpdateRegistrationDraftAsync(id, c.ExpectedRevision!.Value, candidate, op.Id, op.Correlation, h.RequestAborted)
                : app.CreateRegistrationDraftAsync(candidate, op.Id, op.Correlation, h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/registration-content/draft", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminIdentityConfigurationDraft<AdminRegistrationContentBindingCandidate>>(h, profile).ConfigureAwait(false); c.RequireTarget(); var candidate = c.Candidate.Value();
            _ = Scope(profile, new AdminIdentityConfigurationScope { Family = IdentityConfigurationFamily.RegistrationContent, RegistrationPolicyVersion = candidate.RegistrationPolicyVersion });
            return await Write(h, app, session, c.IdempotencyKey, c, op => c.ProposalReference is { } id
                ? app.UpdateRegistrationContentDraftAsync(id, c.ExpectedRevision!.Value, candidate, op.Id, op.Correlation, h.RequestAborted)
                : app.CreateRegistrationContentDraftAsync(candidate, op.Id, op.Correlation, h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/session-listing/draft", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminIdentityConfigurationDraft<AdminSessionListingConfigurationCandidate>>(h, profile).ConfigureAwait(false); c.RequireTarget(); var candidate = c.Candidate.Value();
            _ = Scope(profile, new AdminIdentityConfigurationScope { Family = IdentityConfigurationFamily.SessionListing });
            return await Write(h, app, session, c.IdempotencyKey, c, op => c.ProposalReference is { } id
                ? app.UpdateSessionListingDraftAsync(id, c.ExpectedRevision!.Value, candidate, op.Id, op.Correlation, h.RequestAborted)
                : app.CreateSessionListingDraftAsync(candidate, op.Id, op.Correlation, h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/recovery-codes/draft", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminIdentityConfigurationDraft<AdminRecoveryCodeConfigurationCandidate>>(h, profile).ConfigureAwait(false); c.RequireTarget(); var candidate = c.Candidate.Value();
            _ = Scope(profile, new AdminIdentityConfigurationScope { Family = IdentityConfigurationFamily.RecoveryCodes });
            return await Write(h, app, session, c.IdempotencyKey, c, op => c.ProposalReference is { } id
                ? app.UpdateRecoveryCodeDraftAsync(id, c.ExpectedRevision!.Value, candidate, op.Id, op.Correlation, h.RequestAborted)
                : app.CreateRecoveryCodeDraftAsync(candidate, op.Id, op.Correlation, h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/step-up-content/draft", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminIdentityConfigurationDraft<AdminStepUpContentBindingCandidate>>(h, profile).ConfigureAwait(false); c.RequireTarget(); var candidate = c.Candidate.Value();
            _ = Scope(profile, new AdminIdentityConfigurationScope { Family = IdentityConfigurationFamily.StepUpContent,
                AssuranceScope = c.Candidate.AssuranceScope, AssurancePolicyVersion = candidate.AssurancePolicyVersion });
            return await Write(h, app, session, c.IdempotencyKey, c, op => c.ProposalReference is { } id
                ? app.UpdateStepUpContentDraftAsync(id, c.ExpectedRevision!.Value, candidate, op.Id, op.Correlation, h.RequestAborted)
                : app.CreateStepUpContentDraftAsync(candidate, op.Id, op.Correlation, h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/totp-content/draft", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminIdentityConfigurationDraft<AdminTotpContentBindingCandidate>>(h, profile).ConfigureAwait(false); c.RequireTarget(); var candidate = c.Candidate.Value();
            _ = Scope(profile, new AdminIdentityConfigurationScope { Family = IdentityConfigurationFamily.TotpContent, TotpPolicyVersion = candidate.TotpPolicyVersion });
            return await Write(h, app, session, c.IdempotencyKey, c, op => c.ProposalReference is { } id
                ? app.UpdateTotpContentDraftAsync(id, c.ExpectedRevision!.Value, candidate, op.Id, op.Correlation, h.RequestAborted)
                : app.CreateTotpContentDraftAsync(candidate, op.Id, op.Correlation, h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/passkey-content/draft", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminIdentityConfigurationDraft<AdminPasskeyContentBindingCandidate>>(h, profile).ConfigureAwait(false); c.RequireTarget(); var candidate = c.Candidate.Value();
            _ = Scope(profile, new AdminIdentityConfigurationScope { Family = IdentityConfigurationFamily.PasskeyContent, PasskeyPolicyVersion = candidate.PasskeyPolicyVersion });
            return await Write(h, app, session, c.IdempotencyKey, c, op => c.ProposalReference is { } id
                ? app.UpdatePasskeyContentDraftAsync(id, c.ExpectedRevision!.Value, candidate, op.Id, op.Correlation, h.RequestAborted)
                : app.CreatePasskeyContentDraftAsync(candidate, op.Id, op.Correlation, h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/totp/draft", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminIdentityConfigurationDraft<AdminTotpConfigurationCandidate>>(h, profile).ConfigureAwait(false); c.RequireTarget(); var candidate = c.Candidate.Value();
            _ = Scope(profile, new AdminIdentityConfigurationScope { Family = IdentityConfigurationFamily.Totp });
            return await Write(h, app, session, c.IdempotencyKey, c, op => c.ProposalReference is { } id
                ? app.UpdateTotpDraftAsync(id, c.ExpectedRevision!.Value, candidate, op.Id, op.Correlation, h.RequestAborted)
                : app.CreateTotpDraftAsync(candidate, op.Id, op.Correlation, h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/passkey/draft", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminIdentityConfigurationDraft<AdminPasskeyConfigurationCandidate>>(h, profile).ConfigureAwait(false); c.RequireTarget(); var candidate = c.Candidate.Value();
            _ = Scope(profile, new AdminIdentityConfigurationScope { Family = IdentityConfigurationFamily.Passkey });
            return await Write(h, app, session, c.IdempotencyKey, c, op => c.ProposalReference is { } id
                ? app.UpdatePasskeyDraftAsync(id, c.ExpectedRevision!.Value, candidate, op.Id, op.Correlation, h.RequestAborted)
                : app.CreatePasskeyDraftAsync(candidate, op.Id, op.Correlation, h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/content-binding/draft", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminIdentityConfigurationDraft<AdminAuthenticationContentBindingCandidate>>(h, profile).ConfigureAwait(false); c.RequireTarget(); var candidate = c.Candidate.Value();
            _ = Scope(profile, new AdminIdentityConfigurationScope { Family = IdentityConfigurationFamily.AuthenticationContent, AuthenticationPolicyVersion = candidate.AuthenticationPolicyVersion });
            return await Write(h, app, session, c.IdempotencyKey, c, op => c.ProposalReference is { } id
                ? app.UpdateContentBindingDraftAsync(id, c.ExpectedRevision!.Value, candidate, op.Id, op.Correlation, h.RequestAborted)
                : app.CreateContentBindingDraftAsync(candidate, op.Id, op.Correlation, h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/proposals/validate", (HttpContext h, IdentityRuntimeConfigurationApplication app) => ProposalWrite(h, app, false));
        routes.MapPost("/proposals/approve", (HttpContext h, IdentityRuntimeConfigurationApplication app) => ProposalWrite(h, app, true));
        routes.MapPost("/proposals/preview", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, _) =>
        {
            var c = await Read<AdminIdentityConfigurationProposalQuery>(h, profile).ConfigureAwait(false); var op = Operation();
            return Result(await app.PreviewAsync(new(c.ProposalReference, c.ExpectedRevision, op.Id, op.Correlation), h.RequestAborted).ConfigureAwait(false));
        }));
        routes.MapPost("/proposals/publish", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminPublishIdentityConfiguration>(h, profile).ConfigureAwait(false);
            return await Write(h, app, session, c.IdempotencyKey, c, op => app.PublishAsync(new(c.ProposalReference, c.ExpectedRevision,
                (c.EffectiveInterval ?? throw new ArgumentException("An interval is required.")).Value(), c.SupersedesPublicationReference, op.Id, op.Correlation), h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/history", (HttpContext h, IdentityRuntimeConfigurationApplication app, IIdentityConfigurationBrowseBudgetSource budgets) => Guard(h, async (profile, _) =>
        {
            var c = await Read<AdminIdentityConfigurationHistoryQuery>(h, profile).ConfigureAwait(false); var scope = Scope(profile, c.Scope); var op = Operation();
            return Result(await app.BrowseHistoryAsync(new(scope, c.PageSize, c.Offset, op.Id, op.Correlation), budgets, h.RequestAborted).ConfigureAwait(false));
        }));
        routes.MapPost("/history/preview", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, _) =>
        {
            var c = await Read<AdminIdentityConfigurationHistoryPreviewQuery>(h, profile).ConfigureAwait(false); var scope = Scope(profile, c.Scope); var op = Operation();
            return Result(await app.HistoryPreviewAsync(scope, c.PublicationReference, op.Id, op.Correlation, h.RequestAborted).ConfigureAwait(false));
        }));
        routes.MapPost("/rollback", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminRollbackIdentityConfiguration>(h, profile).ConfigureAwait(false);
            return await Write(h, app, session, c.IdempotencyKey, c, op => app.RollbackAsync(new(c.ApprovedVersionReference, c.SupersedesPublicationReference,
                (c.EffectiveInterval ?? throw new ArgumentException("An interval is required.")).Value(), op.Id, op.Correlation), h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/capabilities", (HttpContext h, IdentityRuntimeConfigurationApplication app) => Guard(h, async (profile, _) =>
        {
            var c = await Read<AdminIdentityConfigurationCapabilityQuery>(h, profile).ConfigureAwait(false); var scope = Scope(profile, c.Scope); var op = Operation();
            return Result(await app.CapabilitiesAsync(new(scope, c.Target, c.ProposalReference, c.ExpectedRevision, c.PublicationReference, op.Id, op.Correlation), h.RequestAborted).ConfigureAwait(false));
        }));
    }
    private static Task<IResult> ProposalWrite(HttpContext h, IdentityRuntimeConfigurationApplication app, bool approve) => Guard(h, async (profile, session) =>
    {
        var c = await Read<AdminIdentityConfigurationWrite>(h, profile).ConfigureAwait(false);
        return await Write(h, app, session, c.IdempotencyKey, c, op => approve
            ? app.ApproveAsync(new(c.ProposalReference, c.ExpectedRevision, op.Id, op.Correlation), h.RequestAborted)
            : app.ValidateAsync(new(c.ProposalReference, c.ExpectedRevision, op.Id, op.Correlation), h.RequestAborted)).ConfigureAwait(false);
    });
    private static async Task<IResult> Write<T>(HttpContext h, IdentityRuntimeConfigurationApplication app, AdminIdentityConfigurationSession session,
        Guid key, object command, Func<(string Id, string Correlation), Task<T>> run)
    {
        if (key == Guid.Empty) throw new ArgumentException("A caller idempotency key is required.");
        var binding = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { session.ActorReference, session.SessionReference, key }, Json)));
        var op = (Id: "OP-IDENTITY-CONFIGURATION-" + binding, Correlation: key.ToString("N"));
        var intent = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { path = h.Request.Path.Value, command }, Json)));
        return Result(await app.ExecuteCommandAsync(op.Id, intent, () => run(op), h.RequestAborted).ConfigureAwait(false));
    }
    private static (string Id, string Correlation) Operation() { var id = Guid.NewGuid().ToString("N"); return ("OP-IDENTITY-CONFIGURATION-" + id, id); }
    private static IdentityConfigurationScope Scope(AdminIdentityConfigurationHttpProfile profile, AdminIdentityConfigurationScope? wire)
    {
        var scope = (wire ?? throw new ArgumentException("A scope is required.")).Value();
        if (!profile.Scopes.Contains(scope)) throw new IdentityConfigurationForbiddenException(); return scope;
    }
    private static IResult Result<T>(T value) => Results.Json(value, Json);
    private static async Task<T> Read<T>(HttpContext h, AdminIdentityConfigurationHttpProfile profile)
    {
        if (!h.Request.HasJsonContentType()) throw new BadHttpRequestException("JSON is required.", 415);
        if (h.Request.ContentLength > profile.MaximumRequestBytes) throw new BadHttpRequestException("The request exceeds its qualified limit.", 413);
        using var body = new MemoryStream(); var buffer = new byte[Math.Min(profile.MaximumRequestBytes, 8192)];
        while (true)
        {
            var read = await h.Request.Body.ReadAsync(buffer, h.RequestAborted).ConfigureAwait(false); if (read == 0) break;
            if (body.Length + read > profile.MaximumRequestBytes) throw new BadHttpRequestException("The request exceeds its qualified limit.", 413);
            body.Write(buffer, 0, read);
        }
        using var json = JsonDocument.Parse(body.ToArray()); if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException(); NoDuplicates(json.RootElement);
        return json.RootElement.Deserialize<T>(Json) ?? throw new JsonException();
    }
    private static void NoDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject()) { if (!names.Add(property.Name)) throw new JsonException(); NoDuplicates(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) NoDuplicates(item);
    }
    private static async Task<IResult> Guard(HttpContext h, Func<AdminIdentityConfigurationHttpProfile, AdminIdentityConfigurationSession, Task<IResult>> run)
    {
        try
        {
            if (h.Request.QueryString.HasValue) throw new ArgumentException("Unexpected query parameters.");
            var clock = h.RequestServices.GetRequiredService<TimeProvider>();
            var profile = h.RequestServices.GetRequiredService<IAdminIdentityConfigurationHttpProfileSource>().RequireEffective(clock.GetUtcNow()); profile.Require(clock.GetUtcNow());
            h.Items[AdminIdentityConfigurationAuthorization.ProfileItem] = JsonSerializer.Serialize(profile);
            var session = await AdminIdentityConfigurationAuthorization.CurrentSession(h, h.RequestServices.GetRequiredService<IAdminIdentityConfigurationSessionSource>(), clock, h.RequestAborted).ConfigureAwait(false);
            return await run(profile, session).ConfigureAwait(false);
        }
        catch (IdentityConfigurationForbiddenException) { return Problem(403, "FORBIDDEN_SCOPE"); }
        catch (IdentityConfigurationConflictException) { return Problem(409, "VERSION_CONFLICT"); }
        catch (BadHttpRequestException error) { return Problem(error.StatusCode, "VALIDATION_ERROR"); }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (Exception error) when (error is InvalidOperationException or DbException or TimeoutException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
    }
    private static IResult Problem(int status, string code) => TechnicalProblemDetails.HttpResult(status, "about:blank", "The request could not be completed.",
        new TechnicalProblemMetadata(code, Guid.NewGuid().ToString("N"), "UNSPECIFIED"));
}
