using System.Text.Json;

namespace Tunner.Governance;

/// <summary>Current committed milestone review through closed Revision lineage; historical receipts remain unchanged.</summary>
internal static class MilestoneReviewRevalidation
{
    internal static string Resolve(DirectoryInfo repository, GovernanceRecord milestone, string historicalPath)
    {
        if (milestone.Scalar("status") != "CLOSED" || !IsCommitted(repository, historicalPath) ||
            !HistoricalReviewsPass(repository, milestone, historicalPath)) return historicalPath;
        var records = GovernanceApplication.LoadRecords(repository, []);
        var revisions = records.Where(r => r.Type.DefinitionName == "revision" &&
            r.Scalar("revision_scope") == "MILESTONE" && r.Scalar("original_scope_id") == milestone.Identifier);
        foreach (var candidate in RevisionApplication.Status(revisions.ToArray()).Where(r => r.Status == "CLOSED").Reverse())
        {
            var revision = revisions.Single(r => r.Identifier == candidate.Id);
            var proof = revision.StringList("evidence_refs").SingleOrDefault(p =>
                p.EndsWith("-MILESTONE-ROLE-REVIEWS.json", StringComparison.Ordinal));
            if (proof is null) continue;
            // A newer claimed review cannot silently fall back to an older claim when its proof is invalid.
            if (candidate.Status != "CLOSED" || !IsCommitted(repository, RepositoryRootResolver.ToLogicalPath(repository, revision.Path)) ||
                milestone.StringList("required_roles").Any(r => !revision.StringList("required_roles").Contains(r, StringComparer.Ordinal)) ||
                revision.StringList("evidence_refs").Any(p => !IsCommitted(repository, p))) return historicalPath;
            var reviews = records.Where(r => r.Type.DefinitionName == "specialist-review" && r.Scalar("work_item_id") == revision.Identifier);
            if (reviews.Any(r => !IsCommitted(repository, RepositoryRootResolver.ToLogicalPath(repository, r.Path))) ||
                RevisionApplication.CloseCheck(repository, revision.Identifier).ExitCode != 0) return historicalPath;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository.FullName, proof)));
                if (!document.RootElement.TryGetProperty("revision_ref", out var owner) || owner.GetString() != revision.Identifier)
                    return historicalPath;
                return proof; // The existing full scope/SHA/role validator still evaluates this receipt.
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidOperationException)
            {
                return historicalPath;
            }
        }
        return historicalPath;
    }

    private static bool HistoricalReviewsPass(DirectoryInfo repository, GovernanceRecord milestone, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository.FullName, path)));
            var root = document.RootElement;
            return root.GetProperty("scope_type").GetString() == "MILESTONE" &&
                root.GetProperty("scope_id").GetString() == milestone.Identifier && root.GetProperty("status").GetString() == "PASS" &&
                milestone.StringList("required_roles").All(role => root.GetProperty("reviews").EnumerateArray().Count(r =>
                    r.GetProperty("role").GetString() == role && r.GetProperty("conclusion").GetString() == "PASS") == 1);
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }

    private static bool IsCommitted(DirectoryInfo repository, string path)
    {
        if (!path.StartsWith("governance/", StringComparison.Ordinal) || path.Contains('\\') ||
            Path.IsPathRooted(path) || path.Split('/').Any(p => p is "" or "." or "..")) return false;
        var blob = CommittedEvidenceApplication.Git(repository, "rev-parse", "HEAD:" + path);
        return blob.Length != 0 && blob == CommittedEvidenceApplication.Git(repository, "hash-object", "--", path);
    }
}
