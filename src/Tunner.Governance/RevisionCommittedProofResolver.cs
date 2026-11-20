using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Tunner.Governance;

/// <summary>Resolve effective ownership through later committed, natively validated closed work-item revisions.</summary>
internal sealed class RevisionCommittedProofResolver
{
    private readonly DirectoryInfo repository;
    private readonly IReadOnlyList<GovernanceRecord> records;
    private readonly GovernanceRecord[] candidates;
    private readonly CommittedProofAssessment assessment;
    private readonly Dictionary<string, (string? Sha, HashSet<string> Paths)> classifications = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> validated = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> validationFailures = new(StringComparer.Ordinal);

    internal RevisionCommittedProofResolver(DirectoryInfo repository, GovernanceRecord original, CommittedProofAssessment assessment)
    {
        this.repository = repository;
        this.assessment = assessment;
        records = GovernanceApplication.LoadRecords(repository, []);
        var originalId = original.Type.DefinitionName == "revision" ? original.Scalar("original_scope_id") : original.Identifier;
        var cutoffId = original.Identifier;
        if (original.Type.DefinitionName == "work-item" && original.Scalar("revision_ref") is { } ownerId)
        {
            var owner = records.SingleOrDefault(r => r.Type.DefinitionName == "revision" && r.Identifier == ownerId &&
                r.Scalar("revision_scope") == "WORK_ITEM");
            // A decomposed child follows its committed owning Revision's root lineage, never a forged live link.
            if (owner is not null && original.Identifier.StartsWith(owner.Identifier + "-", StringComparison.Ordinal) &&
                owner.StringList("work_items").Contains(original.Identifier, StringComparer.Ordinal) &&
                IsCommitted(RepositoryRootResolver.ToLogicalPath(repository, owner.Path)) && HasCommittedChildBinding(original, ownerId))
            {
                originalId = owner.Scalar("original_scope_id");
                cutoffId = owner.Identifier;
            }
        }
        var sameScope = records.Where(r => r.Type.DefinitionName == "revision" && r.Scalar("revision_scope") == "WORK_ITEM" &&
            r.Scalar("original_scope_id") == originalId).ToDictionary(r => r.Identifier, StringComparer.Ordinal);
        // Existing monotonic ordering also guarantees strictly later recursion when checking prior Revision proof.
        candidates = RevisionApplication.Status(sameScope.Values.ToArray()).Reverse()
            .TakeWhile(r => r.Id != cutoffId).Where(r => r.Status == "CLOSED")
            .Select(r => sameScope[r.Id]).ToArray();
    }

    internal string? Resolve(string logicalPath, out string? failure)
    {
        failure = null;
        foreach (var revision in candidates)
        {
            if (!classifications.TryGetValue(revision.Identifier, out var classification))
            {
                classification = Classify(revision);
                classifications.Add(revision.Identifier, classification);
            }
            if (classification.Sha is null)
            {
                failure = "closed Revision ownership is not committed or classified: " + revision.Identifier;
                return null;
            }
            if (!classification.Paths.Contains(logicalPath)) continue;
            if (!validated.TryGetValue(revision.Identifier, out var valid))
            {
                // No uncommitted status/evidence/review can waive original committed validation.
                var reviewFiles = records.Where(r => r.Type.DefinitionName == "specialist-review" &&
                    r.Scalar("work_item_id") == revision.Identifier &&
                    revision.StringList("required_roles").Contains(r.Scalar("role") ?? "", StringComparer.Ordinal));
                try
                {
                    var uncommitted = revision.StringList("evidence_refs").Concat(reviewFiles.Select(r =>
                        RepositoryRootResolver.ToLogicalPath(repository, r.Path))).Where(path => !IsCommitted(path)).ToArray();
                    valid = uncommitted.Length == 0;
                    if (!valid) validationFailures[revision.Identifier] = "uncommitted proof: " + string.Join(", ", uncommitted);
                    else
                    {
                        var closure = RevisionApplication.CloseCheck(repository, revision.Identifier, assessment);
                        valid = closure.ExitCode == 0;
                        if (!valid) validationFailures[revision.Identifier] = string.Join("; ", ((RevisionOperationPayload)closure.Payload).Findings);
                    }
                }
                catch (Exception exception) when (exception is JsonException or InvalidOperationException)
                {
                    valid = false;
                    validationFailures[revision.Identifier] = exception.GetType().Name + ": " + exception.Message;
                }
                validated.Add(revision.Identifier, valid);
            }
            if (!valid)
            {
                failure = "closed Revision proof is invalid for owned behavior: " + revision.Identifier + ": " + logicalPath +
                    " (" + validationFailures[revision.Identifier] + ")";
                return null; // Never fall back to older validation after a later owner has claimed this path.
            }
            return classification.Sha;
        }
        return null;
    }

    private (string? Sha, HashSet<string> Paths) Classify(GovernanceRecord revision)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var state = CommitReadinessApplication.Read(revision);
        if (state?.ValidatedCommitSha is not { } sha || !CommittedEvidenceApplication.IsSha(sha) ||
            !IsCommitted(RepositoryRootResolver.ToLogicalPath(repository, revision.Path))) return (null, paths);
        var matched = false;
        foreach (var reference in revision.StringList("evidence_refs").Where(r => Path.GetExtension(r) == ".json"))
        {
            var text = CommittedEvidenceApplication.Git(repository, assessment, "show", sha + ":" + reference);
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                if (!root.TryGetProperty("scope_id", out var scope) || scope.GetString() != revision.Identifier ||
                    !root.TryGetProperty("validation_kind", out var kind)) continue;
                if (kind.GetString() is not ("COMMITTED" or "RETROSPECTIVE_COMMITTED") ||
                    !root.TryGetProperty("owned_behavior_paths", out var owned) ||
                    owned.ValueKind != JsonValueKind.Array || owned.GetArrayLength() == 0) return (null, paths);
                matched = true;
                foreach (var item in owned.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } path ||
                        path.Contains("..", StringComparison.Ordinal) || path.Contains('\\') || Path.IsPathRooted(path))
                        return (null, paths);
                    paths.Add(path);
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException) { }
        }
        return (matched ? sha : null, paths);
    }

    private bool HasCommittedChildBinding(GovernanceRecord child, string ownerId)
    {
        var path = RepositoryRootResolver.ToLogicalPath(repository, child.Path);
        if (!IsCommitted(path) || CommitReadinessApplication.Read(child)?.ValidatedCommitSha is not { } sha ||
            !CommittedEvidenceApplication.IsSha(sha)) return false;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(CommittedEvidenceApplication.Git(repository, assessment, "show", sha + ":" + path)));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode mapping) return false;
            var fields = GovernanceApplication.Fields(mapping);
            return GovernanceApplication.Scalar(fields, "work_item_id") == child.Identifier &&
                GovernanceApplication.Scalar(fields, "revision_ref") == ownerId;
        }
        catch (YamlException) { return false; }
    }

    private bool IsCommitted(string logicalPath)
    {
        if (logicalPath.Contains("..", StringComparison.Ordinal) || logicalPath.Contains('\\') || Path.IsPathRooted(logicalPath)) return false;
        var committed = CommittedEvidenceApplication.Git(repository, "rev-parse", "HEAD:" + logicalPath);
        return committed.Length != 0 && committed == CommittedEvidenceApplication.Git(repository, "hash-object", "--", logicalPath);
    }
}
