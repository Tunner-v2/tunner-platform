using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tunner.Governance;

/// <summary>Committed validation evidence binds governed ownership to exact Git objects.</summary>
internal static class CommittedEvidenceApplication
{
    internal static bool IsSha(string? value) => value is not null && Regex.IsMatch(value, "^[a-f0-9]{40}$", RegexOptions.CultureInvariant);

    internal static string Git(DirectoryInfo repository, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repository.FullName, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        if (process is null) return "";
        var output = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output.Trim() : "";
    }

    internal static string? ClosureSha(DirectoryInfo repository, GovernanceRecord record)
    {
        foreach (var reference in record.StringList(record.Type.DefinitionName == "revision" ? "evidence_refs" : "required_evidence"))
        {
            var path = Path.GetFullPath(Path.Combine(repository.FullName, reference));
            if (!path.StartsWith(repository.FullName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || Path.GetExtension(path) != ".json") continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("scope_id", out var scope) && scope.GetString() == record.Identifier &&
                    document.RootElement.TryGetProperty("closure_commit_sha", out var sha) && IsSha(sha.GetString()) && Git(repository, "merge-base", sha.GetString()!, "HEAD") == sha.GetString()) return sha.GetString();
            }
            catch (JsonException) { }
        }
        return null;
    }

    internal static IEnumerable<string> Check(DirectoryInfo repository, GovernanceRecord record, GitStateDefinition state)
    {
        if (state.ValidatedCommitSha is not { } sha || !IsSha(sha)) yield break;
        if (Git(repository, "merge-base", sha, "HEAD") != sha) yield return "validated SHA must belong to current Git ancestry";
        foreach (var commit in state.WorkItemCommitShas)
            if (!IsSha(commit) || Git(repository, "merge-base", commit, "HEAD") != commit) yield return "work-item commit lineage must resolve in current ancestry";
        if (state.ClosureCommitSha is { } closure && (!IsSha(closure) || Git(repository, "merge-base", closure, "HEAD") != closure))
            yield return "closure SHA must resolve in current ancestry";
        var matched = false;
        RevisionCommittedProofResolver? revisionProof = null;
        foreach (var reference in record.StringList(record.Type.DefinitionName == "revision" ? "evidence_refs" : "required_evidence"))
        {
            var path = Path.GetFullPath(Path.Combine(repository.FullName, reference));
            if (!path.StartsWith(repository.FullName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || Path.GetExtension(path) != ".json") continue;
            JsonDocument? document;
            try { document = JsonDocument.Parse(File.ReadAllText(path)); }
            catch (JsonException) { continue; }
            using (document)
            {
                var root = document.RootElement;
                if (!root.TryGetProperty("scope_id", out var scope) || scope.GetString() != record.Identifier || !root.TryGetProperty("validation_kind", out var kind)) continue;
                matched = true;
                if (kind.GetString() is not ("COMMITTED" or "RETROSPECTIVE_COMMITTED")) yield return "validation evidence must distinguish committed retrospective remediation from historical validation";
                if (!root.TryGetProperty("validated_commit_sha", out var validated) || validated.GetString() != sha ||
                    !root.TryGetProperty("tested_commit_sha", out var tested) || tested.GetString() != state.TestedCommitSha)
                    yield return "evidence tested/validated SHA disagrees with governed committed-state lineage";
                if (!root.TryGetProperty("result", out var result) || result.GetString() != "PASS") yield return "committed validation evidence must pass";
                if (!root.TryGetProperty("owned_behavior_paths", out var paths) || paths.ValueKind != JsonValueKind.Array) { yield return "committed evidence must classify owned behavior paths"; continue; }
                if (paths.GetArrayLength() == 0) yield return "repository-changing evidence must own at least one behavior path";
                var committedEnvelope = Git(repository, "show", sha + ":" + reference);
                JsonDocument? candidate;
                try { candidate = JsonDocument.Parse(committedEnvelope); }
                catch (JsonException) { candidate = null; }
                using (candidate)
                {
                    if (candidate is null || !candidate.RootElement.TryGetProperty("owned_behavior_paths", out var committedPaths) ||
                        committedPaths.ValueKind != JsonValueKind.Array || !committedPaths.EnumerateArray().Select(x => x.ToString()).SequenceEqual(paths.EnumerateArray().Select(x => x.ToString()), StringComparer.Ordinal))
                        yield return "owned behavior classification must match the validation candidate evidence";
                }
                foreach (var item in paths.EnumerateArray())
                {
                    var logical = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                    if (logical is null || logical.Contains("..", StringComparison.Ordinal) || logical.Contains('\\') || Path.IsPathRooted(logical)) { yield return "owned behavior paths must be repository-relative"; continue; }
                    revisionProof ??= new RevisionCommittedProofResolver(repository, record);
                    var effectiveSha = revisionProof.Resolve(logical, out var proofFailure);
                    if (proofFailure is not null) { yield return proofFailure; continue; }
                    var expected = Git(repository, "rev-parse", (effectiveSha ?? sha) + ":" + logical);
                    var current = Git(repository, "rev-parse", "HEAD:" + logical);
                    var working = Git(repository, "hash-object", "--", logical);
                    if (expected.Length == 0 || expected != current || expected != working)
                        yield return $"owned behavior changed after validated SHA: {logical}";
                }
            }
        }
        if (!matched) yield return "repository-changing readiness requires a scope-bound committed validation evidence envelope";
    }
}
