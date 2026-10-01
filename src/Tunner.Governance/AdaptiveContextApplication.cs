using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Tunner.Governance;
public static class AdaptiveContextApplication
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static CommandResult Build(DirectoryInfo repo, string workItemId, DirectoryInfo output, AdaptiveContextRequest? request = null)
    {
        request ??= AdaptiveContextRequest.Default;
        var mode = Normalize(request.Mode);
        if (mode is null || request.TokenBudget < 0) return Reject(workItemId, output, mode is null ? "unsupported context mode" : "token budget must be zero or greater");
        var baseline = ContextApplication.Build(repo, workItemId, output);
        if (baseline.ExitCode != 0) return baseline;
        var manifestPath = Path.Combine(output.FullName, "manifest.json");
        var manifest = JsonSerializer.Deserialize<ContextManifest>(File.ReadAllText(manifestPath));
        if (manifest is null) return Reject(workItemId, output, "base context manifest could not be read");
        var included = manifest.Included.ToDictionary(x => x.Path, StringComparer.Ordinal);
        var excluded = manifest.Excluded.ToList();
        if (mode == "CORE") foreach (var item in included.Values.Where(x => !Mandatory(x)).Select(x => x.Path).ToArray()) { included.Remove(item); excluded.Add(new(item, "excluded by CORE context policy")); }
        if (mode is "EXPANDED" or "FULL_AUDIT") Add(repo, included, excluded, "src/Tunner.Governance/AdaptiveContextApplication.cs", "governance module source");
        string? indexPath = null, indexHash = null;
        if (mode == "FULL_AUDIT") { indexPath = "artifacts/context-index/index.json"; var index = Index(repo); indexHash = IndexHash(index); Write(repo, indexPath, index); Add(repo, included, excluded, indexPath, "full audit repository index", true); }
        if (excluded.Any(x => x.Reason.StartsWith("required source is unavailable:", StringComparison.Ordinal))) return new(1, "context build", new ContextBuildPayload("INSUFFICIENT_CONTEXT", workItemId, output.FullName, [], excluded, ["mandatory context source is unavailable"]));
        var selected = included.Values.OrderBy(x => x.Path, StringComparer.Ordinal).ToList();
        var mandatoryTokens = selected.Where(Mandatory).Sum(Tokens);
        if (request.TokenBudget is not null && mandatoryTokens > request.TokenBudget) return new(1, "context build", new ContextBuildPayload("CONTEXT_BUDGET_INSUFFICIENT", workItemId, output.FullName, [], excluded, [$"mandatory authority and work-state context requires approximately {mandatoryTokens} tokens, exceeding configured budget {request.TokenBudget}"]));
        var used = mandatoryTokens;
        foreach (var optional in selected.Where(x => !Mandatory(x)).ToArray()) if (request.TokenBudget is not null && used + Tokens(optional) > request.TokenBudget) { selected.Remove(optional); excluded.Add(new(optional.Path, "context budget exhausted before optional artifact selection")); } else used += Tokens(optional);
        var selection = new AdaptiveContextSelection("tunner-adaptive-context-0.1.0", DateTimeOffset.UtcNow, workItemId, mode, request.AccessScope ?? "REPOSITORY_READ", request.TokenBudget, used, indexPath, indexHash, "NONE", selected, excluded);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new ContextManifest(manifest.GeneratorVersion, manifest.GeneratedAt, manifest.WorkItem, selected, excluded, manifest.GitHistory), Json) + Environment.NewLine, new UTF8Encoding(false));
        Write(repo, Rel(repo, Path.Combine(output.FullName, "adaptive-selection.json")), selection);
        return new(0, "context build", new ContextBuildPayload("GENERATED", workItemId, output.FullName, selected, excluded, []));
    }
    public static CommandResult BuildIndex(DirectoryInfo repo, FileInfo output)
    {
        if (!Within(repo, output)) return new(2, "context index build", new AdaptiveIndexPayload("REJECTED", output.FullName, 0, null, ["index output must be inside repository"]));
        var authority = AuthorityApplication.Verify(repo);
        if (authority.ExitCode != 0) return new(1, "context index build", new AdaptiveIndexPayload("AUTHORITY_INVALID", output.FullName, 0, null, ((AuthorityPayload)authority.Payload).Findings));
        var index = Index(repo); Write(repo, Rel(repo, output.FullName), index); return new(0, "context index build", new AdaptiveIndexPayload("GENERATED", output.FullName, index.Entries.Count, IndexHash(index), []));
    }
    public static CommandResult Verify(DirectoryInfo repo, DirectoryInfo output)
    {
        var baseline = ContextApplication.Verify(repo, output); if (baseline.ExitCode != 0) return baseline;
        var path = Path.Combine(output.FullName, "adaptive-selection.json"); if (!File.Exists(path)) return baseline;
        var selection = JsonSerializer.Deserialize<AdaptiveContextSelection>(File.ReadAllText(path));
        if (selection is null) return new(1, "context verify", new ContextVerifyPayload("INVALID", output.FullName, ["adaptive selection could not be read"]));
        return selection.RepositoryIndexSha256 is not null && selection.RepositoryIndexSha256 != IndexHash(Index(repo))
            ? new(1, "context verify", new ContextVerifyPayload("STALE", output.FullName, ["repository index source set changed"]))
            : new(0, "context verify", new ContextVerifyPayload("CURRENT", output.FullName, []));
    }
    public static CommandResult Escalate(DirectoryInfo repo, string id, string current, string reason)
    {
        var authority = AuthorityApplication.Verify(repo); if (authority.ExitCode != 0) return new(1, "context escalate", new AdaptiveEscalationPayload("AUTHORITY_INVALID", id, current, null, reason, ((AuthorityPayload)authority.Payload).Findings));
        var mode = Normalize(current); if (mode is null || String.IsNullOrWhiteSpace(reason)) return new(2, "context escalate", new AdaptiveEscalationPayload("REJECTED", id, current, null, reason, ["a supported current mode and non-empty insufficiency reason are required"]));
        var next = mode switch { "CORE" => "TASK", "TASK" => "EXPANDED", _ => "FULL_AUDIT" };
        return new(0, "context escalate", new AdaptiveEscalationPayload("INSUFFICIENT_CONTEXT", id, mode, next, reason, ["No source was implicitly added. Rebuild at the returned mode and retain the selection record."]));
    }
    static CommandResult Reject(string id, DirectoryInfo output, string finding) => new(2, "context build", new ContextBuildPayload("REJECTED", id, output.FullName, [], [], [finding]));
    static void Add(DirectoryInfo repo, Dictionary<string, ContextArtifact> into, List<ContextExclusion> excluded, string path, string reason, bool required = false) { var full = Path.Combine(repo.FullName, path.Replace('/', Path.DirectorySeparatorChar)); if (!File.Exists(full)) { excluded.Add(new(path, (required ? "required" : "optional") + " source is unavailable: " + reason)); return; } into[path] = new(path, reason, Hash(full), null); }
    static bool Mandatory(ContextArtifact x) => x.Reason is "repository entry contract" or "current authority summary" or "selected work item" or "explicit authority reference" or "explicit flow reference" or "explicit contract reference" or "activated role skill" or "full audit repository index";
    static int Tokens(ContextArtifact x) => Math.Max(1, (Encoding.UTF8.GetByteCount(x.Path) + 4096) / 4);
    static string? Normalize(string? value) => value?.Trim().ToUpperInvariant() switch { "CORE" => "CORE", "TASK" => "TASK", "EXPANDED" => "EXPANDED", "FULL_AUDIT" => "FULL_AUDIT", _ => null };
    static AdaptiveIndexDocument Index(DirectoryInfo repo) => new("tunner-context-index-0.1.0", Directory.EnumerateFiles(repo.FullName, "*", SearchOption.AllDirectories).Where(p => !Skip(repo, p)).Select(p => { var q = Rel(repo, p); return new AdaptiveIndexEntry(q, q.Split('/')[0], new FileInfo(p).Length, Hash(p)); }).OrderBy(x => x.Path, StringComparer.Ordinal).ToArray());
    static bool Skip(DirectoryInfo repo, string path) { var p = Rel(repo, path); var name = Path.GetFileName(p); return p.Split('/').Any(x => x is ".git" or ".vs" or "bin" or "obj" or "artifacts") || p.StartsWith("docs/context/", StringComparison.Ordinal) || name.Equals(".env", StringComparison.OrdinalIgnoreCase) || name.Contains("secret", StringComparison.OrdinalIgnoreCase) || name.Contains("credential", StringComparison.OrdinalIgnoreCase); }
    static string IndexHash(AdaptiveIndexDocument index) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(index, Json)))).ToLowerInvariant();
    static void Write(DirectoryInfo repo, string relative, object value) { var path = Path.Combine(repo.FullName, relative.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(value, Json) + Environment.NewLine, new UTF8Encoding(false)); }
    static string Rel(DirectoryInfo repo, string path) => Path.GetRelativePath(repo.FullName, path).Replace('\\', '/');
    static bool Within(DirectoryInfo repo, FileSystemInfo item) => Path.GetFullPath(item.FullName).StartsWith(Path.GetFullPath(repo.FullName).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    static string Hash(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant(); }
}
public sealed record AdaptiveContextRequest(string? Mode, int? TokenBudget, string? AccessScope) { public static AdaptiveContextRequest Default { get; } = new("TASK", null, "REPOSITORY_READ"); }
public sealed record AdaptiveContextSelection(string GeneratorVersion, DateTimeOffset GeneratedAt, string WorkItemId, string Mode, string AccessScope, int? TokenBudget, int EstimatedTokens, string? RepositoryIndexPath, string? RepositoryIndexSha256, string Escalation, IReadOnlyList<ContextArtifact> Included, IReadOnlyList<ContextExclusion> Excluded);
public sealed record AdaptiveIndexEntry(string Path, string Category, long Bytes, string Sha256);
public sealed record AdaptiveIndexDocument(string GeneratorVersion, IReadOnlyList<AdaptiveIndexEntry> Entries);
public sealed record AdaptiveIndexPayload(string Outcome, string Output, int EntryCount, string? Sha256, IReadOnlyList<string> Findings);
public sealed record AdaptiveEscalationPayload(string Outcome, string WorkItemId, string CurrentMode, string? NextMode, string Reason, IReadOnlyList<string> Findings);