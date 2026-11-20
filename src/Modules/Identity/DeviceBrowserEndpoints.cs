using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Operations;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit trusted-host activation of private User operations. It publishes no Product API or screen.</summary>
public static class DeviceBrowserEndpoints
{
    internal const string Root = "/account/devices";
    internal const string OriginalCookie = "__Host-Tunner.DeviceOperation";
    internal const string DraftCookie = "__Host-Tunner.DeviceDraft";
    internal const string OriginalPurpose = "Tunner.User.Device.Original.v1";
    public static IServiceCollection AddTunnerDevices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] required = [typeof(IDeviceEvidencePolicySource), typeof(IDeviceSafeCharacteristicSource), typeof(IDeviceTrustEligibilitySource),
            typeof(IDeviceHttpBudgetSource), typeof(IDevicePresentationSource), typeof(IAssurancePolicySource), typeof(IStepUpPresentationSource)];
        if (required.Any(x => !services.Any(d => d.ServiceType == x))) throw new InvalidOperationException("Devices require explicit collection, privacy, transport, publication, eligibility and proof authorities.");
        if (services.Any(x => x.ServiceType == typeof(DeviceManagementBrowserService))) throw new InvalidOperationException("Devices may be activated only once.");
        if (!services.Any(x => x.ServiceType == typeof(DeviceBrowserService))) DeviceBrowserService.AddQualifiedEvidence(services);
        services.AddScoped<DeviceManagementBrowserService>(); return services;
    }
    /// <summary>The authenticated host calls this after native owner registration, preserving the actual decorated fallback.</summary>
    public static IServiceCollection CompleteTunnerDevicesComposition(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!services.Any(x => x.ServiceType == typeof(DeviceManagementBrowserService))) return services;
        if (services.Any(x => x.ServiceType == typeof(CompletedComposition))) throw new InvalidOperationException("Device owner composition may be completed only once.");
        var fallback = services.LastOrDefault(x => x.ServiceType == typeof(IUserStepUpOperationSource));
        if (fallback is not null)
        {
            services.Remove(fallback);
            services.Add(new ServiceDescriptor(typeof(DeviceFallbackOwner), p => new DeviceFallbackOwner(
                fallback.ImplementationInstance as IUserStepUpOperationSource ?? fallback.ImplementationFactory?.Invoke(p) as IUserStepUpOperationSource ??
                (IUserStepUpOperationSource)ActivatorUtilities.GetServiceOrCreateInstance(p, fallback.ImplementationType!)), fallback.Lifetime));
        }
        services.AddSingleton(new CompletedComposition()); services.AddScoped<IUserStepUpOperationSource, DeviceStepUpOwner>(); return services;
    }
    private sealed class CompletedComposition;
    internal static void MapIfConfigured(IEndpointRouteBuilder endpoints)
    {
        if (!endpoints.ServiceProvider.GetRequiredService<IServiceProviderIsService>().IsService(typeof(DeviceManagementBrowserService))) return;
        var user = new AuthorizeAttribute { AuthenticationSchemes = AuthenticationIdentity.Scheme };
        endpoints.MapGet(Root + "/options", (HttpContext h, DeviceManagementBrowserService s) => s.OptionsAsync(h)).RequireAuthorization(user);
        endpoints.MapPost(Root + "/list", (HttpContext h, DeviceManagementBrowserService s) => s.ListAsync(h)).RequireAuthorization(user);
        endpoints.MapPost(Root + "/details", (HttpContext h, DeviceManagementBrowserService s) => s.DetailsAsync(h)).RequireAuthorization(user);
        endpoints.MapPost(Root + "/prepare", (HttpContext h, DeviceManagementBrowserService s) => s.PrepareAsync(h)).RequireAuthorization(user);
        endpoints.MapGet(Root + "/context", (HttpContext h, DeviceManagementBrowserService s) => s.ContextAsync(h)).RequireAuthorization(user);
        endpoints.MapPost(Root + "/result", (HttpContext h, DeviceManagementBrowserService s) => s.ResultAsync(h)).RequireAuthorization(user);
        var assets = endpoints.ServiceProvider.GetRequiredService<IdentityWebAssets>();
        var surface = endpoints.ServiceProvider.GetRequiredService<AuthenticationEndpoints.Surface>();
        assets.MapScreens(endpoints, "devices.html", [Root, Root + "/{reference:guid:length(36)}"], http =>
            http.User.Identity is { IsAuthenticated: true, AuthenticationType: AuthenticationIdentity.Scheme } &&
            AuthenticationIdentity.SessionId(http.User) is not null && http.User.FindFirst(AuthenticationIdentity.OriginClaim)?.Value == surface.Origin);
    }
    internal sealed record Original(string AccountId, Guid ActorSessionId, string Origin, DeviceTrustRemovalRequest Request,
        string BindingDigest, string AuthenticationVersion, string TransportDigest, string PublicationDigest, string SelectionBaseline,
        DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset RecoveryExpiresAt, string? FallbackDigest);
    internal static CookieOptions Cookie(TimeSpan remaining) => new() { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, MaxAge = remaining };
    internal static Original? ReadOriginal(HttpContext http, IDataProtectionProvider protection, UserStepUpActor actor)
    {
        if (!http.Request.Cookies.TryGetValue(OriginalCookie, out var value) || value.Length > 4096) return null;
        try
        {
            var original = JsonSerializer.Deserialize<Original>(protection.CreateProtector(OriginalPurpose).Unprotect(value));
            if (original is null || original.AccountId != actor.AccountId || original.ActorSessionId != actor.SessionId || original.Origin != actor.Origin ||
                original.CreatedAt >= original.ExpiresAt || original.ExpiresAt >= original.RecoveryExpiresAt || original.BindingDigest.Length != 64) return null;
            original.Request.Validate(); return original;
        }
        catch (CryptographicException) { return null; } catch (JsonException) { return null; } catch (ArgumentException) { return null; }
    }
    internal static string PublicationDigest(DevicePresentation value)
    {
        if (value.BindingQualificationDigest is { } qualificationDigest)
            return AuthenticationIdentity.Digest(JsonSerializer.Serialize(new {
                value.Locale, Content = value.Content.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Value).ToArray(),
                BindingQualificationDigest = qualificationDigest }));
        // Preserve the exact original digest bytes for ordinary local publication providers.
        return AuthenticationIdentity.Digest(JsonSerializer.Serialize(new {
            value.Locale, Content = value.Content.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Value).ToArray() }));
    }
    internal static DevicePresentation Publication(IDevicePresentationSource source, AuthenticationPolicy authenticationPolicy, string devicePolicyVersion)
    {
        try { return source.RequirePublished(authenticationPolicy, devicePolicyVersion) ?? throw new InvalidOperationException("Device publication is unavailable."); }
        catch (ArgumentException e) { throw new InvalidOperationException("Device publication is unavailable.", e); }
    }
    internal static async ValueTask<DevicePreparedPresentation?> PreparePublicationAsync(IDevicePresentationSource source,
        AuthenticationPolicy authenticationPolicy, string devicePolicyVersion, CancellationToken ct)
    {
        if (source is not IDeviceAsyncPresentationSource asyncSource) return null;
        return await asyncSource.PreparePublishedAsync(authenticationPolicy, devicePolicyVersion, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Prepared Device publication is unavailable.");
    }
    internal static async Task<DevicePresentation> PublicationAsync(IDevicePresentationSource source, AuthenticationPolicy authenticationPolicy,
        string devicePolicyVersion, CancellationToken ct)
    {
        var prepared = await PreparePublicationAsync(source, authenticationPolicy, devicePolicyVersion, ct).ConfigureAwait(false);
        return prepared?.Presentation ?? Publication(source, authenticationPolicy, devicePolicyVersion);
    }
    internal static bool Fits(string name, string value, CookieOptions cookie, int maximum) => SessionRevocationOriginalSelection.Fits(name, value, cookie, maximum);
    internal static IResult Problem(int status, string code) => TechnicalProblemDetails.HttpResult(status, "about:blank", "The request could not be completed.",
        new TechnicalProblemMetadata(code, Guid.NewGuid().ToString("N"), "UNSPECIFIED"));
    internal static async Task<IResult> Guard(HttpContext http, Func<Task<IResult>> action)
    {
        http.Response.Headers.CacheControl = "no-store"; http.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (http.Request.QueryString.HasValue) return Problem(400, "VALIDATION_ERROR");
        try { return await action().ConfigureAwait(false); }
        catch (AssurancePolicyVersionConflictException) { return Problem(409, "VERSION_CONFLICT"); }
        catch (DbUpdateConcurrencyException) { return Problem(409, "VERSION_CONFLICT"); }
        catch (JsonException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (BadHttpRequestException e) { return Problem(e.StatusCode == 413 ? 413 : 400, "VALIDATION_ERROR"); }
        catch (ArgumentException) { return Problem(400, "VALIDATION_ERROR"); }
        catch (CryptographicException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
        catch (InvalidOperationException) { return Problem(503, "DEPENDENCY_UNAVAILABLE"); }
    }
}

internal sealed class DeviceManagementBrowserService(DeviceApplication devices, IdentityPersistenceDbContext database,
    IAuthenticationPolicySource authenticationPolicies, IAssurancePolicySource assurance, IDeviceEvidencePolicySource policies,
    IDeviceHttpBudgetSource budgets, IDevicePresentationSource publications, AuthenticationEndpoints.Surface surface,
    AuthenticationPartitionKey partitions, IDataProtectionProvider protection, TimeProvider clock, IEnumerable<DeviceFallbackOwner> fallback)
{
    private sealed record Draft(Guid RequestId, string AccountId, Guid SessionId, string Origin, string AuthenticationVersion,
        string TransportDigest, string DevicePolicyDigest, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
    private sealed record Cursor(DeviceListingContinuation Boundary, string AuthenticationVersion, string TransportDigest,
        string DevicePolicyDigest, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class ListCommand { public JsonElement Continuation { get; init; } }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class DetailsCommand { public Guid Reference { get; init; } }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class PrepareCommand
    {
        public Guid RequestId { get; init; }
        public Guid Reference { get; init; }
        public string? ExpectedVersion { get; init; }
        public string? ExpectedTrustVersion { get; init; }
        public bool Confirmed { get; init; }
    }
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class ResultCommand { public Guid RequestId { get; init; } }
    private sealed record Item(Guid Reference, string Version, string TrustVersion, bool ObservedForCurrentSession,
        DateTimeOffset FirstObservedAt, DateTimeOffset LastObservedAt, IReadOnlyDictionary<string, string> Characteristics,
        string RecordedTrustState, string EffectiveTrustState, DateTimeOffset EvaluatedAt);
    private StepUpCaller Caller(HttpContext http) => StepUpCaller.FromPrincipal(http.User, surface.Origin,
        partitions.Network(http.Connection.RemoteIpAddress ?? throw new ArgumentException("A current peer is required.")));
    private DeviceEvidencePolicy Policy(DateTimeOffset at) { var value = policies.RequireCurrent(surface.Origin, at); value.Require(surface.Origin, at); return value; }
    private Task<DevicePresentation> PublicationAsync(DeviceEvidencePolicy policy, CancellationToken ct) =>
        DeviceBrowserEndpoints.PublicationAsync(publications, authenticationPolicies.RequireEffective(clock.GetUtcNow()), policy.Metadata.VersionId, ct);
    private static string Digest(DeviceHttpBudget value) => AuthenticationIdentity.Digest(value.Snapshot);
    private static string Digest(DeviceEvidencePolicy value) => AuthenticationIdentity.Digest(value.Snapshot);
    private static bool Version(string? text, out long value) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
        value > 0 && value.ToString(CultureInfo.InvariantCulture) == text;
    private static Item View(DeviceListingItem item) => new(item.DeviceReference, item.Version.ToString(CultureInfo.InvariantCulture),
        item.TrustVersion.ToString(CultureInfo.InvariantCulture), item.ObservedForCurrentSession, item.FirstObservedAt, item.LastObservedAt,
        item.Characteristics, item.RecordedTrustState, item.EffectiveTrustState, item.EvaluatedAt);
    private static DateTimeOffset Clip(DateTimeOffset value, params DateTimeOffset?[] bounds)
    { foreach (var bound in bounds) if (bound is { } at && at < value) value = at; return value; }
    private Draft? ReadDraft(HttpContext http, StepUpCaller caller, DeviceHttpBudget budget, DeviceEvidencePolicy policy, AuthenticationPolicy auth)
    {
        if (!http.Request.Cookies.TryGetValue(DeviceBrowserEndpoints.DraftCookie, out var value) || value.Length > 4096) return null;
        try
        {
            var draft = JsonSerializer.Deserialize<Draft>(protection.CreateProtector("Tunner.User.Device.Draft.v1").Unprotect(value)); var at = clock.GetUtcNow();
            return draft is not null && draft.RequestId != Guid.Empty && draft.AccountId == caller.AccountId && draft.SessionId == caller.SessionId &&
                draft.Origin == caller.Origin && draft.AuthenticationVersion == auth.Version && draft.TransportDigest == Digest(budget) &&
                draft.DevicePolicyDigest == Digest(policy) && draft.CreatedAt <= at && at < draft.ExpiresAt ? draft : null;
        }
        catch (CryptographicException) { return null; } catch (JsonException) { return null; }
    }
    internal Task<IResult> OptionsAsync(HttpContext http) => DeviceBrowserEndpoints.Guard(http, async () =>
    {
        var caller = Caller(http); var at = clock.GetUtcNow(); var budget = DeviceHttpBudget.Current(budgets, at); var policy = Policy(at);
        var auth = authenticationPolicies.RequireEffective(at); auth.Validate(); var publication = await PublicationAsync(policy, http.RequestAborted).ConfigureAwait(false);
        var draft = ReadDraft(http, caller, budget, policy, auth);
        if (draft is not null && await database.Set<DeviceOperationRow>().AsNoTracking().AnyAsync(x => x.SessionId == caller.SessionId && x.RequestId == draft.RequestId && x.State == "COMPLETED", http.RequestAborted).ConfigureAwait(false)) draft = null;
        draft ??= new(Guid.NewGuid(), caller.AccountId, caller.SessionId, caller.Origin, auth.Version, Digest(budget), Digest(policy), at,
            Clip(at + budget.IntentLifetime, at + auth.BrowserContextLifetime, budget.Metadata.EffectiveInterval.Until, policy.Metadata.EffectiveInterval.Until));
        var encoded = protection.CreateProtector("Tunner.User.Device.Draft.v1").Protect(JsonSerializer.Serialize(draft));
        if (!DeviceBrowserEndpoints.Fits(DeviceBrowserEndpoints.DraftCookie, encoded, DeviceBrowserEndpoints.Cookie(draft.ExpiresAt - at), budget.MaximumSetCookieBytes))
            return DeviceBrowserEndpoints.Problem(503, "TRANSPORT_CAPACITY_EXCEEDED");
        var currentPublication = await PublicationAsync(policy, http.RequestAborted).ConfigureAwait(false);
        var finalAt = clock.GetUtcNow();
        if (finalAt >= draft.ExpiresAt || auth != authenticationPolicies.RequireEffective(finalAt) || Digest(budget) != Digest(DeviceHttpBudget.Current(budgets, finalAt)) || Digest(policy) != Digest(Policy(finalAt)) ||
            DeviceBrowserEndpoints.PublicationDigest(publication) != DeviceBrowserEndpoints.PublicationDigest(currentPublication)) return DeviceBrowserEndpoints.Problem(409, "VERSION_CONFLICT");
        http.Response.Cookies.Append(DeviceBrowserEndpoints.DraftCookie, encoded, DeviceBrowserEndpoints.Cookie(draft.ExpiresAt - finalAt));
        return Results.Json(new { draft.RequestId, publication.Locale, publication.Content });
    });
    internal Task<IResult> ListAsync(HttpContext http) => DeviceBrowserEndpoints.Guard(http, async () =>
    {
        if (!http.Request.HasJsonContentType()) return DeviceBrowserEndpoints.Problem(415, "VALIDATION_ERROR");
        var caller = Caller(http); var at = clock.GetUtcNow(); var budget = DeviceHttpBudget.Current(budgets, at); var policy = Policy(at);
        var auth = authenticationPolicies.RequireEffective(at); auth.Validate();
        if (budget.MaximumRows > policy.MaximumListingRows) throw new InvalidOperationException("Device transport capacity exceeds the qualified owner read budget.");
        var command = await IdentityJsonCommand.ReadAsync<ListCommand>(http, budget.MaximumRequestBytes).ConfigureAwait(false); Cursor? cursor = null;
        var protector = protection.CreateProtector("Tunner.User.Device.Cursor.v1");
        if (command.Continuation.ValueKind != JsonValueKind.Undefined)
        {
            if (command.Continuation.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(command.Continuation.GetString())) return DeviceBrowserEndpoints.Problem(400, "VALIDATION_ERROR");
            try { cursor = JsonSerializer.Deserialize<Cursor>(protector.Unprotect(command.Continuation.GetString()!)); }
            catch (CryptographicException) { return DeviceBrowserEndpoints.Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE"); }
            catch (JsonException) { return DeviceBrowserEndpoints.Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE"); }
            if (cursor is null || cursor.Boundary.AccountId != caller.AccountId || cursor.Boundary.ActorSessionId != caller.SessionId || cursor.Boundary.Origin != caller.Origin ||
                cursor.CreatedAt > at || at >= cursor.ExpiresAt) return DeviceBrowserEndpoints.Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
            if (cursor.TransportDigest != Digest(budget) || cursor.DevicePolicyDigest != Digest(policy) || cursor.AuthenticationVersion != auth.Version) return DeviceBrowserEndpoints.Problem(409, "VERSION_CONFLICT");
        }
        var page = await devices.ListAsync(caller, budget.MaximumRows, cursor?.Boundary, http.RequestAborted).ConfigureAwait(false);
        if (page is null) return DeviceBrowserEndpoints.Problem(401, "AUTHENTICATION_FAILED");
        at = clock.GetUtcNow();
        if (Digest(budget) != Digest(DeviceHttpBudget.Current(budgets, at)) || Digest(policy) != Digest(Policy(at)) || auth != authenticationPolicies.RequireEffective(at)) return DeviceBrowserEndpoints.Problem(409, "VERSION_CONFLICT");
        var created = cursor?.CreatedAt ?? at; var expires = cursor?.ExpiresAt ?? Clip(created + budget.CursorLifetime, budget.Metadata.EffectiveInterval.Until, policy.Metadata.EffectiveInterval.Until);
        if (at < created || at >= expires) return DeviceBrowserEndpoints.Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var next = page.Next is null ? null : protector.Protect(JsonSerializer.Serialize(new Cursor(page.Next, auth.Version, Digest(budget), Digest(policy), created, expires)));
        return Results.Json(new { devices = page.Items.Select(View).ToArray(), page.HasMore, continuation = next });
    });
    internal Task<IResult> DetailsAsync(HttpContext http) => DeviceBrowserEndpoints.Guard(http, async () =>
    {
        if (!http.Request.HasJsonContentType()) return DeviceBrowserEndpoints.Problem(415, "VALIDATION_ERROR");
        var budget = DeviceHttpBudget.Current(budgets, clock.GetUtcNow());
        var command = await IdentityJsonCommand.ReadAsync<DetailsCommand>(http, budget.MaximumRequestBytes).ConfigureAwait(false);
        if (command.Reference == Guid.Empty) return DeviceBrowserEndpoints.Problem(400, "VALIDATION_ERROR");
        var page = await devices.ReadDeviceAsync(Caller(http), command.Reference, http.RequestAborted).ConfigureAwait(false);
        return page is null ? DeviceBrowserEndpoints.Problem(401, "AUTHENTICATION_FAILED") : page.Items.Count != 1
            ? DeviceBrowserEndpoints.Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE") : Results.Json(new { device = View(page.Items[0]) });
    });
    internal Task<IResult> PrepareAsync(HttpContext http) => DeviceBrowserEndpoints.Guard(http, async () =>
    {
        if (!http.Request.HasJsonContentType()) return DeviceBrowserEndpoints.Problem(415, "VALIDATION_ERROR");
        var caller = Caller(http); var at = clock.GetUtcNow(); var budget = DeviceHttpBudget.Current(budgets, at); var policy = Policy(at);
        var auth = authenticationPolicies.RequireEffective(at); auth.Validate(); var publication = await PublicationAsync(policy, http.RequestAborted).ConfigureAwait(false);
        var command = await IdentityJsonCommand.ReadAsync<PrepareCommand>(http, budget.MaximumRequestBytes).ConfigureAwait(false);
        if (!command.Confirmed || command.RequestId == Guid.Empty || command.Reference == Guid.Empty || !Version(command.ExpectedVersion, out var version) ||
            !Version(command.ExpectedTrustVersion, out var trustVersion) || trustVersion == long.MaxValue) return DeviceBrowserEndpoints.Problem(400, "VALIDATION_ERROR");
        var old = DeviceBrowserEndpoints.ReadOriginal(http, protection, new(caller));
        if (http.Request.Cookies.ContainsKey(DeviceBrowserEndpoints.OriginalCookie) && old is null) return DeviceBrowserEndpoints.Problem(409, "RESULT_UNCONFIRMED");
        if (old is not null)
        {
            var previous = await devices.ResolveBrowserRemovalAsync(caller, old.Request, http.RequestAborted).ConfigureAwait(false);
            if (old.Request.RequestId == command.RequestId)
            {
                if (old.Request.DeviceId != command.Reference || old.Request.ExpectedDeviceVersion != version || old.Request.ExpectedTrustVersion != trustVersion)
                    return DeviceBrowserEndpoints.Problem(409, "VERSION_CONFLICT");
                return previous.Original is null ? DeviceBrowserEndpoints.Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE") : OriginalView(old, await CurrentOriginalAsync(http, caller, old, previous.Context).ConfigureAwait(false));
            }
            if (previous.Context?.Completed != true) return DeviceBrowserEndpoints.Problem(409, "RESULT_UNCONFIRMED");
        }
        var draft = ReadDraft(http, caller, budget, policy, auth);
        if (draft is null || draft.RequestId != command.RequestId) return DeviceBrowserEndpoints.Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var prior = await database.Set<DeviceOperationRow>().AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == caller.SessionId && x.RequestId == draft.RequestId, http.RequestAborted).ConfigureAwait(false);
        if (prior is not null && (prior.DeviceId != command.Reference || prior.ExpectedDeviceVersion != version || prior.ExpectedTrustVersion != trustVersion)) return DeviceBrowserEndpoints.Problem(409, "VERSION_CONFLICT");
        var accountVersion = prior?.ExpectedAccountVersion ?? await database.Accounts.Where(x => x.AccountId == caller.AccountId).Select(x => x.Version).SingleAsync(http.RequestAborted).ConfigureAwait(false);
        var request = new DeviceTrustRemovalRequest(draft.RequestId, command.Reference, version, trustVersion, accountVersion, prior?.CorrelationId ?? "COR-DEVICE-TRUST-" + draft.RequestId.ToString("N"));
        var prepared = await devices.PrepareRemovalAsync(caller, request, assurance, http.RequestAborted).ConfigureAwait(false);
        if (prepared is null) return DeviceBrowserEndpoints.Problem(409, "VERSION_CONFLICT");
        var resolved = await devices.ResolveBrowserRemovalAsync(caller, request, http.RequestAborted).ConfigureAwait(false);
        if (resolved.Original is not { } row || resolved.Context is null) return DeviceBrowserEndpoints.Problem(409, "VERSION_CONFLICT");
        var other = fallback.SingleOrDefault() is { } f ? await f.Owner.ResolveAsync(new(caller), http.RequestAborted).ConfigureAwait(false) : null;
        var currentPublication = await PublicationAsync(policy, http.RequestAborted).ConfigureAwait(false);
        at = clock.GetUtcNow(); var expires = Clip(row.ExpiresAt, row.CreatedAt + budget.IntentLifetime, row.CreatedAt + auth.BrowserContextLifetime, budget.Metadata.EffectiveInterval.Until, policy.Metadata.EffectiveInterval.Until);
        var recovery = Clip(row.RecoveryExpiresAt, expires + budget.ResultLifetime);
        var original = new DeviceBrowserEndpoints.Original(caller.AccountId, caller.SessionId, caller.Origin, request, StepUpBrowserService.Digest(prepared), auth.Version,
            Digest(budget), DeviceBrowserEndpoints.PublicationDigest(publication), SessionRevocationOriginalSelection.Fingerprint(http), row.CreatedAt, expires, recovery, other is null ? null : StepUpBrowserService.Digest(other));
        var encoded = protection.CreateProtector(DeviceBrowserEndpoints.OriginalPurpose).Protect(JsonSerializer.Serialize(original));
        if (at >= expires || expires >= recovery || Digest(budget) != Digest(DeviceHttpBudget.Current(budgets, at)) || Digest(policy) != Digest(Policy(at)) || auth != authenticationPolicies.RequireEffective(at) ||
            DeviceBrowserEndpoints.PublicationDigest(publication) != DeviceBrowserEndpoints.PublicationDigest(currentPublication)) return DeviceBrowserEndpoints.Problem(409, "VERSION_CONFLICT");
        if (!DeviceBrowserEndpoints.Fits(DeviceBrowserEndpoints.OriginalCookie, encoded, DeviceBrowserEndpoints.Cookie(recovery - at), budget.MaximumSetCookieBytes)) return DeviceBrowserEndpoints.Problem(503, "TRANSPORT_CAPACITY_EXCEEDED");
        http.Response.Cookies.Append(DeviceBrowserEndpoints.OriginalCookie, encoded, DeviceBrowserEndpoints.Cookie(recovery - at));
        // Existing owner selections remain truthful independent originals. Only the old proof dialog is superseded.
        if (http.Request.Cookies.ContainsKey(StepUpBrowserEndpoints.CookieName))
            http.Response.Cookies.Delete(StepUpBrowserEndpoints.CookieName, DeviceBrowserEndpoints.Cookie(TimeSpan.Zero));
        return OriginalView(original, resolved.Context);
    });
    private static IResult OriginalView(DeviceBrowserEndpoints.Original original, UserStepUpOperationContext? context) => Results.Json(new {
        original.Request.RequestId, operationReference = original.Request.CorrelationId, status = context is null ? "CHANGED" : context.Completed ? "COMPLETED" : "PENDING",
        target = new { reference = original.Request.DeviceId, version = original.Request.ExpectedDeviceVersion.ToString(CultureInfo.InvariantCulture), trustVersion = original.Request.ExpectedTrustVersion.ToString(CultureInfo.InvariantCulture) },
        sessionEffect = "PRESERVE_EXISTING_SESSIONS", original.ExpiresAt });
    private async Task<UserStepUpOperationContext?> CurrentOriginalAsync(HttpContext http, StepUpCaller caller, DeviceBrowserEndpoints.Original original,
        UserStepUpOperationContext? context)
    {
        if (context is null || StepUpBrowserService.Digest(context) != original.BindingDigest) return null;
        if (context.Completed) return context;
        var other = fallback.SingleOrDefault() is { } f ? await f.Owner.ResolveAsync(new(caller), http.RequestAborted).ConfigureAwait(false) : null;
        if (other is not null && StepUpBrowserService.Digest(other) != original.FallbackDigest) return null;
        var auth = authenticationPolicies.RequireEffective(clock.GetUtcNow()); auth.Validate();
        var policy = Policy(clock.GetUtcNow());
        var prepared = await DeviceBrowserEndpoints.PreparePublicationAsync(publications, auth, policy.Metadata.VersionId, http.RequestAborted).ConfigureAwait(false);
        var witness = new DeviceBrowserExecutionValidity(http, protection, budgets, publications, policies, authenticationPolicies, original, context, prepared, database);
        return witness.SelectionCurrentAt(caller, context.Binding, clock.GetUtcNow()) ? context : null;
    }
    internal Task<IResult> ContextAsync(HttpContext http) => DeviceBrowserEndpoints.Guard(http, async () =>
    {
        var caller = Caller(http); _ = DeviceHttpBudget.Current(budgets, clock.GetUtcNow()); var original = DeviceBrowserEndpoints.ReadOriginal(http, protection, new(caller));
        if (original is null || clock.GetUtcNow() >= original.RecoveryExpiresAt) return DeviceBrowserEndpoints.Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var result = await devices.ResolveBrowserRemovalAsync(caller, original.Request, http.RequestAborted).ConfigureAwait(false);
        if (result.Original is null) return DeviceBrowserEndpoints.Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var current = await CurrentOriginalAsync(http, caller, original, result.Context).ConfigureAwait(false);
        return OriginalView(original, current);
    });
    internal Task<IResult> ResultAsync(HttpContext http) => DeviceBrowserEndpoints.Guard(http, async () =>
    {
        if (!http.Request.HasJsonContentType()) return DeviceBrowserEndpoints.Problem(415, "VALIDATION_ERROR");
        var budget = DeviceHttpBudget.Current(budgets, clock.GetUtcNow()); var command = await IdentityJsonCommand.ReadAsync<ResultCommand>(http, budget.MaximumRequestBytes).ConfigureAwait(false);
        var caller = Caller(http); var original = DeviceBrowserEndpoints.ReadOriginal(http, protection, new(caller));
        if (command.RequestId == Guid.Empty) return DeviceBrowserEndpoints.Problem(400, "VALIDATION_ERROR");
        if (original is null || original.Request.RequestId != command.RequestId || clock.GetUtcNow() >= original.RecoveryExpiresAt) return DeviceBrowserEndpoints.Problem(404, "NOT_FOUND_OR_NOT_DISCLOSABLE");
        var result = await devices.ReadRemovalResultAsync(caller, original.Request, null, http.RequestAborted).ConfigureAwait(false);
        if (result.Receipt is not { } receipt) return DeviceBrowserEndpoints.Problem(result.ErrorCode == "AUTHENTICATION_FAILED" ? 401 : 409, "RESULT_UNCONFIRMED");
        return Results.Json(new { original.Request.RequestId, operationReference = original.Request.CorrelationId, status = "REVOKED", target = new {
            reference = receipt.DeviceReference, version = receipt.DeviceVersion.ToString(CultureInfo.InvariantCulture), priorTrustVersion = receipt.PriorTrustVersion.ToString(CultureInfo.InvariantCulture),
            trustVersion = receipt.RevokedTrustVersion.ToString(CultureInfo.InvariantCulture) }, receipt.RevokedAt, sessionEffect = "PRESERVE_EXISTING_SESSIONS" });
    });
}
