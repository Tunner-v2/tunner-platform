using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tunner.Governance;

internal sealed record FiniteTotpBuildIdentity(string SourceCommit, string ManifestSha256, int SourceInputs, int BuildOutputs);

/// <summary>Consumes the root-reviewed receipt. It never builds, restores, writes a receipt or selects source authority.</summary>
internal static class FiniteTotpBuildBinding
{
    internal static readonly string[] SourceSelectors = ["src", "tools/browser-testing", "tests/Shared", "tests/Tunner.Identity.Persistence.Tests", "web",
        "tools/dev/new-tunner-totp-build-binding.mjs", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json",
        "Tunner.Platform.slnx", ".editorconfig", ".gitattributes", "NuGet.Config"];
    private const string OutputPrefix = "tools/browser-testing/bin/Debug/net10.0/";
    private const string ReceiptPath = "artifacts/p2-008-totp-20261110/runtime-build-binding.json";

    internal static FiniteTotpBuildIdentity Verify(DirectoryInfo root)
    {
        var bytes = File.ReadAllBytes(Resolve(root, ReceiptPath));
        if (bytes.Length > 2_000_000) throw new InvalidOperationException("FINITE_BUILD_BINDING_INVALID");
        using var receipt = JsonDocument.Parse(bytes);
        var value = receipt.RootElement;
        Keys(value, "schema_version", "source_commit_sha", "source_inputs", "build_outputs");
        var source = value.GetProperty("source_commit_sha").GetString();
        if (value.GetProperty("schema_version").GetInt32() != 1 || source is null || !Regex.IsMatch(source, "^[a-f0-9]{40}$", RegexOptions.CultureInvariant) ||
            Git(root, "merge-base", source, "HEAD").Trim() != source || Git(root, ["status", "--porcelain", "--", .. SourceSelectors]).Length != 0)
            throw new InvalidOperationException("FINITE_BUILD_BINDING_INVALID");
        var expectedSources = Git(root, ["ls-files", "-z", "--", .. SourceSelectors]).Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();
        var sourceRows = value.GetProperty("source_inputs").EnumerateArray().ToArray();
        if (!sourceRows.Select(row => row.GetProperty("path").GetString()).SequenceEqual(expectedSources, StringComparer.Ordinal)) throw new InvalidOperationException("FINITE_BUILD_BINDING_INCOMPLETE");
        var atSource = Tree(root, source); var atHead = Tree(root, "HEAD");
        foreach (var row in sourceRows)
        {
            Keys(row, "path", "sha256", "git_blob");
            var path = row.GetProperty("path").GetString()!;
            var blob = row.GetProperty("git_blob").GetString();
            if (!atSource.TryGetValue(path, out var old) || !atHead.TryGetValue(path, out var current) || old != blob || current != blob ||
                Hash(Resolve(root, path)) != row.GetProperty("sha256").GetString()) throw new InvalidOperationException("FINITE_BUILD_SOURCE_CHANGED");
        }
        var actualOutputs = Walk(root, new DirectoryInfo(Path.Combine(root.FullName, OutputPrefix))).Order(StringComparer.Ordinal).ToArray();
        var outputs = value.GetProperty("build_outputs").EnumerateArray().ToArray();
        if (!outputs.Select(row => row.GetProperty("path").GetString()).SequenceEqual(actualOutputs, StringComparer.Ordinal) || outputs.Length == 0)
            throw new InvalidOperationException("FINITE_BUILD_OUTPUT_COVERAGE_INVALID");
        foreach (var row in outputs)
        {
            Keys(row, "path", "sha256");
            var path = row.GetProperty("path").GetString()!;
            if (!path.StartsWith(OutputPrefix, StringComparison.Ordinal) || Hash(Resolve(root, path)) != row.GetProperty("sha256").GetString())
                throw new InvalidOperationException("FINITE_BUILD_OUTPUT_CHANGED");
        }
        return new(source, Convert.ToHexStringLower(SHA256.HashData(bytes)), sourceRows.Length, outputs.Length);
    }
    private static IEnumerable<string> Walk(DirectoryInfo root, DirectoryInfo directory)
    {
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("FINITE_BUILD_PATH_INVALID");
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("FINITE_BUILD_PATH_INVALID");
            if (entry is DirectoryInfo child) foreach (var path in Walk(root, child)) yield return path;
            else if (entry is FileInfo file) yield return RepositoryRootResolver.ToLogicalPath(root, file.FullName);
            else throw new InvalidOperationException("FINITE_BUILD_PATH_INVALID");
        }
    }
    private static string Resolve(DirectoryInfo root, string path)
    {
        if (Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') || path.Split('/').Any(part => part is "" or "." or "..")) throw new InvalidOperationException("FINITE_BUILD_PATH_INVALID");
        var full = Path.GetFullPath(Path.Combine(root.FullName, path));
        if (RepositoryRootResolver.ToLogicalPath(root, full) != path) throw new InvalidOperationException("FINITE_BUILD_PATH_INVALID");
        for (FileSystemInfo? entry = new FileInfo(full); entry is not null && entry.FullName != root.FullName; entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("FINITE_BUILD_PATH_INVALID");
        return full;
    }
    private static Dictionary<string, string> Tree(DirectoryInfo root, string commit)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in Git(root, ["ls-tree", "-r", "-z", commit, "--", .. SourceSelectors]).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = line.IndexOf('\t'); var fields = line[..split].Split(' ');
            if (fields[0] is not ("100644" or "100755") || fields[1] != "blob") throw new InvalidOperationException("FINITE_BUILD_PATH_INVALID");
            result.Add(line[(split + 1)..], fields[2]);
        }
        return result;
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static void Keys(JsonElement value, params string[] keys)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("FINITE_BUILD_BINDING_INVALID");
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != keys.Length || actual.Distinct(StringComparer.Ordinal).Count() != keys.Length || keys.Except(actual, StringComparer.Ordinal).Any()) throw new InvalidOperationException("FINITE_BUILD_BINDING_INVALID");
    }
    private static string Git(DirectoryInfo root, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root.FullName, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("FINITE_SOURCE_INVALID");
        var output = process.StandardOutput.ReadToEnd(); _ = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("FINITE_SOURCE_INVALID");
        return output;
    }
}
