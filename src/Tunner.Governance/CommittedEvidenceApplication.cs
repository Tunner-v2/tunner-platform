using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

[assembly: InternalsVisibleTo("Tunner.Governance.FunctionalTests")]

namespace Tunner.Governance;

/// <summary>Committed validation evidence binds governed ownership to exact Git objects.</summary>
internal static class CommittedEvidenceApplication
{
    internal static bool IsSha(string? value) => value is not null && Regex.IsMatch(value, "^[a-f0-9]{40}$", RegexOptions.CultureInvariant);

    internal static string Git(DirectoryInfo repository, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repository.FullName, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        if (process is null) return "";
        var output = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output.Trim() : "";
    }

    internal static string Git(DirectoryInfo repository, CommittedProofAssessment assessment, params string[] arguments)
        => assessment.ReadImmutableGit(repository, arguments, () => Git(repository, arguments));

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
        => Check(repository, record, state, new CommittedProofAssessment(repository));

    internal static IEnumerable<string> Check(DirectoryInfo repository, GovernanceRecord record, GitStateDefinition state, CommittedProofAssessment assessment)
    {
        if (state.ValidatedCommitSha is not { } sha || !IsSha(sha)) yield break;
        if (Git(repository, assessment, "merge-base", sha, "HEAD") != sha) yield return "validated SHA must belong to current Git ancestry";
        foreach (var commit in state.WorkItemCommitShas)
            if (!IsSha(commit) || Git(repository, assessment, "merge-base", commit, "HEAD") != commit) yield return "work-item commit lineage must resolve in current ancestry";
        if (state.ClosureCommitSha is { } closure && (!IsSha(closure) || Git(repository, assessment, "merge-base", closure, "HEAD") != closure))
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
                var committedEnvelope = Git(repository, assessment, "show", sha + ":" + reference);
                JsonDocument? candidate;
                try { candidate = JsonDocument.Parse(committedEnvelope); }
                catch (JsonException) { candidate = null; }
                using (candidate)
                {
                    if (candidate is null || !candidate.RootElement.TryGetProperty("scope_id", out var candidateScope) ||
                        candidateScope.ValueKind != JsonValueKind.String || candidateScope.GetString() != record.Identifier)
                        yield return "validation candidate evidence must bind the governed scope";
                    if (candidate is null || !candidate.RootElement.TryGetProperty("validation_kind", out var candidateKind) ||
                        candidateKind.ValueKind != JsonValueKind.String || candidateKind.GetString() is not ("COMMITTED" or "RETROSPECTIVE_COMMITTED"))
                        yield return "validation candidate evidence must declare COMMITTED or RETROSPECTIVE_COMMITTED";
                    if (candidate is null || !candidate.RootElement.TryGetProperty("owned_behavior_paths", out var committedPaths) ||
                        committedPaths.ValueKind != JsonValueKind.Array || !committedPaths.EnumerateArray().Select(x => x.ToString()).SequenceEqual(paths.EnumerateArray().Select(x => x.ToString()), StringComparer.Ordinal))
                        yield return "owned behavior classification must match the validation candidate evidence";
                }
                foreach (var item in paths.EnumerateArray())
                {
                    var logical = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                    if (logical is null || logical.Contains("..", StringComparison.Ordinal) || logical.Contains('\\') || Path.IsPathRooted(logical)) { yield return "owned behavior paths must be repository-relative"; continue; }
                    revisionProof ??= new RevisionCommittedProofResolver(repository, record, assessment);
                    var effectiveSha = revisionProof.Resolve(logical, out var proofFailure);
                    if (proofFailure is not null) { yield return proofFailure; continue; }
                    var expected = Git(repository, assessment, "rev-parse", (effectiveSha ?? sha) + ":" + logical);
                    var current = Git(repository, assessment, "rev-parse", "HEAD:" + logical);
                    var working = Git(repository, assessment, "hash-object", "--", logical);
                    if (expected.Length == 0 || expected != current || expected != working)
                        yield return $"owned behavior changed after validated SHA: {logical}";
                }
            }
        }
        if (!matched) yield return "repository-changing readiness requires a scope-bound committed validation evidence envelope";
    }
}


// One assessment only. Memoize immutable object reads, never live closure or acceptance results.
internal sealed class CommittedProofAssessment(DirectoryInfo repository)
{
    private readonly string root = Path.GetFullPath(repository.FullName);
    private readonly Dictionary<string, string> objects = new(StringComparer.Ordinal);
    private readonly HashSet<string> inProgress = new(StringComparer.Ordinal);
    internal int ImmutableGitComputations { get; private set; }

    internal string ReadImmutableGit(DirectoryInfo repository, IReadOnlyList<string> arguments, Func<string> read)
    {
        RequireRepository(repository);
        if (!IsImmutable(arguments)) return read();
        var key = JsonSerializer.Serialize(arguments);
        if (objects.TryGetValue(key, out var prior)) return prior;
        ImmutableGitComputations++;
        var value = read();
        // The existing Git adapter returns empty on failure. Never retain missing/failed reads.
        if (value.Length != 0) objects.Add(key, value);
        return value;
    }

    internal CommandResult CloseCheck(DirectoryInfo repository, string id, Func<CommandResult> compute)
    {
        RequireRepository(repository);
        if (!inProgress.Add(id))
            return new(1, "revision close-check", new RevisionOperationPayload(id, "NOT_READY", ["cyclic committed Revision proof: " + id], []));
        try { return compute(); } // Every call reruns ALL live closure controls, including roles and their evidence.
        finally { inProgress.Remove(id); }
    }

    private void RequireRepository(DirectoryInfo repository)
    {
        if (!StringComparer.Ordinal.Equals(root, Path.GetFullPath(repository.FullName)))
            throw new ArgumentException("A committed proof assessment belongs to one exact repository.", nameof(repository));
    }

    private static bool IsImmutable(IReadOnlyList<string> arguments)
        => arguments.Count == 2 && arguments[0] is "show" or "rev-parse" && IsObject(arguments[1]) ||
            arguments.Count == 3 && arguments[0] == "rev-parse" && arguments[1] == "--verify" &&
                arguments[2].Length == 49 && arguments[2].EndsWith("^{commit}", StringComparison.Ordinal) &&
                CommittedEvidenceApplication.IsSha(arguments[2][..40]) ||
            arguments.Count == 3 && arguments[0] == "merge-base" &&
                CommittedEvidenceApplication.IsSha(arguments[1]) && CommittedEvidenceApplication.IsSha(arguments[2]);

    private static bool IsObject(string value)
        => value.Length > 41 && value[40] == ':' && CommittedEvidenceApplication.IsSha(value[..40]);
}
