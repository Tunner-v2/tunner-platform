using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tunner.Governance;

/// <summary>Additive current-source pins; locked bootstrap and amendment checks stay unchanged.</summary>
internal static class SupplementalAuthorityMirrors
{
    internal static int Verify(DirectoryInfo repository, List<string> findings)
    {
        _ = Read(repository, findings, out var count);
        return count;
    }

    internal static IReadOnlyList<string> Paths(DirectoryInfo repository) => Read(repository, [], out _);

    private static List<string> Read(DirectoryInfo repository, List<string> findings, out int count)
    {
        count = 0;
        var paths = new List<string>();
        var index = Path.Combine(repository.FullName, "docs", "authority", "current-authority.json");
        if (!File.Exists(index)) return paths;
        try
        {
            using var authority = JsonDocument.Parse(File.ReadAllText(index));
            var registration = Path.Combine(repository.FullName, "docs", "authority", "current", "supplemental-registration.json");
            if (!authority.RootElement.TryGetProperty("execution_mirror", out _) && !File.Exists(registration)) return paths;
            if (!File.Exists(registration)) throw new IOException("required supplemental authority registration is missing");
            count++;
            using var registered = JsonDocument.Parse(File.ReadAllText(registration));
            var mirrors = registered.RootElement.GetProperty("supplemental_mirrors");
            if (mirrors.ValueKind != JsonValueKind.Array || mirrors.GetArrayLength() == 0) throw new JsonException("supplemental_mirrors must be a nonempty array");
            foreach (var mirror in mirrors.EnumerateArray())
            {
                var manifestPath = mirror.GetProperty("manifest_path").GetString() ?? "";
                var manifestHash = mirror.GetProperty("sha256").GetString() ?? "";
                var fullManifest = Resolve(repository, manifestPath);
                count++;
                if (!Matches(fullManifest, manifestHash))
                {
                    findings.Add("supplemental authority manifest missing or hash mismatch: " + manifestPath);
                    continue;
                }
                using var manifest = JsonDocument.Parse(File.ReadAllText(fullManifest));
                var checks = manifest.RootElement.GetProperty("checks");
                if (checks.ValueKind != JsonValueKind.Array || checks.GetArrayLength() == 0)
                    throw new JsonException("supplemental manifest requires nonempty checks");
                foreach (var check in checks.EnumerateArray())
                {
                    var logical = check.GetProperty("local_path").GetString() ?? "";
                    var expected = check.GetProperty("expected_sha256").GetString() ?? "";
                    var fullPath = Resolve(repository, logical);
                    count++;
                    if (paths.Contains(logical, StringComparer.Ordinal))
                        findings.Add("duplicate supplemental authority path: " + logical);
                    else paths.Add(logical);
                    if (!Matches(fullPath, expected))
                        findings.Add("supplemental authority artifact missing or hash mismatch: " + logical);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            findings.Add("supplemental authority registration is invalid: " + exception.Message);
        }
        return paths;
    }

    private static string Resolve(DirectoryInfo repository, string logical)
    {
        if (!logical.StartsWith("docs/authority/current/", StringComparison.Ordinal) ||
            logical.Contains((char)92) || Path.IsPathRooted(logical) ||
            logical.Split('/').Any(part => part is "" or "." or ".."))
            throw new ArgumentException("supplemental authority paths must remain under docs/authority/current");
        return Path.Combine(repository.FullName, logical.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool Matches(string path, string expected) =>
        Regex.IsMatch(expected, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant) &&
        File.Exists(path) && StringComparer.Ordinal.Equals(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), expected);
}
