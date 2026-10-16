using System.Collections.ObjectModel;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Tunner.Identity.Persistence;

public sealed record RegistrationPublishedText(string Key, string VariantRef, string VersionRef, string PublicationRef,
    string Locale, string State, string Text);
public sealed class RegistrationPresentation
{
    private static readonly string[] RequiredKeys = [
        "brand.tunner",
        "registration.heading",
        "registration.description",
        "registration.context.heading",
        "registration.context.description",
        "registration.age.help",
        "registration.birth.label",
        "registration.email.label",
        "registration.password.label",
        "registration.password.help",
        "registration.notices.ack",
        "registration.terms",
        "registration.notices.and",
        "registration.privacy",
        "registration.submit",
        "registration.ready.heading",
        "registration.ready.description",
        "registration.ready.boundary",
        "registration.ready.next",
        "registration.support",
        "registration.working",
        "registration.checkProgress",
        "registration.startAgain",
        "verification.step",
        "verification.heading",
        "verification.description",
        "verification.code.label",
        "verification.expiry",
        "verification.submit",
        "verification.resend",
        "verification.note.heading",
        "verification.note.description",
        "verification.back",
        "registration.error.age",
        "registration.error.changed",
        "registration.error.limited",
        "registration.error.proof",
        "registration.error.unavailable",
        "registration.error.invalid",
        "registration.error.uncertain",
        "registration.error.password"
    ];
    public RegistrationPresentation(string locale, IEnumerable<RegistrationPublishedText> content, Uri termsUri, Uri privacyUri, Uri? supportUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale); ArgumentNullException.ThrowIfNull(content);
        foreach (var uri in new[] { termsUri, privacyUri, supportUri }.OfType<Uri>())
            if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)
                throw new ArgumentException("Published registration links require credential-free HTTPS.");
        var items = content.ToDictionary(x => x.Key, StringComparer.Ordinal);
        if (RequiredKeys.Any(key => !items.ContainsKey(key)) || items.Values.Any(x => x.State != "PUBLISHED" || x.Locale != locale ||
            new[] { x.Key, x.VariantRef, x.VersionRef, x.PublicationRef, x.Text }.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("Registration requires effective published content provenance.", nameof(content));
        Locale = locale; Content = new ReadOnlyDictionary<string, RegistrationPublishedText>(items);
        TermsUri = termsUri; PrivacyUri = privacyUri; SupportUri = supportUri;
    }
    public string Locale { get; }
    public IReadOnlyDictionary<string, RegistrationPublishedText> Content { get; }
    public Uri TermsUri { get; }
    public Uri PrivacyUri { get; }
    public Uri? SupportUri { get; }
}
public interface IRegistrationPresentationSource { RegistrationPresentation RequirePublished(RegistrationPolicy policy); }

/// <summary>Explicit server secret resolved by the environment owner; never an endpoint or policy JSON field.</summary>
public sealed class RegistrationNetworkPartitionKey : IDisposable
{
    private readonly byte[] secret;
    public RegistrationNetworkPartitionKey(ReadOnlySpan<byte> key)
    {
        if (key.Length < 32) throw new ArgumentException("An explicit strong network-partition key is required.", nameof(key));
        secret = key.ToArray();
    }
    public string Partition(IPAddress peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        return Convert.ToHexString(HMACSHA256.HashData(secret, peer.MapToIPv6().GetAddressBytes()));
    }
    public void Dispose() => CryptographicOperations.ZeroMemory(secret);
    public override string ToString() => nameof(RegistrationNetworkPartitionKey);
}
