using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Tunner.Governance;

public static class ContextApplication
{
    private const string GeneratorVersion = "tunner-context-0.1.0";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static CommandResult Build(DirectoryInfo repository, string workItemId, DirectoryInfo output)
    {
        if (!IsWithin(repository, output))
        {
            return new CommandResult(2, "context build", new ContextBuildPayload("REJECTED", workItemId, output.FullName, [], [], ["output directory must be inside the repository"]));
        }

        var authorityResult = AuthorityApplication.Verify(repository);
        if (authorityResult.ExitCode != 0)
        {
            return new CommandResult(1, "context build", new ContextBuildPayload("AUTHORITY_INVALID", workItemId, output.FullName, [], [], ((AuthorityPayload)authorityResult.Payload).Findings));
        }

        var records = GovernanceApplication.LoadRecords(repository, []);
        if (string.IsNullOrWhiteSpace(workItemId))
        {
            var next = (NextPayload)GovernanceApplication.Next(repository).Payload;
            var eligible = next.Items.Where(x => x.Actionable).Select(x => x.WorkItemId).ToHashSet(StringComparer.Ordinal);
            workItemId = RevisionApplication.Revisions(records).Where(x => eligible.Contains(x.Identifier)).OrderByDescending(x => x.Scalar("created_at"), StringComparer.Ordinal).FirstOrDefault()?.Identifier
                ?? records.Where(x => x.Type.DefinitionName == "work-item" && eligible.Contains(x.Identifier) && x.Scalar("status") is "IN_PROGRESS" or "READY" or "VALIDATION" or "CODE_REVIEW").OrderByDescending(x => x.Scalar("created_at"), StringComparer.Ordinal).FirstOrDefault()?.Identifier
                ?? "CURRENT_STATE";
        }
        var selectedRevision = RevisionApplication.Revisions(records).FirstOrDefault(x => x.Identifier == workItemId);
        var workItemPath = Path.Combine(repository.FullName, "governance", "work-items", $"{workItemId}.yaml");
        var workItem = selectedRevision is not null
            ? new ContextWorkItem(selectedRevision.Identifier, selectedRevision.Scalar("title") ?? "Revision", selectedRevision.Scalar("status") ?? "UNKNOWN", "TASK", selectedRevision.Fields)
            : workItemId == "CURRENT_STATE"
                ? new ContextWorkItem("CURRENT_STATE", "Current governed recovery state", "READ_ONLY", "CORE", new Dictionary<string, YamlNode>(StringComparer.Ordinal))
                : ContextWorkItem.Load(workItemPath);
        if (workItem is null)
        {
            return new CommandResult(2, "context build", new ContextBuildPayload("NOT_FOUND", workItemId, output.FullName, [], [], ["work item record does not exist or is not a YAML mapping"]));
        }

        WriteDurableState(repository, records, workItemId);
        var included = new Dictionary<string, ContextArtifact>(StringComparer.Ordinal);
        var excluded = new List<ContextExclusion>();
        AddRequired(repository, included, excluded, "AGENTS.md", "repository entry contract");
        AddRequired(repository, included, excluded, "docs/authority/current-authority.json", "current authority summary");
        AddActiveAuthorityArtifacts(repository, included, excluded);
        if (selectedRevision is null && workItemId != "CURRENT_STATE") AddRequired(repository, included, excluded, Relative(repository, workItemPath), "selected work item");
        if (selectedRevision is not null) AddRevisionContext(repository, selectedRevision, records, included, excluded);
        var owningRevision = records.FirstOrDefault(x => x.Type.DefinitionName == "revision" && x.Identifier == GovernanceApplication.Scalar(workItem.Fields, "revision_ref"));
        if (owningRevision is not null) AddRevisionContext(repository, owningRevision, records, included, excluded);
        foreach (var milestone in records.Where(x => x.Type.DefinitionName == "milestone")) AddRequired(repository, included, excluded, Relative(repository, milestone.Path), "historical milestone state");
        AddOptional(repository, included, excluded, "docs/project/PROJECT_CONTEXT.md", "durable project context");
        AddOptional(repository, included, excluded, "docs/project/CURRENT_STATE.md", "current project state");
        AddOptional(repository, included, excluded, "docs/project/HANDOFF.md", "handoff state");
        AddOptional(repository, included, excluded, "governance/policies/lifecycle-policy.yaml", "lifecycle policy");

        foreach (var authority in workItem.StringList("authority_refs"))
        {
            AddRequired(repository, included, excluded, authority, "explicit authority reference");
        }

        AddCurrentAuthorityOverrides(repository, workItem.StringList("authority_refs"), included, excluded);

        foreach (var flow in workItem.StringList("flow_refs"))
        {
            AddRequired(repository, included, excluded, flow, "explicit flow reference");
        }

        foreach (var contract in workItem.StringList("contract_refs"))
        {
            AddRequired(repository, included, excluded, contract, "explicit contract reference");
        }

        foreach (var decision in workItem.StringList("decision_refs"))
        {
            foreach (var mirrored in Directory.EnumerateFiles(Path.Combine(repository.FullName, "docs", "authority"), "*.md", SearchOption.AllDirectories).Where(path => Relative(repository, path) == decision || Path.GetFileName(path).StartsWith(decision + "_", StringComparison.Ordinal)))
                AddOptional(repository, included, excluded, Relative(repository, mirrored), "explicit decision reference");
            AddOptional(repository, included, excluded, $"governance/decisions/{decision}.yaml", "explicit decision reference");
            AddOptional(repository, included, excluded, $"governance/decisions/{decision.ToLowerInvariant()}-p0-local-integration.yaml", "explicit decision reference");
        }

        foreach (var prerequisite in workItem.StringList("prerequisites"))
        {
            AddOptional(repository, included, excluded, $"governance/work-items/{prerequisite}.yaml", "prerequisite state");
        }

        foreach (var evidence in workItem.StringList("required_evidence"))
        {
            AddOptional(repository, included, excluded, evidence, "required evidence");
        }

        foreach (var role in workItem.StringList("activated_roles"))
        {
            AddOptional(repository, included, excluded, $".agent/skills/{role}/SKILL.md", "activated role skill");
        }

        var gitHistory = GitHistory(repository, included.Keys);
        var pack = new ContextManifest(
            GeneratorVersion,
            DateTimeOffset.UtcNow,
            workItem.ToDescriptor(),
            included.Values.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray(),
            excluded.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray(),
            gitHistory,
            selectedRevision is null && owningRevision is null ? null : RevisionApplication.Status([selectedRevision ?? owningRevision!]).Single(),
            RevisionApplication.HistoricalScopes(repository, records), RevisionSetHash(repository),
            CommitState(repository, selectedRevision ?? owningRevision, records, workItemId),
            RevisionApplication.Status(records).GroupBy(x => x.OriginalScopeId, StringComparer.Ordinal).Select(x => x.Last()).Where(x => selectedRevision is null && owningRevision is null || x.OriginalScopeId == (selectedRevision ?? owningRevision)!.Scalar("original_scope_id")).ToArray(), GovernanceStateHash(repository));

        Directory.CreateDirectory(output.FullName);
        WritePack(repository, output, pack, workItem);
        return new CommandResult(0, "context build", new ContextBuildPayload("GENERATED", workItemId, output.FullName, pack.Included, pack.Excluded, []));
    }

    public static CommandResult Verify(DirectoryInfo repository, DirectoryInfo output)
    {
        if (!IsWithin(repository, output))
        {
            return new CommandResult(2, "context verify", new ContextVerifyPayload("REJECTED", output.FullName, ["output directory must be inside the repository"]));
        }

        var authorityResult = AuthorityApplication.Verify(repository);
        if (authorityResult.ExitCode != 0)
        {
            return new CommandResult(1, "context verify", new ContextVerifyPayload("AUTHORITY_INVALID", output.FullName, ((AuthorityPayload)authorityResult.Payload).Findings));
        }

        var manifestPath = Path.Combine(output.FullName, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new CommandResult(2, "context verify", new ContextVerifyPayload("MISSING", output.FullName, ["manifest.json is missing"]));
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<ContextManifest>(File.ReadAllText(manifestPath));
            if (manifest is null)
            {
                return new CommandResult(1, "context verify", new ContextVerifyPayload("INVALID", output.FullName, ["manifest.json could not be read"]));
            }

            var findings = new List<string>();
            foreach (var artifact in manifest.Included)
            {
                var fullPath = Path.Combine(repository.FullName, artifact.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                {
                    findings.Add($"missing source: {artifact.Path}");
                    continue;
                }

                if (!StringComparer.Ordinal.Equals(HashFile(fullPath), artifact.Sha256))
                {
                    findings.Add($"changed source: {artifact.Path}");
                }
            }

            if (manifest.RevisionSetSha256 is not null && manifest.RevisionSetSha256 != RevisionSetHash(repository)) findings.Add("Revision lineage set changed");
            if (manifest.GovernanceStateSha256 is not null && manifest.GovernanceStateSha256 != GovernanceStateHash(repository)) findings.Add("authoritative governance changed; regenerate CURRENT_STATE/HANDOFF/PROJECT_CONTEXT");
            var outcome = findings.Count == 0 ? "CURRENT" : "STALE";
            return new CommandResult(findings.Count == 0 ? 0 : 1, "context verify", new ContextVerifyPayload(outcome, output.FullName, findings));
        }
        catch (JsonException exception)
        {
            return new CommandResult(1, "context verify", new ContextVerifyPayload("INVALID", output.FullName, [exception.Message]));
        }
    }

    private static void AddActiveAuthorityArtifacts(DirectoryInfo repository, Dictionary<string, ContextArtifact> included, List<ContextExclusion> excluded)
    {
        var authorityPath = Path.Combine(repository.FullName, "docs", "authority", "current-authority.json");
        try
        {
            using var authority = JsonDocument.Parse(File.ReadAllText(authorityPath));
            if (!authority.RootElement.TryGetProperty("active_amendments", out var amendments) || amendments.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var amendment in amendments.EnumerateArray())
            {
                if (!amendment.TryGetProperty("id", out var idNode) || string.IsNullOrWhiteSpace(idNode.GetString()) ||
                    !amendment.TryGetProperty("path", out var pathNode) || string.IsNullOrWhiteSpace(pathNode.GetString()))
                {
                    excluded.Add(new ContextExclusion("docs/authority/current-authority.json", "active amendment is missing an id or repository path"));
                    continue;
                }

                var amendmentRoot = Path.Combine(repository.FullName, pathNode.GetString()!.Replace('/', Path.DirectorySeparatorChar));
                var document = Directory.Exists(amendmentRoot)
                    ? Directory.EnumerateFiles(amendmentRoot, $"{idNode.GetString()}*.md", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.Ordinal).FirstOrDefault()
                    : null;
                if (document is null)
                {
                    excluded.Add(new ContextExclusion(pathNode.GetString()!, "active amendment document is unavailable"));
                    continue;
                }

                AddRequired(repository, included, excluded, Relative(repository, document), "active authority amendment");
            }
        }
        catch (JsonException)
        {
            excluded.Add(new ContextExclusion("docs/authority/current-authority.json", "active amendment index is invalid JSON"));
        }
        catch (IOException)
        {
            excluded.Add(new ContextExclusion("docs/authority/current-authority.json", "active amendment index could not be read"));
        }
    }

    private static void WritePack(DirectoryInfo repository, DirectoryInfo output, ContextManifest manifest, ContextWorkItem workItem)
    {
        Write(output, "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine);
        WriteRecovery(output, manifest);
        Write(output, "summary.md", $"# Tunner context pack\n\nGenerated by `{GeneratorVersion}` at `{manifest.GeneratedAt:O}`.\n\n## Current task\n\n- ID: `{workItem.Id}`\n- Title: {workItem.Title}\n- Status: `{workItem.Status}`\n- Context mode: `{workItem.ContextPolicy}`\n\nThis generated pack is a retrieval aid. Repository authority and governed records remain authoritative.\n");
        Write(output, "authority.md", MarkdownList("# Authority", manifest.Included.Where(item => item.Reason == "current authority summary" || item.Reason == "active authority amendment" || item.Reason == "explicit authority reference")));
        Write(output, "business-rules.md", "# Business rules\n\nNo Product business rules were selected unless explicitly referenced by the work item.\n");
        Write(output, "relevant-flows.md", MarkdownList("# Relevant flows", manifest.Included.Where(item => item.Reason == "explicit flow reference"), "No flow references were declared."));
        Write(output, "contracts.md", MarkdownList("# Contracts", manifest.Included.Where(item => item.Reason == "explicit contract reference"), "No contract references were declared."));
        Write(output, "decisions.md", MarkdownList("# Decisions", manifest.Included.Where(item => item.Reason == "explicit decision reference"), "No decision record was resolved from the work item references."));
        Write(output, "open-blockers.md", MarkdownList("# Open blockers", workItem.StringList("blockers").Select(item => new ContextArtifact(item, "declared blocker", string.Empty)), "No blockers are declared for the selected work item."));
        Write(output, "git-changes.md", "# Recent relevant Git changes\n\n" + (manifest.GitHistory.Count == 0 ? "No bounded Git history was available for the selected sources.\n" : string.Join(Environment.NewLine, manifest.GitHistory.Select(item => $"- `{item}`")) + Environment.NewLine));
        Write(output, "tests.md", MarkdownList("# Tests and evidence", workItem.StringList("required_tests").Select(item => new ContextArtifact(item, "required test", string.Empty))) + "\n## Required evidence\n\n" + MarkdownList(string.Empty, workItem.StringList("required_evidence").Select(item => new ContextArtifact(item, "required evidence", string.Empty))));
        Write(output, "exclusions.md", "# Exclusions\n\n" + (manifest.Excluded.Count == 0 ? "No exclusions were recorded.\n" : string.Join(Environment.NewLine, manifest.Excluded.Select(item => $"- `{item.Path}` — {item.Reason}")) + Environment.NewLine));
    }

    private static ContextCommitState? CommitState(DirectoryInfo repository, GovernanceRecord? revision, IReadOnlyList<GovernanceRecord> records, string id)
    {
        var record = revision ?? records.FirstOrDefault(x => x.Identifier == id);
        var state = record is null ? null : CommitReadinessApplication.Read(record);
        return state is null ? null : new ContextCommitState(state.ValidatedCommitSha, state.TestedCommitSha, state.ClosureCommitSha ?? CommittedEvidenceApplication.ClosureSha(repository, revision ?? records.First(x => x.Identifier == id)));
    }

    private static void AddCurrentAuthorityOverrides(DirectoryInfo repository, IReadOnlyList<string> references, Dictionary<string, ContextArtifact> included, List<ContextExclusion> excluded)
    {
        foreach (var reference in references.Where(x => x.StartsWith("docs/authority/", StringComparison.Ordinal)))
        {
            var current = "docs/authority/current/development/" + Path.GetFileName(reference);
            if (current != reference && File.Exists(Path.Combine(repository.FullName, current)))
                AddRequired(repository, included, excluded, current, "current approved authority for selected scope");
        }
    }

    private static void AddRevisionContext(DirectoryInfo repository, GovernanceRecord revision, IReadOnlyList<GovernanceRecord> records, Dictionary<string, ContextArtifact> included, List<ContextExclusion> excluded)
    {
        AddRequired(repository, included, excluded, Relative(repository, revision.Path), "selected Revision");
        var original = records.FirstOrDefault(x => x.Identifier == revision.Scalar("original_scope_id"));
        if (original is not null)
        {
            AddRequired(repository, included, excluded, Relative(repository, original.Path), "historical original scope");
            foreach (var evidence in original.StringList(original.Type.DefinitionName == "milestone" ? "evidence_refs" : "required_evidence"))
                AddOptional(repository, included, excluded, evidence, "original scope evidence");
        }
        AddCurrentAuthorityOverrides(repository, revision.StringList("authority_refs"), included, excluded);
        foreach (var decision in revision.StringList("decision_refs"))
            foreach (var path in Directory.EnumerateFiles(Path.Combine(repository.FullName, "docs", "authority"), "*.md", SearchOption.AllDirectories).Where(path => Relative(repository, path) == decision || Path.GetFileName(path).StartsWith(decision + "_", StringComparison.Ordinal)))
                AddRequired(repository, included, excluded, Relative(repository, path), "explicit decision reference");
        foreach (var field in new[] { "parent_revision_id", "parent_milestone_revision_id" })
        {
            var parent = records.FirstOrDefault(x => x.Identifier == revision.Scalar(field));
            if (parent is not null) AddRequired(repository, included, excluded, Relative(repository, parent.Path), "relevant Revision lineage");
        }
        foreach (var field in new[] { "defect_refs", "change_refs", "prerequisites" })
            foreach (var reference in revision.StringList(field))
            {
                var record = records.FirstOrDefault(x => x.Identifier == reference);
                AddRequired(repository, included, excluded, record is null ? reference : Relative(repository, record.Path), "Revision dependency or linked finding");
            }
        foreach (var child in records.Where(x => x.Type.DefinitionName == "work-item" && x.Scalar("revision_ref") == revision.Identifier || x.Type.DefinitionName == "revision" && x.Scalar("parent_milestone_revision_id") == revision.Identifier))
        {
            AddOptional(repository, included, excluded, Relative(repository, child.Path), "Revision child state");
            foreach (var source in child.StringList("required_tests").Concat(child.StringList("required_evidence")))
                if (File.Exists(Path.Combine(repository.FullName, source))) AddOptional(repository, included, excluded, source, "Revision implementation test or evidence");
        }
        foreach (var evidence in revision.StringList("evidence_refs")) AddOptional(repository, included, excluded, evidence, "Revision evidence");
        foreach (var role in revision.StringList("required_roles")) AddRequired(repository, included, excluded, $".agent/skills/{role}/SKILL.md", "activated role skill");
    }

    private static void WriteRecovery(DirectoryInfo output, ContextManifest manifest)
    {
        var scopes = manifest.HistoricalScopes ?? [];
        var history = string.Join("\n", scopes.Select(x => $"- {x.Id}: historical {x.HistoricalStatus}; working record {x.WorkingRecordStatus}; active RVs: {string.Join(", ", x.ActiveRevisionRefs)}"));
        var revision = manifest.Revision;
        var sha = $"\n- Validated SHA: {manifest.CommitState?.ValidatedSha ?? "not recorded"}\n- Tested SHA: {manifest.CommitState?.TestedSha ?? "not recorded"}\n- Closure SHA: {manifest.CommitState?.ClosureSha ?? "not recorded"}\n";
        var latest = "\nLatest bounded Revision lineage:\n" + string.Join("\n", (manifest.LatestRevisions ?? []).Select(x => $"- {x.Id}: {x.Status}; original {x.OriginalScopeId}; {x.Type}: {x.Reason}; next action: {x.NextAction}")) + "\n";
        var detail = revision is null
            ? "No Revision is selected. No post-closure Revision is implicitly created.\n"
            : $"- Revision: {revision.Id} ({revision.Status})\n- Original scope: {revision.OriginalScopeId}\n- Type/reason: {revision.Type}: {revision.Reason}\n- Impact: {revision.Impact}; blocked scope: {string.Join(", ", revision.BlockedScope)}\n- Prior Revision: {revision.PreviousRevision ?? "none"}\n- Parent milestone Revision: {revision.ParentMilestoneRevision ?? "none"}\n- Exact next action: {revision.NextAction}\n";
        Write(output, "CURRENT_STATE.md", "# Generated CURRENT_STATE\n\n## Historical milestone state\n\n" + history + "\n\n## Current Revision state\n\n" + detail);
        Write(output, "PROJECT_CONTEXT.md", "# Generated PROJECT_CONTEXT\n\nOriginal closure and current work are separate governance facts.\n\n" + history + "\n\n" + detail + latest + "\nDependency/authority/defect/evidence paths are hash-recorded in manifest.json. Only selected original, immediate prior/parent lineage, and declared delta sources are retrieved.\n");
        Write(output, "HANDOFF.md", "# Generated HANDOFF\n\n- Scope: " + manifest.WorkItem.Id + "\n- State: " + manifest.WorkItem.Status + "\n\n" + detail + sha + "\n" + history + latest + "\n\nThe selected governed record and evidence retain validated/tested SHA and closure SHA. Run governance next before implementation. No chat history is required.\n");
    }

    private static string RecoveryAction(IReadOnlyList<GovernanceRecord> records)
    {
        var milestones = records.Where(x => x.Type.DefinitionName == "milestone" && x.Scalar("status") != "CLOSED")
            .OrderBy(x => x.Identifier, StringComparer.Ordinal).ToArray();
        var defects = records.Where(x => x.Type.DefinitionName == "defect" && !new[] { "CLOSED", "RESOLVED", "CANCELED", "SUPERSEDED", "DUPLICATE", "NOT_APPLICABLE" }.Contains(x.Scalar("status"), StringComparer.Ordinal))
            .Select(x => x.Identifier).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return milestones.Length == 0 ? "No active milestone work; preserve historical closure and inspect governed backlog before starting a new scope."
            : "Run tunner governance milestone close-check " + milestones[0].Identifier +
              " and tunner governance commit-readiness audit --milestone " + milestones[0].Identifier +
              "; reconcile scoped findings and required milestone reviews before lifecycle advancement. Open defects: " + string.Join(", ", defects);
    }

    private static string GovernanceStateHash(DirectoryInfo repository)
    {
        var records = GovernanceApplication.LoadRecords(repository, []);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", records.OrderBy(x => x.Path, StringComparer.Ordinal).Select(x => Relative(repository, x.Path) + ":" + HashFile(x.Path)))))).ToLowerInvariant();
    }

    private static void WriteDurableState(DirectoryInfo repository, IReadOnlyList<GovernanceRecord> records, string selected)
    {
        var output = Directory.CreateDirectory(Path.Combine(repository.FullName, "docs", "project"));
        var history = string.Join("\n", RevisionApplication.HistoricalScopes(repository, records).Where(x => records.Any(r => r.Type.DefinitionName == "milestone" && r.Identifier == x.Id)).Select(x => $"- {x.Id}: historical {x.HistoricalStatus}; active Revisions: {string.Join(", ", x.ActiveRevisionRefs)}"));
        var lineage = RevisionApplication.Status(records).GroupBy(x => x.OriginalScopeId, StringComparer.Ordinal).Select(x => x.Last()).ToArray();
        var current = records.SingleOrDefault(x => x.Identifier == selected);
        var state = current is null ? null : CommitReadinessApplication.Read(current);
        var rows = string.Join("\n", lineage.Select(x => $"- {x.Id}: {x.Status}; original {x.OriginalScopeId}; parent {x.ParentMilestoneRevision ?? "none"}; {x.Type}: {x.Reason}"));
        var next = (NextPayload)GovernanceApplication.Next(repository).Payload;
        var action = next.Items.FirstOrDefault(x => x.Actionable && x.WorkItemId == selected) ?? next.Items.FirstOrDefault(x => x.Actionable);
        var details = current is null ? "" : $"\nPrerequisites: {string.Join(", ", current.StringList("prerequisites"))}\nAuthority: {string.Join(", ", current.StringList("authority_refs"))}\nDefects: {string.Join(", ", current.StringList("defect_refs"))}\nEvidence: {string.Join(", ", current.StringList("evidence_refs"))}\n";
        var content = "# Governed project recovery state\n\nDerived deterministically from repository-native governance. Regenerate with tunner context build.\n\n" + history + "\n\nLatest bounded Revision lineage:\n" + rows +
            $"\n\nSelected scope: {selected}\nValidated SHA: {state?.ValidatedCommitSha ?? "not recorded"}\nTested SHA: {state?.TestedCommitSha ?? "not recorded"}\nClosure SHA: {state?.ClosureCommitSha ?? (current is null ? null : CommittedEvidenceApplication.ClosureSha(repository, current)) ?? "not recorded"}\n" + details +
            $"\nExact next governed action: {(action is null ? RecoveryAction(records) : action.WorkItemId + ": " + action.Reason)}\n\nP1 historical validation findings are retained in governance/evidence/P0-RV001-P1-EVIDENCE-AUDIT.json. No chat history is required.\n";
        foreach (var name in new[] { "CURRENT_STATE.md", "HANDOFF.md", "PROJECT_CONTEXT.md" }) Write(output, name, content);
    }

    private static string RevisionSetHash(DirectoryInfo repository)
    {
        var directory = Path.Combine(repository.FullName, "governance", "revisions");
        IEnumerable<string> paths = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.yaml", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal) : [];
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", paths.Select(x => Relative(repository, x) + ":" + HashFile(x)))))).ToLowerInvariant();
    }

    private static string MarkdownList(string title, IEnumerable<ContextArtifact> artifacts, string? empty = null)
    {
        var rows = artifacts.ToArray();
        var heading = string.IsNullOrEmpty(title) ? string.Empty : title + "\n\n";
        return heading + (rows.Length == 0 ? (empty ?? "No entries.") + Environment.NewLine : string.Join(Environment.NewLine, rows.Select(item => $"- `{item.Path}` — {item.Reason}")) + Environment.NewLine);
    }

    private static void Write(DirectoryInfo output, string name, string content)
        => File.WriteAllText(Path.Combine(output.FullName, name), name.EndsWith(".md", StringComparison.Ordinal) ? string.Join("\n", content.Split('\n').Select(line => line.TrimEnd())) : content, new UTF8Encoding(false));

    private static void AddRequired(DirectoryInfo repository, Dictionary<string, ContextArtifact> included, List<ContextExclusion> excluded, string path, string reason)
    {
        if (!TryAdd(repository, included, path, reason))
        {
            excluded.Add(new ContextExclusion(path, $"required source is unavailable: {reason}"));
        }
    }

    private static void AddOptional(DirectoryInfo repository, Dictionary<string, ContextArtifact> included, List<ContextExclusion> excluded, string path, string reason)
    {
        if (!TryAdd(repository, included, path, reason))
        {
            excluded.Add(new ContextExclusion(path, $"optional source is unavailable: {reason}"));
        }
    }

    private static bool TryAdd(DirectoryInfo repository, Dictionary<string, ContextArtifact> included, string path, string reason)
    {
        var normalized = path.Replace('\\', '/');
        var fullPath = Path.Combine(repository.FullName, normalized.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath) || !IsWithin(repository, new FileInfo(fullPath)))
        {
            return false;
        }

        included[normalized] = new ContextArtifact(normalized, reason, HashFile(fullPath), SkillVersion(fullPath, reason));
        return true;
    }

    private static string? SkillVersion(string path, string reason)
    {
        if (!StringComparer.Ordinal.Equals(reason, "activated role skill"))
        {
            return null;
        }

        try
        {
            var firstLines = File.ReadLines(path).Take(16).ToArray();
            return firstLines.Select(line => line.Split(':', 2)).FirstOrDefault(parts => parts.Length == 2 && StringComparer.Ordinal.Equals(parts[0].Trim(), "version"))?[1].Trim().Trim('"', '\'');
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string[] GitHistory(DirectoryInfo repository, IEnumerable<string> paths)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repository.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("log");
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add("10");
        startInfo.ArgumentList.Add("--pretty=format:%h %s");
        startInfo.ArgumentList.Add("--");
        foreach (var path in paths.OrderBy(item => item, StringComparer.Ordinal))
        {
            startInfo.ArgumentList.Add(path);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries) : [];
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    private static bool IsWithin(DirectoryInfo repository, FileSystemInfo candidate)
    {
        var root = Path.GetFullPath(repository.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var value = Path.GetFullPath(candidate.FullName);
        return value.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string Relative(DirectoryInfo repository, string path)
        => Path.GetRelativePath(repository.FullName, path).Replace('\\', '/');

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

public sealed record ContextArtifact(string Path, string Reason, string Sha256, string? Version = null);
public sealed record ContextExclusion(string Path, string Reason);
public sealed record ContextWorkItemDescriptor(string Id, string Title, string Status, string ContextPolicy);
public sealed record ContextManifest(string GeneratorVersion, DateTimeOffset GeneratedAt, ContextWorkItemDescriptor WorkItem, IReadOnlyList<ContextArtifact> Included, IReadOnlyList<ContextExclusion> Excluded, IReadOnlyList<string> GitHistory, RevisionStatus? Revision = null, IReadOnlyList<HistoricalScopeStatus>? HistoricalScopes = null, string? RevisionSetSha256 = null, ContextCommitState? CommitState = null, IReadOnlyList<RevisionStatus>? LatestRevisions = null, string? GovernanceStateSha256 = null);
public sealed record ContextCommitState(string? ValidatedSha, string? TestedSha, string? ClosureSha);
public sealed record ContextBuildPayload(string Outcome, string WorkItemId, string Output, IReadOnlyList<ContextArtifact> Included, IReadOnlyList<ContextExclusion> Excluded, IReadOnlyList<string> Findings);
public sealed record ContextVerifyPayload(string Outcome, string Output, IReadOnlyList<string> Findings);

internal sealed record ContextWorkItem(string Id, string Title, string Status, string ContextPolicy, IReadOnlyDictionary<string, YamlNode> Fields)
{
    public IReadOnlyList<string> StringList(string name) => GovernanceApplication.StringList(Fields, name);
    public ContextWorkItemDescriptor ToDescriptor() => new(Id, Title, Status, ContextPolicy);

    public static ContextWorkItem? Load(string path)
    {
        try
        {
            var stream = new YamlStream();
            using var reader = File.OpenText(path);
            stream.Load(reader);
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode mapping)
            {
                return null;
            }

            var fields = GovernanceApplication.Fields(mapping);
            var id = GovernanceApplication.Scalar(fields, "work_item_id");
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return new ContextWorkItem(id, GovernanceApplication.Scalar(fields, "title") ?? "Untitled", GovernanceApplication.Scalar(fields, "status") ?? "UNKNOWN", GovernanceApplication.Scalar(fields, "context_policy") ?? "TASK", fields);
        }
        catch (YamlException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
