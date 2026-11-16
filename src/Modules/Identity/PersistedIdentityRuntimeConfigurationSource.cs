using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Only current effective publications with immutable Identity validation/approval/effect receipts are runtime authority.
/// Reading a policy never requires an Admin browser session and never changes historical actor/session state.</summary>
public sealed class PersistedIdentityRuntimeConfigurationSource(IServiceScopeFactory scopes) : IAuthenticationPolicySource, IAssurancePolicySource, IAuthenticationContentBindingSource,
    IRegistrationPolicySource, ITotpPolicySource
{
    RegistrationPolicy IRegistrationPolicySource.RequireEffective(DateTimeOffset effectiveAt)
    {
        var value = Read(new(IdentityConfigurationFamily.Registration), effectiveAt).Document.Registration ?? throw new IdentityConfigurationUnavailableException();
        return new RegistrationPolicyCatalog([value]).RequireEffective(effectiveAt);
    }
    TotpPolicy ITotpPolicySource.RequireEffective(DateTimeOffset effectiveAt)
    {
        var value = Read(new(IdentityConfigurationFamily.Totp), effectiveAt).Document.Totp ?? throw new IdentityConfigurationUnavailableException();
        return new TotpPolicyCatalog([value]).RequireEffective(effectiveAt);
    }
    public AuthenticationPolicy RequireEffective(DateTimeOffset effectiveAt)
    {
        var selected = Read(new(IdentityConfigurationFamily.Authentication), effectiveAt);
        var value = selected.Document.Authentication ?? throw new IdentityConfigurationUnavailableException();
        value.Values.Validate(); return value.Values;
    }
    public AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset executedAt, string? expectedVersion = null)
    {
        ArgumentNullException.ThrowIfNull(scope); if (expectedVersion is not null) ArgumentException.ThrowIfNullOrWhiteSpace(expectedVersion);
        var selected = Read(new(IdentityConfigurationFamily.Assurance, scope), executedAt);
        var value = selected.Document.Assurance ?? throw new IdentityConfigurationUnavailableException();
        if (expectedVersion is not null && expectedVersion != value.Metadata.VersionId) throw new AssurancePolicyVersionConflictException();
        // Proposed evidence strings are data. Actual server-approved receipt references replace them at the runtime boundary.
        var metadata = new PolicyVersionCandidate<AssurancePolicyScope>(value.Metadata.VersionId, "ACTIVE", scope, value.Metadata.EffectiveInterval,
            value.Metadata.SupersedesVersionId, [selected.Publication.ApprovalEventReference.ToString("N"), selected.Publication.ApprovalEvidenceReference]);
        return new(metadata, value.RequiredAssurance, value.ProofLifetime, value.MethodAssurances);
    }
    public AuthenticationContentBinding RequireQualified(AuthenticationPolicy policy, DateTimeOffset effectiveAt)
    {
        ArgumentNullException.ThrowIfNull(policy); policy.Validate();
        var selected = Read(new(IdentityConfigurationFamily.AuthenticationContent, AuthenticationPolicyVersion: policy.Version), effectiveAt);
        var value = selected.Document.ContentBinding ?? throw new IdentityConfigurationUnavailableException();
        if (value.AuthenticationPolicyVersion != policy.Version) throw new IdentityConfigurationUnavailableException();
        return value with { QualificationReference = selected.Publication.ApprovalEventReference.ToString("N") };
    }

    private sealed record Selection(IdentityConfigurationDocument Document, IdentityConfigurationPublicationRow Publication);
    private Selection Read(IdentityConfigurationScope requested, DateTimeOffset at)
    {
        try
        {
            requested.Require(); var key = requested.Key;
            using var scope = scopes.CreateScope(); var database = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
            if (database.Database.CurrentTransaction is not null || database.ChangeTracker.HasChanges()) throw new IdentityConfigurationUnavailableException();
            // Ask the indexed owner store for only current leaves. Historical successors still permanently
            // supersede their predecessors, including after expiry; none are materialized in the request path.
            var eligible = database.Set<IdentityConfigurationPublicationRow>().AsNoTracking().Where(x => x.ScopeKey == key &&
                (x.EffectiveFrom < at || x.EffectiveFrom == at && x.IncludesFrom) &&
                (x.EffectiveUntil == null || x.EffectiveUntil > at || x.EffectiveUntil == at && x.IncludesUntil) &&
                !database.Set<IdentityConfigurationPublicationRow>().Any(y => y.SupersedesPublicationReference == x.Id &&
                    (y.EffectiveFrom < at || y.EffectiveFrom == at && y.IncludesFrom))).Take(2).ToArray();
            var candidates = eligible.Select(x => new PolicyVersionCandidate<string>(x.Id.ToString("N"), "PUBLISHED", key,
                new(x.EffectiveFrom, x.IncludesFrom, x.EffectiveUntil, x.IncludesUntil), x.SupersedesPublicationReference?.ToString("N"),
                [x.ApprovalEventReference.ToString("N"), x.ApprovalEvidenceReference]));
            var resolved = EffectivePolicyVersionResolver.Resolve(at, key, candidates, static (a, b) => a == b, static candidate => candidate.Status == "PUBLISHED");
            if (resolved.SelectedVersion is null) throw new IdentityConfigurationUnavailableException();
            var publication = eligible.Single(x => x.Id.ToString("N") == resolved.SelectedVersion.VersionId);
            if (!database.Database.SqlQuery<bool>($"SELECT identity.runtime_configuration_valid_publication({publication.Id}) AS \"Value\"").Single())
                throw new IdentityConfigurationUnavailableException();
            var version = database.Set<IdentityConfigurationVersionRow>().AsNoTracking().SingleOrDefault(x => x.Id == publication.VersionReference)
                ?? throw new IdentityConfigurationUnavailableException();
            var document = IdentityConfigurationDocument.Read(version.Payload);
            if (version.ScopeKey != key || document.Scope != requested || document.Digest != version.Digest || document.Version != version.PolicyVersionReference ||
                !document.Interval.Contains(at)) throw new IdentityConfigurationUnavailableException();
            return new(document, publication);
        }
        catch (Exception error) when (error is DbException or TimeoutException or JsonException or ArgumentException or InvalidOperationException)
        { throw new IdentityConfigurationUnavailableException(); }
    }
}

public static class IdentityRuntimeConfigurationRegistration
{
    /// <summary>Separate opt-in: registering this port can activate Registration in a composing User host.</summary>
    public static IServiceCollection AddTunnerPersistedRegistrationConfiguration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(x => x.ServiceType == typeof(IRegistrationPolicySource))) throw new InvalidOperationException("Registration configuration source selection must be explicit and unique.");
        services.TryAddSingleton<PersistedIdentityRuntimeConfigurationSource>();
        services.AddSingleton<IRegistrationPolicySource>(provider => provider.GetRequiredService<PersistedIdentityRuntimeConfigurationSource>()); return services;
    }
    /// <summary>Separate opt-in: registering this port can activate TOTP in a composing User host.</summary>
    public static IServiceCollection AddTunnerPersistedTotpConfiguration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(x => x.ServiceType == typeof(ITotpPolicySource))) throw new InvalidOperationException("TOTP configuration source selection must be explicit and unique.");
        services.TryAddSingleton<PersistedIdentityRuntimeConfigurationSource>();
        services.AddSingleton<ITotpPolicySource>(provider => provider.GetRequiredService<PersistedIdentityRuntimeConfigurationSource>()); return services;
    }
    /// <summary>Explicit source selection. Existing providers must be removed intentionally; there is no test or production fallback.</summary>
    public static IServiceCollection AddTunnerPersistedIdentityRuntimeConfiguration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] sources = [typeof(IAuthenticationPolicySource), typeof(IAssurancePolicySource), typeof(IAuthenticationContentBindingSource)];
        if (sources.Any(type => services.Any(item => item.ServiceType == type)))
            throw new InvalidOperationException("Identity runtime configuration source selection must be explicit and unique.");
        services.AddScoped<IdentityRuntimeConfigurationApplication>();
        services.AddSingleton<PersistedIdentityRuntimeConfigurationSource>();
        services.AddSingleton<IAuthenticationPolicySource>(provider => provider.GetRequiredService<PersistedIdentityRuntimeConfigurationSource>());
        services.AddSingleton<IAssurancePolicySource>(provider => provider.GetRequiredService<PersistedIdentityRuntimeConfigurationSource>());
        services.AddSingleton<IAuthenticationContentBindingSource>(provider => provider.GetRequiredService<PersistedIdentityRuntimeConfigurationSource>());
        return services;
    }
}
