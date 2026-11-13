using Tunner.Testing.Recovery;

namespace Tunner.Identity.Persistence.Tests;

public enum RecoveryCodeNativeCohort { PublicCanaries, PublicSql, PrivateRecovery, PublicCapacity }

/// <summary>Only fixed booleans and enums cross the test-assembly boundary. No xUnit runner/TRX is used.</summary>
public sealed record RecoveryCodeNativeReceipt(int Version, string Type, string Cohort, string FailureCode,
    IReadOnlyDictionary<string, bool> Cases, bool CaseExecutionSettled, bool OwnershipVerified,
    bool NoLogPersistenceVerified, bool ContainerAbsentVerified, bool BorrowedPostgresPreserved,
    bool CleanupVerified, bool DockerConfigurationAbsentVerified, bool ContainmentVerified, bool SensitiveAccepted);

public static class RecoveryCodeFiniteNativeExecution
{
    // Bulk migration-only preflight covers every fixed invocation DB; ordinary cases keep their 90s budget.
    private static readonly TimeSpan PublicCapacityProvisioningBudget = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan PublicCapacityCaseBudget = PublicCapacityProvisioningBudget + TimeSpan.FromSeconds(30);
    private static readonly string[] CanaryIds = ["BEFORE_CREATE", "AFTER_CREATE", "AFTER_START", "AFTER_MIGRATION", "CASE_FAILURE", "CLEANUP_REJECT"];
    private static readonly string[] PublicIds = [.. CanaryIds, "OWNERSHIP_REJECT", "NO_LOG_REJECT", "SENSITIVE_REJECT"];
    private static readonly string[] CapacityIds = ["MIGRATION_CAPACITY"];
    private static readonly string[] SqlIds = ["ISSUANCE_BINDING", "RETIREMENT_BINDING", "ENTRY_ONE_WAY", "SET_ONE_WAY", "OPERATION_ONE_WAY",
        "FEDERATION_NAMESPACE_BINDING", "FEDERATION_SINGLE_OWNER", "FEDERATION_OWNER_ROLLBACK", "FEDERATION_IMMUTABLE_VERSION",
        "FEDERATION_QUALIFICATION_LIFECYCLE", "FEDERATION_ATTEMPT_INTEGRITY", "FEDERATION_SESSION_SOURCE", "FEDERATION_MIGRATION_GUARDS"];
    private static readonly string[] PrivateIds = [
        "FEDERATION_PRIMARY_MFA",
        "RECOVERY_GOVERNED_EFFECT", "RECOVERY_GOVERNED_BINDING", "RECOVERY_GOVERNED_CURRENTNESS", "RECOVERY_GOVERNED_ORIGINAL",
        "RECOVERY_GOVERNED_ROLLBACK", "RECOVERY_GOVERNED_HTTPS", "RECOVERY_GOVERNED_SQL_BINDING", "RECOVERY_GOVERNED_SQL_INTERVAL",
        "RECOVERY_CONTACT_EFFECT", "RECOVERY_CONTACT_VALIDITY", "RECOVERY_CONTACT_AUTHORITY",
        "RECOVERY_CONTACT_ORIGINAL", "RECOVERY_CONTACT_ROLLBACK", "RECOVERY_CONTACT_HTTPS",
        "RECOVERY_PASSKEY_EFFECT", "RECOVERY_PASSKEY_ASSERTION", "RECOVERY_PASSKEY_BINDING", "RECOVERY_PASSKEY_CURRENTNESS",
        "RECOVERY_PASSKEY_ORIGINAL", "RECOVERY_PASSKEY_CONCURRENCY", "RECOVERY_PASSKEY_ROLLBACK", "RECOVERY_PASSKEY_HTTPS",
        "RECOVERY_PASSWORD_HTTPS_WIRE",
        "RECOVERY_PASSWORD_ENTRY", "RECOVERY_PASSWORD_EFFECT", "RECOVERY_PASSWORD_VALIDITY", "RECOVERY_PASSWORD_ONE_CODE", "RECOVERY_PASSWORD_ROLLBACK",
        "RECOVERY_CASE_ORIGINAL", "RECOVERY_CASE_ONE_CODE", "RECOVERY_CASE_SELECTED_SESSION", "RECOVERY_CASE_AUTHORITY", "RECOVERY_CASE_ROLLBACK",
        "FEDERATION_HTTPS_WIRE",
        "FEDERATION_REGISTRATION", "FEDERATION_REGISTRATION_REJECTION", "FEDERATION_REGISTRATION_FINALITY",
        "FEDERATION_UNLINK_EFFECTS_HISTORY", "FEDERATION_MANAGEMENT_FINAL_AUTHORITY",
        "FEDERATION_OPERATION_PROOF", "FEDERATION_OPERATION_PROOF_REJECTION",
        "FEDERATION_CONFIRMED_OWNERSHIP", "FEDERATION_REQUIRED_POSTURE",
        "PARENT003_REGISTRATION_BOUNDARY", "PARENT003_REGISTRATION_FLOW", "PARENT003_REGISTRATION_PROGRESS", "PARENT005_AUTH_SESSION",
        "PARENT005_AUTH_DUPLICATE", "PARENT005_AUTH_CONTACT_CURRENT", "PARENT006_PROOF_SESSION", "PARENT006_PROOF_ROLLBACK",
        "PARENT006_BROWSER_ORIGINAL", "PARENT006_BROWSER_ABSENCE", "WINNER_SAFE_READ_REPLACE", "PUBLICATION_WITHDRAWAL",
        "PUBLICATION_VERSION", "PUBLICATION_INTERVAL", "FINAL_PROOF_EXPIRY", "SAME_CODE_CONCURRENCY",
        "OWNER_REJECTION", "FINAL_DRIFT", "TRANSACTION_ADMISSION", "SAFE_DUPLICATE",
        "DEFAULT_ABSENCE", "ADMISSION_WITHDRAWAL", "ADMISSION_POLICY", "ADMISSION_EVIDENCE",
        "ADMISSION_TARGET", "ADMISSION_INTERVAL", "ADMISSION_MISSING", "ADMISSION_UNCHANGED",
        "DISTINCT_CODES_ANY_DIFFERENT_OWNER", "DISTINCT_CODES_ANY_SAME_OWNER", "DISTINCT_CODES_EXACT_DIFFERENT_OWNER", "DISTINCT_CODES_EXACT_SAME_OWNER",
        "ATTEMPT_LIMIT_ACCOUNT", "ATTEMPT_LIMIT_NETWORK", "REPLACEMENT_SAME_FACTS_RACE", "CONSUME_BEFORE_REPLACE",
        "REPLACE_BEFORE_CONSUME", "PROOF_ABSENT", "PROOF_WRONG_PASSWORD", "PROOF_INSUFFICIENT",
        "PROOF_EXPIRED", "PROOF_CONSUMED", "BIND_OPERATION", "BIND_CORRELATION",
        "BIND_SCOPE", "BIND_CONTEXT", "BIND_RESOURCE", "BIND_VERSION",
        "BIND_DIGEST", "BIND_SESSION", "BIND_ACCOUNT", "BIND_ORIGIN",
        "BIND_KEY", "BFF_CSRF_ORIGIN_OWNER", "STABLE_GENERATE_DUPLICATE", "STABLE_REPLACE_DUPLICATE",
        "CONSUME_OWNER_THROW", "CONSUME_OWNER_CANCEL", "GENERATE_POST_COMMIT_OBSERVER", "REPLACE_POST_COMMIT_OBSERVER",
        "CONSUME_POST_COMMIT_OBSERVER", "CORRUPT_VERIFIER_FAIL_CLOSED",
        "FEDERATION_ORIGINAL_SESSION", "FEDERATION_FINAL_AUTHORITY", "FEDERATION_BROWSER_BINDING"];

    internal static async Task<RecoveryCodeNativeReceipt> RunAsync(RecoveryCodeNativeCohort cohort, Uri qualifiedLocalEndpoint, DirectoryInfo repository, RecoveryCodeFixtureOwnershipChannel ownershipChannel)
    {
        var cases = (cohort == RecoveryCodeNativeCohort.PublicCanaries ? PublicIds : cohort == RecoveryCodeNativeCohort.PublicSql ? SqlIds : cohort == RecoveryCodeNativeCohort.PublicCapacity ? CapacityIds : PrivateIds)
            .ToDictionary(id => id, _ => false, StringComparer.Ordinal);
        var failure = "NONE"; var settled = true; var ownership = false; var noLogs = false; var absence = false; var cleanup = false;
        RecoveryCodeBorrowedPostgres? borrowed = null; var preserved = false;
        RecoveryCodeLocalDockerEndpoint? endpoint = null; var configurationAbsent = false;
        try
        {
            if (!Enum.IsDefined(cohort) || Environment.GetEnvironmentVariable("OTEL_SDK_DISABLED") != "true")
                throw new InvalidOperationException("RECOVERY_NATIVE_INPUT_INVALID");
            endpoint = RecoveryCodeLocalDockerEndpoint.Admit(qualifiedLocalEndpoint, ownershipChannel);
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
                            var capacityExecution = cohort == RecoveryCodeNativeCohort.PublicCapacity ? CapacityAsync(fixture, ownershipChannel) : null;
                            Task execution = capacityExecution is null ? Dispatch(cohort, id, fixture) : capacityExecution;
                            try
                            {
                                await execution.WaitAsync(capacityExecution is null ? TimeSpan.FromSeconds(90) : PublicCapacityCaseBudget).ConfigureAwait(false);
                                if (capacityExecution is not null)
                                {
                                    var capacityFailure = await capacityExecution.ConfigureAwait(false);
                                    if (capacityFailure != "NONE") { failure = capacityFailure; break; }
                                }
                                cases[id] = true;
                            }
                            catch (TimeoutException)
                            {
                                failure = "CASE_TIMEOUT"; settled = false;
                                try { await owner.DisposeAsync().ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { }
                                try { await execution.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); settled = true; }
                                catch (Exception error) when (error is not OutOfMemoryException) { settled = execution.IsCompleted; }
                                break;
                            }
                            catch (RecoveryCodeFirstCaseFailure error) { failure = error.FailureCode; break; }
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
                try { preserved = await borrowed.VerifyPreservedAsync(deadline.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is not OutOfMemoryException) { preserved = false; }
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
    internal static void RequireCaseRegistry(System.Text.Json.JsonElement registry)
    {
        var names = new[] { "PUBLIC_CANARIES", "PUBLIC_SQL", "PRIVATE_RECOVERY", "PUBLIC_CAPACITY" };
        var expected = new[] { PublicIds, SqlIds, PrivateIds, CapacityIds };
        for (var i = 0; i < names.Length; i++)
        {
            var current = registry.GetProperty(names[i]).EnumerateArray().Select(x => x.GetString() ?? throw new InvalidOperationException("RECOVERY_CASE_REGISTRY_INVALID"));
            if (!current.SequenceEqual(expected[i], StringComparer.Ordinal)) throw new InvalidOperationException("RECOVERY_CASE_REGISTRY_INVALID");
        }
    }

    public static bool AcceptReceipt(RecoveryCodeNativeReceipt receipt)
    {
        var expected = receipt.Cohort switch
        {
            nameof(RecoveryCodeNativeCohort.PublicCanaries) => PublicIds,
            nameof(RecoveryCodeNativeCohort.PublicSql) => SqlIds,
            nameof(RecoveryCodeNativeCohort.PublicCapacity) => CapacityIds,
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

    private static async Task<string> CapacityAsync(IdentityPostgresFixture fixture, RecoveryCodeFixtureOwnershipChannel channel)
    {
        // Actual public migration-only load: one DB per fixed private invocation plus the existing foreign-transaction DB.
        var expected = PrivateIds.Length + 1; var created = 0; string? connectionString = null;
        var failure = "CAPACITY_PROVISION_FAILED";
        using var deadline = new CancellationTokenSource(PublicCapacityProvisioningBudget);
        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, expected), new ParallelOptions
            {
                MaxDegreeOfParallelism = 2, CancellationToken = deadline.Token
            }, async (_, provisionCancellation) =>
            {
                await using var database = await fixture.CreateFiniteDatabaseAsync(provisionCancellation).ConfigureAwait(false);
                Interlocked.CompareExchange(ref connectionString, database.ConnectionString, null);
                Interlocked.Increment(ref created);
            }).ConfigureAwait(false);
            failure = "CAPACITY_CONNECT_FAILED";
            await using var connection = new Npgsql.NpgsqlConnection(connectionString ?? throw new InvalidOperationException("RECOVERY_PUBLIC_CAPACITY_NOT_VERIFIED")); await connection.OpenAsync(deadline.Token).ConfigureAwait(false);
            failure = "CAPACITY_DATABASE_BYTES_FAILED";
            await using var databases = new Npgsql.NpgsqlCommand("SELECT COALESCE(sum(pg_database_size(datname)),0)::bigint FROM pg_database WHERE datname LIKE 'identity_%'", connection);
            var databaseBytes = (long)(await databases.ExecuteScalarAsync(deadline.Token).ConfigureAwait(false))!;
            failure = "CAPACITY_WAL_BYTES_FAILED";
            await using var wal = new Npgsql.NpgsqlCommand("SELECT COALESCE(sum(size),0)::bigint FROM pg_ls_waldir()", connection);
            var walBytes = (long)(await wal.ExecuteScalarAsync(deadline.Token).ConfigureAwait(false))!;
            failure = "CAPACITY_REPORT_FAILED";
            await channel.ReportCapacityAsync(created, databaseBytes, walBytes, deadline.Token).ConfigureAwait(false);
            failure = "CAPACITY_LIMIT_FAILED";
            if (created != expected || databaseBytes <= 0 || walBytes < 0 || databaseBytes + walBytes >= RecoveryCodeOwnedDockerFixture.NativePostgresCapacityBytes)
                return failure;
            failure = "CAPACITY_FINALIZE_FAILED";
            return "NONE";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Closed public stages only. No exception type/message, SQL values, or provider diagnostics leave this method.
            if (!deadline.IsCancellationRequested && failure == "CAPACITY_PROVISION_FAILED") return CapacityProvisionFailure(error);
            return deadline.IsCancellationRequested ? failure switch
            {
                "CAPACITY_PROVISION_FAILED" => "CAPACITY_PROVISION_DEADLINE",
                "CAPACITY_CONNECT_FAILED" => "CAPACITY_CONNECT_DEADLINE",
                "CAPACITY_DATABASE_BYTES_FAILED" => "CAPACITY_DATABASE_BYTES_DEADLINE",
                "CAPACITY_WAL_BYTES_FAILED" => "CAPACITY_WAL_BYTES_DEADLINE",
                "CAPACITY_REPORT_FAILED" => "CAPACITY_REPORT_DEADLINE",
                "CAPACITY_FINALIZE_FAILED" => "CAPACITY_FINALIZE_DEADLINE",
                _ => "CAPACITY_LIMIT_FAILED"
            } : failure;
        }
    }

    internal const string NpgsqlTransientFailureMessage = "An exception has been raised that is likely due to a transient failure.";
    internal static string CapacityProvisionFailure(Exception error)
    {
        // Pinned Npgsql10.0.3 execution strategy: admit exactly one known envelope, never recursively unwrap.
        if (error.GetType() == typeof(InvalidOperationException) && error.Message == NpgsqlTransientFailureMessage &&
            error.InnerException is { } cause && (cause is Npgsql.NpgsqlException or TimeoutException ||
                cause is Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: Npgsql.PostgresException }))
            error = cause;
        // Only the direct provider type or this existing EF wrapper is admitted; never walk an arbitrary exception graph.
        var postgres = error as Npgsql.PostgresException ??
            (error is Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: Npgsql.PostgresException wrapped } ? wrapped : null);
        if (postgres is not null) return postgres.SqlState switch
        {
            "53100" => "CAPACITY_PROVISION_DISK_FULL",
            "53200" => "CAPACITY_PROVISION_OUT_OF_MEMORY",
            "53300" => "CAPACITY_PROVISION_CONNECTION_LIMIT",
            "57014" => "CAPACITY_PROVISION_QUERY_CANCELED",
            "3D000" => "CAPACITY_PROVISION_INVALID_CATALOG",
            _ => "CAPACITY_PROVISION_FAILED"
        };
        return error switch
        {
            Npgsql.NpgsqlException { InnerException: TimeoutException } => "CAPACITY_PROVISION_CLIENT_TIMEOUT",
            Npgsql.NpgsqlException { InnerException: IOException } => "CAPACITY_PROVISION_CLIENT_IO",
            Npgsql.NpgsqlException => "CAPACITY_PROVISION_NPGSQL_FAILED",
            OperationCanceledException => "CAPACITY_PROVISION_CANCELED",
            TimeoutException => "CAPACITY_PROVISION_CLIENT_TIMEOUT",
            IOException => "CAPACITY_PROVISION_CLIENT_IO",
            Microsoft.EntityFrameworkCore.DbUpdateException => "CAPACITY_PROVISION_EF_UPDATE_FAILED",
            ObjectDisposedException => "CAPACITY_PROVISION_OBJECT_DISPOSED",
            InvalidOperationException invalid => CapacityProvisionInvariant(invalid.Message),
            _ => "CAPACITY_PROVISION_FAILED"
        };
    }

    private static string CapacityProvisionInvariant(string message)
    {
        // This public-migration-only diagnostic returns identifiers, never text. Unknown/localized/oversized messages stay generic.
        if (message.Length > 2048) return "CAPACITY_PROVISION_INVALID_OPERATION";
        if (message == "RECOVERY_IDENTITY_DATABASE_OPTIONS_MISMATCH") return "CAPACITY_PROVISION_OPTIONS_MISMATCH";
        if (message == "Identity test database must have the complete current migration before a test can run.")
            return "CAPACITY_PROVISION_INCOMPLETE_MIGRATION";
        const string prefix = "An error was generated for warning '";
        if (message.StartsWith(prefix + Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning.Name + "': ", StringComparison.Ordinal))
            return "CAPACITY_PROVISION_EF_MANY_PROVIDERS";
        if (message.StartsWith(prefix + Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning.Name + "': ", StringComparison.Ordinal))
            return "CAPACITY_PROVISION_EF_PENDING_MODEL";
        return "CAPACITY_PROVISION_INVALID_OPERATION";
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
                "FEDERATION_NAMESPACE_BINDING" => new FederationIdentityPersistenceTests(fixture).ExactRegistrationNamespacesRoundTripWithoutCrossClientOrSubjectFolding(),
                "FEDERATION_SINGLE_OWNER" => new FederationIdentityPersistenceTests(fixture).ConcurrentExactIdentityHasOneOwnerAndCannotRewriteThatOwner(),
                "FEDERATION_OWNER_ROLLBACK" => new FederationIdentityPersistenceTests(fixture).OwnerRollbackLeavesNoPartialIdentityReservation(),
                "FEDERATION_IMMUTABLE_VERSION" => new FederationIdentityPersistenceTests(fixture).PersistedSubjectIsImmutableAndEvidenceUpdatesDetectStaleVersion(),
                "FEDERATION_QUALIFICATION_LIFECYCLE" => new FederationSqlIntegrityTests(fixture).LegacyQualificationRequiresFreshEvidenceAndPreservesClientIdentity(),
                "FEDERATION_ATTEMPT_INTEGRITY" => new FederationSqlIntegrityTests(fixture).AttemptsPreserveBindingVerifiedTupleAndOneUseTerminalResult(),
                "FEDERATION_SESSION_SOURCE" => new FederationSqlIntegrityTests(fixture).FederatedSessionSourceIsImmutableAndPasswordGuardsRemainActive(),
                "FEDERATION_MIGRATION_GUARDS" => new FederationSqlIntegrityTests(fixture).MigrationRoundTripInstallsGuardsAndRejectsPopulatedDown(),
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
            "FINAL_PROOF_EXPIRY" => tests.PublicationChangeAfterOwnerWorkBeforeFinalDecisionRollsBackCodeSetProofConsumeAndOutbox("proof-expiry"),
            "SAME_CODE_CONCURRENCY" => tests.SameCodeTwoOwnerOperationsCommitOnlyOneRealAccountMutationAndOutbox(),
            "OWNER_REJECTION" => tests.OwnerRejectionOrFinalDriftRollsBackClaimActualAccountEffectAndOutbox(false),
            "FINAL_DRIFT" => tests.OwnerRejectionOrFinalDriftRollsBackClaimActualAccountEffectAndOutbox(true),
            "TRANSACTION_ADMISSION" => tests.ConsumeRejectsNestedForeignOrWrongOwnerTransactionBeforeClaim(),
            "SAFE_DUPLICATE" => tests.SameOwnerDuplicateReadsOnlyOriginalSafeResultWithoutSecondCodeAcceptance(),
            "DEFAULT_ABSENCE" => tests.OrdinaryAuthenticatedHostDoesNotRegisterRecoveryRoutesOrOwnerByDefault(),
            "ADMISSION_WITHDRAWAL" => tests.OwnerAdmissionChangeAfterApplyUsesOriginalFactsAndRejectsDrift("withdrawal"),
            "ADMISSION_POLICY" => tests.OwnerAdmissionChangeAfterApplyUsesOriginalFactsAndRejectsDrift("policy"),
            "ADMISSION_EVIDENCE" => tests.OwnerAdmissionChangeAfterApplyUsesOriginalFactsAndRejectsDrift("evidence"),
            "ADMISSION_TARGET" => tests.OwnerAdmissionChangeAfterApplyUsesOriginalFactsAndRejectsDrift("target"),
            "ADMISSION_INTERVAL" => tests.OwnerAdmissionChangeAfterApplyUsesOriginalFactsAndRejectsDrift("interval"),
            "ADMISSION_MISSING" => tests.OwnerAdmissionChangeAfterApplyUsesOriginalFactsAndRejectsDrift("missing"),
            "ADMISSION_UNCHANGED" => tests.OwnerAdmissionChangeAfterApplyUsesOriginalFactsAndRejectsDrift("unchanged"),
            "DISTINCT_CODES_ANY_DIFFERENT_OWNER" => tests.DifferentCodesLinearizeUnderSelectedOwnerUsageAdmissionWithoutASurvivingSession(false, false),
            "DISTINCT_CODES_ANY_SAME_OWNER" => tests.DifferentCodesLinearizeUnderSelectedOwnerUsageAdmissionWithoutASurvivingSession(false, true),
            "DISTINCT_CODES_EXACT_DIFFERENT_OWNER" => tests.DifferentCodesLinearizeUnderSelectedOwnerUsageAdmissionWithoutASurvivingSession(true, false),
            "DISTINCT_CODES_EXACT_SAME_OWNER" => tests.DifferentCodesLinearizeUnderSelectedOwnerUsageAdmissionWithoutASurvivingSession(true, true),
            "ATTEMPT_LIMIT_ACCOUNT" => tests.MalformedFailuresPersistSelectedAccountAndNetworkAttemptLimits(false),
            "ATTEMPT_LIMIT_NETWORK" => tests.MalformedFailuresPersistSelectedAccountAndNetworkAttemptLimits(true),
            "REPLACEMENT_SAME_FACTS_RACE" => tests.ConcurrentReplacementsOfSameOriginalFactsReleaseOnlyOneGeneration(),
            "CONSUME_BEFORE_REPLACE" => tests.ConsumeAndReplacementRespectBothActualLockOrderings(false),
            "REPLACE_BEFORE_CONSUME" => tests.ConsumeAndReplacementRespectBothActualLockOrderings(true),
            "PROOF_ABSENT" => tests.RecoveryOwnerCannotRunWithoutFreshSufficientUnconsumedProof("absent"),
            "PROOF_WRONG_PASSWORD" => tests.RecoveryOwnerCannotRunWithoutFreshSufficientUnconsumedProof("wrong-password"),
            "PROOF_INSUFFICIENT" => tests.RecoveryOwnerCannotRunWithoutFreshSufficientUnconsumedProof("insufficient"),
            "PROOF_EXPIRED" => tests.RecoveryOwnerCannotRunWithoutFreshSufficientUnconsumedProof("expired"),
            "PROOF_CONSUMED" => tests.RecoveryOwnerCannotRunWithoutFreshSufficientUnconsumedProof("consumed"),
            "BIND_OPERATION" => tests.RecoveryProofRejectsEverySubstitutedOwnerBindingBeforeCallback("operation"),
            "BIND_CORRELATION" => tests.RecoveryProofRejectsEverySubstitutedOwnerBindingBeforeCallback("correlation"),
            "BIND_SCOPE" => tests.RecoveryProofRejectsEverySubstitutedOwnerBindingBeforeCallback("scope"),
            "BIND_CONTEXT" => tests.RecoveryProofRejectsEverySubstitutedOwnerBindingBeforeCallback("context"),
            "BIND_RESOURCE" => tests.RecoveryProofRejectsEverySubstitutedOwnerBindingBeforeCallback("resource"),
            "BIND_VERSION" => tests.RecoveryProofRejectsEverySubstitutedOwnerBindingBeforeCallback("version"),
            "BIND_DIGEST" => tests.RecoveryProofRejectsEverySubstitutedOwnerBindingBeforeCallback("digest"),
            "BIND_SESSION" => tests.RecoveryProofRejectsEverySubstitutedOwnerBindingBeforeCallback("session"),
            "BIND_ACCOUNT" => tests.RecoveryProofRejectsEverySubstitutedOwnerBindingBeforeCallback("account"),
            "BIND_ORIGIN" => tests.RecoveryProofRejectsEverySubstitutedOwnerBindingBeforeCallback("origin"),
            "BIND_KEY" => tests.RecoveryProofRejectsEverySubstitutedOwnerBindingBeforeCallback("key"),
            "BFF_CSRF_ORIGIN_OWNER" => tests.RecoveryBffRejectsCrossOwnerCsrfOriginAndUnmappedAccountSelectorsWithoutEnumeration(),
            "STABLE_GENERATE_DUPLICATE" => tests.StableGenerateOrReplaceRequestRacesAndChangedBindingAreIdempotent(false),
            "STABLE_REPLACE_DUPLICATE" => tests.StableGenerateOrReplaceRequestRacesAndChangedBindingAreIdempotent(true),
            "CONSUME_OWNER_THROW" => tests.ConsumeOwnerThrowOrCancellationRollsBackProvisionalClaim(false),
            "CONSUME_OWNER_CANCEL" => tests.ConsumeOwnerThrowOrCancellationRollsBackProvisionalClaim(true),
            "GENERATE_POST_COMMIT_OBSERVER" => tests.IssuancePostCommitObserverFailureReturnsNoPayloadAndOnlySafeOriginal(false),
            "REPLACE_POST_COMMIT_OBSERVER" => tests.IssuancePostCommitObserverFailureReturnsNoPayloadAndOnlySafeOriginal(true),
            "CONSUME_POST_COMMIT_OBSERVER" => tests.ConsumePostCommitObserverFailureReconcilesOnlyOriginalSafeOutcome(),
            "CORRUPT_VERIFIER_FAIL_CLOSED" => tests.CorruptedPersistedVerifierFailsClosedBeforeAnyOwnerApplication(),
            "FEDERATION_ORIGINAL_SESSION" => new FederationPrivateIntegrationTests(fixture).LinkedSignInCommitsOneOriginalSessionAndNeverReacceptsCallback(),
            "FEDERATION_PRIMARY_MFA" => new FederationPrivateIntegrationTests(fixture).RequiredTotpDefersSessionAndRecoversOnlyOriginalCompositeResult(),
            "FEDERATION_FINAL_AUTHORITY" => new FederationPrivateIntegrationTests(fixture).FinalFederationAuthorityDriftRollsBackStagedOwnerResultAndPreviousSessionEffect(),
            "FEDERATION_BROWSER_BINDING" => new FederationPrivateIntegrationTests(fixture).ChangedBrowserCookieCannotCompleteOrBorrowOriginalFederatedResult(),
            "FEDERATION_CONFIRMED_OWNERSHIP" => new FederationManagementPrivateIntegrationTests(fixture).ExplicitConfirmationAndExactExternalOwnershipHaveOneWinnerWithoutEmailLinking(),
            "FEDERATION_REQUIRED_POSTURE" => new FederationManagementPrivateIntegrationTests(fixture).RequiredRemainingPostureDenialPreservesLinkAndUnconsumedProof(),
            "FEDERATION_UNLINK_EFFECTS_HISTORY" => new FederationManagementPrivateIntegrationTests(fixture).ExplicitSessionEffectsAndFreshSameAccountRelinkPreserveExactHistory(),
            "FEDERATION_MANAGEMENT_FINAL_AUTHORITY" => new FederationManagementPrivateIntegrationTests(fixture).FinalPureAuthorityDriftRollsBackLinkProofAndOutboxWithExactOriginalRecovery(),
            "FEDERATION_OPERATION_PROOF" => new FederationManagementPrivateIntegrationTests(fixture).QualifiedFederationProofCompletesOneOriginalOwnerAndConsumesOneRealProof(),
            "FEDERATION_OPERATION_PROOF_REJECTION" => new FederationManagementPrivateIntegrationTests(fixture).RejectedFederationProofEvidenceAndLateSourceDriftCannotIssueOrMutateOwner(),
            "FEDERATION_REGISTRATION" => new FederationPrivateIntegrationTests(fixture).QualifiedGoogleRegistrationRequiresNoticesThenSharesPersonalInformationAndOriginalRecoveryWithoutSession(),
            "FEDERATION_REGISTRATION_REJECTION" => new FederationPrivateIntegrationTests(fixture).UnqualifiedExpiredForeignOrCollidingFederationIntakeCannotAdoptAccountOrIssueSession(),
            "FEDERATION_REGISTRATION_FINALITY" => new FederationPrivateIntegrationTests(fixture).LateRegistrationProfileNoticeOrContactDriftRollsBackCanonicalIntakeAndRecoversOriginal(),
            "RECOVERY_GOVERNED_EFFECT" => new RecoveryGovernedRepairPrivateIntegrationTests(fixture).ExactTrustedDispositionRepairsOnlyPasswordAndRecordsExplicitEffects(),
            "RECOVERY_GOVERNED_BINDING" => new RecoveryGovernedRepairPrivateIntegrationTests(fixture).ForeignUnqualifiedOrUncontrolledDispositionCannotAuthorizeAnyEffect(),
            "RECOVERY_GOVERNED_CURRENTNESS" => new RecoveryGovernedRepairPrivateIntegrationTests(fixture).HigherProofTiersAndLateAuthorityOrContactDriftRemainAuthoritative(),
            "RECOVERY_GOVERNED_ORIGINAL" => new RecoveryGovernedRepairPrivateIntegrationTests(fixture).OriginalAndCompetingDispositionExecutionsHaveOnlyOneCommittedEffect(),
            "RECOVERY_GOVERNED_ROLLBACK" => new RecoveryGovernedRepairPrivateIntegrationTests(fixture).PostSaveFailureAndCancellationRollbackDispositionPasswordAndOutbox(),
            "RECOVERY_GOVERNED_HTTPS" => new RecoveryGovernedRepairPrivateIntegrationTests(fixture).ActualHttpsGovernedConsumerIsNeutralStrictAndOriginalBrowserBound(),
            "RECOVERY_GOVERNED_SQL_BINDING" => new RecoveryGovernedRepairPrivateIntegrationTests(fixture).SqlGuardRequiresExactFullDispositionBindingsAndImmutableResults(),
            "RECOVERY_GOVERNED_SQL_INTERVAL" => new RecoveryGovernedRepairPrivateIntegrationTests(fixture).SqlIntervalPreservesHundredNanosecondsOffsetsInclusiveEdgesAndInvalidDates(),
            "RECOVERY_CONTACT_EFFECT" => new RecoveryContactRepairPrivateIntegrationTests(fixture).ContactProofRepairsPasswordAndQueuesCommittedDeduplicatedNotice(),
            "RECOVERY_CONTACT_VALIDITY" => new RecoveryContactRepairPrivateIntegrationTests(fixture).ContactProofValidationAttemptsExpiryAndPurgeRemainOneUse(),
            "RECOVERY_CONTACT_AUTHORITY" => new RecoveryContactRepairPrivateIntegrationTests(fixture).ContactRiskHierarchyPolicyAndVerifiedContactDriftFailClosed(),
            "RECOVERY_CONTACT_ORIGINAL" => new RecoveryContactRepairPrivateIntegrationTests(fixture).ContactOriginalAndConcurrentRepairsApplyOnePasswordEffect(),
            "RECOVERY_CONTACT_ROLLBACK" => new RecoveryContactRepairPrivateIntegrationTests(fixture).ContactPostSaveFaultAndCancellationPreserveProofAndPassword(),
            "RECOVERY_CONTACT_HTTPS" => new RecoveryContactRepairPrivateIntegrationTests(fixture).ActualHttpsContactRecoveryIsNeutralBoundedAndBrowserBound(),
            "RECOVERY_PASSKEY_EFFECT" => new RecoveryPasskeyRepairPrivateIntegrationTests(fixture).NativeStrongProofRepairsPasswordWithoutCodeCompositionOrLogin(),
            "RECOVERY_PASSKEY_ASSERTION" => new RecoveryPasskeyRepairPrivateIntegrationTests(fixture).NativeStrongProofRejectsSignatureFlagsOriginChallengeAndUserHandle(),
            "RECOVERY_PASSKEY_BINDING" => new RecoveryPasskeyRepairPrivateIntegrationTests(fixture).OriginalCaseAndCredentialBindingsRejectCrossContextReplay(),
            "RECOVERY_PASSKEY_CURRENTNESS" => new RecoveryPasskeyRepairPrivateIntegrationTests(fixture).CurrentPolicyHierarchyAndNativeSourceDriftNeverBurnProof(),
            "RECOVERY_PASSKEY_ORIGINAL" => new RecoveryPasskeyRepairPrivateIntegrationTests(fixture).ExactOriginalDuplicateReconcilesAndAlteredAssertionCannotReplay(),
            "RECOVERY_PASSKEY_CONCURRENCY" => new RecoveryPasskeyRepairPrivateIntegrationTests(fixture).ConcurrentStrongRepairsApplyOneOriginalEffect(),
            "RECOVERY_PASSKEY_ROLLBACK" => new RecoveryPasskeyRepairPrivateIntegrationTests(fixture).PostSaveFaultAndCancellationRollbackNativeCounterAndPasswordEffects(),
            "RECOVERY_PASSKEY_HTTPS" => new RecoveryPasskeyRepairPrivateIntegrationTests(fixture).ActualHttpsStrongRecoveryIsNeutralAndRequiresOriginalBrowserCsrf(),
            "RECOVERY_PASSWORD_HTTPS_WIRE" => new RecoveryPasswordRepairPrivateIntegrationTests(fixture).ActualHttpsRecoveryRequiresOriginalBrowserCsrfAndBoundedPublicCommandsWithoutLogin(),
            "RECOVERY_PASSWORD_ENTRY" => new RecoveryPasswordRepairPrivateIntegrationTests(fixture).NeutralEntryOriginalAndCancellationNeverSelectPublicAccountOrConsumeCode(),
            "RECOVERY_PASSWORD_EFFECT" => new RecoveryPasswordRepairPrivateIntegrationTests(fixture).ExactCodeRepairsNativePasswordWithoutLoginOrUnrelatedStateMutation(),
            "RECOVERY_PASSWORD_VALIDITY" => new RecoveryPasswordRepairPrivateIntegrationTests(fixture).ValidationAndAuthorityDriftLeaveOriginalCodeAndCredentialUnchanged(),
            "RECOVERY_PASSWORD_ONE_CODE" => new RecoveryPasswordRepairPrivateIntegrationTests(fixture).CompetingPasswordRepairsConsumeOneCodeAndRecoverOnlyOriginalResult(),
            "RECOVERY_PASSWORD_ROLLBACK" => new RecoveryPasswordRepairPrivateIntegrationTests(fixture).PostSaveFaultAndCancellationRollbackPasswordEntryCaseCodeResultAndOutbox(),
            "RECOVERY_CASE_ORIGINAL" => new RecoveryCasePrivateIntegrationTests(fixture).OriginalCaseReplayRejectsSubstitutedContextAndRecoversOnlyExactContainedResult(),
            "RECOVERY_CASE_ONE_CODE" => new RecoveryCasePrivateIntegrationTests(fixture).CompetingRecoveryCasesCannotConsumeOneCodeOrRevokeTwoSessions(),
            "RECOVERY_CASE_SELECTED_SESSION" => new RecoveryCasePrivateIntegrationTests(fixture).ContainmentPreservesOtherSessionsCredentialsAndMinorAccountWithoutRestoration(),
            "RECOVERY_CASE_AUTHORITY" => new RecoveryCasePrivateIntegrationTests(fixture).UnknownHigherTierOrLatePolicyAndHierarchyDriftCannotConsumeOrContain(),
            "RECOVERY_CASE_ROLLBACK" => new RecoveryCasePrivateIntegrationTests(fixture).SavedOwnerFailureAndCancellationRollbackCodeSessionCaseResultAndOutbox(),
            "FEDERATION_HTTPS_WIRE" => new FederationBffPrivateIntegrationTests(fixture).ActualHttpsNativeRegistrationRequiresExactPairedContextAndNoticesBeforeSharedOnboarding(),
            "PARENT003_REGISTRATION_BOUNDARY" => new RegistrationBffTests(fixture).ActualHttpsBffKeepsRegistrationAnonymousAndRequiresOriginCsrfAndProtectedBrowserContext(),
            "PARENT003_REGISTRATION_FLOW" => new RegistrationBffTests(fixture).ActualHttpsFlowVerifiesContactBeforePersonalInformationAndActivatesUnder18WithoutSession(),
            "PARENT003_REGISTRATION_PROGRESS" => new RegistrationBffTests(fixture).ProtectedProgressRecoversOnlyItsOwnServerStageAndRestartPreservesAccountEvidence(),
            "PARENT005_AUTH_SESSION" => new AuthenticationTests(fixture).PasswordEstablishesOpaqueHostOnlySessionAndSignOutRevokesIt(),
            "PARENT005_AUTH_DUPLICATE" => new AuthenticationTests(fixture).ConcurrentSuccessfulRetryKeepsOneSessionAndCannotReviveAfterLogout(),
            "PARENT005_AUTH_CONTACT_CURRENT" => new AuthenticationTests(fixture).CurrentAuthorityImmediatelyInvalidatesExistingSession("contact"),
            "PARENT006_PROOF_SESSION" => new StepUpTests(fixture).FreshProofIsConsumedOnceWithoutUpgradingSessionAndEventsContainOnlySafeReferences(),
            "PARENT006_PROOF_ROLLBACK" => new StepUpTests(fixture).TargetConflictAndThrownMutationRollBackBothProofAndTargetAndAllowSafeRetry(),
            "PARENT006_BROWSER_ORIGINAL" => new StepUpBrowserTests(fixture).BoundBrowserActionCompletesOnceReconcilesRetryAndNeverReturnsProofMaterial(),
            "PARENT006_BROWSER_ABSENCE" => new StepUpBrowserTests(fixture).UnauthenticatedOrUnactivatedSurfaceHasNoUsableBridge(),
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
            if (id != "BEFORE_CREATE" && reached) await endpoint.Ownership.PublicStageAsync(owner.InstanceId.ToString("N"), id, deadline.Token).ConfigureAwait(false);
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
