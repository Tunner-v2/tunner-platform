using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Tunner.Identity.Persistence;

namespace Tunner.User.Bff;

internal static class FederationOidcEndpoints
{
    internal static void MapTunnerFederationOidc(this WebApplication application, IReadOnlyCollection<FederationOidcRegistration> registrations)
    {
        var bound = registrations.Min(value => value.MaximumCommandBytes);
        // These are ordinary same-origin CSRF-protected requests. Do not attach CredentialAuthenticationMetadata:
        // the real current User principal is required for the original browser/session fingerprint.
        application.MapPost("/authentication/federation/start", async (HttpContext http, FederationBrowserService facade) =>
        {
            using var command = await Command(http, bound).ConfigureAwait(false);
            if (command is null || !Shape(command.RootElement, "requestId", "provider", "rememberMe")) return Results.BadRequest();
            var json = command.RootElement;
            if (json.GetProperty("requestId").ValueKind != JsonValueKind.String || !json.GetProperty("requestId").TryGetGuid(out var requestId) || requestId == Guid.Empty ||
                json.GetProperty("provider").ValueKind != JsonValueKind.String || json.GetProperty("rememberMe").ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                return Results.BadRequest();
            var provider = json.GetProperty("provider").GetString() switch
            { "Google" => FederationProviderKind.Google, "Microsoft" => FederationProviderKind.Microsoft, _ => (FederationProviderKind?)null };
            return provider is { } kind ? await facade.StartAsync(http, requestId, kind, json.GetProperty("rememberMe").GetBoolean(), http.RequestAborted).ConfigureAwait(false)
                : Results.BadRequest();
        }).AllowAnonymous();
        MapEmpty("/authentication/federation/result", static (facade, http) => facade.ReadOriginalAsync(http, http.RequestAborted));
        application.MapPost("/authentication/federation/complete", async (HttpContext http,FederationBrowserService facade) =>
        {
            using var command = await Command(http,bound).ConfigureAwait(false); if (command is null) return Results.BadRequest();
            var json = command.RootElement; if (Shape(json)) return await facade.CompleteSignInAsync(http,http.RequestAborted).ConfigureAwait(false);
            if (!Shape(json,"attemptId","federationRequestId","registrationKey","expectedVersion","termsVersion","privacyVersion","acknowledgedNotices") ||
                json.GetProperty("attemptId").ValueKind != JsonValueKind.String || !json.GetProperty("attemptId").TryGetGuid(out var attempt) || attempt == Guid.Empty ||
                json.GetProperty("federationRequestId").ValueKind != JsonValueKind.String || !json.GetProperty("federationRequestId").TryGetGuid(out var request) || request == Guid.Empty ||
                json.GetProperty("registrationKey").ValueKind != JsonValueKind.String || !json.GetProperty("registrationKey").TryGetGuid(out var key) || key == Guid.Empty ||
                json.GetProperty("expectedVersion").ValueKind != JsonValueKind.Number || !json.GetProperty("expectedVersion").TryGetInt64(out var version) || version < 1 ||
                json.GetProperty("termsVersion").ValueKind != JsonValueKind.String || json.GetProperty("privacyVersion").ValueKind != JsonValueKind.String ||
                json.GetProperty("acknowledgedNotices").ValueKind is not JsonValueKind.True and not JsonValueKind.False) return Results.BadRequest();
            return await facade.CompleteSignInAsync(http,new(attempt,request,key,version,json.GetProperty("termsVersion").GetString()!,
                json.GetProperty("privacyVersion").GetString()!,json.GetProperty("acknowledgedNotices").GetBoolean()),http.RequestAborted).ConfigureAwait(false);
        }).AllowAnonymous();
        MapEmpty("/authentication/federation/cancel", static (facade, http) => facade.CancelAsync(http, http.RequestAborted));
        foreach (var registration in registrations)
        {
            application.MapGet(registration.ChallengePath, async (HttpContext http, FederationBrowserService facade) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                try
                {
                    if (!registration.Current(http)) return Results.StatusCode(403);
                    var admitted = await facade.AdmitChallengeAsync(http, http.RequestAborted).ConfigureAwait(false);
                    if (admitted is null || admitted.Provider != registration.Provider || admitted.Scheme != registration.Scheme ||
                        admitted.ProfileVersion != registration.Profile.Values.Version || !registration.Current(http)) return Results.StatusCode(403);
                    var properties = new AuthenticationProperties();
                    properties.Items[FederationOidcTransport.AttemptProperty] = admitted.AttemptId;
                    properties.Items[FederationOidcTransport.ProfileProperty] = admitted.ProfileVersion;
                    properties.Items[FederationOidcTransport.PurposeProperty] = admitted.Purpose;
                    await http.ChallengeAsync(registration.Scheme, properties).ConfigureAwait(false);
                    return Results.Empty;
                }
                catch (Exception) { return Results.StatusCode(503); }
            }).AllowAnonymous();
        }
        void MapEmpty(string path, Func<FederationBrowserService, HttpContext, Task<IResult>> action) =>
            application.MapPost(path, async (HttpContext http, FederationBrowserService facade) =>
            {
                using var command = await Command(http, bound).ConfigureAwait(false);
                return command is not null && Shape(command.RootElement) ? await action(facade, http).ConfigureAwait(false) : Results.BadRequest();
            }).AllowAnonymous();
    }

    internal static bool Shape(JsonElement json, params string[] expected) => json.ValueKind == JsonValueKind.Object &&
        json.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    // One bounded command shape, independent of MVC model binding and tolerant JSON DTO defaults.
    internal static async Task<JsonDocument?> Command(HttpContext http, int maximumBytes)
    {
        if (http.Request.QueryString.HasValue || http.Request.ContentType?.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase) != true ||
            http.Request.ContentLength > maximumBytes) return null;
        using var body = new MemoryStream(); var buffer = new byte[Math.Min(maximumBytes, 8192)];
        try
        {
            while (true)
            {
                var remaining = maximumBytes - body.Length;
                var read = await http.Request.Body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining + 1)), http.RequestAborted).ConfigureAwait(false);
                if (read == 0) break;
                if (read > remaining) return null;
                await body.WriteAsync(buffer.AsMemory(0, read), http.RequestAborted).ConfigureAwait(false);
            }
            body.Position = 0;
            return await JsonDocument.ParseAsync(body, cancellationToken: http.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException) { return null; }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer); }
    }
}
