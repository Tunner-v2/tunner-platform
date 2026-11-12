using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Logging;
using Tunner.Identity.Persistence;
using Tunner.Platform.Bff;
using Tunner.Platform.Configuration;
using Tunner.Platform.Secrets;

namespace Tunner.User.Bff;

/// <summary>Explicit admitted native transport inputs. This registers no provider policy or ordinary sign-in scheme.</summary>
public sealed class FederationOidcRegistration
{
    private readonly string snapshot;
    internal FederationProfileVersion Profile { get; }
    internal EngineeringSecretReference SecretReference { get; }
    internal string SecretField { get; }
    internal FederationOidcTransport Transport { get; }
    internal int MaximumCommandBytes { get; }
    internal int MaximumBodyBytes { get; }
    internal int MaximumFields { get; }
    internal int MaximumKeyLength { get; }
    internal int MaximumValueLength { get; }
    internal string CallbackPath { get; }
    internal string ChallengePath { get; }
    internal string Scheme => Profile.Values.TransportScheme;
    internal FederationProviderKind Provider => Profile.Values.Provider;

    public FederationOidcRegistration(FederationProfileVersion admittedProfile, Uri metadataAddress,
        EngineeringSecretReference secretReference, string secretField, TimeSpan authenticationTimeout,
        TimeSpan nonceLifetime, TimeSpan clockSkew, int maximumTokenBytes, int maximumCallbackBodyBytes,
        int maximumFormFields, int maximumFormKeyLength, int maximumFormValueLength, int maximumCommandBytes,
        string correlationCookieName, string nonceCookieName)
    {
        ArgumentNullException.ThrowIfNull(admittedProfile); ArgumentNullException.ThrowIfNull(metadataAddress);
        ArgumentNullException.ThrowIfNull(secretReference);
        if (string.IsNullOrWhiteSpace(secretField) || secretField.Any(char.IsControl) ||
            admittedProfile.Metadata.ApprovalEvidenceReferences.Count == 0 || admittedProfile.Metadata.Status != "ACTIVE" ||
            admittedProfile.Values.TransportScheme is "Tunner.User.Bff.Session" or "Tunner.Admin.Bff.Session" or FederationOidcTransport.NoSignInScheme)
            throw new ArgumentException("Explicit approved native transport inputs are required.");
        foreach (var bound in new[] { maximumCallbackBodyBytes, maximumFormFields, maximumFormKeyLength, maximumFormValueLength, maximumCommandBytes })
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bound);
        var values = admittedProfile.Values;
        var issuers = values.AdmittedIssuerAliases.Append(values.Issuer).Distinct(StringComparer.Ordinal).ToArray();
        if (values.Provider == FederationProviderKind.Google &&
            (values.Issuer != "https://accounts.google.com" || issuers.Any(value => value is not "https://accounts.google.com" and not "accounts.google.com")))
            throw new ArgumentException("Only explicitly qualified exact Google issuer mappings are admitted.");
        if (values.Provider == FederationProviderKind.Microsoft && issuers.Any(value => value != values.Issuer))
            throw new ArgumentException("Microsoft transport requires the exact admitted issuer.");
        Profile = admittedProfile; SecretReference = secretReference; SecretField = secretField;
        var segment = values.Provider == FederationProviderKind.Google ? "google" : "microsoft";
        CallbackPath = "/authentication/federation/" + segment + "/callback";
        ChallengePath = "/authentication/federation/" + segment + "/challenge";
        MaximumBodyBytes = maximumCallbackBodyBytes; MaximumFields = maximumFormFields;
        MaximumKeyLength = maximumFormKeyLength; MaximumValueLength = maximumFormValueLength; MaximumCommandBytes = maximumCommandBytes;
        Transport = new(Scheme, values.Issuer, metadataAddress.OriginalString, values.ClientId, values.Origin + CallbackPath,
            values.AllowedContinuations, authenticationTimeout, nonceLifetime, clockSkew, maximumTokenBytes,
            correlationCookieName, nonceCookieName, issuers);
        snapshot = Snapshot(admittedProfile);
    }

    internal bool Current(HttpContext http)
    {
        try
        {
            var at = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
            var current = http.RequestServices.GetRequiredService<IFederationProfileSource>().RequireCurrent(Provider, at);
            return current.Metadata.Status == "ACTIVE" && current.Metadata.EffectiveInterval.Contains(at) &&
                StringComparer.Ordinal.Equals(snapshot, Snapshot(current));
        }
        catch (Exception) { return false; }
    }

    // A stable value comparison survives reconstructed sources. No reference identity or namespace rekeying.
    private static string Snapshot(FederationProfileVersion value) => JsonSerializer.Serialize(new
    {
        value.Metadata.VersionId, value.Metadata.Status, value.Metadata.Scope, value.Metadata.EffectiveInterval, value.Metadata.SupersedesVersionId,
        Evidence = value.Metadata.ApprovalEvidenceReferences.Order(StringComparer.Ordinal), value.Values
    });
    public override string ToString() => nameof(FederationOidcRegistration);
}

public static class FederationOidcRegistrationExtensions
{
    public static IServiceCollection AddTunnerFederationOidc(this IServiceCollection services, FederationOidcRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(registration);
        var existing = services.Where(value => value.ServiceType == typeof(FederationOidcRegistration)).ToArray();
        if (existing.Any(value => value.ImplementationInstance is not FederationOidcRegistration) ||
            existing.Select(value => (FederationOidcRegistration)value.ImplementationInstance!).Any(value =>
                value.Provider == registration.Provider || value.Scheme == registration.Scheme))
            throw new InvalidOperationException("One exact transport per admitted provider and scheme is required.");
        // Framework protocol diagnostics can run before terminal events. Never activate with artifact logging enabled.
        if (IdentityModelEventSource.ShowPII || IdentityModelEventSource.LogCompleteSecurityArtifact)
            throw new InvalidOperationException("Private native protocol diagnostics must be disabled before activation.");
        services.AddLogging(logging =>
        {
            logging.AddFilter("Microsoft.AspNetCore.Authentication.OpenIdConnect", LogLevel.None);
            logging.AddFilter("Microsoft.IdentityModel", LogLevel.None);
            logging.AddFilter("System.IdentityModel", LogLevel.None);
        });
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(registration);
        services.AddAuthentication().AddOpenIdConnect(registration.Scheme, _ => { });
        services.AddOptions<OpenIdConnectOptions>(registration.Scheme).Configure<TimeProvider>((options, clock) =>
            registration.Transport.Configure(options, clock, registration.Profile.Values.Version, registration.Current,
                async (http, ct) =>
                {
                    using var values = await http.RequestServices.GetRequiredService<ISecretStore>().ReadAsync(registration.SecretReference, ct).ConfigureAwait(false);
                    return values.GetRequired(registration.SecretField);
                },
                async (http, facts, ct) =>
                {
                    if (!Guid.TryParseExact(facts.AttemptId, "N", out var attempt)) return null;
                    var result = await http.RequestServices.GetRequiredService<FederationBrowserService>().RecordVerifiedAsync(
                        new(registration.Provider, registration.Profile.Values.Version, attempt, facts.Issuer, facts.Subject, facts.ClientId,
                            facts.AuthenticatedAt, facts.AuthenticationMethods), ct).ConfigureAwait(false);
                    return result.Continuation;
                },
                (_, _, _) => Task.FromResult<string?>(registration.Profile.Values.ContinuationPath),
                registration.Profile.Values.FreshAuthentication is null ? null : registration.Provider));
        services.AddSingleton(new BffProtocolCallback(registration.CallbackPath, registration.MaximumBodyBytes,
            registration.MaximumFields, registration.MaximumKeyLength, registration.MaximumValueLength, async http =>
            {
                http.Response.Headers.CacheControl = "no-store";
                try
                {
                    var provider = http.RequestServices.GetRequiredService<IAuthenticationHandlerProvider>();
                    var handler = await provider.GetHandlerAsync(http, registration.Scheme).ConfigureAwait(false);
                    if (handler is IAuthenticationRequestHandler native && await native.HandleRequestAsync().ConfigureAwait(false)) return;
                }
                catch (Exception) { /* Terminal safe failure. Native material is never serialized or logged here. */ }
                http.Response.StatusCode = StatusCodes.Status303SeeOther;
                http.Response.Headers.Location = registration.Profile.Values.ContinuationPath;
            }));
        return services;
    }
}
