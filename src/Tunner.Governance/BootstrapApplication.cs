using System.Diagnostics;
using YamlDotNet.RepresentationModel;

namespace Tunner.Governance;

public static class BootstrapApplication
{
    public static CommandResult Replay(DirectoryInfo repository)
    {
        var root = Path.Combine(repository.FullName, "governance", "bootstrap");
        var findings = new List<string>();
        if (!Directory.Exists(root)) return new CommandResult(1, "bootstrap replay", new BootstrapReplayPayload("BLOCKED", ["bootstrap records directory is missing"]));
        foreach (var path in Directory.EnumerateFiles(root, "*.yaml", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.Ordinal))
        {
            try
            {
                var stream = new YamlStream(); using var reader = File.OpenText(path); stream.Load(reader);
                var fields = GovernanceApplication.Fields((YamlMappingNode)stream.Documents.Single().RootNode);
                var required = new[] { "bootstrap_id", "title", "status", "bootstrap_scope", "authority_refs", "acceptance_criteria", "activated_roles", "changes", "tests", "evidence", "commit_sha", "created_at", "updated_at" };
                foreach (var name in required) if (!fields.ContainsKey(name)) findings.Add($"{Path.GetFileName(path)}: required bootstrap field is missing: {name}");
                var id = GovernanceApplication.Scalar(fields, "bootstrap_id") ?? Path.GetFileName(path);
                if (!StringComparer.Ordinal.Equals(GovernanceApplication.Scalar(fields, "bootstrap_scope"), "CONTROL_PLANE_ONLY")) findings.Add($"{id}: bootstrap scope must be CONTROL_PLANE_ONLY");
                if (!StringComparer.Ordinal.Equals(GovernanceApplication.Scalar(fields, "status"), "COMPLETE")) findings.Add($"{id}: bootstrap record is not COMPLETE");
                foreach (var evidence in GovernanceApplication.StringList(fields, "evidence"))
                    if (!File.Exists(Path.Combine(repository.FullName, evidence.Replace('/', Path.DirectorySeparatorChar)))) findings.Add($"{id}: evidence is missing: {evidence}");
                var sha = GovernanceApplication.Scalar(fields, "commit_sha");
                if (string.IsNullOrWhiteSpace(sha) || !CommitExists(repository, sha)) findings.Add($"{id}: recorded commit is not available");
                foreach (var change in GovernanceApplication.StringList(fields, "changes"))
                    if (change.Contains("Product", StringComparison.OrdinalIgnoreCase) || change.Contains("feature", StringComparison.OrdinalIgnoreCase)) findings.Add($"{id}: Product feature work is prohibited in bootstrap mode");
            }
            catch (Exception exception) when (exception is IOException or YamlDotNet.Core.YamlException or InvalidCastException)
            { findings.Add($"{Path.GetFileName(path)}: malformed bootstrap record"); }
        }
        return new CommandResult(findings.Count == 0 ? 0 : 1, "bootstrap replay", new BootstrapReplayPayload(findings.Count == 0 ? "PASS" : "BLOCKED", findings));
    }
    private static bool CommitExists(DirectoryInfo repository, string sha)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = repository.FullName, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("cat-file"); info.ArgumentList.Add("-e"); info.ArgumentList.Add(sha + "^{commit}");
        using var process = Process.Start(info); if (process is null) return false; process.WaitForExit(); return process.ExitCode == 0;
    }
}
public sealed record BootstrapReplayPayload(string Outcome, IReadOnlyList<string> Findings);