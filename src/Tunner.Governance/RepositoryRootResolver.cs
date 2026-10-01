using System.Diagnostics;

namespace Tunner.Governance;

public static class RepositoryRootResolver
{
    private const string Marker = ".tunner-root";

    public static DirectoryInfo Resolve(DirectoryInfo? explicitRoot = null)
    {
        if (explicitRoot is not null)
        {
            return Validate(explicitRoot.FullName, "--repo-root");
        }

        var environmentRoot = Environment.GetEnvironmentVariable("TUNNER_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(environmentRoot))
        {
            return Validate(environmentRoot, "TUNNER_REPO_ROOT");
        }

        var start = Directory.GetCurrentDirectory();
        if (TryGitRoot(start, out var gitRoot))
        {
            return Validate(gitRoot, "Git top-level");
        }

        for (var current = new DirectoryInfo(Path.GetFullPath(start)); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, Marker)))
            {
                return current;
            }
        }

        throw new InvalidOperationException("Unable to resolve the Tunner repository root. Supply --repo-root, set TUNNER_REPO_ROOT, run inside a Git checkout, or ensure an ancestor contains .tunner-root.");
    }

    public static string ToLogicalPath(DirectoryInfo repository, string candidate)
    {
        var root = Path.GetFullPath(repository.FullName);
        var relative = Path.GetRelativePath(root, Path.GetFullPath(candidate));
        if (relative == ".")
        {
            return relative;
        }

        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Path must remain inside the resolved repository root.");
        }

        return relative.Replace('\\', '/');
    }

    private static DirectoryInfo Validate(string candidate, string source)
    {
        var root = new DirectoryInfo(Path.GetFullPath(candidate));
        if (!root.Exists || !File.Exists(Path.Combine(root.FullName, Marker)))
        {
            throw new InvalidOperationException($"{source} must identify a directory containing {Marker}.");
        }

        return root;
    }

    private static bool TryGitRoot(string start, out string root)
    {
        root = string.Empty;
        try
        {
            var info = new ProcessStartInfo("git") { WorkingDirectory = start, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("rev-parse"); info.ArgumentList.Add("--show-toplevel");
            using var process = Process.Start(info);
            if (process is null) return false;
            root = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(root);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}