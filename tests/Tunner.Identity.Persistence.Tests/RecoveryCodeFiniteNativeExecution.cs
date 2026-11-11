using Tunner.Testing.Recovery;

namespace Tunner.Identity.Persistence.Tests;

public enum RecoveryCodeNativeCohort { PublicCanaries, PublicSql, PrivateRecovery }

/// <summary>Only fixed booleans and enums cross the test-assembly boundary. No xUnit runner/TRX is used.</summary>
public sealed record RecoveryCodeNativeReceipt(int Version, string Type, string Cohort, string FailureCode,
    IReadOnlyDictionary<string, bool> Cases, bool CaseExecutionSettled, bool OwnershipVerified,
    bool NoLogPersistenceVerified, bool ContainerAbsentVerified, bool BorrowedPostgresPreserved,
    bool CleanupVerified, bool DockerConfigurationAbsentVerified, bool ContainmentVerified, bool SensitiveAccepted);

public static class RecoveryCodeFiniteNativeExecution
{
    private static readonly string[] CanaryIds = ["BEFORE_CREATE", "AFTER_CREATE", "AFTER_START", "AFTER_MIGRATION", "CASE_FAILURE", "CLEANUP_REJECT"];
    private static readonly string[] PublicIds = [.. CanaryIds, "OWNERSHIP_REJECT", "NO_LOG_REJECT", "SENSITIVE_REJECT"];
    private static readonly string[] SqlIds = ["ISSUANCE_BINDING", "RETIREMENT_BINDING", "ENTRY_ONE_WAY", "SET_ONE_WAY", "OPERATION_ONE_WAY"];
    private static readonly string[] PrivateIds = ["WINNER_SAFE_READ_REPLACE", "PUBLICATION_WITHDRAWAL", "PUBLICATION_VERSION", "PUBLICATION_INTERVAL",
        "SAME_CODE_CONCURRENCY", "OWNER_REJECTION", "FINAL_DRIFT", "TRANSACTION_ADMISSION", "SAFE_DUPLICATE", "DEFAULT_ABSENCE"];

    public static async Task<RecoveryCodeNativeReceipt> RunAsync(RecoveryCodeNativeCohort cohort, Uri qualifiedLocalEndpoint, DirectoryInfo repository)
    {
        var cases = (cohort == RecoveryCodeNativeCohort.PublicCanaries ? PublicIds : cohort == RecoveryCodeNativeCohort.PublicSql ? SqlIds : PrivateIds)
            .ToDictionary(id => id, _ => false, StringComparer.Ordinal);
        var failure = "NONE"; var settled = true; var ownership = false; var noLogs = false; var absence = false; var cleanup = false;
        RecoveryCodeBorrowedPostgres? borrowed = null; var preserved = false;
        RecoveryCodeLocalDockerEndpoint? endpoint = null; var configurationAbsent = false;
        try
        {
            if (!Enum.IsDefined(cohort) || Environment.GetEnvironmentVariable("OTEL_SDK_DISABLED") != "true")
                throw new InvalidOperationException("RECOVERY_NATIVE_INPUT_INVALID");
            endpoint = RecoveryCodeLocalDockerEndpoint.Admit(qualifiedLocalEndpoint);
            using var setupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            borrowed = await RecoveryCodeOwnedDockerFixture.VerifyBorrowedBrowserPostgresAsync(endpoint, repository, setupDeadline.Token).ConfigureAwait(false);
            if (cohort == RecoveryCodeNativeCohort.PublicCanaries)
            {
                ownership = true; noLogs = true; absence = true; cleanup = true;
                foreach (var id in CanaryIds)
                {
                    var result = await CanaryAsync(endpoint, repository, id).ConfigureAwait(false);
                    cases[id] = result.Observed; ownership &= result.Ownership; noLogs &= result.NoLogs; absence &= result.Absent; cleanup &= result.Cleanup;
                    if (!result.Observed || !result.Cleanup) { failure = "PUBLIC_CANARY_FAILED"; break; }
                }
                // NONFUNCTIONAL PUBLIC reducer vectors only: no private method or value is created/executed here.
                var publicVector = new RecoveryCodeNativeReceipt(1, "nativeReceipt", nameof(RecoveryCodeNativeCohort.PrivateRecovery), "NONE",
                    PrivateIds.ToDictionary(id => id, _ => true, StringComparer.Ordinal), true, true, true, true, true, true, true, true, true);
                var positiveVector = AcceptReceipt(publicVector);
                cases["OWNERSHIP_REJECT"] = positiveVector && !AcceptReceipt(publicVector with { OwnershipVerified = false });
                cases["NO_LOG_REJECT"] = positiveVector && !AcceptReceipt(publicVector with { NoLogPersistenceVerified = false });
                cases["SENSITIVE_REJECT"] = positiveVector && !AcceptReceipt(publicVector with { SensitiveAccepted = false });
            }
            else
            {
                var owner = RecoveryCodeOwnedDockerFixture.PrepareNativePostgres(endpoint, repository);
                cleanup = true;
                try
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    await owner.StartAsync(deadline.Token).ConfigureAwait(false);
                    ownership = owner.OwnershipVerified; noLogs = owner.NoLogPersistenceVerified;
                    var fixture = new IdentityPostgresFixture(owner);
                    try
                    {
                        foreach (var id in cases.Keys.ToArray())
                        {
                            // Literal calls to existing authored methods; no reflection, discovery, dynamic names or xUnit execution.
                            var execution = Dispatch(cohort, id, fixture);
                            try { await execution.WaitAsync(TimeSpan.FromSeconds(90)).ConfigureAwait(false); cases[id] = true; }
                            catch (TimeoutException)
                            {
                                failure = "CASE_TIMEOUT"; settled = false;
                                try { await owner.DisposeAsync().ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { }
                                try { await execution.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); settled = true; }
                                catch (Exception error) when (error is not OutOfMemoryException) { settled = execution.IsCompleted; }
                                break;
                            }
                            catch (Exception error) when (error is not OutOfMemoryException) { failure = "CASE_FAILED"; break; }
                        }
                    }
                    finally { await fixture.DisposeAsync().ConfigureAwait(false); }
                }
                catch (Exception error) when (error is not OutOfMemoryException) { if (failure == "NONE") failure = "RESOURCE_FAILED"; }
                finally
                {
                    try { await owner.DisposeAsync().ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { cleanup = false; }
                    absence = owner.ContainerAbsentVerified; cleanup &= owner.CleanupVerified;
                }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { if (failure == "NONE") failure = "ADMISSION_FAILED"; }
        finally
        {
            if (borrowed is not null)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                preserved = await borrowed.VerifyPreservedAsync(deadline.Token).ConfigureAwait(false);
            }
            if (endpoint is not null)
            {
                try { endpoint.Dispose(); configurationAbsent = endpoint.CleanupVerified; }
                catch (Exception error) when (error is not OutOfMemoryException) { }
            }
        }
        var containment = settled && absence && cleanup && preserved && configurationAbsent;
        var passed = AcceptSensitive(failure == "NONE" && cases.Values.All(value => value), ownership, noLogs, containment);
        return new(1, "nativeReceipt", cohort.ToString(), failure, cases, settled, ownership, noLogs, absence, preserved, cleanup,
            configurationAbsent, containment, cohort == RecoveryCodeNativeCohort.PrivateRecovery && passed);
    }

    private static bool AcceptSensitive(bool casesPass, bool ownership, bool noLogs, bool containment) => casesPass && ownership && noLogs && containment;

    public static bool AcceptReceipt(RecoveryCodeNativeReceipt receipt)
    {
        var expected = receipt.Cohort switch
        {
            nameof(RecoveryCodeNativeCohort.PublicCanaries) => PublicIds,
            nameof(RecoveryCodeNativeCohort.PublicSql) => SqlIds,
            nameof(RecoveryCodeNativeCohort.PrivateRecovery) => PrivateIds,
            _ => null
        };
        if (expected is null || receipt.Version != 1 || receipt.Type != "nativeReceipt" || receipt.FailureCode != "NONE") return false;
        var complete = receipt.Cases.Count == expected.Length && expected.All(id => receipt.Cases.TryGetValue(id, out var passed) && passed);
        var contained = receipt.ContainmentVerified && receipt.CaseExecutionSettled && receipt.ContainerAbsentVerified &&
            receipt.BorrowedPostgresPreserved && receipt.CleanupVerified && receipt.DockerConfigurationAbsentVerified;
        return AcceptSensitive(complete, receipt.OwnershipVerified, receipt.NoLogPersistenceVerified, contained) &&
            (receipt.Cohort == nameof(RecoveryCodeNativeCohort.PrivateRecovery) ? receipt.SensitiveAccepted : !receipt.SensitiveAccepted);
    }

    private static Task Dispatch(RecoveryCodeNativeCohort cohort, string id, IdentityPostgresFixture fixture)
    {
        if (cohort == RecoveryCodeNativeCohort.PublicSql)
        {
            var sql = new RecoveryCodeSqlIntegrityTests(fixture);
            return id switch
            {
                "ISSUANCE_BINDING" => sql.IssuanceRejectsForeignOwnerWrongPlannedSetAndOrphanOperation(),
                "RETIREMENT_BINDING" => sql.RetirementRejectsForeignOwnerAndOrphanCause(),
                "ENTRY_ONE_WAY" => sql.ConsumedEntryCannotRearmRotateVerifierOrRewriteOwnerOutcome(),
                "SET_ONE_WAY" => sql.SetUsageCannotRewindOrBecomeLifecycleAndRetirementIsTerminal(),
                "OPERATION_ONE_WAY" => sql.TerminalOperationCannotReopenOrRewriteContextPublicationOrFirstResponse(),
                _ => throw new InvalidOperationException("RECOVERY_NATIVE_CASE_INVALID")
            };
        }
        var tests = new RecoveryCodePrivateIntegrationTests(fixture);
        return id switch
        {
            "WINNER_SAFE_READ_REPLACE" => tests.WinningDeliveryIsAbsentFromEverySafeOriginalReadAndReplacementRetiresExactPriorSet(),
            "PUBLICATION_WITHDRAWAL" => tests.PublicationChangeAfterOwnerWorkBeforeFinalDecisionRollsBackCodeSetProofConsumeAndOutbox("withdrawal"),
            "PUBLICATION_VERSION" => tests.PublicationChangeAfterOwnerWorkBeforeFinalDecisionRollsBackCodeSetProofConsumeAndOutbox("version"),
            "PUBLICATION_INTERVAL" => tests.PublicationChangeAfterOwnerWorkBeforeFinalDecisionRollsBackCodeSetProofConsumeAndOutbox("interval"),
            "SAME_CODE_CONCURRENCY" => tests.SameCodeTwoOwnerOperationsCommitOnlyOneRealAccountMutationAndOutbox(),
            "OWNER_REJECTION" => tests.OwnerRejectionOrFinalDriftRollsBackClaimActualAccountEffectAndOutbox(false),
            "FINAL_DRIFT" => tests.OwnerRejectionOrFinalDriftRollsBackClaimActualAccountEffectAndOutbox(true),
            "TRANSACTION_ADMISSION" => tests.ConsumeRejectsNestedForeignOrWrongOwnerTransactionBeforeClaim(),
            "SAFE_DUPLICATE" => tests.SameOwnerDuplicateReadsOnlyOriginalSafeResultWithoutSecondCodeAcceptance(),
            "DEFAULT_ABSENCE" => tests.OrdinaryAuthenticatedHostDoesNotRegisterRecoveryRoutesOrOwnerByDefault(),
            _ => throw new InvalidOperationException("RECOVERY_NATIVE_CASE_INVALID")
        };
    }

    private sealed record CanaryResult(bool Observed, bool Ownership, bool NoLogs, bool Absent, bool Cleanup);
    private static async Task<CanaryResult> CanaryAsync(RecoveryCodeLocalDockerEndpoint endpoint, DirectoryInfo repository, string id)
    {
        var owner = RecoveryCodeOwnedDockerFixture.PrepareNativePostgres(endpoint, repository);
        var reached = false; var expectedFailureObserved = false; var cleanup = false;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            if (id == "AFTER_CREATE") { await owner.StartPublicCreateCanaryAsync(deadline.Token).ConfigureAwait(false); reached = owner.CreatedIdentityCaptured; }
            else if (id != "BEFORE_CREATE")
            {
                await owner.StartAsync(deadline.Token).ConfigureAwait(false); reached = owner.OwnershipVerified;
                if (id is "AFTER_MIGRATION" or "CASE_FAILURE")
                {
                    var fixture = new IdentityPostgresFixture(owner);
                    try { await using var database = await fixture.CreateFiniteDatabaseAsync(deadline.Token).ConfigureAwait(false); }
                    finally { await fixture.DisposeAsync().ConfigureAwait(false); }
                }
            }
            else reached = true;
            // This unmistakable public sentinel occurs without calling any account/code-producing method.
            if (id == "CASE_FAILURE") throw new PublicCanaryException();
        }
        catch (PublicCanaryException) { expectedFailureObserved = true; }
        catch (Exception error) when (error is not OutOfMemoryException) { reached = false; }
        finally
        {
            try { await owner.DisposeAsync().ConfigureAwait(false); cleanup = owner.CleanupVerified; }
            catch (Exception error) when (error is not OutOfMemoryException) { }
        }
        // Cleanup rejection is simulated only after actual cleanup; it can never set SensitiveAccepted.
        var sensitiveCleanupAccepted = AcceptSensitive(reached, owner.CreatedIdentityCaptured, owner.NoLogPersistenceVerified,
            id != "CLEANUP_REJECT" && cleanup && owner.ContainerAbsentVerified);
        var observed = reached && cleanup && owner.ContainerAbsentVerified &&
            (id != "CASE_FAILURE" || expectedFailureObserved) && (id != "CLEANUP_REJECT" || !sensitiveCleanupAccepted);
        var noContainer = id == "BEFORE_CREATE";
        return new(observed, noContainer || owner.CreatedIdentityCaptured, noContainer || owner.NoLogPersistenceVerified, owner.ContainerAbsentVerified, cleanup);
    }
    private sealed class PublicCanaryException : Exception;
}
