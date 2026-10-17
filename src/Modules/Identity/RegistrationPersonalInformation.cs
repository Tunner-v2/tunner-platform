using Microsoft.EntityFrameworkCore;
using Tunner.Accounts;
namespace Tunner.Identity.Persistence;

public sealed partial class RegistrationApplication
{
    public async Task<RegistrationResult> CompletePersonalInformationAsync(RegistrationCaller caller,CompletePersonalInformationCommand command,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(caller);ArgumentNullException.ThrowIfNull(command);caller.Validate();
        var now=timeProvider.GetUtcNow();var policy=policies.RequireEffective(now);policy.Validate();
        await using var transaction=await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if(!await AllowAsync(caller,"PERSONAL_INFORMATION",policy.VerifyLimit,policy,now,cancellationToken).ConfigureAwait(false))
            return await CompleteAsync(transaction,Failure(command.OperationId,"RATE_OR_ABUSE_LIMITED"),null,cancellationToken).ConfigureAwait(false);
        await LockAsync("operation:"+command.OperationId,cancellationToken).ConfigureAwait(false);
        var row=await OwnedAsync(caller,command.OperationId,cancellationToken).ConfigureAwait(false);
        if(row is null)return await CompleteAsync(transaction,Failure(command.OperationId,"NOT_FOUND_OR_NOT_DISCLOSABLE"),null,cancellationToken).ConfigureAwait(false);
        var replay=await ReplayAsync(row.OperationId,"PERSONAL_INFORMATION",command.IdempotencyKey,cancellationToken).ConfigureAwait(false);
        if(replay is not null)return await CompleteAsync(transaction,replay,null,cancellationToken).ConfigureAwait(false);
        RegistrationResult result;
        if(command.IdempotencyKey==Guid.Empty||row.Stage!="PERSONAL_INFORMATION"||row.Status!="REQUIRES_ACTION"||row.PolicyVersion!=policy.Version||now>=row.PurgeAfter||!await HasVerifiedOnboardingAsync(row,cancellationToken).ConfigureAwait(false))
            result=Failure(row.OperationId,"NOT_FOUND_OR_NOT_DISCLOSABLE",row);
        else if(row.Version!=command.ExpectedVersion)result=Failure(row.OperationId,"VERSION_CONFLICT",row);
        else
        {
            await LockAsync("personal:"+row.AccountId,cancellationToken).ConfigureAwait(false);
            AccountCapabilityEvaluation evaluation;
            try{evaluation=await personalInformation.CreateAsync(row.AccountId!,command,now,cancellationToken).ConfigureAwait(false);}
            catch(ArgumentException){return await CompleteAsync(transaction,Failure(row.OperationId,"VALIDATION_ERROR",row),null,cancellationToken).ConfigureAwait(false);}
            row.Version++;row.Stage=evaluation.OrdinaryUseConsentSatisfied?"COMPLETE":"CONSENT_REQUIRED";
            AddEvent(row,"AccountAgeEvaluated",evaluation.EvidenceReference,1,now);
            if(evaluation.OrdinaryUseConsentSatisfied)await ActivateAfterPolicyAsync(row,evaluation,now,cancellationToken).ConfigureAwait(false);
            result=Result(row) with{AgeCapabilities=evaluation};
        }
        Remember(row.OperationId,"PERSONAL_INFORMATION",command.IdempotencyKey,result);
        return await CompleteAsync(transaction,result,null,cancellationToken).ConfigureAwait(false);
    }
    public async Task<RegistrationResult> ContinueAsync(RegistrationCaller caller,ResendRegistrationCommand command,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(caller);ArgumentNullException.ThrowIfNull(command);caller.Validate();
        var now=timeProvider.GetUtcNow();var policy=policies.RequireEffective(now);policy.Validate();
        await using var transaction=await database.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if(!await AllowAsync(caller,"CONTINUE",policy.VerifyLimit,policy,now,cancellationToken).ConfigureAwait(false))
            return await CompleteAsync(transaction,Failure(command.OperationId,"RATE_OR_ABUSE_LIMITED"),null,cancellationToken).ConfigureAwait(false);
        await LockAsync("operation:"+command.OperationId,cancellationToken).ConfigureAwait(false);
        var row=await OwnedAsync(caller,command.OperationId,cancellationToken).ConfigureAwait(false);
        if(row is null)return await CompleteAsync(transaction,Failure(command.OperationId,"NOT_FOUND_OR_NOT_DISCLOSABLE"),null,cancellationToken).ConfigureAwait(false);
        var replay=await ReplayAsync(row.OperationId,"CONTINUE",command.IdempotencyKey,cancellationToken).ConfigureAwait(false);
        if(replay is not null)return await CompleteAsync(transaction,replay,null,cancellationToken).ConfigureAwait(false);
        RegistrationResult result;
        if(command.IdempotencyKey==Guid.Empty||row.Stage!="CONSENT_REQUIRED"||row.Status!="REQUIRES_ACTION"||row.PolicyVersion!=policy.Version||now>=row.PurgeAfter||!await HasVerifiedOnboardingAsync(row,cancellationToken).ConfigureAwait(false))
            result=Failure(row.OperationId,"NOT_FOUND_OR_NOT_DISCLOSABLE",row);
        else if(command.ExpectedVersion!=row.Version)result=Failure(row.OperationId,"VERSION_CONFLICT",row);
        else
        {
            var evaluation=await personalInformation.ReadCurrentAsync(row.AccountId!,cancellationToken).ConfigureAwait(false);
            if(evaluation is null)result=Failure(row.OperationId,"DEPENDENCY_UNAVAILABLE",row);
            else
            {
                if(evaluation.OrdinaryUseConsentSatisfied){row.Version++;await ActivateAfterPolicyAsync(row,evaluation,now,cancellationToken).ConfigureAwait(false);}
                result=Result(row) with{AgeCapabilities=evaluation};
            }
        }
        Remember(row.OperationId,"CONTINUE",command.IdempotencyKey,result);
        return await CompleteAsync(transaction,result,null,cancellationToken).ConfigureAwait(false);
    }
    private async Task<bool> HasVerifiedOnboardingAsync(RegistrationOperationRow row,CancellationToken cancellationToken)
    {
        if(row.AccountId is null||row.ContactId is null)return false;
        return await database.Accounts.AnyAsync(x=>x.AccountId==row.AccountId&&x.State==AccountState.VerificationPending,cancellationToken).ConfigureAwait(false)&&
            await database.VerifiedContacts.AnyAsync(x=>x.ContactId==row.ContactId&&x.AccountId==row.AccountId&&x.State==ContactVerificationState.Verified,cancellationToken).ConfigureAwait(false)&&
            await database.Users.AnyAsync(x=>x.AccountId==row.AccountId&&x.EmailConfirmed,cancellationToken).ConfigureAwait(false);
    }
    private async Task ActivateAfterPolicyAsync(RegistrationOperationRow row,AccountCapabilityEvaluation evaluation,DateTimeOffset now,CancellationToken cancellationToken)
    {
        var account=await database.Accounts.SingleAsync(x=>x.AccountId==row.AccountId,cancellationToken).ConfigureAwait(false);
        if(account.State!=AccountState.VerificationPending)throw new InvalidOperationException("Account state changed before onboarding completion.");
        account.State=AccountState.Active;account.Version++;account.RecordedAtUtc=now;account.OperationId=OperationReference(row.OperationId);
        account.CorrelationId=row.CorrelationId;account.EvidenceReference=evaluation.EvidenceReference;
        row.Status="SUCCEEDED";row.Stage="COMPLETE";
        AddEvent(row,"AccountActivated",account.AccountId,account.Version,now);
    }
    private async Task<RegistrationResult> DescribeAsync(RegistrationOperationRow row,DateTimeOffset now,CancellationToken cancellationToken)
    {
        var result=Result(row);
        if(row.AccountId is null)return result;
        if(row.Stage=="PERSONAL_INFORMATION")return result with{PersonalInformation=await personalInformation.ViewAsync(row.AccountId,now,cancellationToken).ConfigureAwait(false)};
        if(row.Stage is "CONSENT_REQUIRED" or "COMPLETE")return result with{AgeCapabilities=await personalInformation.ReadCurrentAsync(row.AccountId,cancellationToken).ConfigureAwait(false)};
        return result;
    }
}
