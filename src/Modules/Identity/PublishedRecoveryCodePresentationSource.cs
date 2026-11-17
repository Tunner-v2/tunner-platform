using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;

namespace Tunner.Identity.Persistence;

/// <summary>Server-qualified exact impact selection and deployment read admission; never browser policy or an inferred impact message.</summary>
public sealed record RecoveryCodeContentBinding(string RecoveryCodePolicyVersion, RecoveryCodeImpactFacts Impact,
    string ConfigurationVersionReference, string QualificationReference, string ContentScope, string VariantReference, string ImpactKey,
    EffectivePolicyInterval EffectiveInterval, ContentTransactionReadQualification ReadQualification);
public interface IRecoveryCodeContentBindingSource
{
    ValueTask<RecoveryCodeContentBinding> RequireQualifiedAsync(RecoveryCodePolicy policy, RecoveryCodeImpactFacts facts,
        DateTimeOffset effectiveAt, CancellationToken cancellationToken);
}

/// <summary>Explicit authoritative local binding lookup: no database, network, blocking waits or stale cache.
/// The owner must select this authority contract; asynchronous lookup alone does not establish final currentness.</summary>
public interface IRecoveryCodeLocalContentBindingSource : IRecoveryCodeContentBindingSource
{
    RecoveryCodeContentBinding RequireCurrentLocal(RecoveryCodePolicy policy, RecoveryCodeImpactFacts facts, DateTimeOffset effectiveAt);
}

public sealed class PublishedRecoveryCodePresentationSource(IdentityPersistenceDbContext database, PresentationContentApplication content,
    IRecoveryCodeContentBindingSource bindings, TimeProvider clock) : IRecoveryCodeAsyncPresentationSource
{
    public RecoveryCodePresentation RequirePublished(RecoveryCodePolicy policy, RecoveryCodeImpactFacts facts, DateTimeOffset effectiveAt) =>
        throw new InvalidOperationException("This explicitly selected recovery publication requires asynchronous owner-transaction preparation.");

    public async ValueTask<RecoveryCodePreparedPresentation> PreparePublishedAsync(RecoveryCodePolicy policy, RecoveryCodeImpactFacts facts,
        DateTimeOffset effectiveAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(facts); policy.Validate();
        if (bindings is not IRecoveryCodeLocalContentBindingSource localBindings) throw new InvalidOperationException("An authoritative local recovery binding source is required.");
        var transaction = database.Database.CurrentTransaction?.GetDbTransaction() ?? throw new InvalidOperationException("The current Identity owner transaction is required.");
        var connection = database.Database.GetDbConnection();
        var binding = await bindings.RequireQualifiedAsync(policy, facts, effectiveAt, cancellationToken).ConfigureAwait(false); Require(binding, policy, facts, clock.GetUtcNow());
        var original = JsonSerializer.Serialize(binding);
        var resolved = await content.ResolveInTransactionAsync(new(binding.ContentScope, binding.VariantReference, clock.GetUtcNow()),
            connection, transaction, binding.ReadQualification, cancellationToken).ConfigureAwait(false);
        var projection = resolved.Resolution;
        if (projection.Scope != binding.ContentScope || projection.RequestedVariantReference != binding.VariantReference ||
            string.IsNullOrWhiteSpace(projection.Locale) || string.IsNullOrWhiteSpace(projection.EffectiveVariantReference) || projection.Content.Any(x => x.Key != x.Value.Key ||
                x.Value.Locale != projection.Locale || x.Value.VariantReference != projection.EffectiveVariantReference)) throw new InvalidOperationException("Current recovery publication provenance is unavailable.");
        var texts = projection.Content.Values.Select(x => new RegistrationPublishedText(x.Key, x.VariantReference, x.VersionReference, x.PublicationReference, x.Locale, "PUBLISHED", x.Text)).ToArray();
        var current = await bindings.RequireQualifiedAsync(policy, facts, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var at = clock.GetUtcNow(); Require(current, policy, facts, at);
        if (JsonSerializer.Serialize(current) != original || !ReferenceEquals(database.Database.CurrentTransaction?.GetDbTransaction(), transaction))
            throw new InvalidOperationException("Recovery publication qualification changed.");
        var profileWitness = content.RequireLocalProfileWitness(resolved, clock.GetUtcNow());
        var a = binding.EffectiveInterval; var b = resolved.Availability; var from = a.From > b.From ? a.From : b.From;
        var includesFrom = (from != a.From || a.IncludesFrom) && (from != b.From || b.IncludesFrom);
        var until = a.Until is null || b.Until is { } end && end < a.Until ? b.Until : a.Until;
        var includesUntil = (until != a.Until || a.IncludesUntil) && (until != b.Until || b.IncludesUntil);
        var interval = new EffectivePolicyInterval(from, includesFrom, until, includesUntil);
        RecoveryCodePresentation presentation;
        try { presentation = new(projection.Locale, texts, binding.ImpactKey, facts.FactsDigest, binding.ConfigurationVersionReference, interval); }
        catch (ArgumentException) { throw new InvalidOperationException("Qualified recovery publication content is unavailable."); }
        var prepared = new RecoveryCodePreparedPresentation(database, transaction, policy, facts, presentation, profileWitness, localBindings, binding);
        if (!prepared.CurrentAt(database, policy, facts, clock.GetUtcNow())) throw new InvalidOperationException("Recovery publication expired during preparation.");
        cancellationToken.ThrowIfCancellationRequested(); return prepared;
    }
    private static void Require(RecoveryCodeContentBinding binding, RecoveryCodePolicy policy, RecoveryCodeImpactFacts facts, DateTimeOffset at)
    {
        if (binding is null || binding.Impact != facts || binding.RecoveryCodePolicyVersion != policy.Version || binding.EffectiveInterval is null || !binding.EffectiveInterval.Contains(at) ||
            binding.ReadQualification is null || new[] { binding.ConfigurationVersionReference, binding.QualificationReference, binding.ContentScope, binding.VariantReference, binding.ImpactKey }.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Exact qualified recovery impact publication is unavailable.");
    }
}
public static class PublishedRecoveryCodePresentationRegistration
{
    /// <summary>Explicit async publication selection only; no policy/native admission/Account workflow is activated.</summary>
    public static IServiceCollection AddTunnerPublishedRecoveryCodePresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] required = [typeof(IdentityPersistenceDbContext), typeof(PresentationContentApplication), typeof(IRecoveryCodeContentBindingSource), typeof(TimeProvider)];
        if (required.Any(type => !services.Any(x => x.ServiceType == type)) || services.Any(x => x.ServiceType == typeof(IRecoveryCodePresentationSource)))
            throw new InvalidOperationException("Recovery publication requires explicit unique Content, binding, clock and Identity owner providers.");
        services.AddScoped<IRecoveryCodePresentationSource, PublishedRecoveryCodePresentationSource>(); return services;
    }
}
