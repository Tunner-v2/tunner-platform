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

        var workItemPath = Path.Combine(repository.FullName, "governance", "work-items", $"{workItemId}.yaml");
        var workItem = ContextWorkItem.Load(workItemPath);
        if (workItem is null)
        {
            return new CommandResult(2, "context build", new ContextBuildPayload("NOT_FOUND", workItemId, output.FullName, [], [], ["work item record does not exist or is not a YAML mapping"]));
        }

        var included = new Dictionary<string, ContextArtifact>(StringComparer.Ordinal);
        var excluded = new List<ContextExclusion>();
        AddRequired(repository, included, excluded, "AGENTS.md", "repository entry contract");
        AddRequired(repository, included, excluded, "docs/authority/current-authority.json", "current authority summary");
        AddRequired(repository, included, excluded, Relative(repository, workItemPath), "selected work item");
        AddOptional(repository, included, excluded, "docs/project/PROJECT_CONTEXT.md", "durable project context");
        AddOptional(repository, included, excluded, "docs/project/CURRENT_STATE.md", "current project state");
        AddOptional(repository, included, excluded, "docs/project/HANDOFF.md", "handoff state");
        AddOptional(repository, included, excluded, "governance/policies/lifecycle-policy.yaml", "lifecycle policy");

        foreach (var authority in workItem.StringList("authority_refs"))
        {
            AddRequired(repository, included, excluded, authority, "explicit authority reference");
        }

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
            gitHistory);

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

            var outcome = findings.Count == 0 ? "CURRENT" : "STALE";
            return new CommandResult(findings.Count == 0 ? 0 : 1, "context verify", new ContextVerifyPayload(outcome, output.FullName, findings));
        }
        catch (JsonException exception)
        {
            return new CommandResult(1, "context verify", new ContextVerifyPayload("INVALID", output.FullName, [exception.Message]));
        }
    }

    private static void WritePack(DirectoryInfo repository, DirectoryInfo output, ContextManifest manifest, ContextWorkItem workItem)
    {
        Write(output, "manifest.json", JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine);
        Write(output, "summary.md", $"# Tunner context pack\n\nGenerated by `{GeneratorVersion}` at `{manifest.GeneratedAt:O}`.\n\n## Current task\n\n- ID: `{workItem.Id}`\n- Title: {workItem.Title}\n- Status: `{workItem.Status}`\n- Context mode: `{workItem.ContextPolicy}`\n\nThis generated pack is a retrieval aid. Repository authority and governed records remain authoritative.\n");
        Write(output, "authority.md", MarkdownList("# Authority", manifest.Included.Where(item => item.Reason == "current authority summary" || item.Reason == "explicit authority reference")));
        Write(output, "business-rules.md", "# Business rules\n\nNo Product business rules were selected unless explicitly referenced by the work item.\n");
        Write(output, "relevant-flows.md", MarkdownList("# Relevant flows", manifest.Included.Where(item => item.Reason == "explicit flow reference"), "No flow references were declared."));
        Write(output, "contracts.md", MarkdownList("# Contracts", manifest.Included.Where(item => item.Reason == "explicit contract reference"), "No contract references were declared."));
        Write(output, "decisions.md", MarkdownList("# Decisions", manifest.Included.Where(item => item.Reason == "explicit decision reference"), "No decision record was resolved from the work item references."));
        Write(output, "open-blockers.md", MarkdownList("# Open blockers", workItem.StringList("blockers").Select(item => new ContextArtifact(item, "declared blocker", string.Empty)), "No blockers are declared for the selected work item."));
        Write(output, "git-changes.md", "# Recent relevant Git changes\n\n" + (manifest.GitHistory.Count == 0 ? "No bounded Git history was available for the selected sources.\n" : string.Join(Environment.NewLine, manifest.GitHistory.Select(item => $"- `{item}`")) + Environment.NewLine));
        Write(output, "tests.md", MarkdownList("# Tests and evidence", workItem.StringList("required_tests").Select(item => new ContextArtifact(item, "required test", string.Empty))) + "\n## Required evidence\n\n" + MarkdownList(string.Empty, workItem.StringList("required_evidence").Select(item => new ContextArtifact(item, "required evidence", string.Empty))));
        Write(output, "exclusions.md", "# Exclusions\n\n" + (manifest.Excluded.Count == 0 ? "No exclusions were recorded.\n" : string.Join(Environment.NewLine, manifest.Excluded.Select(item => $"- `{item.Path}` — {item.Reason}")) + Environment.NewLine));
    }

    private static string MarkdownList(string title, IEnumerable<ContextArtifact> artifacts, string? empty = null)
    {
        var rows = artifacts.ToArray();
        var heading = string.IsNullOrEmpty(title) ? string.Empty : title + "\n\n";
        return heading + (rows.Length == 0 ? (empty ?? "No entries.") + Environment.NewLine : string.Join(Environment.NewLine, rows.Select(item => $"- `{item.Path}` — {item.Reason}")) + Environment.NewLine);
    }

    private static void Write(DirectoryInfo output, string name, string content)
        => File.WriteAllText(Path.Combine(output.FullName, name), content, new UTF8Encoding(false));

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

        included[normalized] = new ContextArtifact(normalized, reason, HashFile(fullPath));
        return true;
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

public sealed record ContextArtifact(string Path, string Reason, string Sha256);
public sealed record ContextExclusion(string Path, string Reason);
public sealed record ContextWorkItemDescriptor(string Id, string Title, string Status, string ContextPolicy);
public sealed record ContextManifest(string GeneratorVersion, DateTimeOffset GeneratedAt, ContextWorkItemDescriptor WorkItem, IReadOnlyList<ContextArtifact> Included, IReadOnlyList<ContextExclusion> Excluded, IReadOnlyList<string> GitHistory);
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