using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Tunner.Identity.Persistence;

internal sealed class DeviceFallbackOwner(IUserStepUpOperationSource owner)
{ internal IUserStepUpOperationSource Owner { get; } = owner; }

/// <summary>Existing shared proof dispatch, with exact protected Device selection and unchanged fallback outcomes.</summary>
internal sealed class DeviceStepUpOwner(DeviceApplication devices, IdentityPersistenceDbContext database,
    IHttpContextAccessor accessor, IDataProtectionProvider protection, AuthenticationPartitionKey partitions,
    AuthenticationEndpoints.Surface surface, IDeviceHttpBudgetSource budgets, IDevicePresentationSource publications,
    IDeviceEvidencePolicySource devicePolicies, IAuthenticationPolicySource authenticationPolicies,
    IEnumerable<DeviceFallbackOwner> fallback, TimeProvider clock)
    : IUserStepUpOperationOutcomeSource, IUserStepUpBrowserCompletionSource
{
    private StepUpCaller Caller(HttpContext http) => StepUpCaller.FromPrincipal(http.User, surface.Origin,
        partitions.Network(http.Connection.RemoteIpAddress ?? throw new ArgumentException("A current peer is required.")));
    private async ValueTask<DevicePreparedPresentation?> PreparePublicationAsync(CancellationToken ct)
    {
        var auth = authenticationPolicies.RequireEffective(clock.GetUtcNow()); auth.Validate();
        var policy = devicePolicies.RequireCurrent(surface.Origin, clock.GetUtcNow()); policy.Require(surface.Origin, clock.GetUtcNow());
        return await DeviceBrowserEndpoints.PreparePublicationAsync(publications, auth, policy.Metadata.VersionId, ct).ConfigureAwait(false);
    }
    private DeviceBrowserExecutionValidity Witness(HttpContext http, DeviceBrowserEndpoints.Original original,
        UserStepUpOperationContext context, DevicePreparedPresentation? prepared) =>
        new(http, protection, budgets, publications, devicePolicies, authenticationPolicies, original, context, prepared, database);
    public async Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken ct)
    {
        var http = accessor.HttpContext ?? throw new InvalidOperationException("A current User request is required.");
        var other = fallback.SingleOrDefault() is { } f ? await f.Owner.ResolveAsync(actor, ct).ConfigureAwait(false) : null;
        var original = DeviceBrowserEndpoints.ReadOriginal(http, protection, actor);
        if (original is null) return other;
        var caller = Caller(http); var at = clock.GetUtcNow();
        if (caller.AccountId != actor.AccountId || caller.SessionId != actor.SessionId || caller.Origin != actor.Origin) return null;
        if (other is not null && StepUpBrowserService.Digest(other) != original.FallbackDigest) return other;
        if (at < original.CreatedAt || at >= original.ExpiresAt) return other;
        // In a native owner transaction, Content locks must precede ResolveBrowserRemovalAsync's Device lock.
        var prepared = await PreparePublicationAsync(ct).ConfigureAwait(false);
        var resolved = await devices.ResolveBrowserRemovalAsync(caller, original.Request, ct).ConfigureAwait(false);
        if (resolved.Context is not { } context || StepUpBrowserService.Digest(context) != original.BindingDigest) return null;
        var witness = Witness(http, original, context, prepared);
        // The outer Session router compares this same candidate with the baseline it captured at its Confirm.
        // Changing its selector must not manufacture a different underlying fallback during that comparison.
        // Execution still requires the original selector and exact live proof view at the final decision.
        if (!witness.SelectionCurrentAt(caller, context.Binding, clock.GetUtcNow(), requireSessionSelection: false)) return null;
        if (context.Completed && other is not null &&
            (HttpMethods.IsGet(http.Request.Method) && http.Request.Path == "/authentication/step-up/options" ||
             StepUpBrowserService.BrowserBoundTo(http, protection, clock.GetUtcNow(), other, original.AuthenticationVersion))) return other;
        // Native proof preparation and generic execution re-resolve inside their existing Identity transaction.
        // They must retain the exact live proof view; no second transaction or replacement original is created.
        if (database.Database.CurrentTransaction is not null && !witness.CurrentAt(caller, context.Binding, clock.GetUtcNow())) return null;
        return context;
    }
    public async Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext context, CancellationToken ct)
    { using var result = await ApplyWithOutcomeAsync(actor, operation, context, ct).ConfigureAwait(false); return result.Applied; }
    public async Task<UserStepUpOperationApplyOutcome> ApplyWithOutcomeAsync(UserStepUpActor actor, UserStepUpOperationContext operation,
        IdentityPersistenceDbContext context, CancellationToken ct)
    {
        if (operation.Binding.Scope != DeviceApplication.RevokeScope)
        {
            var other = fallback.SingleOrDefault()?.Owner;
            if (other is IUserStepUpOperationOutcomeSource typed) return await typed.ApplyWithOutcomeAsync(actor, operation, context, ct).ConfigureAwait(false);
            return other is not null && await other.ApplyAsync(actor, operation, context, ct).ConfigureAwait(false)
                ? UserStepUpOperationApplyOutcome.SessionContinues() : UserStepUpOperationApplyOutcome.Conflict();
        }
        if (!ReferenceEquals(context, database) || database.Database.CurrentTransaction is null) return UserStepUpOperationApplyOutcome.Conflict();
        var http = accessor.HttpContext ?? throw new InvalidOperationException("A current User request is required.");
        var original = DeviceBrowserEndpoints.ReadOriginal(http, protection, actor); if (original is null) return UserStepUpOperationApplyOutcome.Conflict();
        var prepared = await PreparePublicationAsync(ct).ConfigureAwait(false);
        var caller = Caller(http); var witness = Witness(http, original, operation, prepared);
        if (!witness.CurrentAt(caller, operation.Binding, clock.GetUtcNow())) return UserStepUpOperationApplyOutcome.Conflict();
        using var outcome = await devices.ApplyRemovalAsync(caller, operation, ct).ConfigureAwait(false);
        return outcome.DeviceValidity is { } validity ? outcome.ForDevice(validity with { Browser = witness }) : UserStepUpOperationApplyOutcome.Conflict();
    }
    public Task CompleteBrowserAsync(UserStepUpOperationContext operation, bool signInRequired, CancellationToken ct)
    {
        if (operation.Binding.Scope != DeviceApplication.RevokeScope)
            return fallback.SingleOrDefault()?.Owner is IUserStepUpBrowserCompletionSource hook
                ? hook.CompleteBrowserAsync(operation, signInRequired, ct) : Task.CompletedTask;
        // The immutable original remains available for separately authorized causal result recovery.
        if (signInRequired) throw new InvalidOperationException("Device trust revocation cannot terminate a session in this qualified profile.");
        return Task.CompletedTask;
    }
}

/// <summary>Pure final-clock browser qualification, carried by the existing Device owner outcome.</summary>
internal sealed class DeviceBrowserExecutionValidity(HttpContext http, IDataProtectionProvider protection, IDeviceHttpBudgetSource budgets,
    IDevicePresentationSource publications, IDeviceEvidencePolicySource devicePolicies, IAuthenticationPolicySource authenticationPolicies,
    DeviceBrowserEndpoints.Original original, UserStepUpOperationContext operation, DevicePreparedPresentation? prepared = null,
    IdentityPersistenceDbContext? publicationOwner = null)
{
    internal bool UsesPreparedPublication => prepared is not null;
    internal bool SelectionCurrentAt(StepUpCaller caller, StepUpOperation binding, DateTimeOffset at, bool requireSessionSelection = true)
    {
        if (caller.AccountId != original.AccountId || caller.SessionId != original.ActorSessionId || caller.Origin != original.Origin ||
            http.User.FindFirstValue(ClaimTypes.NameIdentifier) != caller.AccountId || AuthenticationIdentity.SessionId(http.User) != caller.SessionId ||
            at < original.CreatedAt || at >= original.ExpiresAt || binding.Scope != DeviceApplication.RevokeScope ||
            StepUpBrowserService.Digest(operation) != original.BindingDigest || operation.Actor.AccountId != caller.AccountId ||
            operation.Actor.SessionId != caller.SessionId || operation.Actor.Origin != caller.Origin || operation.Binding.OperationId != binding.OperationId ||
            operation.Binding.ContextDigest != binding.ContextDigest || operation.Binding.CorrelationId != binding.CorrelationId ||
            requireSessionSelection && original.SelectionBaseline != SessionRevocationOriginalSelection.Fingerprint(http) ||
            DeviceBrowserEndpoints.ReadOriginal(http, protection, new(caller)) != original) return false;
        var auth = authenticationPolicies.RequireEffective(at); auth.Validate();
        var policy = devicePolicies.RequireCurrent(caller.Origin, at); policy.Require(caller.Origin, at);
        var budget = DeviceHttpBudget.Current(budgets, at);
        // Ordinary owners retain their existing local port. Persisted publications use only their prepared local witness here.
        if (prepared is not null && (publicationOwner is null || !prepared.CurrentAt(publicationOwner, auth, policy.Metadata.VersionId, at))) return false;
        if (prepared is null && publications is IDeviceAsyncPresentationSource) return false;
        var published = prepared?.Presentation ?? DeviceBrowserEndpoints.Publication(publications, auth, policy.Metadata.VersionId);
        return auth.Version == original.AuthenticationVersion && AuthenticationIdentity.Digest(budget.Snapshot) == original.TransportDigest &&
            DeviceBrowserEndpoints.PublicationDigest(published) == original.PublicationDigest;
    }
    internal bool CurrentAt(StepUpCaller caller, StepUpOperation binding, DateTimeOffset at) => SelectionCurrentAt(caller, binding, at) &&
        StepUpBrowserService.BrowserBoundTo(http, protection, at, operation, original.AuthenticationVersion);
}
