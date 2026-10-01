using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tunner.Governance;

public static class EvidenceApplication
{
    private const string GeneratorVersion = "tunner-evidence-0.1.0";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex ScopeIdPattern = new("^[A-Z][A-Z0-9]*(?:-[A-Z0-9]+)+$", RegexOptions.CultureInvariant);
    private static readonly Regex SecretPattern = new("(?:gh[pousr]_[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----|(?:password|pwd)\\s*=\\s*[^\\s;]{4,})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static CommandResult Generate(
        DirectoryInfo repository,
        string scopeId,
        FileInfo? output,
        IReadOnlyList<string> artifacts,
        IReadOnlyList<string> buildReferences,
        IReadOnlyList<string> testReferences,
        IReadOnlyList<string> securityScanReferences)
    {
        var findings = new List<string>();
        if (!ScopeIdPattern.IsMatch(scopeId))
        {
            findings.Add("scope-id must be a governed identifier such as TUN-P0-011");
        }

        var workItemPath = $"governance/work-items/{scopeId}.yaml";
        if (findings.Count == 0 && !File.Exists(Path.Combine(repository.FullName, workItemPath.Replace('/', Path.DirectorySeparatorChar))))
        {
            findings.Add("scope-id must resolve to an existing governed work-item record");
        }

        if (output is null)
        {
            findings.Add("output is required");
        }
        else
        {
            findings.AddRange(ValidateOutput(repository, output));
        }

        RequireReferenceGroup(buildReferences, "build-reference", findings);
        RequireReferenceGroup(testReferences, "test-reference", findings);
        RequireReferenceGroup(securityScanReferences, "security-scan-reference", findings);
        if (findings.Count > 0)
        {
            return Rejected(scopeId, output, findings);
        }

        var authorityResult = AuthorityApplication.Verify(repository);
        if (authorityResult.ExitCode != 0)
        {
            return new CommandResult(1, "evidence generate", new EvidenceGeneratePayload("AUTHORITY_INVALID", scopeId, OutputPath(output), [], ((AuthorityPayload)authorityResult.Payload).Findings));
        }

        if (!TryReadGitIdentity(repository, out var gitIdentity, out var gitFinding))
        {
            return Rejected(scopeId, output, [gitFinding]);
        }

        var selectedArtifacts = new List<EvidenceArtifact>();
        AddReferences(repository, "artifact", artifacts.Append(workItemPath), selectedArtifacts, findings);
        AddReferences(repository, "build", buildReferences, selectedArtifacts, findings);
        AddReferences(repository, "test", testReferences, selectedArtifacts, findings);
        AddReferences(repository, "security_scan", securityScanReferences, selectedArtifacts, findings);
        if (findings.Count > 0)
        {
            return Rejected(scopeId, output, findings);
        }

        var authorityPath = "docs/authority/current-authority.json";
        var authorityHash = HashFile(Path.Combine(repository.FullName, authorityPath.Replace('/', Path.DirectorySeparatorChar)));
        var manifest = new EvidenceManifest(
            1,
            GeneratorVersion,
            "WORK_ITEM",
            scopeId,
            gitIdentity.CommitSha,
            gitIdentity.WorkingTreeState,
            new SortedDictionary<string, string>(StringComparer.Ordinal) { ["tunner-evidence"] = GeneratorVersion },
            new SortedDictionary<string, string>(StringComparer.Ordinal) { [authorityPath] = authorityHash },
            selectedArtifacts.Where(item => item.Kind == "build").OrderBy(item => item.Path, StringComparer.Ordinal).ToArray(),
            selectedArtifacts.Where(item => item.Kind == "test").OrderBy(item => item.Path, StringComparer.Ordinal).ToArray(),
            selectedArtifacts.Where(item => item.Kind == "security_scan").OrderBy(item => item.Path, StringComparer.Ordinal).ToArray(),
            selectedArtifacts.Where(item => item.Kind == "artifact").OrderBy(item => item.Path, StringComparer.Ordinal).ToArray(),
            [
                "Generated locally from repository-relative paths and SHA-256 hashes; source contents are intentionally not retained.",
                "This manifest does not approve a release, authorize a protected-main merge, prove production deployment, or establish Product acceptance.",
                "The recorded Git identity is the checked-out commit and working-tree state before the manifest file is written."
            ]);
        var json = JsonSerializer.Serialize(manifest, JsonOptions) + Environment.NewLine;
        if (SecretPattern.IsMatch(json))
        {
            return Rejected(scopeId, output, ["generated manifest contains a value matching the secret-safety pattern"]);
        }

        try
        {
            Directory.CreateDirectory(output!.DirectoryName!);
            using var stream = new FileStream(output.FullName, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(json);
        }
        catch (IOException)
        {
            return Rejected(scopeId, output, ["output must not already exist; evidence manifests are write-once"]);
        }

        return new CommandResult(0, "evidence generate", new EvidenceGeneratePayload("GENERATED", scopeId, output!.FullName, selectedArtifacts.OrderBy(item => item.Kind, StringComparer.Ordinal).ThenBy(item => item.Path, StringComparer.Ordinal).ToArray(), []));
    }

    private static CommandResult Rejected(string scopeId, FileInfo? output, IReadOnlyList<string> findings)
        => new(2, "evidence generate", new EvidenceGeneratePayload("REJECTED", scopeId, OutputPath(output), [], findings));

    private static string OutputPath(FileInfo? output) => output?.FullName ?? string.Empty;

    private static void RequireReferenceGroup(IReadOnlyList<string> references, string name, List<string> findings)
    {
        if (references.Count == 0 || references.All(string.IsNullOrWhiteSpace))
        {
            findings.Add($"at least one --{name} is required");
        }
    }

    private static List<string> ValidateOutput(DirectoryInfo repository, FileInfo output)
    {
        var findings = new List<string>();
        var expectedDirectory = Path.Combine(repository.FullName, "artifacts", "evidence");
        var outputPath = Path.GetFullPath(output.FullName);
        if (!IsWithin(repository.FullName, outputPath))
        {
            findings.Add("output must be inside the repository");
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(outputPath), expectedDirectory))
        {
            findings.Add("output must be a direct file beneath artifacts/evidence");
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(outputPath), ".json"))
        {
            findings.Add("output must have a .json extension");
        }

        if (File.Exists(outputPath) || Directory.Exists(outputPath))
        {
            findings.Add("output must not already exist; evidence manifests are write-once");
        }

        var existingEvidenceDirectory = new DirectoryInfo(expectedDirectory);
        if (existingEvidenceDirectory.Exists && existingEvidenceDirectory.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            findings.Add("artifacts/evidence must not be a reparse point");
        }

        return findings;
    }

    private static void AddReferences(DirectoryInfo repository, string kind, IEnumerable<string> references, List<EvidenceArtifact> artifacts, List<string> findings)
    {
        foreach (var reference in references.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Replace('\\', '/')).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal))
        {
            if (!TryResolveArtifact(repository, reference, out var path, out var fullPath, out var finding))
            {
                findings.Add($"{kind} reference '{reference}': {finding}");
                continue;
            }

            artifacts.Add(new EvidenceArtifact(kind, path, HashFile(fullPath)));
        }
    }

    private static bool TryResolveArtifact(DirectoryInfo repository, string reference, out string path, out string fullPath, out string finding)
    {
        path = string.Empty;
        fullPath = string.Empty;
        finding = string.Empty;
        if (Path.IsPathRooted(reference))
        {
            finding = "must be repository-relative";
            return false;
        }

        fullPath = Path.GetFullPath(Path.Combine(repository.FullName, reference));
        if (!IsWithin(repository.FullName, fullPath))
        {
            finding = "must not escape the repository";
            return false;
        }

        if (!File.Exists(fullPath))
        {
            finding = "does not exist or is not a regular file";
            return false;
        }

        var file = new FileInfo(fullPath);
        if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            finding = "must not be a reparse point";
            return false;
        }

        path = Path.GetRelativePath(repository.FullName, fullPath).Replace('\\', '/');
        return true;
    }

    private static bool TryReadGitIdentity(DirectoryInfo repository, out GitIdentity identity, out string finding)
    {
        identity = new GitIdentity(string.Empty, string.Empty);
        finding = string.Empty;
        if (!TryRunGit(repository, ["rev-parse", "HEAD"], out var commit, out finding))
        {
            finding = $"exact Git commit could not be resolved: {finding}";
            return false;
        }

        if (!Regex.IsMatch(commit, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant))
        {
            finding = "Git returned an invalid commit SHA";
            return false;
        }

        if (!TryRunGit(repository, ["status", "--porcelain", "--untracked-files=all"], out var status, out finding))
        {
            finding = $"working-tree state could not be resolved: {finding}";
            return false;
        }

        identity = new GitIdentity(commit, string.IsNullOrWhiteSpace(status) ? "CLEAN" : "DIRTY");
        return true;
    }

    private static bool TryRunGit(DirectoryInfo repository, IReadOnlyList<string> arguments, out string output, out string finding)
    {
        output = string.Empty;
        finding = string.Empty;
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repository.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                finding = "Git process could not be started";
                return false;
            }

            output = process.StandardOutput.ReadToEnd().Trim();
            var error = process.StandardError.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                finding = string.IsNullOrWhiteSpace(error) ? $"Git exited with code {process.ExitCode}" : error;
                return false;
            }

            return true;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            finding = exception.Message;
            return false;
        }
    }

    private static bool IsWithin(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

public sealed record EvidenceArtifact(string Kind, string Path, string Sha256);
public sealed record EvidenceManifest(
    int SchemaVersion,
    string GeneratorVersion,
    string ScopeType,
    string ScopeId,
    string CommitSha,
    string WorkingTreeState,
    IReadOnlyDictionary<string, string> ToolVersions,
    IReadOnlyDictionary<string, string> AuthorityHashes,
    IReadOnlyList<EvidenceArtifact> Builds,
    IReadOnlyList<EvidenceArtifact> Tests,
    IReadOnlyList<EvidenceArtifact> SecurityScans,
    IReadOnlyList<EvidenceArtifact> Artifacts,
    IReadOnlyList<string> Limitations);
public sealed record EvidenceGeneratePayload(string Outcome, string ScopeId, string Output, IReadOnlyList<EvidenceArtifact> Artifacts, IReadOnlyList<string> Findings);
internal sealed record GitIdentity(string CommitSha, string WorkingTreeState);