using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Npgsql;
using Tunner.Governance;
using Tunner.Identity.Persistence;
using Tunner.Installer;
using Tunner.Testing.Recovery;

/// <summary>One finite Development-only recovery browser composition; no owner consumption workflow.</summary>
internal static class FiniteRecoveryCoordinator
{
    private static readonly string[] PublicFaults = ["happy", "unarmed", "duplicate", "wrong-page", "wrong-bytes", "oversize", "outside-identity", "stream-error", "delete-error", "unexpected-output", "invalid-result", "action-timeout", "child-timeout", "unexpected-artifact", "fatal-rejection", "journey-entry", "missing-predicate", "cross-component", "cleanup", "oracle-roundtrip", "oracle-input", "oracle-exit", "oracle-timeout", "oracle-stderr", "production-controller", "relay-roundtrip", "relay-malformed", "relay-timeout"];
    private static readonly string[] ResourceFaults = ["after-db", "after-mailpit", "after-vault-session", "after-app-start", "cleanup-reject"];
    private static readonly string[] SupervisorIds = ["SOURCE_CURRENT", "PROTOCOL_VALID", "OUTPUT_CONTAINED", "CHILD_CLOSED", "ORACLE_CLOSED", "CHILD_SURFACES_CLOSED", "SURFACE_REMOVED"];
    private static readonly string[] CoordinatorIds = ["SOURCE_BUILD_CURRENT", "OWNED_DATABASE", "OWNED_MAILPIT", "EPHEMERAL_DP", "CURRENT_MIGRATIONS", "SAFE_PERSISTENCE", "SAFE_OUTBOX", "HOST_STOPPED", "RESOURCES_REMOVED", "BORROWED_POSTGRES_PRESERVED"];
    private static readonly string[] CommonBrowserIds = ["BROWSER_ENGINE", "SURFACE_CHECKED", "BFF_IDENTITY", "ACTUAL_PROOF_METHOD", "EXACT_OWNER_AND_STEP_UP_BINDING", "CANONICAL_FIRST_DELIVERY", "DUPLICATE_NO_REDISCLOSURE", "NO_EARLY_DELIVERY", "COOKIE_FLAGS", "NO_SECRET_URL_OR_STORAGE", "VOLATILE_CLEAR", "REQUESTED_NOT_SAVED", "STRICT_OWNED_SURFACE"];
    private static readonly string[] ClosedLiveFacts = ["contextClosed", "browserClosed", "serverExited", "oracleExited", "oracleGraceful", "surfaceClean", "surfaceRemoved", "cleanupCompleted"];
    private static readonly string[] ClosedPublicFacts = ["contextClosed", "browserClosed", "serverExited", "oracleExited", "surfaceRemoved"];
    private static readonly string[] ClosedPublicSupervisorFacts = ["SOURCE_CURRENT", "CHILD_CLOSED", "ORACLE_CLOSED", "CHILD_SURFACES_CLOSED", "SURFACE_REMOVED"];
    private static readonly string[] Stages = ["START", "ENVIRONMENT", "LIBRARY", "BROWSER", "PUBLIC_ENTRY", "ORACLE", "JOURNEY", "SURFACE", "CLEANUP", "COMPLETE"];
    private static readonly string[] FailureCodes = ["NONE", "INPUT_INVALID", "SOURCE_INVALID", "DEPENDENCY_NOT_READY", "PROTOCOL_FAILED", "BOUNDARY_ERROR", "PREDICATE_FAILED", "ADMISSION_REJECTED", "PATH_REJECTED", "SIZE_LIMIT", "BYTES_MISMATCH", "STREAM_FAILED", "DELETE_FAILED", "ACTION_FAILED", "ORACLE_FAILED", "SUPERVISOR_TIMEOUT", "SURFACE_FAILED", "CLEANUP_FAILED"];

    private const string PublicProtocolSource = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly string[] PublicProtocolIds = ["REAL_EXACT_WINNING_MAP", "REAL_EACH_MISSING_FALSE", "REAL_EXTRA_CROSS_COMPONENT", "CONDITIONAL_COORDINATOR_OWNER", "STRICT_SCALAR_AND_KEY_SHAPE", "SERVER_LIFECYCLE", "PUBLIC_FAILURE_NEVER_REAL", "RELAY_PUBLIC_TOPOLOGY", "PRODUCTION_CONTROLLER_FACTS", "PUBLIC_CONTAINMENT_NEGATIVES", "RAW_MANAGED_OUTPUT", "SOURCE_SELECTOR_COMPATIBILITY"];
    private static readonly string[] PublicProtocolBrowserFacts = ["BROWSER_ENGINE", "SURFACE_CHECKED", "PUBLIC_ENTRY", "ERROR_CONTAINED", "DOWNLOAD_ADMITTED", "DOWNLOAD_REMOVED", "PRODUCTION_CONTROLLER_PUBLIC", "FIXTURE_RELAY_PUBLIC"];
    private static readonly string[] PublicProtocolComponents = ["checks", "supervisor"];
    private static readonly string[] PublicProtocolExtraChecks = ["PUBLIC_ENTRY", "SOURCE_BUILD_CURRENT", "EXACT_SUBSET_REPLACEMENT", "PUBLIC_NONFUNCTIONAL_UNKNOWN"];
    private static readonly string[] PublicProtocolTrueFlags = ["sourceCommitted", "childResultAccepted", .. ClosedLiveFacts];
    private static readonly string[] PublicProtocolFalseFlags = ["protocolRejected", "rawOutputRejected", "timedOut", "cleanupForced", "publicFaultObserved"];
    private static readonly string[] PublicProtocolBooleanFlags = [.. PublicProtocolTrueFlags, .. PublicProtocolFalseFlags, "accepted"];
    private static readonly string[] PublicProtocolRelayFaults = ["relay-malformed", "relay-timeout"];
    private static readonly string[] PublicProtocolControllerFacts = ["PRODUCTION_CONTROLLER_PUBLIC", "DOWNLOAD_ADMITTED", "DOWNLOAD_REMOVED"];
    private static readonly string[] PublicProtocolPidFields = ["childPid", "oraclePid", "serverPid"];
    private static readonly Dictionary<string, object> PublicProtocolBadScalars = new(StringComparer.Ordinal)
    {
        ["version"] = 2, ["type"] = "PUBLIC_NONFUNCTIONAL", ["journeyId"] = "PUBLIC_NONFUNCTIONAL", ["mode"] = "PUBLIC_NONFUNCTIONAL",
        ["sourceCommit"] = new string('c', 40), ["sourceFingerprint"] = "PUBLIC_NONFUNCTIONAL", ["browserVersion"] = "PUBLIC_NONFUNCTIONAL",
        ["status"] = "PUBLIC_NONFUNCTIONAL", ["stage"] = "PUBLIC_NONFUNCTIONAL", ["failureCode"] = "PUBLIC_NONFUNCTIONAL", ["exitCode"] = 1,
        ["checks"] = "PUBLIC_NONFUNCTIONAL", ["supervisor"] = "PUBLIC_NONFUNCTIONAL", ["publicFault"] = "happy", ["publicOwnership"] = "PUBLIC_NONFUNCTIONAL"
    };

    private static string AdmitFixtureRequest(JsonElement frame, int sequence, bool publicMode, string? publicFault)
    {
        FiniteTotpCoordinator.RequireKeys(frame, "version", "type", "requestId", "action");
        var action = frame.GetProperty("action").GetString();
        if (frame.GetProperty("version").GetInt32() != 1 || frame.GetProperty("type").GetString() != "recoveryFixtureRequest" ||
            sequence is < 1 or > 16 || frame.GetProperty("requestId").GetInt32() != sequence || !BrowserTestRecoveryCodes.Actions.Contains(action, StringComparer.Ordinal) ||
            publicMode && (sequence != 1 || action != "READ_SAFE_COUNTS" || publicFault is not ("relay-roundtrip" or "relay-malformed" or "relay-timeout")))
            throw new InvalidOperationException("FINITE_RECOVERY_RELAY_INVALID");
        return action!;
    }
    private static JsonElement? PublicFixtureReply(string fault, int requestId)
    {
        if (requestId != 1) throw new InvalidOperationException("FINITE_RECOVERY_RELAY_INVALID");
        if (fault == "relay-timeout") return null;
        if (fault == "relay-malformed") return JsonSerializer.SerializeToElement(new { version = 1, type = "recoveryFixtureResult", requestId, status = "PUBLIC_INVALID", counts = new { } });
        if (fault != "relay-roundtrip") throw new InvalidOperationException("FINITE_RECOVERY_RELAY_INVALID");
        return JsonSerializer.SerializeToElement(new { version = 1, type = "recoveryFixtureResult", requestId, status = "OK", counts = BrowserTestRecoveryCodes.CountKeys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal) });
    }

    // Public synthetic protocol vectors only. No browser, fixture, code, account or owner is created.
    internal static async Task<int> RunPublicProtocolAsync()
    {
        await using var output = Console.OpenStandardOutput();
        var managed = new ManagedOutputCounter(); Console.SetOut(managed); Console.SetError(managed);
        var cases = PublicProtocolIds.ToDictionary(id => id, _ => false, StringComparer.Ordinal);
        DirectoryInfo? root = null; FiniteTotpBuildIdentity? before = null; var source = string.Empty; var current = false;
        try
        {
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development") throw new InvalidOperationException();
            root = RepositoryRootResolver.Resolve(); source = FiniteTotpCoordinator.Git(root, "rev-parse", "HEAD");
            before = FiniteTotpBuildBinding.Verify(root, recovery: true);
            try { cases["REAL_EXACT_WINNING_MAP"] = ProtocolWinningMaps(); } catch (Exception error) when (error is not OutOfMemoryException) { }
            try { cases["REAL_EACH_MISSING_FALSE"] = ProtocolMissingFalse(); } catch (Exception error) when (error is not OutOfMemoryException) { }
            try { cases["REAL_EXTRA_CROSS_COMPONENT"] = ProtocolExtraChecks(); } catch (Exception error) when (error is not OutOfMemoryException) { }
            try { cases["CONDITIONAL_COORDINATOR_OWNER"] = ProtocolConditionalChecks(); } catch (Exception error) when (error is not OutOfMemoryException) { }
            try { cases["STRICT_SCALAR_AND_KEY_SHAPE"] = ProtocolShapes(); } catch (Exception error) when (error is not OutOfMemoryException) { }
            try { cases["SERVER_LIFECYCLE"] = ProtocolServerClosure(); } catch (Exception error) when (error is not OutOfMemoryException) { }
            try { cases["PUBLIC_FAILURE_NEVER_REAL"] = ProtocolSeparation(); } catch (Exception error) when (error is not OutOfMemoryException) { }
            try { cases["RELAY_PUBLIC_TOPOLOGY"] = await ProtocolRelayAsync().ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { }
            try { cases["PRODUCTION_CONTROLLER_FACTS"] = ProtocolControllerFacts(); } catch (Exception error) when (error is not OutOfMemoryException) { }
            try { cases["PUBLIC_CONTAINMENT_NEGATIVES"] = ProtocolPublicNegatives(); } catch (Exception error) when (error is not OutOfMemoryException) { }
            try { cases["RAW_MANAGED_OUTPUT"] = ProtocolManagedOutput(); } catch (Exception error) when (error is not OutOfMemoryException) { }
            try { cases["SOURCE_SELECTOR_COMPATIBILITY"] = ProtocolSourceBoundary(root, before); } catch (Exception error) when (error is not OutOfMemoryException) { }
            current = FiniteTotpBuildBinding.Verify(root, recovery: true) == before && FiniteTotpCoordinator.Git(root, "rev-parse", "HEAD") == source;
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        var qualified = current && cases.Count == PublicProtocolIds.Length && cases.Values.All(flag => flag) && managed.Count == 0;
        using var write = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await JsonSerializer.SerializeAsync(output, new { version = 1, type = "recoveryPublicProtocolQualification", sourceCommit = source.Length == 40 ? source : null,
            sourceBuildCurrent = current, buildBinding = before, cases, qualified, accepted = false, sensitiveRunAccepted = false,
            unexpectedManagedOutput = managed.Count != 0, failureCode = qualified ? "NONE" : "PUBLIC_PROTOCOL_FAILED",
            scope = "Synthetic in-process validators/reducers/framing only; no browser, Docker, native values or real journey.",
            relayTopology = "NOT_EXECUTED; actual28 browser/oracle/relay/controller matrix remains required",
            recorderCompatibility = "Default/JS recorder branches unchanged and statically reviewed; recorder not executed" }, cancellationToken: write.Token).ConfigureAwait(false);
        await output.WriteAsync("\n"u8.ToArray(), write.Token).ConfigureAwait(false);
        return qualified ? 0 : 1;
    }
    private static JsonObject ProtocolMap(string[] ids)
    {
        var map = new JsonObject(); foreach (var id in ids) map[id] = true; return map;
    }
    private static JsonObject ProtocolReceipt(string journey)
    {
        return new JsonObject
        {
            ["version"] = 1, ["type"] = "recoveryPrivateBrowserReceipt", ["journeyId"] = journey, ["mode"] = "live", ["publicFault"] = null,
            ["sourceCommit"] = PublicProtocolSource, ["sourceCommitted"] = true, ["sourceFingerprint"] = new string('b', 64), ["browserVersion"] = "1.2.3.4",
            ["status"] = "JOURNEY_OK", ["stage"] = "COMPLETE", ["failureCode"] = "NONE", ["checks"] = ProtocolMap(BrowserIds(journey)), ["supervisor"] = ProtocolMap(SupervisorIds),
            ["childResultAccepted"] = true, ["protocolRejected"] = false, ["rawOutputRejected"] = false, ["timedOut"] = false, ["cleanupForced"] = false, ["exitCode"] = 0,
            ["contextClosed"] = true, ["browserClosed"] = true, ["serverExited"] = true, ["oracleExited"] = true, ["oracleGraceful"] = true,
            ["surfaceClean"] = true, ["surfaceRemoved"] = true, ["cleanupCompleted"] = true, ["publicFaultObserved"] = false, ["publicOwnership"] = null, ["accepted"] = true
        };
    }
    private static JsonObject ProtocolPublic(string fault)
    {
        var value = ProtocolReceipt(BrowserTestRecoveryCodes.JourneyIds[0]);
        value["mode"] = "public"; value["publicFault"] = fault; value["status"] = "PUBLIC_BOUNDARY_OK"; value["accepted"] = false; value["publicFaultObserved"] = true;
        value["checks"] = ProtocolMap(PublicProtocolBrowserFacts);
        value["publicOwnership"] = new JsonObject { ["surface"] = null, ["childPid"] = 1, ["oraclePid"] = null, ["serverPid"] = fault == "production-controller" ? 2 : null };
        var checks = value["checks"]!.AsObject();
        if (fault == "missing-predicate") checks.Remove("PUBLIC_ENTRY");
        if (fault is "invalid-result" or "cross-component" or "relay-malformed" or "relay-timeout")
        {
            value["protocolRejected"] = true; value["childResultAccepted"] = false; value["publicFaultObserved"] = false;
            value["supervisor"]!["PROTOCOL_VALID"] = false;
            value["failureCode"] = "PROTOCOL_FAILED"; value["status"] = "NOT_ACCEPTED";
            if (fault.StartsWith("relay-", StringComparison.Ordinal)) value["exitCode"] = 1;
        }
        else if (fault == "unexpected-output") { value["rawOutputRejected"] = true; value["supervisor"]!["OUTPUT_CONTAINED"] = false; }
        else if (fault == "unexpected-artifact") { value["surfaceClean"] = false; Failed("SURFACE_FAILED"); }
        else if (fault == "cleanup") { value["cleanupCompleted"] = false; Failed("CLEANUP_FAILED"); }
        else if (fault == "child-timeout") { value["timedOut"] = true; Failed("SUPERVISOR_TIMEOUT"); }
        else
        {
            var code = fault switch
            {
                "unarmed" or "duplicate" or "wrong-page" => "ADMISSION_REJECTED", "wrong-bytes" => "BYTES_MISMATCH", "oversize" => "SIZE_LIMIT",
                "outside-identity" => "PATH_REJECTED", "stream-error" => "STREAM_FAILED", "delete-error" => "DELETE_FAILED", "action-timeout" => "ACTION_FAILED", "fatal-rejection" => "SUPERVISOR_TIMEOUT", _ => null
            };
            if (code is not null) Failed(code);
        }
        return value;
        void Failed(string code) { value["status"] = "BOUNDARY_FAILED"; value["failureCode"] = code; value["exitCode"] = 1; }
    }
    private static JsonObject ProtocolChanged(JsonObject value, string key, object? replacement)
    {
        var copy = value.DeepClone().AsObject(); copy[key] = JsonSerializer.SerializeToNode(replacement); return copy;
    }
    private static bool ProtocolAdmitted(JsonObject value)
    {
        try
        {
            var journey = value["journeyId"]?.GetValue<string>();
            return ProtocolAdmitted(JsonSerializer.SerializeToElement(value), BrowserTestRecoveryCodes.JourneyIds.Contains(journey, StringComparer.Ordinal) ? journey! : BrowserTestRecoveryCodes.JourneyIds[0], value["mode"]?.GetValue<string>() == "public");
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return false; }
    }
    private static bool ProtocolAdmitted(JsonElement value, string journey, bool publicMode)
    {
        try { RequireReceipt(value, journey, PublicProtocolSource, publicMode); return true; }
        catch (Exception error) when (error is not OutOfMemoryException) { return false; }
    }
    private static bool ProtocolQualifies(JsonObject value, string fault) => ProtocolAdmitted(value) && PublicContained(JsonSerializer.SerializeToElement(value), fault);
    private static bool ProtocolWinningMaps()
    {
        foreach (var journey in BrowserTestRecoveryCodes.JourneyIds)
            if (!ProtocolAdmitted(ProtocolReceipt(journey)) || !Accepted(0, true, "NONE", true, CoordinatorIdsFor(journey).ToDictionary(id => id, _ => true, StringComparer.Ordinal), journey)) return false;
        return true;
    }
    private static bool ProtocolMissingFalse()
    {
        foreach (var journey in BrowserTestRecoveryCodes.JourneyIds)
        {
            foreach (var component in PublicProtocolComponents)
                foreach (var id in component == "checks" ? BrowserIds(journey) : SupervisorIds)
                {
                    var value = ProtocolReceipt(journey); value[component]!.AsObject().Remove(id); if (ProtocolAdmitted(value)) return false;
                    value = ProtocolReceipt(journey); value[component]![id] = false; if (ProtocolAdmitted(value)) return false;
                }
            foreach (var id in CoordinatorIdsFor(journey))
            {
                var map = CoordinatorIdsFor(journey).ToDictionary(key => key, _ => true, StringComparer.Ordinal); map.Remove(id);
                if (Accepted(0, true, "NONE", true, map, journey)) return false;
                map[id] = false; if (Accepted(0, true, "NONE", true, map, journey)) return false;
            }
        }
        return true;
    }
    private static bool ProtocolExtraChecks()
    {
        foreach (var journey in BrowserTestRecoveryCodes.JourneyIds)
            foreach (var extra in PublicProtocolExtraChecks)
            {
                var value = ProtocolReceipt(journey); value["checks"]![extra] = true; if (ProtocolAdmitted(value)) return false;
                value["checks"]!.AsObject().Remove(BrowserIds(journey)[0]); if (ProtocolAdmitted(value)) return false;
            }
        return true;
    }
    private static bool ProtocolConditionalChecks()
    {
        foreach (var journey in BrowserTestRecoveryCodes.JourneyIds)
        {
            var map = CoordinatorIdsFor(journey).ToDictionary(id => id, _ => true, StringComparer.Ordinal);
            var subset = journey == "RECOVERY_REPLACE_PASSWORD_V1"; var publication = journey == "RECOVERY_REPLACE_TOTP_V1";
            if (map.ContainsKey("EXACT_SUBSET_REPLACEMENT") != subset || map.ContainsKey("PUBLICATION_DRIFT_ROLLED_BACK") != publication) return false;
            map[subset ? "PUBLICATION_DRIFT_ROLLED_BACK" : "EXACT_SUBSET_REPLACEMENT"] = true;
            if (Accepted(0, true, "NONE", true, map, journey)) return false;
        }
        return true;
    }
    private static bool ProtocolShapes()
    {
        var live = ProtocolReceipt(BrowserTestRecoveryCodes.JourneyIds[0]);
        foreach (var key in live.Select(property => property.Key).ToArray()) { var copy = live.DeepClone().AsObject(); copy.Remove(key); if (ProtocolAdmitted(copy)) return false; }
        foreach (var key in PublicProtocolTrueFlags) if (ProtocolAdmitted(ProtocolChanged(live, key, false))) return false;
        foreach (var key in PublicProtocolFalseFlags) if (ProtocolAdmitted(ProtocolChanged(live, key, true))) return false;
        foreach (var key in PublicProtocolBooleanFlags) if (ProtocolAdmitted(ProtocolChanged(live, key, "PUBLIC_NONFUNCTIONAL"))) return false;
        foreach (var pair in PublicProtocolBadScalars) if (ProtocolAdmitted(ProtocolChanged(live, pair.Key, pair.Value))) return false;
        if (ProtocolAdmitted(ProtocolChanged(live, "browserVersion", null)) || ProtocolAdmitted(ProtocolChanged(live, "exitCode", null))) return false;
        using var duplicate = JsonDocument.Parse(live.ToJsonString().Insert(1, "\"version\":1,"));
        if (ProtocolAdmitted(duplicate.RootElement, BrowserTestRecoveryCodes.JourneyIds[0], false)) return false;
        using var duplicateCheck = JsonDocument.Parse(live.ToJsonString().Replace("\"checks\":{", "\"checks\":{\"BROWSER_ENGINE\":true,", StringComparison.Ordinal));
        if (ProtocolAdmitted(duplicateCheck.RootElement, BrowserTestRecoveryCodes.JourneyIds[0], false)) return false;
        var wrongCheck = live.DeepClone().AsObject(); wrongCheck["checks"]!["BROWSER_ENGINE"] = "PUBLIC_NONFUNCTIONAL";
        if (ProtocolAdmitted(wrongCheck)) return false;
        var expected = JsonSerializer.SerializeToElement(live);
        return !ProtocolAdmitted(expected, "RECOVERY_REPLACE_PASSWORD_V1", false) && !ProtocolAdmitted(expected, BrowserTestRecoveryCodes.JourneyIds[0], true);
    }
    private static bool ProtocolServerClosure()
    {
        var live = ProtocolReceipt(BrowserTestRecoveryCodes.JourneyIds[0]);
        if (ProtocolAdmitted(ProtocolChanged(live, "serverExited", false))) return false;
        foreach (var fault in PublicFaults)
        {
            var value = ProtocolPublic(fault); if (ProtocolQualifies(ProtocolChanged(value, "serverExited", false), fault)) return false;
            value.Remove("serverExited"); if (ProtocolAdmitted(value)) return false;
        }
        var publicValue = ProtocolPublic("happy"); publicValue["publicOwnership"]!["serverPid"] = 2; return !ProtocolAdmitted(publicValue);
    }
    private static bool ProtocolSeparation()
    {
        foreach (var fault in PublicFaults)
        {
            var value = ProtocolPublic(fault); if (!ProtocolQualifies(value, fault) || ProtocolAdmitted(ProtocolChanged(value, "accepted", true))) return false;
            if (Accepted(0, false, "NONE", true, CoordinatorIdsFor(BrowserTestRecoveryCodes.JourneyIds[0]).ToDictionary(id => id, _ => true, StringComparer.Ordinal), BrowserTestRecoveryCodes.JourneyIds[0])) return false;
        }
        var failed = ProtocolReceipt(BrowserTestRecoveryCodes.JourneyIds[0]); failed["accepted"] = false; failed["status"] = "BOUNDARY_FAILED"; failed["failureCode"] = "PREDICATE_FAILED";
        return ProtocolAdmitted(failed) && !Accepted(0, true, "NONE", false, CoordinatorIdsFor(BrowserTestRecoveryCodes.JourneyIds[0]).ToDictionary(id => id, _ => true, StringComparer.Ordinal), BrowserTestRecoveryCodes.JourneyIds[0]);
    }
    private static async Task<bool> ProtocolRelayAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var request = JsonDocument.Parse("{\"version\":1,\"type\":\"recoveryFixtureRequest\",\"requestId\":1,\"action\":\"READ_SAFE_COUNTS\"}");
        if (AdmitFixtureRequest(request.RootElement, 1, true, "relay-roundtrip") != "READ_SAFE_COUNTS") return false;
        await using var stream = new MemoryStream();
        var reply = PublicFixtureReply("relay-roundtrip", 1); if (reply is null) return false;
        await FiniteTotpCoordinator.WriteFrameAsync(stream, reply.Value, deadline.Token).ConfigureAwait(false); stream.Position = 0;
        var frame = await FiniteTotpCoordinator.ReadFrameAsync(stream, deadline.Token).ConfigureAwait(false);
        FiniteTotpCoordinator.RequireKeys(frame, "version", "type", "requestId", "status", "counts");
        FiniteTotpCoordinator.RequireKeys(frame.GetProperty("counts"), BrowserTestRecoveryCodes.CountKeys);
        if (frame.GetProperty("version").GetInt32() != 1 || frame.GetProperty("type").GetString() != "recoveryFixtureResult" || frame.GetProperty("status").GetString() != "OK" || frame.GetProperty("requestId").GetInt32() != 1 || BrowserTestRecoveryCodes.CountKeys.Any(key => frame.GetProperty("counts").GetProperty(key).GetInt32() != 0)) return false;
        foreach (var action in BrowserTestRecoveryCodes.Actions)
        {
            var live = JsonSerializer.SerializeToElement(new { version = 1, type = "recoveryFixtureRequest", requestId = 16, action });
            if (AdmitFixtureRequest(live, 16, false, null) != action) return false;
        }
        var unknown = JsonSerializer.SerializeToElement(new { version = 1, type = "recoveryFixtureRequest", requestId = 1, action = "PUBLIC_NONFUNCTIONAL_UNKNOWN" });
        var wrongType = JsonSerializer.SerializeToElement(new { version = 1, type = "PUBLIC_NONFUNCTIONAL_UNKNOWN", requestId = 1, action = "READ_SAFE_COUNTS" });
        if (ProtocolRequestAdmitted(unknown, 1, false, null) || ProtocolRequestAdmitted(wrongType, 1, false, null)) return false;
        foreach (var fault in PublicProtocolRelayFaults)
        {
            var value = ProtocolPublic(fault); if (!ProtocolQualifies(value, fault) || ProtocolQualifies(ProtocolChanged(value, "protocolRejected", false), fault) || ProtocolQualifies(ProtocolChanged(value, "cleanupCompleted", false), fault)) return false;
        }
        if (PublicFixtureReply("relay-timeout", 1) is not null || PublicFixtureReply("relay-malformed", 1)?.GetProperty("status").GetString() != "PUBLIC_INVALID") return false;
        if (ProtocolRequestAdmitted(request.RootElement, 2, true, "relay-roundtrip") || ProtocolRequestAdmitted(request.RootElement, 1, true, "happy") || ProtocolRequestAdmitted(request.RootElement, 17, false, null)) return false;
        using var duplicate = JsonDocument.Parse("{\"version\":1,\"version\":1,\"type\":\"recoveryFixtureRequest\",\"requestId\":1,\"action\":\"READ_SAFE_COUNTS\"}");
        if (ProtocolRequestAdmitted(duplicate.RootElement, 1, true, "relay-roundtrip")) return false;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await FiniteTotpCoordinator.ReadFrameAsync(stream, cancelled.Token).ConfigureAwait(false); return false; }
        catch (OperationCanceledException) { return true; }
    }
    private static bool ProtocolRequestAdmitted(JsonElement value, int sequence, bool publicMode, string? fault)
    {
        try { _ = AdmitFixtureRequest(value, sequence, publicMode, fault); return true; }
        catch (Exception error) when (error is not OutOfMemoryException) { return false; }
    }
    private static bool ProtocolControllerFacts()
    {
        var value = ProtocolPublic("production-controller"); if (!ProtocolQualifies(value, "production-controller")) return false;
        foreach (var id in PublicProtocolControllerFacts)
        {
            var copy = value.DeepClone().AsObject(); copy["checks"]![id] = false; if (ProtocolQualifies(copy, "production-controller")) return false;
            copy["checks"]!.AsObject().Remove(id); if (ProtocolQualifies(copy, "production-controller")) return false;
        }
        value["publicOwnership"]!["serverPid"] = null; return !ProtocolQualifies(value, "production-controller");
    }
    private static bool ProtocolPublicNegatives()
    {
        if (PublicFaults.Length != 28 || PublicFaults.Distinct(StringComparer.Ordinal).Count() != 28) return false;
        foreach (var fault in PublicFaults)
        {
            var value = ProtocolPublic(fault); if (!ProtocolQualifies(value, fault) || ProtocolQualifies(value, "PUBLIC_NONFUNCTIONAL_UNREGISTERED")) return false;
            foreach (var key in ClosedPublicFacts) if (ProtocolQualifies(ProtocolChanged(value, key, false), fault)) return false;
            foreach (var key in ClosedPublicSupervisorFacts) { var copy = value.DeepClone().AsObject(); copy["supervisor"]![key] = false; if (ProtocolQualifies(copy, fault)) return false; }
            if (ProtocolQualifies(ProtocolChanged(value, "cleanupForced", true), fault) || ProtocolQualifies(ProtocolChanged(value, "exitCode", 7), fault)) return false;
        }
        foreach (var key in PublicProtocolPidFields) { var value = ProtocolPublic("production-controller"); value["publicOwnership"]![key] = -1; if (ProtocolAdmitted(value)) return false; }
        var badPath = ProtocolPublic("happy"); badPath["publicOwnership"]!["surface"] = "PUBLIC_NONFUNCTIONAL_RELATIVE"; return !ProtocolAdmitted(badPath);
    }
    private static bool ProtocolManagedOutput()
    {
        var counter = new ManagedOutputCounter(); const string marker = "TUNNER_PUBLIC_NONFUNCTIONAL_PROTOCOL_OUTPUT";
        var journey = BrowserTestRecoveryCodes.JourneyIds[0]; var map = CoordinatorIdsFor(journey).ToDictionary(id => id, _ => true, StringComparer.Ordinal);
        if (!Accepted(counter.Count, true, "NONE", true, map, journey)) return false;
        counter.Write(marker);
        return counter.Count == marker.Length && !Accepted(counter.Count, true, "NONE", true, map, journey) && ProtocolQualifies(ProtocolPublic("unexpected-output"), "unexpected-output") && !ProtocolAdmitted(ProtocolChanged(ProtocolReceipt(journey), "rawOutputRejected", true));
    }
    private static bool ProtocolSourceBoundary(DirectoryInfo root, FiniteTotpBuildIdentity binding)
    {
        var live = ProtocolReceipt(BrowserTestRecoveryCodes.JourneyIds[0]);
        return FiniteTotpBuildBinding.Verify(root, recovery: true) == binding && binding.SourceInputs > 0 && binding.BuildOutputs > 0 &&
            FiniteTotpBuildBinding.SourceSelectors.Contains("tests/Shared", StringComparer.Ordinal) && FiniteTotpBuildBinding.SourceSelectors.Contains("tools/dev/new-tunner-totp-build-binding.mjs", StringComparer.Ordinal) &&
            !ProtocolAdmitted(ProtocolChanged(live, "receiptPath", "PUBLIC_NONFUNCTIONAL")) && !ProtocolAdmitted(ProtocolChanged(live, "sourceCommit", new string('c', 40))) && !ProtocolAdmitted(ProtocolChanged(live, "sourceFingerprint", "PUBLIC_NONFUNCTIONAL"));
    }

    internal static async Task<int> RunAsync(string selection, string mode)
    {
        await using var output = Console.OpenStandardOutput(); var managed = new ManagedOutputCounter(); Console.SetOut(managed); Console.SetError(managed);
        var checks = CoordinatorIds.ToDictionary(x => x, _ => false, StringComparer.Ordinal);
        var cleanup = new Dictionary<string, bool>(StringComparer.Ordinal) { ["app"] = true, ["postgres"] = true, ["mailpit"] = true, ["vaultSession"] = true, ["vaultContainer"] = true, ["dockerConfig"] = true };
        var runId = Guid.NewGuid().ToString("N"); var failure = "INPUT_INVALID"; var source = string.Empty;
        var publicMode = mode == "public-fault"; var resourceMode = mode == "resource-canary";
        var journey = BrowserTestRecoveryCodes.JourneyIds[0];
        JsonElement? browser = null; FiniteTotpBuildIdentity? binding = null; RecoveryCodeBorrowedPostgres? borrowed = null; DirectoryInfo? root = null;
        BrowserTestRecoveryCodes? profile = null; RecoveryCodeOwnedPostgres? postgres = null; RecoveryCodeOwnedMailpit? mailpit = null; RecoveryCodeLocalDockerEndpoint? docker = null;
        RecoveryCodeOwnedOpenBao? vault = null; LocalOpenBaoSession? session = null; WebApplication? app = null; X509Certificate2? certificate = null; string? databaseConnection = null;
        var interrupted = false; var browserExited = false; var raw = false; var browserExit = -1;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(270));
        try
        {
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development" || !new[] { "browser", "public-fault", "resource-canary" }.Contains(mode, StringComparer.Ordinal) ||
                !(publicMode ? PublicFaults.Contains(selection, StringComparer.Ordinal) : resourceMode ? ResourceFaults.Contains(selection, StringComparer.Ordinal) : BrowserTestRecoveryCodes.JourneyIds.Contains(selection, StringComparer.Ordinal))) throw new InvalidOperationException();
            journey = publicMode || resourceMode ? "RECOVERY_GENERATE_TOTP_V1" : selection;
            foreach (var id in CoordinatorIdsFor(journey)) checks.TryAdd(id, false);
            // This dedicated finite process has no outbound proxy authority. Never read the prior proxy value.
            HttpClient.DefaultProxy = new WebProxy();
            root = RepositoryRootResolver.Resolve(); source = FiniteTotpCoordinator.Git(root, "rev-parse", "HEAD");
            failure = "SOURCE_BUILD_BINDING_INVALID"; binding = FiniteTotpBuildBinding.Verify(root, recovery: true); checks["SOURCE_BUILD_CURRENT"] = true;
            var assembly = Path.GetFullPath(Path.Combine(root.FullName, "tools", "browser-testing", "bin", "Debug", "net10.0", "Tunner.BrowserTesting.dll"));
            if (!string.Equals(Path.GetFullPath(typeof(FiniteRecoveryCoordinator).Assembly.Location), assembly, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException();
            string? origin = null, mailpitOrigin = null;
            if (!publicMode)
            {
                failure = "RESOURCE_START_FAILED"; cleanup["dockerConfig"] = false;
                docker = RecoveryCodeLocalDockerEndpoint.Admit(new Uri("npipe:////./pipe/dockerDesktopLinuxEngine"));
                Environment.SetEnvironmentVariable("OTEL_SDK_DISABLED", "true");
                using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root.FullName, "artifacts", "browser-testing", "runtime.json"), deadline.Token).ConfigureAwait(false));
                if (metadata.RootElement.GetProperty("Mode").GetString() != "ISOLATED_LOCAL_BROWSER_TESTING" || metadata.RootElement.GetProperty("PostgresPort").GetInt32() != 25432) throw new InvalidOperationException();
                // Exact metadata-only preservation; the existing json-file logger is never used for private traffic.
                borrowed = await RecoveryCodeOwnedDockerFixture.VerifyBorrowedBrowserPostgresAsync(docker, root, deadline.Token).ConfigureAwait(false);
                if (borrowed.ProjectName != metadata.RootElement.GetProperty("ProjectName").GetString()) throw new InvalidOperationException();
                using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
                {
                    store.Open(OpenFlags.ReadOnly); certificate = store.Certificates.Find(X509FindType.FindByThumbprint, metadata.RootElement.GetProperty("CertificateThumbprint").GetString() ?? throw new InvalidOperationException("FINITE_RECOVERY_CERTIFICATE_METADATA_INVALID"), false)
                        .OfType<X509Certificate2>().Single(x => x.HasPrivateKey && x.NotAfter > DateTime.Now);
                }
                if (certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256) != metadata.RootElement.GetProperty("CertificateSha256").GetString()) throw new InvalidOperationException();
                cleanup["postgres"] = false; postgres = RecoveryCodeOwnedDockerFixture.PrepareNativePostgres(docker, root); await postgres.StartAsync(deadline.Token).ConfigureAwait(false);
                if (!postgres.OwnershipVerified || !postgres.NoLogPersistenceVerified) throw new InvalidOperationException();
                databaseConnection = await postgres.CreateIdentityDatabaseAsync(deadline.Token).ConfigureAwait(false); checks["OWNED_DATABASE"] = true; Interrupt("after-db");
                cleanup["mailpit"] = false; mailpit = RecoveryCodeOwnedDockerFixture.PrepareMailpit(docker, root); await mailpit.StartAsync(deadline.Token).ConfigureAwait(false);
                checks["OWNED_MAILPIT"] = mailpit.OwnershipVerified && mailpit.NoLogPersistenceVerified; mailpitOrigin = mailpit.Origin; Interrupt("after-mailpit");
                ITotpSeedStore? seeds = null;
                if (journey.EndsWith("_TOTP_V1", StringComparison.Ordinal))
                {
                    cleanup["vaultContainer"] = false; vault = RecoveryCodeOwnedDockerFixture.PrepareOpenBao(docker, root); await vault.StartAsync(deadline.Token).ConfigureAwait(false);
                    if (!vault.OwnershipVerified || !vault.NoLogPersistenceVerified || vault.ImageId != RecoveryCodeOwnedDockerFixture.OpenBaoImage) throw new InvalidOperationException();
                    var scope = vault.CredentialScope(); cleanup["vaultSession"] = false; session = await vault.CreateSessionAsync(scope, deadline.Token).ConfigureAwait(false);
                    // Existing native seed store/helper only. Recovery values never enter OpenBao.
                    seeds = new OpenBaoTotpSeedStore(session.Secrets, scope.StoreReference, scope.ResourcePrefix, BrowserTestTotp.BindingVersion); Interrupt("after-vault-session");
                }
                var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); origin = "https://localhost:" + port;
                BrowserTestTotp? totp = seeds is null ? null : new(origin, BrowserTestTotp.JourneyIds[0], databaseConnection, seeds);
                profile = new(origin, journey, databaseConnection, TimeProvider.System);
                var options = new BrowserTestHostOptions(origin, port, databaseConnection, certificate, mailpit.SmtpPort, null, "FINITE_RECOVERY_BROWSER_TESTING", runId, totp, profile);
                cleanup["app"] = false; app = BrowserTestHost.Create(options); await BrowserTestHost.MigrateAndMapAsync(app, options, deadline.Token).ConfigureAwait(false);
                checks["CURRENT_MIGRATIONS"] = checks["EPHEMERAL_DP"] = true; await app.StartAsync(deadline.Token).ConfigureAwait(false); Interrupt("after-app-start"); Interrupt("cleanup-reject");
            }
            failure = "BROWSER_FAILED";
            var control = new { version = 1, type = "runRecoveryPrivate", mode = publicMode ? "public" : "live", journeyId = journey, publicFault = publicMode ? selection : null,
                sourceCommit = source, sourceCommitted = true, origin, mailpitOrigin, runId };
            (browser, browserExited, browserExit, raw) = await RunBrowserAsync(root, control, journey, source, publicMode, profile, deadline.Token).ConfigureAwait(false);
            if (FiniteTotpBuildBinding.Verify(root, recovery: true) != binding) { checks["SOURCE_BUILD_CURRENT"] = false; throw new InvalidOperationException(); }
            if (!publicMode && databaseConnection is not null && postgres is not null) (checks["SAFE_PERSISTENCE"], checks["SAFE_OUTBOX"]) = await InspectPersistenceAsync(databaseConnection, postgres.OwnershipVerified, deadline.Token).ConfigureAwait(false);
            if (!publicMode && databaseConnection is not null && profile is not null)
            {
                if (journey == "RECOVERY_REPLACE_PASSWORD_V1") checks["EXACT_SUBSET_REPLACEMENT"] = await InspectOwnerOutcomeAsync(databaseConnection, profile, completed: true, deadline.Token).ConfigureAwait(false);
                if (journey == "RECOVERY_REPLACE_TOTP_V1") checks["PUBLICATION_DRIFT_ROLLED_BACK"] = profile.PublicationFinalFaultObserved && await InspectOwnerOutcomeAsync(databaseConnection, profile, completed: false, deadline.Token).ConfigureAwait(false);
            }
            failure = "NONE";
        }
        catch (ResourceInterruption) { interrupted = true; failure = "EXPECTED_PUBLIC_RESOURCE_INTERRUPTION"; }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        finally
        {
            if (app is not null)
            {
                var stopped = true; using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try { await app.StopAsync(stop.Token).ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { stopped = false; }
                try { await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { stopped = false; }
                cleanup["app"] = stopped; checks["HOST_STOPPED"] = stopped;
            }
            if (session is not null) { try { await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); cleanup["vaultSession"] = session.CleanupVerified; } catch (Exception error) when (error is not OutOfMemoryException) { } }
            if (vault is not null) { try { await vault.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); cleanup["vaultContainer"] = vault.CleanupVerified && vault.ContainerAbsentVerified; } catch (Exception error) when (error is not OutOfMemoryException) { } }
            if (mailpit is not null) { try { await mailpit.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); cleanup["mailpit"] = mailpit.CleanupVerified && mailpit.ContainerAbsentVerified; } catch (Exception error) when (error is not OutOfMemoryException) { } }
            if (postgres is not null) { try { await postgres.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); cleanup["postgres"] = postgres.CleanupVerified && postgres.ContainerAbsentVerified; } catch (Exception error) when (error is not OutOfMemoryException) { } }
            if (borrowed is not null) { using var verify = new CancellationTokenSource(TimeSpan.FromSeconds(20)); try { checks["BORROWED_POSTGRES_PRESERVED"] = await borrowed.VerifyPreservedAsync(verify.Token).ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { } }
            if (docker is not null) { try { docker.Dispose(); cleanup["dockerConfig"] = docker.CleanupVerified; } catch (Exception error) when (error is not OutOfMemoryException) { } }
            checks["RESOURCES_REMOVED"] = cleanup.Values.All(x => x); certificate?.Dispose();
        }
        if (root is not null && binding is not null)
        {
            try { checks["SOURCE_BUILD_CURRENT"] = FiniteTotpBuildBinding.Verify(root, recovery: true) == binding; }
            catch (Exception error) when (error is not OutOfMemoryException) { checks["SOURCE_BUILD_CURRENT"] = false; }
        }
        var browserAccepted = browser is { } receipt && True(receipt, "accepted") && browserExited && browserExit == 0 && !raw;
        const string publicManagedMarker = "TUNNER_PUBLIC_NONFUNCTIONAL_RECOVERY_MANAGED_009";
        var managedBeforeCanary = managed.Count;
        if (publicMode && selection == "unexpected-output") Console.Write(publicManagedMarker);
        var managedQualified = publicMode && selection == "unexpected-output" ? managedBeforeCanary == 0 && managed.Count == publicManagedMarker.Length : managed.Count == 0;
        var accepted = Accepted(managed.Count, mode == "browser", failure, browserAccepted, checks, journey);
        var publicContained = managedQualified && publicMode && failure == "NONE" && checks["SOURCE_BUILD_CURRENT"] && browser is { } observed && PublicContained(observed, selection) && browserExited && browserExit == 1 && !raw;
        var resourceContained = managed.Count == 0 && resourceMode && checks["SOURCE_BUILD_CURRENT"] && interrupted && !accepted && cleanup.Values.All(x => x) && checks["BORROWED_POSTGRES_PRESERVED"];
        var cleanupRejection = false;
        if (resourceContained && selection == "cleanup-reject") { var synthetic = CoordinatorIdsFor(journey).ToDictionary(x => x, _ => true, StringComparer.Ordinal); var positive = Accepted(0, true, "NONE", true, synthetic, journey); synthetic["HOST_STOPPED"] = false; cleanupRejection = positive && !Accepted(0, true, "NONE", true, synthetic, journey); resourceContained &= cleanupRejection; }
        if (!publicMode && failure == "NONE" && !accepted) failure = "PREDICATE_OR_CLEANUP_FAILED";
        var selected = PublicFaults.Contains(selection, StringComparer.Ordinal) || ResourceFaults.Contains(selection, StringComparer.Ordinal) || BrowserTestRecoveryCodes.JourneyIds.Contains(selection, StringComparer.Ordinal) ? selection : null;
        await JsonSerializer.SerializeAsync(output, new { version = 1, sourceCommit = source.Length == 40 ? source : null, runId, journeyId = journey, mode = new[] { "browser", "public-fault", "resource-canary" }.Contains(mode, StringComparer.Ordinal) ? mode : "INVALID", selection = selected,
            accepted, failureCode = failure, managedOutputRejected = managed.Count != 0, publicBoundaryContained = publicContained, publicResourceContained = resourceContained, publicCleanupReducerRejected = cleanupRejection,
            buildBinding = binding, browserReceipt = browser, coordinatorChecks = checks, cleanup, fixtureObservations = profile is null ? null : new { profile.OwnerFailureObserved, profile.PublicationFinalFaultObserved, profile.BodyDropObserved },
            boundary = "Finite local test only; no production policy, P2-011 workflow, Product or global filesystem acceptance." }).ConfigureAwait(false);
        await output.WriteAsync("\n"u8.ToArray()).ConfigureAwait(false); return accepted || publicContained || resourceContained ? 0 : 1;
        void Interrupt(string stage) { if (resourceMode && selection == stage) throw new ResourceInterruption(); }
    }

    private static async Task<(JsonElement Receipt, bool Exited, int ExitCode, bool Raw)> RunBrowserAsync<T>(DirectoryInfo root, T control, string journey, string source, bool publicMode, BrowserTestRecoveryCodes? profile, CancellationToken token)
    {
        var start = new ProcessStartInfo(FiniteTotpCoordinator.FindNode()) { WorkingDirectory = root.FullName, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(root.FullName, "web", "tests", "recovery", "private", "supervisor.mjs"));
        start.Environment.Clear(); foreach (var name in new[] { "PATH", "SystemRoot", "SystemDrive", "ProgramData", "WINDIR", "COMSPEC", "PATHEXT", "LOCALAPPDATA", "APPDATA", "USERPROFILE", "TEMP", "TMP" }) if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"; start.Environment["NODE_OPTIONS"] = string.Empty;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("FINITE_RECOVERY_NODE_FAILED"); var raw = false; var sequence = 0;
        using var errorsDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(300)); var errors = Drain();
        try
        {
            using (var write = CancellationTokenSource.CreateLinkedTokenSource(token)) { write.CancelAfter(TimeSpan.FromSeconds(4)); await FiniteTotpCoordinator.WriteFrameAsync(process.StandardInput.BaseStream, control, write.Token).ConfigureAwait(false); }
            JsonElement receipt;
            var publicFault = publicMode ? JsonSerializer.SerializeToElement(control).GetProperty("publicFault").GetString() : null;
            for (;;)
            {
                var frame = await FiniteTotpCoordinator.ReadFrameAsync(process.StandardOutput.BaseStream, token).ConfigureAwait(false);
                if (frame.TryGetProperty("type", out var type) && type.GetString() == "recoveryFixtureRequest")
                {
                    var action = AdmitFixtureRequest(frame, ++sequence, publicMode, publicFault);
                    using var request = CancellationTokenSource.CreateLinkedTokenSource(token); request.CancelAfter(TimeSpan.FromSeconds(4));
                    if (publicMode)
                    {
                        var reply = PublicFixtureReply(publicFault!, sequence);
                        if (reply is { } answer) await FiniteTotpCoordinator.WriteFrameAsync(process.StandardInput.BaseStream, answer, request.Token).ConfigureAwait(false);
                        continue;
                    }
                    if (profile is null) throw new InvalidOperationException("FINITE_RECOVERY_RELAY_INVALID");
                    profile.Arm(action); var counts = await profile.CountsAsync(request.Token).ConfigureAwait(false);
                    await FiniteTotpCoordinator.WriteFrameAsync(process.StandardInput.BaseStream, new { version = 1, type = "recoveryFixtureResult", requestId = sequence, status = "OK", counts }, request.Token).ConfigureAwait(false); continue;
                }
                RequireReceipt(frame, journey, source, publicMode); receipt = frame; break;
            }
            process.StandardInput.Close(); await process.WaitForExitAsync(token).ConfigureAwait(false); var trailing = new byte[1]; if (await process.StandardOutput.BaseStream.ReadAsync(trailing, token).ConfigureAwait(false) != 0) raw = true;
            await errors.ConfigureAwait(false); return (receipt, process.HasExited, process.ExitCode, raw);
        }
        finally
        {
            if (!process.HasExited) { try { using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1)); await FiniteTotpCoordinator.WriteFrameAsync(process.StandardInput.BaseStream, new { version = 1, type = "cancelRecoveryPrivate" }, cancel.Token).ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { } }
            if (!process.HasExited) { try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { } }
            if (!process.HasExited) { try { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None).ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { } }
            errorsDeadline.Cancel(); try { await errors.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false); } catch (Exception error) when (error is not OutOfMemoryException) { }
        }
        async Task Drain() { var bytes = new byte[4096]; try { while (await process.StandardError.BaseStream.ReadAsync(bytes, errorsDeadline.Token).ConfigureAwait(false) > 0) raw = true; } catch (OperationCanceledException) { } finally { Array.Clear(bytes); } }
    }
    private static void RequireReceipt(JsonElement value, string journey, string source, bool publicMode)
    {
        FiniteTotpCoordinator.RequireKeys(value, "version", "type", "journeyId", "mode", "publicFault", "sourceCommit", "sourceCommitted", "sourceFingerprint", "browserVersion", "status", "stage", "failureCode", "checks", "supervisor", "childResultAccepted", "protocolRejected", "rawOutputRejected", "timedOut", "cleanupForced", "exitCode", "contextClosed", "browserClosed", "serverExited", "oracleExited", "oracleGraceful", "surfaceClean", "surfaceRemoved", "cleanupCompleted", "publicFaultObserved", "publicOwnership", "accepted");
        if (value.GetProperty("version").GetInt32() != 1 || value.GetProperty("type").GetString() != "recoveryPrivateBrowserReceipt" || value.GetProperty("journeyId").GetString() != journey || value.GetProperty("mode").GetString() != (publicMode ? "public" : "live") || value.GetProperty("sourceCommit").GetString() != source ||
            !Regex.IsMatch(value.GetProperty("sourceFingerprint").GetString() ?? "", "^[a-f0-9]{64}$", RegexOptions.CultureInvariant) || !Stages.Contains(value.GetProperty("stage").GetString(), StringComparer.Ordinal) || !FailureCodes.Contains(value.GetProperty("failureCode").GetString(), StringComparer.Ordinal) ||
            !new[] { "NOT_ACCEPTED", "JOURNEY_OK", "PUBLIC_BOUNDARY_OK", "BOUNDARY_FAILED" }.Contains(value.GetProperty("status").GetString(), StringComparer.Ordinal)) throw new InvalidOperationException("FINITE_RECOVERY_RECEIPT_INVALID");
        foreach (var key in new[] { "sourceCommitted", "childResultAccepted", "protocolRejected", "rawOutputRejected", "timedOut", "cleanupForced", "contextClosed", "browserClosed", "serverExited", "oracleExited", "oracleGraceful", "surfaceClean", "surfaceRemoved", "cleanupCompleted", "publicFaultObserved", "accepted" }) if (value.GetProperty(key).ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidOperationException("FINITE_RECOVERY_RECEIPT_INVALID");
        if (value.GetProperty("browserVersion") is { ValueKind: not JsonValueKind.Null } browserVersion &&
            (browserVersion.ValueKind != JsonValueKind.String || !Regex.IsMatch(browserVersion.GetString() ?? "", "^\\d{1,5}\\.\\d{1,5}\\.\\d{1,5}\\.\\d{1,5}$", RegexOptions.CultureInvariant))) throw new InvalidOperationException();
        if (value.GetProperty("exitCode") is { ValueKind: not JsonValueKind.Null } exit && (!exit.TryGetInt32(out var code) || code > 255)) throw new InvalidOperationException();
        FiniteTotpCoordinator.RequireKeys(value.GetProperty("supervisor"), SupervisorIds); foreach (var flag in value.GetProperty("supervisor").EnumerateObject()) if (flag.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidOperationException();
        var allowed = BrowserIds(journey).Concat(["PUBLIC_ENTRY", "ERROR_CONTAINED", "DOWNLOAD_ADMITTED", "DOWNLOAD_REMOVED", "PRODUCTION_CONTROLLER_PUBLIC", "FIXTURE_RELAY_PUBLIC"]).ToHashSet(StringComparer.Ordinal); var checkRows = value.GetProperty("checks").EnumerateObject().ToArray();
        if (checkRows.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != checkRows.Length || checkRows.Any(x => !allowed.Contains(x.Name) || x.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))) throw new InvalidOperationException();
        if (publicMode)
        {
            if (!PublicFaults.Contains(value.GetProperty("publicFault").GetString(), StringComparer.Ordinal) || True(value, "accepted")) throw new InvalidOperationException();
            var ownership = value.GetProperty("publicOwnership"); FiniteTotpCoordinator.RequireKeys(ownership, "surface", "childPid", "oraclePid", "serverPid");
            if (ownership.GetProperty("surface") is { ValueKind: not JsonValueKind.Null } surface)
            {
                if (surface.ValueKind != JsonValueKind.String) throw new InvalidOperationException(); var path = surface.GetString() ?? string.Empty;
                if (path.Length > 1024 || !Path.IsPathFullyQualified(path) || !Regex.IsMatch(Path.GetFileName(path), "^tunner-p2-009-private-[A-Za-z0-9]{6}$", RegexOptions.CultureInvariant)) throw new InvalidOperationException();
            }
            foreach (var name in new[] { "childPid", "oraclePid", "serverPid" }) if (ownership.GetProperty(name) is { ValueKind: not JsonValueKind.Null } pid && (!pid.TryGetInt32(out var id) || id <= 0)) throw new InvalidOperationException();
            if (value.GetProperty("publicFault").GetString() != "production-controller" && ownership.GetProperty("serverPid").ValueKind != JsonValueKind.Null) throw new InvalidOperationException();
        }
        else if (value.GetProperty("publicFault").ValueKind != JsonValueKind.Null || value.GetProperty("publicOwnership").ValueKind != JsonValueKind.Null) throw new InvalidOperationException();
        if (True(value, "accepted") && (publicMode || value.GetProperty("status").GetString() != "JOURNEY_OK" || value.GetProperty("stage").GetString() != "COMPLETE" || value.GetProperty("failureCode").GetString() != "NONE" || !True(value, "childResultAccepted") || True(value, "protocolRejected") || value.GetProperty("browserVersion").ValueKind == JsonValueKind.Null || value.GetProperty("exitCode").ValueKind != JsonValueKind.Number || !value.GetProperty("exitCode").TryGetInt32(out var acceptedChildExit) || acceptedChildExit != 0 || checkRows.Length != BrowserIds(journey).Length || BrowserIds(journey).Any(id => !True(value.GetProperty("checks"), id)) || checkRows.Any(x => x.Value.ValueKind != JsonValueKind.True) || SupervisorIds.Any(x => !True(value.GetProperty("supervisor"), x)) || !True(value, "sourceCommitted") || True(value, "publicFaultObserved") || True(value, "rawOutputRejected") || True(value, "timedOut") || True(value, "cleanupForced") || ClosedLiveFacts.Any(x => !True(value, x)))) throw new InvalidOperationException();
    }
    private static string[] BrowserIds(string journey) => [.. CommonBrowserIds, .. (journey switch
    {
        "RECOVERY_GENERATE_PASSWORD_V1" => new[] { "ONE_REQUESTED_DOWNLOAD", "CLOSE_NO_REVEAL" }, "RECOVERY_REPLACE_PASSWORD_V1" => ["PUBLISHED_IMPACT_ORIGINAL_BINDING", "BODY_LOSS_NO_REDISCLOSURE"],
        "RECOVERY_GENERATE_PASSKEY_V1" => ["NATIVE_PASSKEY_PROOF", "OWNER_FAILURE_SAME_PROOF"], "RECOVERY_REPLACE_PASSKEY_V1" => ["NATIVE_PASSKEY_PROOF", "SOURCE_FRESHNESS", "NO_SESSION_MUTATION"],
        "RECOVERY_GENERATE_TOTP_V1" => ["NATIVE_TOTP_PROOF", "OWNER_FAILURE_SAME_PROOF", "PAGEHIDE_NO_REVEAL"], "RECOVERY_REPLACE_TOTP_V1" => ["NATIVE_TOTP_PROOF", "CURRENT_PUBLICATION", "NO_SESSION_MUTATION"], _ => []
    })];
    private static bool True(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.True;
    private static string[] CoordinatorIdsFor(string journey) => [.. CoordinatorIds, .. (journey switch { "RECOVERY_REPLACE_PASSWORD_V1" => new[] { "EXACT_SUBSET_REPLACEMENT" }, "RECOVERY_REPLACE_TOTP_V1" => ["PUBLICATION_DRIFT_ROLLED_BACK"], _ => [] })];
    private static bool Accepted(long managedOutput, bool real, string failure, bool browser, Dictionary<string, bool> checks, string journey) => managedOutput == 0 && real && failure == "NONE" && browser && checks.Count == CoordinatorIdsFor(journey).Length && CoordinatorIdsFor(journey).All(id => checks.TryGetValue(id, out var observed) && observed);
    private static bool PublicContained(JsonElement value, string fault)
    {
        if (value.GetProperty("publicFault").GetString() != fault || True(value, "accepted") || !True(value, "sourceCommitted") || True(value, "cleanupForced") || ClosedPublicFacts.Any(x => !True(value, x))) return false;
        if (ClosedPublicSupervisorFacts.Any(id => !True(value.GetProperty("supervisor"), id))) return false;
        if (value.GetProperty("exitCode").ValueKind != JsonValueKind.Number || !value.GetProperty("exitCode").TryGetInt32(out var childExit)) return false;
        if (fault is "relay-malformed" or "relay-timeout") return childExit == 1 && True(value, "protocolRejected") && !True(value, "rawOutputRejected") && value.GetProperty("failureCode").GetString() == "PROTOCOL_FAILED" && True(value, "surfaceClean") && True(value, "cleanupCompleted");
        if (new[] { "invalid-result", "cross-component" }.Contains(fault, StringComparer.Ordinal)) return childExit == 0 && True(value, "protocolRejected") && !True(value, "childResultAccepted") && !True(value, "rawOutputRejected") && True(value, "surfaceClean");
        if (!True(value, "childResultAccepted") || True(value, "protocolRejected")) return false;
        var checks = value.GetProperty("checks"); if (!checks.TryGetProperty("BROWSER_ENGINE", out var engine) || engine.ValueKind != JsonValueKind.True || !checks.TryGetProperty("SURFACE_CHECKED", out var scan) || scan.ValueKind != JsonValueKind.True) return false;
        if (fault == "missing-predicate") return childExit == 0 && !checks.TryGetProperty("PUBLIC_ENTRY", out _) && True(value, "publicFaultObserved") && !True(value, "rawOutputRejected") && True(value, "surfaceClean") && True(value, "cleanupCompleted");
        if (!checks.TryGetProperty("PUBLIC_ENTRY", out var entry) || entry.ValueKind != JsonValueKind.True) return false;
        if (fault == "unexpected-output") return childExit == 0 && True(value, "rawOutputRejected") && True(value, "surfaceClean") && True(value, "cleanupCompleted");
        if (True(value, "rawOutputRejected")) return false;
        if (fault == "unexpected-artifact") return childExit == 1 && !True(value, "surfaceClean") && True(value, "publicFaultObserved") && True(value, "cleanupCompleted");
        if (!True(value, "surfaceClean")) return false;
        if (fault == "cleanup") return childExit == 1 && !True(value, "cleanupCompleted") && value.GetProperty("failureCode").GetString() == "CLEANUP_FAILED";
        if (!True(value, "cleanupCompleted")) return false;
        if (fault == "child-timeout") return childExit == 1 && True(value, "timedOut") && value.GetProperty("failureCode").GetString() == "SUPERVISOR_TIMEOUT" && True(value, "publicFaultObserved");
        if (True(value, "timedOut")) return false;
        if (fault.StartsWith("oracle-", StringComparison.Ordinal)) return childExit == 0 && True(value, "publicFaultObserved") && (fault != "oracle-roundtrip" || True(value, "oracleGraceful"));
        if (fault == "happy") return childExit == 0 && value.GetProperty("status").GetString() == "PUBLIC_BOUNDARY_OK" && value.GetProperty("failureCode").GetString() == "NONE" && True(checks, "DOWNLOAD_ADMITTED") && True(checks, "DOWNLOAD_REMOVED");
        if (fault == "journey-entry") return childExit == 0 && True(checks, "ERROR_CONTAINED") && True(value, "publicFaultObserved");
        if (fault == "production-controller") return childExit == 0 && True(value, "publicFaultObserved") && value.GetProperty("publicOwnership").GetProperty("serverPid").ValueKind == JsonValueKind.Number && True(checks, "PRODUCTION_CONTROLLER_PUBLIC") && True(checks, "DOWNLOAD_ADMITTED") && True(checks, "DOWNLOAD_REMOVED");
        if (fault == "relay-roundtrip") return childExit == 0 && value.GetProperty("status").GetString() == "PUBLIC_BOUNDARY_OK" && value.GetProperty("failureCode").GetString() == "NONE" && True(value, "publicFaultObserved") && True(checks, "FIXTURE_RELAY_PUBLIC");
        var expected = fault switch { "unarmed" or "duplicate" or "wrong-page" => "ADMISSION_REJECTED", "wrong-bytes" => "BYTES_MISMATCH", "oversize" => "SIZE_LIMIT", "outside-identity" => "PATH_REJECTED", "stream-error" => "STREAM_FAILED", "delete-error" => "DELETE_FAILED", "action-timeout" => "ACTION_FAILED", "fatal-rejection" => "SUPERVISOR_TIMEOUT", _ => "INVALID" };
        return childExit == 1 && value.GetProperty("status").GetString() == "BOUNDARY_FAILED" && value.GetProperty("failureCode").GetString() == expected && True(value, "publicFaultObserved");
    }
    private static async Task<bool> InspectOwnerOutcomeAsync(string connectionString, BrowserTestRecoveryCodes profile, bool completed, CancellationToken token)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(token).ConfigureAwait(false);
        Guid operation, issued; string account, facts, context; long accountVersion; RecoveryCodeRetirementTarget[]? plan;
        await using (var command = new NpgsqlCommand("SELECT operation_id,set_reference,account_id,expected_version,impact_facts_digest,context_digest,retirement_plan_json FROM identity.recovery_code_operations WHERE kind='REPLACE' AND state=@state", connection))
        {
            command.Parameters.AddWithValue("state", completed ? "COMPLETED" : "PENDING_PROOF");
            await using var rows = await command.ExecuteReaderAsync(token).ConfigureAwait(false); if (!await rows.ReadAsync(token).ConfigureAwait(false)) return false;
            operation = rows.GetGuid(0); issued = rows.GetGuid(1); account = rows.GetString(2); accountVersion = rows.GetInt64(3); facts = rows.GetString(4); context = rows.GetString(5);
            var json = rows.GetString(6); if (json.Length > 4096) return false; plan = JsonSerializer.Deserialize<RecoveryCodeRetirementTarget[]>(json);
            if (await rows.ReadAsync(token).ConfigureAwait(false)) return false;
        }
        var baseline = profile.Baseline(account, accountVersion, "REPLACE", facts);
        if (baseline is null || plan is not { Length: 1 } || baseline.Targets.Count != 1 || baseline.Targets[0] != plan[0] || baseline.Sets.Count != (completed ? 3 : 1)) return false;
        var target = plan[0]; if (!baseline.Sets.Any(x => x.SetReference == target.SetReference && x.LifecycleVersion == target.LifecycleVersion)) return false;
        var actualReferences = new HashSet<Guid>();
        await using (var command = new NpgsqlCommand("SELECT set_reference,state,lifecycle_version,usage_version,retired_by_operation_id,issuance_operation_id FROM identity.recovery_code_sets WHERE account_id=@account AND (state='ACTIVE' OR set_reference=ANY(@baseline) OR set_reference=@issued)", connection))
        {
            command.Parameters.AddWithValue("account", account); command.Parameters.AddWithValue("baseline", baseline.Sets.Select(x => x.SetReference).ToArray()); command.Parameters.AddWithValue("issued", issued);
            await using var rows = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await rows.ReadAsync(token).ConfigureAwait(false))
            {
                var reference = rows.GetGuid(0); if (!actualReferences.Add(reference)) return false;
                if (reference == issued)
                {
                    if (!completed || rows.GetString(1) != "ACTIVE" || rows.GetInt64(2) != 1 || rows.GetInt64(3) != 0 || !rows.IsDBNull(4) || rows.GetGuid(5) != operation) return false;
                    continue;
                }
                var original = baseline.Sets.SingleOrDefault(x => x.SetReference == reference); if (original is null || rows.GetInt64(3) != original.UsageVersion) return false;
                var retired = completed && reference == target.SetReference;
                if (retired ? rows.GetString(1) != "REPLACED" || rows.GetInt64(2) != original.LifecycleVersion + 1 || rows.IsDBNull(4) || rows.GetGuid(4) != operation
                    : rows.GetString(1) != "ACTIVE" || rows.GetInt64(2) != original.LifecycleVersion || !rows.IsDBNull(4)) return false;
            }
        }
        if (actualReferences.Count != baseline.Sets.Count + (completed ? 1 : 0) || baseline.Sets.Any(x => !actualReferences.Contains(x.SetReference)) || actualReferences.Contains(issued) != completed) return false;
        foreach (var original in baseline.Sets)
        {
            await using var remaining = new NpgsqlCommand("SELECT count(*) FROM identity.recovery_code_entries WHERE account_id=@account AND set_reference=@set AND consumed_at IS NULL", connection);
            remaining.Parameters.AddWithValue("account", account); remaining.Parameters.AddWithValue("set", original.SetReference);
            if ((long)(await remaining.ExecuteScalarAsync(token).ConfigureAwait(false))! != original.RemainingCodeCount) return false;
        }
        if (completed)
        {
            await using var entries = new NpgsqlCommand("SELECT count(*)=@count AND bool_and(consumed_at IS NULL AND version=1 AND crypto_profile_version=@profile) FROM identity.recovery_code_entries WHERE account_id=@account AND set_reference=@set", connection);
            entries.Parameters.AddWithValue("account", account); entries.Parameters.AddWithValue("set", issued); entries.Parameters.AddWithValue("count", profile.Crypto.CodesPerSet); entries.Parameters.AddWithValue("profile", profile.Crypto.Version);
            return await entries.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
        }
        await using var rollback = new NpgsqlCommand("SELECT (SELECT count(*) FROM identity.step_up_proofs WHERE account_id=@account AND operation_id=@operation AND context_digest=@context AND state='ACTIVE')=1 AND NOT EXISTS(SELECT 1 FROM platform.outbox_messages WHERE operation_id=@operation AND event_type IN ('RecoveryCodeSetGenerated','RecoveryCodeSetReplaced'))", connection);
        rollback.Parameters.AddWithValue("account", account); rollback.Parameters.AddWithValue("operation", "OP-RECOVERY-CODE-" + operation.ToString("N")); rollback.Parameters.AddWithValue("context", context);
        return await rollback.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
    }
    private static async Task<(bool Safe, bool Audit)> InspectPersistenceAsync(string connectionString, bool databaseOwned, CancellationToken token)
    {
        if (!databaseOwned) return (false, false); await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(token).ConfigureAwait(false);
        await using var shape = new NpgsqlCommand("SELECT NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='identity' AND table_name LIKE 'recovery_code_%' AND column_name IN ('code','codes','plaintext','protected_code','secret')) AND NOT EXISTS(SELECT 1 FROM identity.\"AspNetUserTokens\") AND NOT EXISTS(SELECT 1 FROM identity.recovery_code_entries WHERE length(verifier)<>84 OR crypto_profile_version<>'SYNTHETIC_FINITE_RECOVERY_009_CRYPTO_V1')", connection);
        var safe = await shape.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
        await using var audit = new NpgsqlCommand("""
            SELECT NOT EXISTS(SELECT 1 FROM platform.outbox_messages m WHERE source='tunner/identity' AND CASE
              WHEN jsonb_typeof(payload)<>'object' THEN true
              WHEN event_type LIKE 'RecoveryCode%' THEN NOT (
                event_type IN ('RecoveryCodeOperationStarted','RecoveryCodeSetGenerated','RecoveryCodeSetReplaced','RecoveryCodeOperationCancelled') AND
                (SELECT count(*) FROM jsonb_object_keys(payload))=3 AND EXISTS(
                  SELECT 1 FROM identity.recovery_code_operations o WHERE m.operation_id='OP-RECOVERY-CODE-'||replace(o.operation_id::text,'-','') AND
                    m.correlation_id=replace(o.operation_id::text,'-','') AND m.subject=replace(o.set_reference::text,'-','') AND
                    payload->>'reference'=replace(o.operation_id::text,'-','') AND payload->>'setReference'=o.set_reference::text AND
                    payload->>'operation'=CASE o.kind WHEN 'GENERATE' THEN 'GENERATE_RECOVERY_CODES' ELSE 'REPLACE_RECOVERY_CODES' END))
              ELSE EXISTS(SELECT 1 FROM jsonb_each(m.payload) p WHERE NOT CASE lower(p.key)
                WHEN 'reference' THEN jsonb_typeof(p.value)='string' AND (
                  ((p.value #>> '{}') IN (m.subject,m.operation_id) AND (p.value #>> '{}') ~ '^(ACC-|OP-|OP-TOTP-|OP-PASSKEY-|OP-RECOVERY-CODE-|OP-LOCAL-STEP-UP-)?[A-Fa-f0-9-]{32,36}$') OR
                  (m.event_type='AccountAgeEvaluated' AND (p.value #>> '{}')=m.subject AND (p.value #>> '{}') ~ '^account-age:[a-f0-9]{32}$') OR
                  EXISTS(SELECT 1 FROM identity.totp_operations t WHERE (p.value #>> '{}')=t.enrollment_id::text AND m.operation_id='OP-TOTP-'||replace(t.enrollment_id::text,'-','')))
                WHEN 'operationreference' THEN jsonb_typeof(p.value)='string' AND (p.value #>> '{}') ~ '^[A-Fa-f0-9-]{32,36}$'
                WHEN 'authenticatorreference' THEN p.value='null'::jsonb OR (jsonb_typeof(p.value)='string' AND (p.value #>> '{}') ~ '^[A-Fa-f0-9-]{32,36}$')
                WHEN 'schemaversion' THEN p.value='"1"'::jsonb
                WHEN 'authenticatortype' THEN p.value IN ('"TOTP"'::jsonb,'"PASSKEY"'::jsonb)
                WHEN 'operation' THEN jsonb_typeof(p.value)='string' AND (p.value #>> '{}') IN
                  ('ENROLL_TOTP','DISABLE_TOTP','RESET_TOTP','REGISTER_PASSKEY','REVOKE_PASSKEY','AUTHENTICATE_PASSKEY','LOCAL_TEST_VERIFY_IDENTITY','GENERATE_RECOVERY_CODES','REPLACE_RECOVERY_CODES')
                WHEN 'eventtype' THEN jsonb_typeof(p.value)='string' AND (p.value #>> '{}')=m.event_type
                ELSE false END) END)
            """, connection);
        return (safe, await audit.ExecuteScalarAsync(token).ConfigureAwait(false) is true);
    }
    private sealed class ResourceInterruption : Exception { }
    private sealed class ManagedOutputCounter : TextWriter
    {
        private long count;
        public override Encoding Encoding => Encoding.UTF8;
        internal long Count => Interlocked.Read(ref count);
        public override void Write(char value) => Interlocked.Increment(ref count);
        public override void Write(string? value) { if (value is not null) Interlocked.Add(ref count, value.Length); }
        public override void Write(char[] buffer, int index, int length) => Interlocked.Add(ref count, length);
        public override void Write(ReadOnlySpan<char> buffer) => Interlocked.Add(ref count, buffer.Length);
    }
}
