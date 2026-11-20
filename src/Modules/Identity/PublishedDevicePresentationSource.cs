using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;

namespace Tunner.Identity.Persistence;

public sealed record DeviceContentBinding(string AuthenticationPolicyVersion, string DevicePolicyVersion,
    string ConfigurationVersionReference, string QualificationReference, string ContentScope, string VariantReference,
    EffectivePolicyInterval EffectiveInterval, ContentTransactionReadQualification ReadQualification);

/// <summary>Explicit authoritative local binding: no database/network I/O, blocking waits or stale cache.</summary>
public interface IDeviceLocalContentBindingSource
{
    DeviceContentBinding RequireCurrentLocal(AuthenticationPolicy authenticationPolicy, string devicePolicyVersion, DateTimeOffset effectiveAt);
}

/// <summary>Optional owner-transaction preparation; the ordinary local presentation contract is unchanged.</summary>
public interface IDeviceAsyncPresentationSource : IDevicePresentationSource
{
    ValueTask<DevicePreparedPresentation> PreparePublishedAsync(AuthenticationPolicy authenticationPolicy, string devicePolicyVersion,
        CancellationToken cancellationToken);
}

/// <summary>Request-scoped publication witness. A browser point-in-time read cannot qualify a native transaction.</summary>
public sealed class DevicePreparedPresentation
{
    private readonly IdentityPersistenceDbContext owner;
    private readonly DbTransaction? transaction;
    private readonly string authenticationSnapshot;
    private readonly string deviceVersion;
    private readonly IDeviceLocalContentBindingSource bindings;
    private readonly string bindingSnapshot;
    private readonly ContentTransactionProfileWitness profile;
    private readonly EffectivePolicyInterval availability;
    internal DevicePreparedPresentation(IdentityPersistenceDbContext owner, DbTransaction? transaction, AuthenticationPolicy authenticationPolicy,
        string devicePolicyVersion, IDeviceLocalContentBindingSource bindings, DeviceContentBinding binding,
        ContentTransactionProfileWitness profile, EffectivePolicyInterval availability, DevicePresentation presentation)
    {
        this.owner = owner; this.transaction = transaction; authenticationSnapshot = JsonSerializer.Serialize(authenticationPolicy);
        deviceVersion = devicePolicyVersion; this.bindings = bindings; bindingSnapshot = JsonSerializer.Serialize(binding);
        this.profile = profile; this.availability = availability; Presentation = presentation;
    }
    public DevicePresentation Presentation { get; }
    internal bool IsOwnerTransaction(IdentityPersistenceDbContext database) => transaction is not null && ReferenceEquals(owner, database) &&
        ReferenceEquals(database.Database.CurrentTransaction?.GetDbTransaction(), transaction) &&
        ReferenceEquals(transaction.Connection, database.Database.GetDbConnection());
    internal bool CurrentAt(IdentityPersistenceDbContext database, AuthenticationPolicy authenticationPolicy, string devicePolicyVersion, DateTimeOffset at)
    {
        try
        {
            if (!ReferenceEquals(owner, database) || authenticationSnapshot != JsonSerializer.Serialize(authenticationPolicy) || deviceVersion != devicePolicyVersion ||
                !availability.Contains(at) || !profile.CurrentAt(at)) return false;
            // A detached browser snapshot is never promoted into an owner/native witness.
            if (transaction is null ? owner.Database.CurrentTransaction is not null :
                !ReferenceEquals(owner.Database.CurrentTransaction?.GetDbTransaction(), transaction) ||
                !ReferenceEquals(transaction.Connection, owner.Database.GetDbConnection())) return false;
            var current = bindings.RequireCurrentLocal(authenticationPolicy, devicePolicyVersion, at);
            return current is not null && current.EffectiveInterval is not null && current.EffectiveInterval.Contains(at) &&
                JsonSerializer.Serialize(current) == bindingSnapshot;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException) { return false; }
    }
    internal DevicePreparedPresentation ForBrowser(AuthenticationPolicy authenticationPolicy, string devicePolicyVersion,
        IDeviceLocalContentBindingSource source, DeviceContentBinding binding) =>
        new(owner, null, authenticationPolicy, devicePolicyVersion, source, binding, profile, availability, Presentation);
    public override string ToString() => nameof(DevicePreparedPresentation);
}

/// <summary>Presentation-only adapter. Device permission, collection, trust, proof and session effects retain their owners.</summary>
public sealed class PublishedDevicePresentationSource(IdentityPersistenceDbContext database, PresentationContentApplication content,
    IDeviceLocalContentBindingSource bindings, TimeProvider clock) : IDeviceAsyncPresentationSource
{
    private DevicePreparedPresentation? nativePreparation;
    public DevicePresentation RequirePublished(AuthenticationPolicy authenticationPolicy, string devicePolicyVersion) =>
        throw new InvalidOperationException("This explicitly selected Device publication requires asynchronous preparation.");
    public async ValueTask<DevicePreparedPresentation> PreparePublishedAsync(AuthenticationPolicy authenticationPolicy, string devicePolicyVersion,
        CancellationToken cancellationToken)
    {
        if (database.Database.CurrentTransaction is not null)
        {
            // Repeat owner resolution must not acquire new Content keys after the Device lock.
            // A transaction-pinned witness is reused only while all its current local authorities still match.
            if (nativePreparation?.IsOwnerTransaction(database) == true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!nativePreparation.CurrentAt(database, authenticationPolicy, devicePolicyVersion, clock.GetUtcNow()))
                    throw new InvalidOperationException("Prepared Device publication qualification changed or expired.");
                return nativePreparation;
            }
            nativePreparation = (await PrepareInTransactionAsync(authenticationPolicy, devicePolicyVersion, cancellationToken).ConfigureAwait(false)).Prepared;
            return nativePreparation;
        }
        // Browser selection has point-in-time read semantics. Only this newly owned, read-only transaction is disposed here.
        (DevicePreparedPresentation Prepared, DeviceContentBinding Binding) read;
        await using (var transaction = await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            read = await PrepareInTransactionAsync(authenticationPolicy, devicePolicyVersion, cancellationToken).ConfigureAwait(false);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        var detached = read.Prepared.ForBrowser(authenticationPolicy, devicePolicyVersion, bindings, read.Binding);
        if (!detached.CurrentAt(database, authenticationPolicy, devicePolicyVersion, clock.GetUtcNow()))
            throw new InvalidOperationException("Device browser publication changed or expired.");
        cancellationToken.ThrowIfCancellationRequested(); return detached;
    }
    private async Task<(DevicePreparedPresentation Prepared, DeviceContentBinding Binding)> PrepareInTransactionAsync(AuthenticationPolicy authenticationPolicy,
        string devicePolicyVersion, CancellationToken cancellationToken)
    {
        var transaction = database.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("The current Identity owner transaction is required.");
        var binding = Require(authenticationPolicy, devicePolicyVersion, clock.GetUtcNow());
        var resolved = await content.ResolveInTransactionAsync(new(binding.ContentScope, binding.VariantReference, clock.GetUtcNow()),
            database.Database.GetDbConnection(), transaction, binding.ReadQualification, cancellationToken).ConfigureAwait(false);
        var resolution = resolved.Resolution;
        if (resolution.Scope != binding.ContentScope || resolution.RequestedVariantReference != binding.VariantReference ||
            string.IsNullOrWhiteSpace(resolution.Locale) || string.IsNullOrWhiteSpace(resolution.EffectiveVariantReference) ||
            resolution.Content.Any(x => x.Key != x.Value.Key || x.Value.Locale != resolution.Locale || x.Value.VariantReference != resolution.EffectiveVariantReference))
            throw new InvalidOperationException("Published Device content provenance is unavailable.");
        var presentation = Project(resolution.Locale, resolution.Content.Values.Select(x => new RegistrationPublishedText(x.Key, x.VariantReference,
            x.VersionReference, x.PublicationReference, x.Locale, "PUBLISHED", x.Text)), AuthenticationIdentity.Digest(JsonSerializer.Serialize(binding)));
        var current = Require(authenticationPolicy, devicePolicyVersion, clock.GetUtcNow());
        if (JsonSerializer.Serialize(current) != JsonSerializer.Serialize(binding) ||
            !ReferenceEquals(database.Database.CurrentTransaction?.GetDbTransaction(), transaction))
            throw new InvalidOperationException("Device publication qualification changed.");
        var profile = content.RequireLocalProfileWitness(resolved, clock.GetUtcNow());
        var interval = PublishedPresentationAvailability.Intersect(binding.EffectiveInterval, resolved.Availability);
        var prepared = new DevicePreparedPresentation(database, transaction, authenticationPolicy, devicePolicyVersion, bindings, binding, profile, interval, presentation);
        if (!prepared.CurrentAt(database, authenticationPolicy, devicePolicyVersion, clock.GetUtcNow()))
            throw new InvalidOperationException("Device publication changed or expired during preparation.");
        cancellationToken.ThrowIfCancellationRequested(); return (prepared, binding);
    }
    private DeviceContentBinding Require(AuthenticationPolicy authenticationPolicy, string devicePolicyVersion, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(authenticationPolicy); authenticationPolicy.Validate(); ArgumentException.ThrowIfNullOrWhiteSpace(devicePolicyVersion);
        var binding = bindings.RequireCurrentLocal(authenticationPolicy, devicePolicyVersion, at);
        if (binding is null || binding.AuthenticationPolicyVersion != authenticationPolicy.Version || binding.DevicePolicyVersion != devicePolicyVersion ||
            binding.EffectiveInterval is null || !binding.EffectiveInterval.Contains(at) || binding.ReadQualification is null ||
            new[] { binding.ConfigurationVersionReference, binding.QualificationReference, binding.ContentScope, binding.VariantReference }.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Exact qualified Device content configuration is unavailable.");
        return binding;
    }
    private static DevicePresentation Project(string locale, IEnumerable<RegistrationPublishedText> texts, string bindingQualificationDigest)
    {
        try { return new(locale, texts) { BindingQualificationDigest = bindingQualificationDigest }; }
        catch (ArgumentException) { throw new InvalidOperationException("Complete published Device content is unavailable."); }
    }
}

public static class PublishedDevicePresentationRegistration
{
    /// <summary>Explicit publication selection only. Device routes, policy and trust-removal activation remain separate.</summary>
    public static IServiceCollection AddTunnerPublishedDevicePresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Type[] required = [typeof(IdentityPersistenceDbContext), typeof(PresentationContentApplication),
            typeof(IDeviceLocalContentBindingSource), typeof(TimeProvider)];
        if (required.Any(type => !services.Any(x => x.ServiceType == type)) || services.Any(x => x.ServiceType == typeof(IDevicePresentationSource)))
            throw new InvalidOperationException("Published Device content requires explicit unique Content, binding, Identity and clock providers.");
        services.AddScoped<IDevicePresentationSource, PublishedDevicePresentationSource>(); return services;
    }
}
