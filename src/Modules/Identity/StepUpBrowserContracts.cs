using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Tunner.Identity.Persistence;

/// <summary>Server-derived User actor for a trusted operation owner, never a browser command.</summary>
public sealed class UserStepUpActor
{
    internal UserStepUpActor(StepUpCaller caller) { AccountId = caller.AccountId; SessionId = caller.SessionId; Origin = caller.Origin; }
    public string AccountId { get; }
    public Guid SessionId { get; }
    public string Origin { get; }
    public override string ToString() => nameof(UserStepUpActor);
}

/// <summary>Private BFF/owner contract; no Product API or runtime policy defaults.</summary>
public sealed class UserStepUpOperationContext
{
    public UserStepUpOperationContext(UserStepUpActor actor, string operationId, string correlationId, AssurancePolicyScope scope,
        string resourceId, long expectedVersion, string contextDigest, string expectedPolicyVersion, bool completed = false)
    {
        ArgumentNullException.ThrowIfNull(actor); ArgumentException.ThrowIfNullOrWhiteSpace(expectedPolicyVersion);
        Actor = actor; Binding = new(operationId, correlationId, scope, resourceId, expectedVersion, contextDigest);
        ExpectedPolicyVersion = expectedPolicyVersion; Completed = completed;
    }
    public UserStepUpActor Actor { get; }
    public string ExpectedPolicyVersion { get; }
    public bool Completed { get; }
    internal StepUpOperation Binding { get; }
    public override string ToString() => nameof(UserStepUpOperationContext);
}

/// <summary>Trusted owner resolves stable workflow state and mutates/records its result in the supplied Identity transaction. No network I/O.</summary>
public interface IUserStepUpOperationSource
{
    Task<UserStepUpOperationContext?> ResolveAsync(UserStepUpActor actor, CancellationToken cancellationToken);
    Task<bool> ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext database, CancellationToken cancellationToken);
}

public sealed class StepUpPresentation
{
    internal static readonly string[] TotpRequiredKeys = ["stepUp.totp.description", "stepUp.totp.code.label", "stepUp.totp.code.placeholder", "stepUp.totp.pending", "stepUp.totp.unknown", "stepUp.totp.expired", "stepUp.totp.error.required", "stepUp.totp.error.code", "stepUp.totp.error.credentials", "stepUp.totp.error.replayed", "stepUp.totp.check", "stepUp.totp.close", "totp.selection.heading", "totp.selection.proofDescription", "totp.selection.loginDescription", "totp.selection.optionLabel", "totp.selection.added", "totp.selection.numberExplanation", "totp.selection.selectedDescription", "totp.selection.change"];
    internal void RequireTotpPublished()
    {
        if (TotpRequiredKeys.Any(x => !Content.ContainsKey(x))) throw new ArgumentException("TOTP operation proof requires its published field, pending, recovery and error variants.");
    }
    private static readonly string[] RequiredKeys = ["stepUp.heading","stepUp.description","stepUp.passkey","stepUp.passkey.description",
        "stepUp.authenticator","stepUp.authenticator.description","stepUp.other","stepUp.unavailable","stepUp.password.description",
        "stepUp.password.label","stepUp.password.placeholder","stepUp.back","stepUp.cancel","stepUp.confirm","stepUp.working",
        "stepUp.passkey.pending","stepUp.passkey.notCompleted","stepUp.passkey.unknown","stepUp.passkey.check","stepUp.passkey.resume","stepUp.passkey.unsupported","stepUp.passkey.close",
        "stepUp.error.password","stepUp.error.highlighted","stepUp.error.credentials","stepUp.error.limited","stepUp.error.unavailable","stepUp.error.changed"];
    public StepUpPresentation(string locale, IEnumerable<RegistrationPublishedText> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale); ArgumentNullException.ThrowIfNull(content);
        var values = content.ToDictionary(x => x.Key, StringComparer.Ordinal);
        if (RequiredKeys.Any(key => !values.ContainsKey(key)) || values.Values.Any(x => x.State != "PUBLISHED" || x.Locale != locale ||
            new[] { x.Key, x.VariantRef, x.VersionRef, x.PublicationRef, x.Text }.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("Step-up requires published presentation with provenance.", nameof(content));
        Locale = locale; Content = new ReadOnlyDictionary<string, RegistrationPublishedText>(values);
    }
    public string Locale { get; }
    public IReadOnlyDictionary<string, RegistrationPublishedText> Content { get; }
}
public interface IStepUpPresentationSource { StepUpPresentation RequirePublished(AssurancePolicyVersion policy); }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class PasswordStepUpCommand
{
    public required Guid RequestId { get; init; }
    public required string Password { get; init; }
    public override string ToString() => nameof(PasswordStepUpCommand);
}
