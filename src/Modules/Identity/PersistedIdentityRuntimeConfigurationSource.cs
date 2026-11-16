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
    IRegistrationPolicySource, ITotpPolicySource, IRegistrationContentBindingSource, IPasskeyPolicySource, ITotpContentBindingSource, IPasskeyContentBindingSource, IStepUpContentBindingSource, IRecoveryCodePolicySource
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
    PasskeyPolicy IPasskeyPolicySource.RequireEffective(DateTimeOffset effectiveAt)
    {
        var value = Read(new(IdentityConfigurationFamily.Passkey), effectiveAt).Document.Passkey ?? throw new IdentityConfigurationUnavailableException();
        return new PasskeyPolicyCatalog([new(value.Metadata, value.Values.ToPolicy())]).RequireEffective(effectiveAt);
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
    public RegistrationContentBinding RequireQualified(RegistrationPolicy policy, DateTimeOffset effectiveAt)
    {
        ArgumentNullException.ThrowIfNull(policy); policy.Validate();
        var selected = Read(new(IdentityConfigurationFamily.RegistrationContent, RegistrationPolicyVersion: policy.Version), effectiveAt);
        var value = selected.Document.RegistrationContent ?? throw new IdentityConfigurationUnavailableException();
        if (value.RegistrationPolicyVersion != policy.Version || value.TermsVersion != policy.TermsVersion || value.PrivacyVersion != policy.PrivacyVersion)
            throw new IdentityConfigurationUnavailableException();
        return value with { QualificationReference = selected.Publication.ApprovalEventReference.ToString("N") };
    }

    public TotpContentBinding RequireQualified(TotpPolicy policy, DateTimeOffset effectiveAt)
    {
        ArgumentNullException.ThrowIfNull(policy); policy.Validate();
        var selected = Read(new(IdentityConfigurationFamily.TotpContent, TotpPolicyVersion: policy.Version), effectiveAt);
        var value = selected.Document.TotpContent ?? throw new IdentityConfigurationUnavailableException();
        if (value.TotpPolicyVersion != policy.Version) throw new IdentityConfigurationUnavailableException();
        return value with { QualificationReference = selected.Publication.ApprovalEventReference.ToString("N") };
    }
    public PasskeyContentBinding RequireQualified(PasskeyPolicy policy, DateTimeOffset effectiveAt)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var selected = Read(new(IdentityConfigurationFamily.PasskeyContent, PasskeyPolicyVersion: policy.Version), effectiveAt);
        var value = selected.Document.PasskeyContent ?? throw new IdentityConfigurationUnavailableException();
        if (value.PasskeyPolicyVersion != policy.Version) throw new IdentityConfigurationUnavailableException();
        return value with { QualificationReference = selected.Publication.ApprovalEventReference.ToString("N") };
    }

    public StepUpContentBinding RequireQualified(AssurancePolicyVersion policy, DateTimeOffset effectiveAt)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _ = new AssurancePolicyCatalog([policy]).RequireEffective(policy.Metadata.Scope, effectiveAt, policy.Metadata.VersionId);
        var selected = Read(new(IdentityConfigurationFamily.StepUpContent, policy.Metadata.Scope, AssurancePolicyVersion: policy.Metadata.VersionId), effectiveAt);
        var value = selected.Document.StepUpContent ?? throw new IdentityConfigurationUnavailableException(); value.RequireCurrent(policy, effectiveAt);
        return value with { QualificationReference = selected.Publication.ApprovalEventReference.ToString("N") };
    }

    RecoveryCodePolicyVersion IRecoveryCodePolicySource.RequireEffective(DateTimeOffset effectiveAt)
    {
        var value = Read(new(IdentityConfigurationFamily.RecoveryCodes), effectiveAt).Document.RecoveryCodes ?? throw new IdentityConfigurationUnavailableException();
        return new RecoveryCodePolicyCatalog([new(value.Metadata, value.Values.ToPolicy())]).RequireEffective(effectiveAt);
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
    /// <summary>Separate policy opt-in; the composing host still requires an explicitly admitted native crypto profile and all other owners.</summary>
    public static IServiceCollection AddTunnerPersistedRecoveryCodeConfiguration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(x => x.ServiceType == typeof(IRecoveryCodePolicySource))) throw new InvalidOperationException("Recovery-code configuration source selection must be explicit and unique.");
        services.TryAddSingleton<PersistedIdentityRuntimeConfigurationSource>();
        services.AddSingleton<IRecoveryCodePolicySource>(provider => provider.GetRequiredService<PersistedIdentityRuntimeConfigurationSource>()); return services;
    }
    /// <summary>Binding-only opt-in; no assurance, presentation, proof or operation owner is registered.</summary>
    public static IServiceCollection AddTunnerPersistedStepUpContentBinding(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(x => x.ServiceType == typeof(IStepUpContentBindingSource))) throw new InvalidOperationException("Operation proof content binding source selection must be explicit and unique.");
        services.TryAddSingleton<PersistedIdentityRuntimeConfigurationSource>();
        services.AddSingleton<IStepUpContentBindingSource>(provider => provider.GetRequiredService<PersistedIdentityRuntimeConfigurationSource>()); return services;
    }
    /// <summary>Binding-only opt-in; no TOTP policy, presentation or workflow is registered.</summary>
    public static IServiceCollection AddTunnerPersistedTotpContentBinding(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(x => x.ServiceType == typeof(ITotpContentBindingSource))) throw new InvalidOperationException("TOTP content binding source selection must be explicit and unique.");
        services.TryAddSingleton<PersistedIdentityRuntimeConfigurationSource>();
        services.AddSingleton<ITotpContentBindingSource>(provider => provider.GetRequiredService<PersistedIdentityRuntimeConfigurationSource>()); return services;
    }
    /// <summary>Binding-only opt-in; no Passkey policy, presentation or workflow is registered.</summary>
    public static IServiceCollection AddTunnerPersistedPasskeyContentBinding(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(x => x.ServiceType == typeof(IPasskeyContentBindingSource))) throw new InvalidOperationException("Passkey content binding source selection must be explicit and unique.");
        services.TryAddSingleton<PersistedIdentityRuntimeConfigurationSource>();
        services.AddSingleton<IPasskeyContentBindingSource>(provider => provider.GetRequiredService<PersistedIdentityRuntimeConfigurationSource>()); return services;
    }
    /// <summary>Separate opt-in: composing User hosts may activate Passkeys when this policy port is intentionally registered.</summary>
    public static IServiceCollection AddTunnerPersistedPasskeyConfiguration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(x => x.ServiceType == typeof(IPasskeyPolicySource))) throw new InvalidOperationException("Passkey configuration source selection must be explicit and unique.");
        services.TryAddSingleton<PersistedIdentityRuntimeConfigurationSource>();
        services.AddSingleton<IPasskeyPolicySource>(provider => provider.GetRequiredService<PersistedIdentityRuntimeConfigurationSource>()); return services;
    }
    /// <summary>Binding-only opt-in. This does not register Registration policy, presentation or workflow providers.</summary>
    public static IServiceCollection AddTunnerPersistedRegistrationContentBinding(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(x => x.ServiceType == typeof(IRegistrationContentBindingSource))) throw new InvalidOperationException("Registration content binding source selection must be explicit and unique.");
        services.TryAddSingleton<PersistedIdentityRuntimeConfigurationSource>();
        services.AddSingleton<IRegistrationContentBindingSource>(provider => provider.GetRequiredService<PersistedIdentityRuntimeConfigurationSource>()); return services;
    }
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
