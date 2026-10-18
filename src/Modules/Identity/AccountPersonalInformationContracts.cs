using System.Text.Json.Serialization;
using Tunner.Accounts;
using Tunner.Platform.Policies;
namespace Tunner.Identity.Persistence;

public sealed record AccountPersonalInformationPolicy(AccountAgePolicy Age, string ProcessingReference,
    string RetentionReference, TimeSpan ProtectedDataRetention, int FullNameMaximumLength, int DisplayNameMaximumLength)
{
    public void Validate()
    {
        Age.Validate();
        if(string.IsNullOrWhiteSpace(ProcessingReference)||string.IsNullOrWhiteSpace(RetentionReference)||
            ProtectedDataRetention<=TimeSpan.Zero||FullNameMaximumLength is <1 or >256||DisplayNameMaximumLength is <1 or >256)
            throw new InvalidOperationException("Explicit approved profile processing, retention and field policy are required.");
    }
}
public interface IAccountJurisdictionSource { string RequireJurisdictionReference(string accountId); }
public interface IAccountAgePolicySource { AccountPersonalInformationPolicy RequireEffective(string accountId,DateTimeOffset now); }
public interface IAccountConsentEvidenceSource
{
    // Trusted owner adapter verifies actor/authorization; no browser endpoint accepts consent assertions.
    Task<IReadOnlyList<AccountConsentEvidence>> ReadAsync(string accountId,CancellationToken cancellationToken);
}
public sealed record AccountAgePolicyVersion(PolicyVersionCandidate<string> Metadata,AccountPersonalInformationPolicy Values);
public sealed class AccountAgePolicyCatalog(IAccountJurisdictionSource jurisdiction,IEnumerable<AccountAgePolicyVersion> versions):IAccountAgePolicySource
{
    private readonly AccountAgePolicyVersion[] versions=versions.ToArray();
    public AccountPersonalInformationPolicy RequireEffective(string accountId,DateTimeOffset now)
    {
        var scope=jurisdiction.RequireJurisdictionReference(accountId);ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        foreach(var item in versions){item.Values.Validate();if(item.Metadata.VersionId!=item.Values.Age.Version||item.Metadata.Scope!=item.Values.Age.JurisdictionReference)throw new InvalidOperationException("Age policy metadata is inconsistent.");}
        var resolved=EffectivePolicyVersionResolver.Resolve(now,scope,versions.Select(x=>x.Metadata),static(a,b)=>StringComparer.Ordinal.Equals(a,b),static x=>x.Status=="ACTIVE").RequireResolvedVersion();
        return versions.Single(x=>x.Metadata.VersionId==resolved.VersionId).Values;
    }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CompletePersonalInformationCommand
{
    public required Guid OperationId {get;init;}
    public required Guid IdempotencyKey {get;init;}
    public required long ExpectedVersion {get;init;}
    public required DateOnly DateOfBirth {get;init;}
    public string? FullName {get;init;}
    public string? DisplayName {get;init;}
    public override string ToString()=>nameof(CompletePersonalInformationCommand);
}
public sealed record PersonalInformationView(string? FullName,string? DisplayName,string VerifiedEmail,int FullNameMaximumLength,int DisplayNameMaximumLength);
internal sealed class EvaluationClock(DateTimeOffset now):TimeProvider {public override DateTimeOffset GetUtcNow()=>now;}
