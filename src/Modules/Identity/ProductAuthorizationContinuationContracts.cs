using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Tunner.Platform.Policies;

namespace Tunner.Identity.Persistence;

/// <summary>Explicit qualification for private browser continuation; no production transport or retention defaults.</summary>
public sealed record ProductAuthorizationContinuationPolicy(PolicyVersionCandidate<string> Metadata, string Origin,
    string EvidenceReference, string ProcessingReference, string RetentionReference, string Classification,
    TimeSpan Lifetime, int MaximumRequestBytes, int MaximumCookieBytes)
{
    internal string Snapshot => JsonSerializer.Serialize(this);
    internal void Require(string origin, DateTimeOffset at)
    {
        if (Metadata is null || Metadata.Scope != origin || Metadata.Status != "ACTIVE" ||
            string.IsNullOrWhiteSpace(Metadata.VersionId) || !Metadata.EffectiveInterval.Contains(at) || Origin != origin ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            origin != uri.GetLeftPart(UriPartial.Authority) ||
            new[] { EvidenceReference, ProcessingReference, RetentionReference }.Any(string.IsNullOrWhiteSpace) ||
            Classification is not ("INTERNAL" or "CONFIDENTIAL" or "RESTRICTED_PERSONAL" or "RESTRICTED_SECURITY") ||
            Lifetime <= TimeSpan.Zero || MaximumRequestBytes <= 0 || MaximumCookieBytes is <= 0 or > 4096)
            throw new InvalidOperationException("Current explicit Product continuation qualification is unavailable.");
    }
    public override string ToString() => nameof(ProductAuthorizationContinuationPolicy);
}

public interface IProductAuthorizationContinuationPolicySource
{
    ProductAuthorizationContinuationPolicy RequireCurrent(string origin, DateTimeOffset at);
}

/// <summary>Private request-scoped restoration. Account/session refer to the actual authenticated resume action, never registration completion.</summary>
internal sealed record ProductAuthorizationRestoredContinuation(HttpContext Http, string TokenId, ProductAuthorizationRequest Request,
    string NativeApplicationId, string OperationId, string CorrelationId, string Origin, DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt, string PolicySnapshot, string BindingDigest, string? AccountId, Guid? SessionId)
{
    public override string ToString() => nameof(ProductAuthorizationRestoredContinuation);
}
