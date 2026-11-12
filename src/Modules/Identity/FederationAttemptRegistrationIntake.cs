using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tunner.Identity.Persistence;

public sealed record FederationRegistrationAcknowledgement(Guid AttemptId, Guid FederationRequestId, Guid RegistrationKey,
    long ExpectedVersion, string TermsVersion, string PrivacyVersion, bool AcknowledgedNotices);
public sealed record FederationRegistrationIntakeView(string Mode, string Provider, string? VerifiedEmail,
    Guid FederationRequestId, Guid AttemptId, Guid RegistrationKey, long ExpectedVersion, bool CanContinue)
{ public override string ToString() => nameof(FederationRegistrationIntakeView); }
internal sealed record FederationRegistrationBrowser(RegistrationCaller Caller, Guid IdempotencyKey, DateTimeOffset ExpiresAt)
{ public override string ToString() => nameof(FederationRegistrationBrowser); }

internal sealed partial class FederationAttemptApplication
{
    private readonly IDataProtector? registrationEvidenceProtection = protection?.CreateProtector("Tunner.Identity.Federation.RegistrationEvidence.v1");
    private sealed record RegistrationContactMaterial(string Subject, string Email, string MappingEvidenceReference)
    { public override string ToString() => nameof(RegistrationContactMaterial); }

    private static bool RegistrationBrowserMatches(FederationAttemptRow row, FederationRegistrationBrowser? browser, DateTimeOffset at) =>
        browser is not null && browser.Caller.BrowserDigest == row.RegistrationBrowserDigest && browser.IdempotencyKey == row.RegistrationIdempotencyKey &&
        IdentityPersistenceTime.Normalize(browser.ExpiresAt) == row.RegistrationBrowserExpiresAt && browser.ExpiresAt > at;

    private bool CanResolveRegistration(FederationAttemptRow row, FederationProfileVersion profile, FederationProtocolEvidence evidence) =>
        row.Purpose == nameof(FederationAttemptPurpose.SignIn) && row.Resolution == "UNRESOLVED" && row.LinkId is null &&
        profile.Values.Provider == FederationProviderKind.Google && profile.Values.Registration is { Complete: true } qualification &&
        evidence.ProviderEmailMappingEvidenceReference == qualification.ProviderVerifiedEmailMappingEvidenceReference &&
        IdentityEmail.IsValid(evidence.ProviderVerifiedEmail) && row.RegistrationBrowserDigest is not null &&
        row.RegistrationIdempotencyKey is not null && row.RegistrationBrowserExpiresAt > clock.GetUtcNow() &&
        registrationApplication is RegistrationApplication && registrationEvidenceProtection is not null;

    private async Task<FederationCallbackResult> RecordRegistrationIntake(IDbContextTransaction tx,FederationAttemptRow row,
        FederationProfileVersion original,FederationProtocolEvidence evidence,CancellationToken ct)
    {
        var at = clock.GetUtcNow(); var profile = Current(ParseProvider(row),row.Origin,at);
        if (!ExactProfile(row,profile) || original.Snapshot != profile.Snapshot || at < row.CreatedAt || at >= row.ExpiresAt ||
            !CanResolveRegistration(row,profile,evidence))
        { await Reject(tx,row,at,"VERSION_CONFLICT",ct).ConfigureAwait(false); return new(row.ContinuationPath,false); }
        // A closed validated identity/contact projection only. No email lookup, Account/link/session mutation or native payload.
        var key = profile.Registration.BindExternalIdentity(evidence.Issuer,evidence.ClientId,evidence.Subject);
        row.ProtectedRegistrationEvidence = registrationEvidenceProtection!.CreateProtector(row.AttemptId.ToString("N")).Protect(
            JsonSerializer.Serialize(new RegistrationContactMaterial(key.Subject,evidence.ProviderVerifiedEmail!,evidence.ProviderEmailMappingEvidenceReference!)));
        row.Resolution = "REGISTRATION_INTAKE"; row.ValidatedIssuer = evidence.Issuer; row.VerifiedAt = at;
        row.VerificationExpiresAt = profile.Clip(at + profile.Values.VerifiedLifetime);
        if (row.VerificationExpiresAt > row.ExpiresAt) row.VerificationExpiresAt = row.ExpiresAt;
        row.State = "VERIFIED"; row.Version++;
        await Finish(tx,new(row),ct).ConfigureAwait(false); return new(row.ContinuationPath,true);
    }

    private async Task<FederationAttemptResult> CompleteRegistrationIntake(IDbContextTransaction tx,FederationAttemptRow row,
        FederationCurrentBrowser browser,FederationRegistrationBrowser? registrationBrowser,FederationRegistrationAcknowledgement? acknowledgement,
        FederationProfileVersion profile,CancellationToken ct)
    {
        var at = clock.GetUtcNow();
        if (!RegistrationBrowserMatches(row,registrationBrowser,at) || registrationApplication is not RegistrationApplication owner ||
            registrationEvidenceProtection is null || row.ProtectedRegistrationEvidence is null || profile.Values.Provider != FederationProviderKind.Google ||
            profile.Values.Registration is not { Complete: true }) return await Finish(tx,new(row,"VERSION_CONFLICT"),ct).ConfigureAwait(false);
        // Reading requirements is not consent and cannot create a registration.
        if (acknowledgement is null) return await Finish(tx,new(row),ct).ConfigureAwait(false);
        if (acknowledgement.AttemptId != row.AttemptId || acknowledgement.FederationRequestId != row.RequestId ||
            acknowledgement.RegistrationKey != row.RegistrationIdempotencyKey || acknowledgement.ExpectedVersion != row.Version)
            return await Finish(tx,new(row,"VERSION_CONFLICT"),ct).ConfigureAwait(false);
        if (!acknowledgement.AcknowledgedNotices || !owner.FederationRegistrationNoticeCurrentAt(profile,acknowledgement,at))
            return await Finish(tx,new(row,"VERSION_CONFLICT"),ct).ConfigureAwait(false);
        RegistrationContactMaterial material;
        try { material = JsonSerializer.Deserialize<RegistrationContactMaterial>(registrationEvidenceProtection.CreateProtector(row.AttemptId.ToString("N"))
            .Unprotect(row.ProtectedRegistrationEvidence)) ?? throw new CryptographicException(); }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        { return await Reject(tx,row,at,"AUTHENTICATION_FAILED",ct).ConfigureAwait(false); }
        var key = profile.Registration.BindExternalIdentity(row.ValidatedIssuer!,row.ClientId,material.Subject);
        var intake = new FederationRegistrationIntake(row,key,material.Email,material.MappingEvidenceReference,registrationBrowser!.Caller.BrowserDigest);
        var registration = await owner.IntakeFederationInTransactionAsync(registrationBrowser.Caller,browser,intake,registrationBrowser.IdempotencyKey,
            acknowledgement.TermsVersion,acknowledgement.PrivacyVersion,acknowledgement.AcknowledgedNotices,ct).ConfigureAwait(false);
        if (registration.ErrorCode is not null) return await Reject(tx,row,clock.GetUtcNow(),SafeError(registration.ErrorCode),ct).ConfigureAwait(false);
        // Save the actual Account/contact/link/outbox before the final pure witness. Failure rolls back the SAME transaction.
        row.State = "COMPLETED"; row.Version++; row.ResultRegistrationOperationId = registration.OperationId;
        row.EndedAt = clock.GetUtcNow(); row.ResultExpiresAt = row.EndedAt + TimeSpan.FromTicks(row.ResultRecoveryLifetimeTicks);
        row.ProtectedRegistrationEvidence = null;
        await database.SaveChangesAsync(ct).ConfigureAwait(false);
        registration = await owner.DescribeFederationIntakeInTransactionAsync(registration.OperationId,ct).ConfigureAwait(false);
        at = clock.GetUtcNow(); var finalProfile = Current(ParseProvider(row),row.Origin,at);
        if (!PendingCurrent(row,browser,finalProfile,at) || !Original(row,browser) || !browser.OriginalFingerprint ||
            !RegistrationBrowserMatches(row,registrationBrowser,at) || row.VerifiedAt is not { } verifiedAt || verifiedAt > at ||
            row.VerificationExpiresAt is not { } until || until <= at || row.ResultExpiresAt is not { } resultUntil || resultUntil <= at ||
            !owner.FederationIntakeCurrentAt(registration.OperationId,intake,registrationBrowser,acknowledgement,at))
            throw new InvalidOperationException("Federated registration authority changed before commit.");
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new(row,registration:registration);
    }

    private async Task<FederationAttemptResult> ReadRegistrationCompleted(IDbContextTransaction tx,FederationAttemptRow row,
        FederationCurrentBrowser browser,FederationRegistrationBrowser? registrationBrowser,CancellationToken ct)
    {
        var at = clock.GetUtcNow();
        if (!Original(row,browser) || !browser.OriginalFingerprint || !RegistrationBrowserMatches(row,registrationBrowser,at) ||
            row.ResultExpiresAt is not { } expires || expires <= at || row.ResultRegistrationOperationId is not { } operation ||
            registrationApplication is not RegistrationApplication owner) return await Finish(tx,new(row,"NOT_FOUND_OR_NOT_DISCLOSABLE"),ct).ConfigureAwait(false);
        var registration = await owner.ReadFederationOriginalInTransactionAsync(registrationBrowser!,operation,row.AttemptId,ct).ConfigureAwait(false);
        at = clock.GetUtcNow();
        return await Finish(tx,registration is not null && Original(row,browser) && browser.OriginalFingerprint &&
            RegistrationBrowserMatches(row,registrationBrowser,at) && row.ResultExpiresAt > at
            ? new(row,registration:registration) : new(row,"NOT_FOUND_OR_NOT_DISCLOSABLE"),ct).ConfigureAwait(false);
    }

    internal async Task<FederationRegistrationIntakeView?> ReadRegistrationIntakeAsync(FederationCurrentBrowser browser,
        FederationRegistrationBrowser registrationBrowser,Guid id,CancellationToken ct)
    {
        Clean(); await using var tx = await database.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = await Owned(browser,id,ct).ConfigureAwait(false); var at = clock.GetUtcNow();
        if (row is null || row.Resolution != "REGISTRATION_INTAKE" || row.State != "VERIFIED" ||
            !Original(row,browser) || !browser.OriginalFingerprint || !RegistrationBrowserMatches(row,registrationBrowser,at)) return null;
        FederationRegistrationIntakeView Blocked() => new("FEDERATED","Google",null,row.RequestId,row.AttemptId,
            registrationBrowser.IdempotencyKey,row.Version,false);
        if (registrationApplication is not RegistrationApplication owner || registrationEvidenceProtection is null ||
            row.ProtectedRegistrationEvidence is null) return Blocked();
        try
        {
            var material = JsonSerializer.Deserialize<RegistrationContactMaterial>(registrationEvidenceProtection.CreateProtector(row.AttemptId.ToString("N"))
                .Unprotect(row.ProtectedRegistrationEvidence));
            at = clock.GetUtcNow(); var profile = Current(ParseProvider(row),row.Origin,at);
            if (material is null || !IdentityEmail.IsValid(material.Email) || !PendingCurrent(row,browser,profile,at) ||
                row.VerifiedAt is not { } verifiedAt || verifiedAt > at || row.VerificationExpiresAt is not { } until || until <= at ||
                !RegistrationBrowserMatches(row,registrationBrowser,at) ||
                profile.Values.Provider != FederationProviderKind.Google || profile.Values.Registration is not { Complete: true } qualification ||
                qualification.ProviderVerifiedEmailMappingEvidenceReference != material.MappingEvidenceReference ||
                !owner.FederationRegistrationPolicyCurrentAt(profile,at)) return Blocked();
            _ = profile.Registration.BindExternalIdentity(row.ValidatedIssuer!,row.ClientId,material.Subject);
            return new("FEDERATED","Google",material.Email,row.RequestId,row.AttemptId,registrationBrowser.IdempotencyKey,row.Version,true);
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or InvalidOperationException or ArgumentException)
        { return Blocked(); }
    }
}
