using System.Globalization;
using System.Text.Json;
using Tunner.Accounts;
using Xunit;
namespace Tunner.Accounts.Tests;
public sealed class AccountAgeCapabilityTests
{
    private static readonly DateTimeOffset Now=new(2026,10,17,12,0,0,TimeSpan.Zero);
    private static AccountAgePolicy Policy(int threshold=0,bool stronger=false,bool school=false)=>new("age-v2","jurisdiction","consent-v2",threshold,stronger,school);
    private static AccountConsentEvidence Consent(string source="GUARDIAN",string? strong=null)=>new("ACC-fixture","consent-evidence","consent-v2",source,"authorized-actor","owner-verification",Now.AddDays(-1),Now.AddDays(1),false,strong);
    private static AccountCapabilityEvaluation Evaluate(DateOnly birth,AccountAgePolicy? policy=null,IEnumerable<AccountConsentEvidence>? evidence=null,DateTimeOffset? now=null)=>new AccountAgeCapabilityEvaluator(new Clock(now??Now)).Evaluate("ACC-fixture",birth,policy??Policy(),evidence??[],"age-evidence","correlation");
    [Theory]
    [InlineData(2008,10,17,"ADULT",true)]
    [InlineData(2008,10,18,"UNDER_18",false)]
    [InlineData(2015,1,1,"UNDER_18",false)]
    public void AgeNeverRejectsAccountLifecycleButRestrictsIndependentPayer(int y,int m,int d,string classification,bool independent)
    {var r=Evaluate(new(y,m,d));Assert.Equal(classification,r.AgeClassification);Assert.True(r.AgeAllowsAccountLifecycle);Assert.True(r.OrdinaryUseConsentSatisfied);Assert.Equal(independent,r.IndependentCommercialEligible);Assert.Equal(independent?null:"PAYER_CONTEXT_REQUIRED",r.PayerResolutionCode);Assert.DoesNotContain("AGE_INELIGIBLE",JsonSerializer.Serialize(r));}
    [Theory]
    [InlineData("2026-02-27",false)]
    [InlineData("2026-02-28",true)]
    [InlineData("2026-03-01",true)]
    public void LeapDayReusesCalendarArithmetic(string day,bool adult)
    {Assert.Equal(adult,Evaluate(new(2008,2,29),now:DateTimeOffset.Parse(day+"T12:00:00+00:00",CultureInfo.InvariantCulture)).IndependentCommercialEligible);}
    [Fact]
    public void Turning18ReevaluatesCapabilityWithoutReplacingAccount()
    {var child=Evaluate(new(2008,10,18));var adult=Evaluate(new(2008,10,18),now:Now.AddDays(1));Assert.False(child.IndependentCommercialEligible);Assert.True(adult.IndependentCommercialEligible);Assert.Equal(child.AccountId,adult.AccountId);}
    [Fact]
    public void JurisdictionPolicyControlsConsentWhichNeverGrantsPayerAuthority()
    {var birth=new DateOnly(2015,1,1);Assert.False(Evaluate(birth).ConsentRequired);var pending=Evaluate(birth,Policy(16));Assert.True(pending.ConsentRequired);Assert.False(pending.ConsentSatisfied);var given=Evaluate(birth,Policy(16),[Consent()]);Assert.True(given.ConsentSatisfied);Assert.False(given.IndependentCommercialEligible);Assert.Equal("PAYER_CONTEXT_REQUIRED",given.PayerResolutionCode);Assert.Equal("consent-evidence",given.ConsentEvidenceReference);}
    [Theory]
    [InlineData("GUARDIAN",false,true)]
    [InlineData("AUTHORIZED",false,true)]
    [InlineData("SCHOOL",false,false)]
    [InlineData("SCHOOL",true,true)]
    public void GuardianAndSchoolEvidenceFollowActivePolicy(string source,bool school,bool satisfied)
    {Assert.Equal(satisfied,Evaluate(new(2015,1,1),Policy(16,school:school),[Consent(source)]).ConsentSatisfied);}
    [Fact]
    public void StrongerProofOnlyWhenPolicyRequiresIt()
    {var b=new DateOnly(2015,1,1);Assert.True(Evaluate(b,Policy(16),[Consent()]).ConsentSatisfied);Assert.False(Evaluate(b,Policy(16,true),[Consent()]).ConsentSatisfied);Assert.True(Evaluate(b,Policy(16,true),[Consent(strong:"approved-proof")]).ConsentSatisfied);}
    [Fact]
    public void InvalidConsentCannotSatisfyPolicy()
    {AccountConsentEvidence[] invalid=[Consent() with{Revoked=true},Consent() with{ExpiresAt=Now},Consent() with{EffectiveFrom=Now.AddSeconds(1)},Consent() with{AccountId="ACC-other"},Consent() with{ConsentPolicyVersion="stale"},Consent() with{VerificationReference=""}];foreach(var x in invalid)Assert.False(Evaluate(new(2015,1,1),Policy(16),[x]).ConsentSatisfied);}
    [Fact]
    public void PolicyChangeInvalidatesOldConsentAndFutureDobFailsSafely()
    {Assert.False(Evaluate(new(2015,1,1),Policy(16) with{ConsentPolicyVersion="next"},[Consent()]).ConsentSatisfied);var e=Assert.Throws<ArgumentException>(()=>Evaluate(new(2027,1,1)));Assert.DoesNotContain("2027-01-01",e.ToString());}
    [Fact]
    public void UtcDateAndSingleClockReadBindClassificationAndEvidence()
    {var now=new DateTimeOffset(2026,10,18,0,30,0,TimeSpan.FromHours(5));var clock=new Clock(now);var r=new AccountAgeCapabilityEvaluator(clock).Evaluate("ACC-fixture",new(2008,10,18),Policy(),[],"e","c");Assert.False(r.IndependentCommercialEligible);Assert.Equal(1,clock.Reads);Assert.Equal(now.ToUniversalTime(),r.EvaluatedAt);Assert.Equal("age-v2",r.PolicyVersion);}
    [Fact]
    public void DerivedEvidenceContainsNoRawDob()
    {var r=Evaluate(new(2015,1,1),Policy(16),[Consent()]);Assert.DoesNotContain("2015-01-01",JsonSerializer.Serialize(r));Assert.DoesNotContain("2015-01-01",r.ToString());Assert.DoesNotContain(typeof(AccountCapabilityEvaluation).GetProperties(),p=>p.Name.Contains("Birth",StringComparison.OrdinalIgnoreCase));}
    [Fact]
    public void MissingPolicyNeverCreatesImplicitWorldwideConsentThreshold()
    {Assert.Throws<InvalidOperationException>(()=>Evaluate(new(2015,1,1),Policy() with{JurisdictionReference=""}));Assert.Throws<InvalidOperationException>(()=>Evaluate(new(2015,1,1),Policy(-1)));}
    private sealed class Clock(DateTimeOffset now):TimeProvider {public int Reads{get;private set;}public override DateTimeOffset GetUtcNow(){Reads++;return now;}}
}
