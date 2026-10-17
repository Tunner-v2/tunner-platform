using Tunner.Accounts;
using Tunner.Platform.Policies;
namespace Tunner.Identity.Persistence.Tests;
internal sealed class FixtureAccountAgePolicies:IAccountAgePolicySource,IAccountConsentEvidenceSource,IAccountJurisdictionSource
{
    // Explicit test-only values are not production/legal defaults.
    public AccountPersonalInformationPolicy Policy {get;set;}=new(new("fixture-age-v2","fixture-jurisdiction","fixture-consent-v2",0,false,false),"fixture-profile-processing","fixture-profile-retention",TimeSpan.FromDays(365),100,80);
    public List<AccountConsentEvidence> Evidence {get;}=[];
    public bool Missing {get;set;}
    public AccountPersonalInformationPolicy RequireEffective(string accountId,DateTimeOffset now)
    {
        var versions=Missing?Array.Empty<AccountAgePolicyVersion>():[new AccountAgePolicyVersion(new(Policy.Age.Version,"ACTIVE",Policy.Age.JurisdictionReference,new EffectivePolicyInterval(now.AddDays(-1),true,null,false),null,["fixture-only-approved"]),Policy)];
        return new AccountAgePolicyCatalog(this,versions).RequireEffective(accountId,now);
    }
    public string RequireJurisdictionReference(string accountId)=>"fixture-jurisdiction";
    public Task<IReadOnlyList<AccountConsentEvidence>> ReadAsync(string accountId,CancellationToken cancellationToken)=>Task.FromResult<IReadOnlyList<AccountConsentEvidence>>(Evidence.ToArray());
}
