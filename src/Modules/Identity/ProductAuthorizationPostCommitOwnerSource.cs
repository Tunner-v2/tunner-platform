using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Tunner.Database.Outbox;

namespace Tunner.Identity.Persistence;

/// <summary>Historical native Product grant/redemption and explicit denial receipts. No current cookie, account eligibility,
/// notification default or Product delivery is inferred. Lifecycle digests currently do not retain their actual requesting actor.</summary>
public sealed class ProductAuthorizationPostCommitOwnerSource(IServiceScopeFactory services, IDataProtectionProvider protection) : IIdentityPostCommitOwnerSource
{
    private readonly IDataProtector materialProtection = protection.CreateProtector("Tunner.Identity.ProductAuthorization.Continuation.Material.v1");

    public bool Owns(PendingOutboxMessage message) => message.EventType is
        "ProductAuthorizationGranted" or "ProductAuthorizationRedeemed" or "ProductAuthorizationDeclined";

    public async ValueTask<IdentityPostCommitOwnerFacts?> ReadCommittedAsync(PendingOutboxMessage message, CancellationToken cancellationToken)
    {
        if (!Owns(message) || message.Source != "tunner/identity" || message.EnvelopeVersion != "1.0" ||
            message.EventSchemaVersion != "1" || message.ResourceVersion != "1" ||
            string.IsNullOrWhiteSpace(message.OperationId) || string.IsNullOrWhiteSpace(message.CorrelationId)) return null;
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
        var committed = await database.Set<OutboxMessage>().AsNoTracking().SingleOrDefaultAsync(x => x.EventId == message.EventId, cancellationToken).ConfigureAwait(false);
        if (committed is null || IdentityPostCommitProcessor.Project(committed) != message) return null;
        using var payload = JsonDocument.Parse(message.Payload);
        if (message.EventType == "ProductAuthorizationDeclined")
            return await DeclinedAsync(scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>(), message, payload.RootElement, cancellationToken).ConfigureAwait(false);
        var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await authorizations.FindByIdAsync(message.Subject, cancellationToken).ConfigureAwait(false);
        if (authorization is null) return null;
        var properties = await authorizations.GetPropertiesAsync(authorization, cancellationToken).ConfigureAwait(false);
        if (!properties.TryGetValue(ProductAuthorizationApplication.LineageProperty, out var original) ||
            ProductAuthorizationApplication.ReadLineage(original) is not { } lineage ||
            await authorizations.GetApplicationIdAsync(authorization, cancellationToken).ConfigureAwait(false) != lineage.Registration.NativeApplicationId ||
            await authorizations.GetSubjectAsync(authorization, cancellationToken).ConfigureAwait(false) != lineage.Relationship.Subject) return null;
        return GrantFacts(message, payload.RootElement, lineage);
    }

    private static IdentityPostCommitOwnerFacts? GrantFacts(PendingOutboxMessage e, JsonElement p, ProductAuthorizationLineage original)
    {
        if (!Properties(p, "reference", "product", "environment", "relationship", "relationshipVersion", "policyVersion", "originalRequestDigest") ||
            original.SchemaVersion != 1 || original.SessionId == Guid.Empty || string.IsNullOrWhiteSpace(original.AccountId) || original.AccountVersion < 1 ||
            original.Registration.RequestDigest != original.Request.Digest || original.RelationshipRequest.RequestDigest != original.Request.Digest ||
            original.Relationship.RequestDigest != original.RelationshipRequest.Digest || original.RelationshipRequest.AccountId != original.AccountId ||
            original.RelationshipRequest.SessionId != original.SessionId || original.OperationId != e.OperationId || original.CorrelationId != e.CorrelationId ||
            Text(p, "reference") != e.Subject || Text(p, "product") != original.Request.ProductId || Text(p, "environment") != original.Request.Environment ||
            Text(p, "relationship") != original.Relationship.RelationshipId || !p.GetProperty("relationshipVersion").TryGetInt64(out var relationshipVersion) ||
            relationshipVersion != original.Relationship.Version || Text(p, "originalRequestDigest") != original.Request.Digest) return null;
        using var policyDocument = JsonDocument.Parse(original.PolicySnapshot); var policy = policyDocument.RootElement;
        var version = Text(policy.GetProperty("Metadata"), "VersionId"); var processing = Text(policy, "ProcessingReference"); var retention = Text(policy, "RetentionReference");
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(processing) || string.IsNullOrWhiteSpace(retention) ||
            Text(p, "policyVersion") != version || Text(policy, "Classification") != e.DataClassification) return null;
        var references = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["authorizationReference"] = e.Subject, ["productReference"] = original.Request.ProductId,
            ["environment"] = original.Request.Environment, ["originalRequestDigest"] = original.Request.Digest,
            ["relationshipReference"] = original.Relationship.RelationshipId,
            ["targetAccountReference"] = original.AccountId, ["originalSessionReference"] = original.SessionId.ToString("N"),
            ["originalClientReference"] = original.Request.ClientId, ["nativeApplicationReference"] = original.Registration.NativeApplicationId
        };
        var redeemed = e.EventType == "ProductAuthorizationRedeemed";
        // Code redemption explicitly binds Request.ClientId to this immutable original client in the native handler.
        // The user remains the grant target; the native client is the redemption actor.
        if (redeemed) references.Add("actorClientReference", original.Request.ClientId);
        return new(redeemed ? "PRODUCT_CODE_REDEMPTION" : "PRODUCT_AUTHORIZATION", e.Subject,
            redeemed ? null : original.AccountId, redeemed ? null : original.SessionId.ToString("N"), version, processing, retention, references);
    }

    private async Task<IdentityPostCommitOwnerFacts?> DeclinedAsync(IOpenIddictTokenManager tokens, PendingOutboxMessage e, JsonElement p, CancellationToken ct)
    {
        if (!Properties(p, "reference", "product", "environment", "originalRequestDigest", "presentationVersion", "EvidenceReference", "ProcessingReference", "RetentionReference") ||
            Text(p, "reference") != e.Subject) return null;
        var token = await tokens.FindByIdAsync(e.Subject, ct).ConfigureAwait(false);
        if (token is null || await tokens.GetTypeAsync(token, ct).ConfigureAwait(false) != OpenIddictConstants.TokenTypeIdentifiers.Private.RequestToken ||
            await tokens.GetStatusAsync(token, ct).ConfigureAwait(false) != OpenIddictConstants.Statuses.Redeemed) return null;
        var properties = await tokens.GetPropertiesAsync(token, ct).ConfigureAwait(false);
        if (!properties.TryGetValue("tunner.product.continuation.consumed.v1", out var consumed) ||
            !properties.TryGetValue("tunner.product.continuation.review.v1", out var review) ||
            !properties.TryGetValue("tunner.product.continuation.v1", out var metadata)) return null;
        var account = Text(consumed, "AccountId"); var sessionText = Text(consumed, "SessionId");
        if (string.IsNullOrWhiteSpace(account) || !Guid.TryParse(sessionText, out var session) || session == Guid.Empty ||
            Text(consumed, "OperationId") != e.OperationId || Text(consumed, "RequestDigest") != Text(p, "originalRequestDigest") ||
            !review.TryGetProperty("Denied", out var denied) || denied.ValueKind != JsonValueKind.True ||
            !review.TryGetProperty("ApprovalDigest", out var approval) || approval.ValueKind != JsonValueKind.Null ||
            string.IsNullOrWhiteSpace(Text(review, "DenialDigest")) || Text(review, "AccountId") != account ||
            !Guid.TryParse(Text(review, "SessionId"), out var reviewSession) || reviewSession != session) return null;
        var protectedMaterial = await tokens.GetPayloadAsync(token, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(protectedMaterial)) return null;
        string material;
        try { material = materialProtection.Unprotect(protectedMaterial); }
        catch (CryptographicException) { return null; }
        using var materialDocument = JsonDocument.Parse(material); var saved = materialDocument.RootElement;
        var request = saved.GetProperty("Request").Deserialize<ProductAuthorizationRequest>();
        var nativeApplication = Text(saved, "NativeApplicationId"); var binding = Text(saved, "BindingDigest"); var origin = Text(saved, "Origin");
        if (string.IsNullOrWhiteSpace(nativeApplication) || string.IsNullOrWhiteSpace(origin) || binding is not { Length: 64 } || !binding.All(Uri.IsHexDigit) ||
            request is null || request.Digest != Text(consumed, "RequestDigest") || request.ProductId != Text(p, "product") || request.Environment != Text(p, "environment") ||
            Text(saved, "OperationId") != e.OperationId || Text(saved, "CorrelationId") != e.CorrelationId ||
            Text(metadata, "OperationId") != e.OperationId || Text(metadata, "CorrelationId") != e.CorrelationId ||
            Text(metadata, "MaterialDigest") != AuthenticationIdentity.Digest(material) || Text(metadata, "BindingDigest") != binding || Text(metadata, "Origin") != origin ||
            await tokens.GetApplicationIdAsync(token, ct).ConfigureAwait(false) != nativeApplication) return null;
        var version = Text(p, "presentationVersion"); var processing = Text(p, "ProcessingReference"); var retention = Text(p, "RetentionReference");
        if (new[] { version, processing, retention, Text(p, "EvidenceReference") }.Any(string.IsNullOrWhiteSpace)) return null;
        return new("PRODUCT_AUTHORIZATION_DENIAL", e.Subject, account, session.ToString("N"), version!, processing!, retention!,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["nativeRequestReference"] = e.Subject,
                ["productReference"] = request.ProductId, ["environment"] = request.Environment,
                ["originalRequestDigest"] = request.Digest, ["denialReference"] = Text(review, "DenialDigest")!,
                ["presentationEvidenceReference"] = Text(p, "EvidenceReference")!, ["targetAccountReference"] = account });
    }

    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static bool Properties(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var actual = value.EnumerateObject().Select(x => x.Name).ToArray();
        return actual.Length == names.Length && actual.Distinct(StringComparer.Ordinal).Count() == actual.Length && names.All(x => actual.Contains(x, StringComparer.Ordinal));
    }
}
