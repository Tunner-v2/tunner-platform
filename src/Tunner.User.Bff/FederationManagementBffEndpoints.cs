using System.Text.Json;
using Tunner.Identity.Persistence;

namespace Tunner.User.Bff;

internal static class FederationManagementBffEndpoints
{
    private static readonly string[] StartFields = ["requestId", "provider", "expectedLinkVersion", "expectedAccountVersion"];
    private static readonly string[] TargetStartFields = ["requestId", "provider", "expectedLinkVersion", "expectedAccountVersion", "targetLinkReference"];
    private static readonly string[] ConfirmFields = ["operationId", "requestId", "expectedVersion", "confirmed"];
    private static readonly string[] ResultFields = ["requestId"];
    private static readonly string[] OperationFields = ["operationId", "requestId"];
    internal static void MapTunnerFederationManagement(this WebApplication application, IReadOnlyCollection<FederationOidcRegistration> transports)
    {
        var bound = transports.Min(value => value.MaximumCommandBytes);
        // Ordinary User-principal + Origin + CSRF boundary. No credential-only metadata or protocol exception.
        application.MapGet("/account/security/federation", (HttpContext http, FederationBrowserService facade) =>
            facade.ReadLinkSettingsAsync(http, http.RequestAborted)).RequireAuthorization();
        MapStart("/account/security/federation/link/start", unlink: false);
        MapStart("/account/security/federation/unlink/start", unlink: true);
        application.MapPost("/account/security/federation/confirm", async (HttpContext http, FederationBrowserService facade) =>
        {
            using var command = await FederationOidcEndpoints.Command(http, bound).ConfigureAwait(false);
            if (command is null || !FederationOidcEndpoints.Shape(command.RootElement, ConfirmFields)) return Results.BadRequest();
            var json = command.RootElement;
            if (!Id(json, "operationId", out var operationId) || !Id(json, "requestId", out var requestId) ||
                !Version(json, "expectedVersion", out var version) || json.GetProperty("confirmed").ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                return Results.BadRequest();
            return await facade.ConfirmLinkOperationAsync(http, operationId, requestId, version, json.GetProperty("confirmed").GetBoolean(), http.RequestAborted).ConfigureAwait(false);
        }).RequireAuthorization();
        application.MapPost("/account/security/federation/result", async (HttpContext http, FederationBrowserService facade) =>
        {
            using var command = await FederationOidcEndpoints.Command(http, bound).ConfigureAwait(false);
            if (command is null) return Results.BadRequest();
            var json = command.RootElement;
            if (!(FederationOidcEndpoints.Shape(json, ResultFields) || FederationOidcEndpoints.Shape(json, OperationFields)) || !Id(json, "requestId", out var requestId))
                return Results.BadRequest();
            var operationId = Guid.Empty;
            if (json.TryGetProperty("operationId", out var operation) && (operation.ValueKind != JsonValueKind.String || !operation.TryGetGuid(out operationId)))
                return Results.BadRequest();
            // Empty/absent operation ID is the exact Session+RequestId recovery path, never latest inference.
            return await facade.ReadLinkOperationAsync(http, operationId, requestId, http.RequestAborted).ConfigureAwait(false);
        }).RequireAuthorization();
        application.MapPost("/account/security/federation/cancel", async (HttpContext http, FederationBrowserService facade) =>
        {
            using var command = await FederationOidcEndpoints.Command(http, bound).ConfigureAwait(false);
            if (command is null || !FederationOidcEndpoints.Shape(command.RootElement, OperationFields) ||
                !Id(command.RootElement, "operationId", out var operationId) || !Id(command.RootElement, "requestId", out var requestId)) return Results.BadRequest();
            return await facade.CancelLinkOperationAsync(http, operationId, requestId, http.RequestAborted).ConfigureAwait(false);
        }).RequireAuthorization();

        void MapStart(string path, bool unlink) => application.MapPost(path, async (HttpContext http, FederationBrowserService facade) =>
        {
            using var command = await FederationOidcEndpoints.Command(http, bound).ConfigureAwait(false);
            if (command is null) return Results.BadRequest();
            var json = command.RootElement;
            if (!(FederationOidcEndpoints.Shape(json, StartFields) || FederationOidcEndpoints.Shape(json, TargetStartFields)) ||
                !Id(json, "requestId", out var requestId) || !Version(json, "expectedLinkVersion", out var linkVersion) ||
                !Version(json, "expectedAccountVersion", out var accountVersion) || json.GetProperty("provider").ValueKind != JsonValueKind.String) return Results.BadRequest();
            var provider = json.GetProperty("provider").GetString() switch
            { "Google" => FederationProviderKind.Google, "Microsoft" => FederationProviderKind.Microsoft, _ => (FederationProviderKind?)null };
            if (provider is null) return Results.BadRequest();
            Guid? target = null;
            if (json.TryGetProperty("targetLinkReference", out var targetValue) && targetValue.ValueKind != JsonValueKind.Null)
            {
                if (targetValue.ValueKind != JsonValueKind.String || !targetValue.TryGetGuid(out var reference) || reference == Guid.Empty) return Results.BadRequest();
                target = reference;
            }
            if (unlink && target is null) return Results.BadRequest();
            return unlink ? await facade.StartUnlinkAsync(http, requestId, provider.Value, target!.Value, linkVersion, accountVersion, http.RequestAborted).ConfigureAwait(false)
                : await facade.StartLinkAsync(http, requestId, provider.Value, target, linkVersion, accountVersion, http.RequestAborted).ConfigureAwait(false);
        }).RequireAuthorization();
    }
    private static bool Id(JsonElement json, string name, out Guid value)
    { value = Guid.Empty; return json.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String && item.TryGetGuid(out value) && value != Guid.Empty; }
    private static bool Version(JsonElement json, string name, out long value)
    { value = 0; return json.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out value) && value >= 0; }
}
