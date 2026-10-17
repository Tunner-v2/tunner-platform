using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Tunner.Accounts;
namespace Tunner.Identity.Persistence;

/// <summary>Account-owned profile storage reused by onboarding and future authenticated adapters.</summary>
public sealed class AccountPersonalInformationApplication(IdentityPersistenceDbContext database,IAccountAgePolicySource policies,
    IAccountConsentEvidenceSource consent,IDataProtectionProvider protection,TimeProvider clock):IAccountCommercialEligibilityReader
{
    private readonly IDataProtector protector=protection.CreateProtector("Tunner.Accounts.PersonalInformation.v1");
    public async Task<AccountCapabilityEvaluation?> ReadCurrentAsync(string accountId,CancellationToken cancellationToken=default)
    {
        var now=clock.GetUtcNow();var row=await database.Set<AccountPersonalInformationRow>().AsNoTracking().SingleOrDefaultAsync(x=>x.AccountId==accountId,cancellationToken).ConfigureAwait(false);
        if(row?.ProtectedData is null||row.PurgeAfter<=now)return null;
        var policy=policies.RequireEffective(accountId,now);policy.Validate();
        if(row.RecordedAt+policy.ProtectedDataRetention<=now||row.ProcessingReference!=policy.ProcessingReference||row.RetentionReference!=policy.RetentionReference)return null;
        var data=Decode(row.ProtectedData);
        return await EvaluateAsync(accountId,data.DateOfBirth,policy,now,cancellationToken).ConfigureAwait(false);
    }
    internal AccountPersonalInformationPolicy RequirePolicy(string accountId,DateTimeOffset now)
    {var value=policies.RequireEffective(accountId,now);value.Validate();return value;}
    internal async Task<AccountCapabilityEvaluation> CreateAsync(string accountId,CompletePersonalInformationCommand command,DateTimeOffset now,CancellationToken cancellationToken)
    {
        var policy=RequirePolicy(accountId,now);
        if((command.FullName?.EnumerateRunes().Count()??0)>policy.FullNameMaximumLength||(command.DisplayName?.EnumerateRunes().Count()??0)>policy.DisplayNameMaximumLength)
            throw new ArgumentException("Personal Information fields exceed the current supported policy.",nameof(command));
        if(await database.Set<AccountPersonalInformationRow>().AnyAsync(x=>x.AccountId==accountId,cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("Initial profile already exists; authenticated profile editing is a separate owner workflow.");
        var evaluation=await EvaluateAsync(accountId,command.DateOfBirth,policy,now,cancellationToken).ConfigureAwait(false);
        var payload=new ProtectedPersonalInformation {DateOfBirth=command.DateOfBirth,FullName=command.FullName?.Trim(),DisplayName=command.DisplayName?.Trim()};
        var safe=JsonSerializer.Serialize(evaluation);
        database.Add(new AccountPersonalInformationRow {AccountId=accountId,ProtectedData=protector.Protect(JsonSerializer.Serialize(payload)),Version=1,
            RecordedAt=now,PurgeAfter=now+policy.ProtectedDataRetention,ProcessingReference=policy.ProcessingReference,RetentionReference=policy.RetentionReference,EvaluationJson=safe});
        database.Add(new AccountPersonalInformationVersionRow {AccountId=accountId,Version=1,IdempotencyKey=command.IdempotencyKey,OnboardingOperationId=command.OperationId,RecordedAt=now,EvaluationJson=safe});
        return evaluation;
    }
    internal async Task<PersonalInformationView> ViewAsync(string accountId,DateTimeOffset now,CancellationToken cancellationToken)
    {
        var policy=RequirePolicy(accountId,now);var user=await database.Users.AsNoTracking().SingleAsync(x=>x.AccountId==accountId&&x.EmailConfirmed,cancellationToken).ConfigureAwait(false);
        return new(null,null,user.Email!,policy.FullNameMaximumLength,policy.DisplayNameMaximumLength);
    }
    public async Task<int> PurgeExpiredMaterialAsync(int batchSize,CancellationToken cancellationToken=default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);var now=clock.GetUtcNow();
        var rows=await database.Set<AccountPersonalInformationRow>().Where(x=>x.ProtectedData!=null&&x.PurgeAfter<=now).Take(batchSize).ToArrayAsync(cancellationToken).ConfigureAwait(false);
        foreach(var row in rows)
        {
            _=RequirePolicy(row.AccountId,now);row.ProtectedData=null;row.Version++;
            database.Add(new AccountPersonalInformationVersionRow {AccountId=row.AccountId,Version=row.Version,IdempotencyKey=Guid.NewGuid(),OnboardingOperationId=Guid.NewGuid(),RecordedAt=now,EvaluationJson=row.EvaluationJson});
        }
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);return rows.Length;
    }
    private async Task<AccountCapabilityEvaluation> EvaluateAsync(string accountId,DateOnly birth,AccountPersonalInformationPolicy policy,DateTimeOffset now,CancellationToken cancellationToken)
    {
        var evidence=await consent.ReadAsync(accountId,cancellationToken).ConfigureAwait(false);
        return new AccountAgeCapabilityEvaluator(new EvaluationClock(now)).Evaluate(accountId,birth,policy.Age,evidence,"account-age:"+Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"));
    }
    private ProtectedPersonalInformation Decode(string value)=>JsonSerializer.Deserialize<ProtectedPersonalInformation>(protector.Unprotect(value))??throw new InvalidOperationException("Protected Personal Information is unavailable.");
    private sealed class ProtectedPersonalInformation
    {
        public DateOnly DateOfBirth {get;set;}
        public string? FullName {get;set;}
        public string? DisplayName {get;set;}
    }
}
