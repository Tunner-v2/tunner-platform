using System.Security.Cryptography;
using System.Text.Json;
using YamlDotNet.RepresentationModel;
using Json.Schema;

namespace Tunner.Governance;

/// <summary>AMD-0008/0009 per-item evidence gate; a registration PASS cannot cover another route or form.</summary>
public static class UiComplianceApplication
{
    public const string InventoryPath = "docs/contracts/ui-inventory.json";
    public static readonly IReadOnlyList<string> UrlChecks = Enumerable.Range(1, 6).Select(i => $"URL-{i:D3}").ToArray();
    public static readonly IReadOnlyList<string> ValidationChecks = Enumerable.Range(1, 8).Select(i => $"VAL-{i:D3}").ToArray();
    public static readonly IReadOnlyList<string> FidelityChecks = Enumerable.Range(1, 8).Select(i => $"UX-FID-{i:D3}").ToArray();

    public static CommandResult Check(DirectoryInfo repository, string scopeId = "GLOBAL_UI", string evidenceRef = "governance/evidence/GLOBAL-UI-COMPLIANCE.json")
    {
        var failures = new List<string>();
        var covered = new List<string>();
        try
        {
            using var inventory = JsonDocument.Parse(File.ReadAllText(Resolve(repository, InventoryPath)));
            using var evidence = JsonDocument.Parse(File.ReadAllText(Resolve(repository, evidenceRef)));
            var proof = evidence.RootElement;
            var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(repository.FullName, "governance/schemas/v1/ui-inventory.schema.json")));
            if (!schema.Evaluate(inventory.RootElement).IsValid) failures.Add("UI inventory fails its typed schema");
            var evidenceBlob = CommittedEvidenceApplication.Git(repository, "rev-parse", "HEAD:" + evidenceRef);
            if (evidenceBlob.Length == 0 || evidenceBlob != CommittedEvidenceApplication.Git(repository, "hash-object", "--", evidenceRef))
                failures.Add("UI acceptance evidence must be committed and unchanged");
            if (proof.GetProperty("scope_id").GetString() != scopeId || proof.GetProperty("result").GetString() != "PASS")
                failures.Add("UI evidence must bind the requested scope and result PASS");
            if (proof.GetProperty("inventory_sha256").GetString() != Hash(Resolve(repository, InventoryPath)))
                failures.Add("UI inventory changed after evidence was collected");
            var sha = proof.GetProperty("tested_commit_sha").GetString();
            if (!CommittedEvidenceApplication.IsSha(sha) || CommittedEvidenceApplication.Git(repository, "merge-base", sha!, "HEAD") != sha)
                failures.Add("UI evidence requires a tested committed ancestor SHA");
            var items = inventory.RootElement.GetProperty("items").EnumerateArray().ToArray();
            if (items.Length == 0 || items.Select(i => i.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count() != items.Length)
                failures.Add("UI inventory must have unique nonempty item identities");
            var live = items.Where(i => i.GetProperty("status").GetString() is "IMPLEMENTED" or "TEST_FIXTURE").ToArray();
            CheckSourceBinding(repository, inventory.RootElement, proof, live, scopeId, sha, failures);
            foreach (var item in items)
                if (string.IsNullOrWhiteSpace(item.GetProperty("id").GetString()) || item.GetProperty("kind").GetString() is not ("SCREEN" or "FORM" or "COMPONENT") ||
                    item.GetProperty("status").GetString() is not ("IMPLEMENTED" or "TEST_FIXTURE" or "PLANNED")) failures.Add("UI inventory item has invalid identity/kind/status");
            var duplicatePaths = live.Where(i => i.GetProperty("kind").GetString() == "SCREEN")
                .GroupBy(i => i.GetProperty("app").GetString() + ":" + i.GetProperty("pathname").GetString(), StringComparer.Ordinal)
                .Where(g => g.Count() > 1);
            if (duplicatePaths.Any()) failures.Add("UI_ROUTING_DEFECT: duplicate canonical screen path within an application");
            var rows = proof.GetProperty("items").EnumerateArray().ToArray();
            if (rows.Select(i => i.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count() != rows.Length)
                failures.Add("UI evidence contains duplicate item identities");
            foreach (var item in live.Where(i => scopeId == "GLOBAL_UI" || i.GetProperty("owner_scopes").EnumerateArray().Any(o => o.GetString() == scopeId)))
            {
                var id = item.GetProperty("id").GetString() ?? "";
                var kind = item.GetProperty("kind").GetString();
                if (id.Length == 0 || !item.GetProperty("authority_refs").EnumerateArray().Any() || !item.GetProperty("figma_refs").EnumerateArray().Any())
                    failures.Add("UI_REQUIRED: missing screen/form authority or approved design: " + id);
                if (kind == "SCREEN")
                {
                    var pathname = item.GetProperty("pathname").GetString() ?? "";
                    if (!pathname.StartsWith('/') || pathname.StartsWith("//", StringComparison.Ordinal) || pathname.IndexOfAny(['?', '#', '\\']) >= 0 ||
                        pathname.Split('/').Any(p => p is "." or "..") || item.GetProperty("runtime_pathname").GetString() != pathname ||
                        string.IsNullOrWhiteSpace(item.GetProperty("guard").GetString()))
                        failures.Add("UI_ROUTING_DEFECT: invalid/stale pathname or missing direct-entry guard: " + id);
                }
                var matches = rows.Where(r => r.GetProperty("id").GetString() == id).ToArray();
                if (matches.Length != 1) { failures.Add("UI evidence missing for applicable item: " + id); continue; }
                var row = matches[0];
                var requirements = kind == "SCREEN" ? UrlChecks.Concat(FidelityChecks) : ValidationChecks.Concat(FidelityChecks);
                var checks = row.GetProperty("checks").EnumerateArray().ToArray();
                foreach (var required in requirements)
                {
                    var results = checks.Where(c => c.GetProperty("id").GetString() == required).ToArray();
                    if (results.Length != 1) { failures.Add(id + ": missing or duplicate " + required); continue; }
                    var check = results[0];
                    var result = check.GetProperty("result").GetString();
                    if (result == "NOT_APPLICABLE")
                    {
                        if (string.IsNullOrWhiteSpace(check.GetProperty("reason").GetString()) ||
                            !check.GetProperty("authority_refs").EnumerateArray().Any()) failures.Add(id + ": unsupported exclusion " + required);
                    }
                    else if (result != "PASS") failures.Add(id + ": " + required + " is not PASS");
                    else
                    {
                        var refs = check.GetProperty("evidence_refs").EnumerateArray().ToArray();
                        if (refs.Length == 0) failures.Add(id + ": evidence missing for " + required);
                        foreach (var reference in refs) CheckReference(repository, reference, failures);
                    }
                }
                var screenshots = row.GetProperty("screenshots").EnumerateArray().ToArray();
                if (screenshots.Length == 0) failures.Add("UI_FIDELITY_DEFECT: rendered/reference screenshots missing: " + id);
                foreach (var screenshot in screenshots) CheckReference(repository, screenshot, failures);
                foreach (var deviation in row.GetProperty("deviations").EnumerateArray())
                {
                    if (deviation.GetProperty("status").GetString() != "PRODUCT_OWNER_APPROVED")
                        failures.Add("UI_REQUIRED: material visual deviation is not Product Owner approved: " + id);
                    CheckReference(repository, deviation.GetProperty("approval_evidence"), failures);
                }
                covered.Add(id);
            }
            if (covered.Count == 0) failures.Add("UI scope has no applicable inventory items; do not substitute another form's PASS");
        }
        catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            failures.Add("UI compliance inventory/evidence is missing or malformed: " + error.GetType().Name);
        }
        return new CommandResult(failures.Count == 0 ? 0 : 1, "ui check", new UiCompliancePayload(failures.Count == 0 ? "PASS" : "BLOCKED", scopeId, covered, failures));
    }

    internal static void AddGateFailures(DirectoryInfo repository, GovernanceRecord record, List<string> failures, List<string> satisfied)
    {
        // Historical completion is retained. New/final UI scopes and the global release command require current per-item proof.
        if (record.Scalar("status") is "DONE" or "CLOSED") return;
        var uiImpact = record.Fields.TryGetValue("impact", out var impact) && impact is YamlMappingNode map &&
            GovernanceApplication.Scalar(GovernanceApplication.Fields(map), "ui") == "true";
        var reference = record.Scalar("ui_evidence_ref");
        if (!uiImpact && reference is null) return;
        var authority = Path.Combine(repository.FullName, "docs/authority/current-authority.json");
        using var current = JsonDocument.Parse(File.ReadAllText(authority));
        if (!current.RootElement.GetProperty("effective_authority").GetString()!.Contains("AMD-0009", StringComparison.Ordinal)) return;
        if (reference is null) { failures.Add("AMD-0009 UI scope requires ui_evidence_ref and per-route/form acceptance"); return; }
        var result = (UiCompliancePayload)Check(repository, record.Identifier, reference).Payload;
        failures.AddRange(result.Failures);
        if (result.Failures.Count == 0) satisfied.Add("AMD-0008/0009 per-route/form UI compliance passed");
    }

    private static void CheckSourceBinding(DirectoryInfo repository, JsonElement inventory, JsonElement proof,
        JsonElement[] live, string scopeId, string? sha, List<string> failures)
    {
        var owned = proof.GetProperty("owned_behavior_paths").EnumerateArray().Select(p => p.GetString() ?? "").ToArray();
        if (owned.Length == 0 || owned.Any(p => !SafeSource(p))) { failures.Add("UI evidence requires safe nonempty source ownership"); return; }
        var required = new HashSet<string>(StringComparer.Ordinal) { InventoryPath };
        foreach (var item in live.Where(i => scopeId == "GLOBAL_UI" || i.GetProperty("owner_scopes").EnumerateArray().Any(o => o.GetString() == scopeId)))
            foreach (var source in item.GetProperty("source_paths").EnumerateArray())
            {
                var logical = source.GetString() ?? "";
                if (!SafeSource(logical)) { failures.Add("UI source path is invalid"); continue; }
                var full = Path.Combine(repository.FullName, logical);
                if (Directory.Exists(full))
                    foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Where(p =>
                        !Path.GetRelativePath(full, p).Split(Path.DirectorySeparatorChar).Any(part => part is "node_modules" or "dist" or "test-results" or "obj" or "bin")))
                        required.Add(RepositoryRootResolver.ToLogicalPath(repository, file));
                else if (File.Exists(full)) required.Add(logical);
                else failures.Add("UI registered source is missing: " + logical);
            }
        if (required.Except(owned, StringComparer.Ordinal).Any()) failures.Add("UI source ownership omits an applicable registered file");
        if (scopeId == "GLOBAL_UI" && Directory.Exists(Path.Combine(repository.FullName, "web/apps")))
            foreach (var app in Directory.GetDirectories(Path.Combine(repository.FullName, "web/apps")))
                if (!inventory.GetProperty("apps").EnumerateArray().Any(a => a.TryGetProperty("source_root", out var root) &&
                    root.GetString() == RepositoryRootResolver.ToLogicalPath(repository, app))) failures.Add("UI application is absent from inventory");
        if (!CommittedEvidenceApplication.IsSha(sha)) return;
        var atSha = CommittedEvidenceApplication.Git(repository, ["ls-tree", "-r", "--name-only", sha!, "--", .. owned])
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (owned.Any(p => !atSha.Contains(p))) failures.Add("UI owned source is absent from tested commit");
        if (CommittedEvidenceApplication.Git(repository, ["diff", "--name-only", sha!, "--", .. owned]).Length != 0)
            failures.Add("UI behavior changed after the tested commit; recommit and revalidate");
    }
    private static bool SafeSource(string path) => !Path.IsPathRooted(path) && !path.Contains('\\') && !path.Contains(':') &&
        !path.Split('/').Any(p => p is "" or "." or "..") && (path.StartsWith("web/", StringComparison.Ordinal) ||
        path.StartsWith("src/", StringComparison.Ordinal) || path.StartsWith("docs/contracts/", StringComparison.Ordinal));

    private static void CheckReference(DirectoryInfo repository, JsonElement reference, List<string> failures)
    {
        var path = reference.GetProperty("path").GetString() ?? "";
        var expected = reference.GetProperty("sha256").GetString();
        if (expected is null || expected.Length != 64 || Hash(Resolve(repository, path)) != expected)
            failures.Add("UI evidence reference missing or hash mismatch: " + path);
    }
    private static string Resolve(DirectoryInfo repository, string path)
    {
        if (Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') || path.Split('/').Any(p => p is "" or "." or "..") ||
            !(path.StartsWith("docs/", StringComparison.Ordinal) || path.StartsWith("governance/evidence/", StringComparison.Ordinal) || path.StartsWith("artifacts/", StringComparison.Ordinal)))
            throw new ArgumentException("UI evidence paths must remain in governed repository evidence/contract locations");
        return Path.Combine(repository.FullName, path.Replace('/', Path.DirectorySeparatorChar));
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
public sealed record UiCompliancePayload(string Outcome, string ScopeId, IReadOnlyList<string> CoveredItems, IReadOnlyList<string> Failures);
