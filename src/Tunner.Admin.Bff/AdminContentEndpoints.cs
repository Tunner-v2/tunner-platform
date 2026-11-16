using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Routing;
using Tunner.Platform.Operations;
using Tunner.PresentationContent;

namespace Tunner.Admin.Bff;

/// <summary>Typed Admin-only HTTP operations. No screen, workforce grant, browser actor or raw policy editor is exposed.</summary>
public static class AdminContentEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapTunnerAdminContent(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if (endpoints.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>().GetSchemeAsync(AdminContentAuthorization.Scheme).GetAwaiter().GetResult() is null)
            throw new InvalidOperationException("Content endpoints require the separate Admin browser foundation.");
        var routes = endpoints.MapGroup("/content").RequireAuthorization(policy => policy.AddAuthenticationSchemes(AdminContentAuthorization.Scheme).RequireAuthenticatedUser());
        routes.MapGet("/workspace", (HttpContext h, PresentationContentApplication app, IContentBrowseBudgetSource budgets) => Guard(h, async (profile, _) =>
        {
            var workspaces = new List<ContentWorkspaceInfo>();
            foreach (var scope in profile.Scopes)
            { var op = Operation(); workspaces.Add(await app.WorkspaceAsync(scope, budgets, op.Id, op.Correlation, h.RequestAborted).ConfigureAwait(false)); }
            var presentation = await Presentation(h, profile).ConfigureAwait(false);
            return Results.Json(new { workspaces, presentation, csrfToken = h.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(h).RequestToken });
        }));
        routes.MapPost("/capabilities", (HttpContext h, PresentationContentApplication app) => Guard(h, async (profile, _) =>
        {
            var c = await Read<AdminContentCapabilityQuery>(h, profile).ConfigureAwait(false); Scope(profile, c.Scope); var op = Operation();
            if (!Enum.TryParse<ContentCapabilityTarget>(c.Target, out var target) || !Enum.IsDefined(target) || target.ToString() != c.Target)
                throw new ArgumentException("An exact content capability target is required.");
            return Results.Json(await app.CapabilitiesAsync(new(c.Scope, target, c.Key, c.VariantReference, c.ProposalReference,
                c.ExpectedRevision, c.PublicationReference, op.Id, op.Correlation), h.RequestAborted).ConfigureAwait(false));
        }));
        routes.MapPost("/definitions", (HttpContext h, PresentationContentApplication app, IContentBrowseBudgetSource budgets) => Guard(h, async (profile, _) =>
        {
            var c = await Read<AdminContentDefinitionQuery>(h, profile).ConfigureAwait(false); Scope(profile, c.Scope); var op = Operation();
            return Results.Json(await app.DefinitionsAsync(new(c.Scope, c.Search, c.PageSize, c.Offset, op.Id, op.Correlation), budgets, h.RequestAborted).ConfigureAwait(false));
        }));
        routes.MapPost("/proposals/search", (HttpContext h, PresentationContentApplication app, IContentBrowseBudgetSource budgets) => Guard(h, async (profile, _) =>
        {
            var c = await Read<AdminContentProposalQuery>(h, profile).ConfigureAwait(false); Scope(profile, c.Scope); var op = Operation();
            return Results.Json(await app.SearchProposalsAsync(new(c.Scope, c.Key, c.VariantReference, c.Search, c.PageSize, c.Offset, op.Id, op.Correlation), budgets, h.RequestAborted).ConfigureAwait(false));
        }));
        routes.MapPost("/proposals", (HttpContext h, PresentationContentApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminCreateContentDraft>(h, profile).ConfigureAwait(false); Scope(profile, c.Scope); Required(c.Key); Required(c.VariantReference);
            return await Write(h, app, session, c.IdempotencyKey, c, op => app.CreateDraftAsync(new(c.Scope, c.Key, c.VariantReference, c.Text, op.Id, op.Correlation) { ChangeReason = c.ChangeReason }, h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/proposals/update", (HttpContext h, PresentationContentApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminChangeContentDraft>(h, profile).ConfigureAwait(false);
            return await Write(h, app, session, c.IdempotencyKey, c, op => app.UpdateDraftAsync(new(c.ProposalReference, c.ExpectedRevision, c.Text, op.Id, op.Correlation) { ChangeReason = c.ChangeReason }, h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/proposals/validate", (HttpContext h, PresentationContentApplication app) => ProposalWrite(h, app, false));
        routes.MapPost("/proposals/approve", (HttpContext h, PresentationContentApplication app) => ProposalWrite(h, app, true));
        routes.MapPost("/proposals/preview", (HttpContext h, PresentationContentApplication app) => Guard(h, async (profile, _) =>
        {
            var c = await Read<AdminContentProposalCommand>(h, profile).ConfigureAwait(false); var op = Operation();
            return Results.Json(await app.PreviewAsync(new(c.ProposalReference, c.ExpectedRevision, op.Id, op.Correlation), h.RequestAborted).ConfigureAwait(false));
        }));
        routes.MapPost("/proposals/diff", (HttpContext h, PresentationContentApplication app) => Guard(h, async (profile, _) =>
        {
            var c = await Read<AdminContentDiffQuery>(h, profile).ConfigureAwait(false); var op = Operation();
            return Results.Json(await app.CompareAsync(new(c.ProposalReference, c.ExpectedRevision, c.ComparedVersionReference, op.Id, op.Correlation), h.RequestAborted).ConfigureAwait(false));
        }));
        routes.MapPost("/proposals/publish", (HttpContext h, PresentationContentApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminPublishContent>(h, profile).ConfigureAwait(false);
            if (c.EffectiveInterval is null) throw new ArgumentException("A publication interval is required.");
            return await Write(h, app, session, c.IdempotencyKey, c, op => app.PublishAsync(new(c.ProposalReference, c.ExpectedRevision, c.EffectiveInterval.Value(), c.SupersedesPublicationReference, op.Id, op.Correlation), h.RequestAborted)).ConfigureAwait(false);
        }));
        routes.MapPost("/history", (HttpContext h, PresentationContentApplication app, IContentBrowseBudgetSource budgets) => Guard(h, async (profile, _) =>
        {
            var c = await Read<AdminContentHistoryQuery>(h, profile).ConfigureAwait(false); Scope(profile, c.Scope); var op = Operation();
            return Results.Json(await app.BrowseHistoryAsync(new(c.Scope, c.Key, c.VariantReference, c.PageSize, c.Offset, op.Id, op.Correlation), budgets, h.RequestAborted).ConfigureAwait(false));
        }));
        routes.MapPost("/rollback", (HttpContext h, PresentationContentApplication app) => Guard(h, async (profile, session) =>
        {
            var c = await Read<AdminRollbackContent>(h, profile).ConfigureAwait(false);
            if (c.EffectiveInterval is null) throw new ArgumentException("A rollback interval is required.");
            return await Write(h, app, session, c.IdempotencyKey, c, op => app.RollbackAsync(new(c.ApprovedVersionReference, c.SupersedesPublicationReference, c.EffectiveInterval.Value(), op.Id, op.Correlation), h.RequestAborted)).ConfigureAwait(false);
        }));
    }

    private static async Task<ContentResolution?> Presentation(HttpContext h, AdminContentHttpProfile profile)
    {
        if (profile.Presentation is not { } binding) return null;
        var clock = h.RequestServices.GetRequiredService<TimeProvider>();
        var resolver = h.RequestServices.GetService<IPresentationContentResolver>() ?? throw new ContentUnavailableException();
        var result = await resolver.ResolveAsync(new(binding.Scope, binding.VariantReference, clock.GetUtcNow()), h.RequestAborted).ConfigureAwait(false);
        if (result is null || result.Scope != binding.Scope || result.RequestedVariantReference != binding.VariantReference ||
            string.IsNullOrWhiteSpace(result.EffectiveVariantReference) || string.IsNullOrWhiteSpace(result.Locale) || result.Content is null || result.Content.Count == 0 ||
            result.Content.Any(x => x.Value is null || x.Key != x.Value.Key || x.Value.VariantReference != result.EffectiveVariantReference ||
                x.Value.Locale != result.Locale || string.IsNullOrWhiteSpace(x.Value.VersionReference) || string.IsNullOrWhiteSpace(x.Value.PublicationReference)))
            throw new ContentUnavailableException();
        var current = h.RequestServices.GetRequiredService<IAdminContentHttpProfileSource>().RequireEffective(clock.GetUtcNow()); current.Require(clock.GetUtcNow());
        if (JsonSerializer.Serialize(current, Json) != JsonSerializer.Serialize(profile, Json)) throw new ContentUnavailableException();
        return result;
    }

    private static Task<IResult> ProposalWrite(HttpContext h, PresentationContentApplication app, bool approve) => Guard(h, async (profile, session) =>
    {
        var c = await Read<AdminContentWriteCommand>(h, profile).ConfigureAwait(false);
        return await Write(h, app, session, c.IdempotencyKey, c, op => approve
            ? app.ApproveAsync(new(c.ProposalReference, c.ExpectedRevision, op.Id, op.Correlation), h.RequestAborted)
            : app.ValidateAsync(new(c.ProposalReference, c.ExpectedRevision, op.Id, op.Correlation), h.RequestAborted)).ConfigureAwait(false);
    });
    private static async Task<IResult> Write<T>(HttpContext h, PresentationContentApplication app, AdminContentSession session, Guid key, object command,
        Func<(string Id, string Correlation), Task<T>> run)
    {
        if (key == Guid.Empty) throw new ArgumentException("A caller idempotency key is required.");
        var binding = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { session.ActorReference, session.SessionReference, key }, Json)));
        var op = (Id: "OP-CONTENT-" + binding, Correlation: key.ToString("N"));
        var intent = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { path = h.Request.Path.Value, command }, Json)));
        return Results.Json(await app.ExecuteCommandAsync(op.Id, intent, () => run(op), h.RequestAborted).ConfigureAwait(false));
    }
    private static (string Id, string Correlation) Operation() { var id = Guid.NewGuid().ToString("N"); return ("OP-CONTENT-" + id, id); }
    private static void Required(string? value) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A contextual content reference is required."); }
    private static void Scope(AdminContentHttpProfile profile, string scope)
    { Required(scope); if (!profile.Scopes.Contains(scope, StringComparer.Ordinal)) throw new ContentForbiddenException(); }
    private static async Task<T> Read<T>(HttpContext h, AdminContentHttpProfile profile)
    {
        if (!h.Request.HasJsonContentType()) throw new BadHttpRequestException("JSON is required.", StatusCodes.Status415UnsupportedMediaType);
        if (h.Request.ContentLength > profile.MaximumRequestBytes) throw new BadHttpRequestException("The request exceeds the qualified limit.", 413);
        using var body = new MemoryStream(); var buffer = new byte[Math.Min(profile.MaximumRequestBytes, 8192)];
        while (true)
        {
            var read = await h.Request.Body.ReadAsync(buffer, h.RequestAborted).ConfigureAwait(false); if (read == 0) break;
            if (body.Length + read > profile.MaximumRequestBytes) throw new BadHttpRequestException("The request exceeds the qualified limit.", 413);
            body.Write(buffer, 0, read);
        }
        using var json = JsonDocument.Parse(body.ToArray()); if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
        NoDuplicates(json.RootElement);
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
    private static async Task<IResult> Guard(HttpContext h, Func<AdminContentHttpProfile, AdminContentSession, Task<IResult>> run)
    {
        try
        {
            if (h.Request.QueryString.HasValue) throw new ArgumentException("Unexpected query parameters.");
            var clock = h.RequestServices.GetRequiredService<TimeProvider>();
            var profile = h.RequestServices.GetRequiredService<IAdminContentHttpProfileSource>().RequireEffective(clock.GetUtcNow()); profile.Require(clock.GetUtcNow());
            h.Items[AdminContentAuthorization.ProfileItem] = JsonSerializer.Serialize(profile);
            var session = await AdminContentAuthorization.CurrentSession(h, h.RequestServices.GetRequiredService<IAdminContentSessionSource>(), clock, h.RequestAborted).ConfigureAwait(false);
            return await run(profile, session).ConfigureAwait(false);
        }
        catch (ContentForbiddenException) { return Problem(403, "FORBIDDEN_SCOPE"); }
        catch (ContentConflictException) { return Problem(409, "VERSION_CONFLICT"); }
        catch (BadHttpRequestException error) { return Problem(error.StatusCode, "VALIDATION_ERROR"); }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (Exception error) when (error is InvalidOperationException or DbException or TimeoutException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
    }
    private static IResult Problem(int status, string code) => TechnicalProblemDetails.HttpResult(status, "about:blank", "The request could not be completed.",
        new TechnicalProblemMetadata(code, Guid.NewGuid().ToString("N"), "UNSPECIFIED"));
}
