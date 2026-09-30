using System.Security.Cryptography;
using System.Text.Json;

namespace Tunner.Governance;

public static class AuthorityApplication
{
    public static CommandResult Verify(DirectoryInfo repository)
    {
        var findings = new List<string>();
        var checkedArtifacts = 0;
        var integrityPath = Path.Combine(repository.FullName, "governance", "evidence", "BOOT-P0-001-integrity.json");
        var currentAuthorityPath = Path.Combine(repository.FullName, "docs", "authority", "current-authority.json");

        if (!File.Exists(integrityPath))
        {
            findings.Add("bootstrap authority integrity evidence is missing");
        }
        else
        {
            try
            {
                using var integrity = JsonDocument.Parse(File.ReadAllText(integrityPath));
                if (!integrity.RootElement.TryGetProperty("checks", out var checks) || checks.ValueKind != JsonValueKind.Array)
                {
                    findings.Add("bootstrap authority integrity evidence does not contain a checks array");
                }
                else
                {
                    foreach (var check in checks.EnumerateArray())
                    {
                        if (!check.TryGetProperty("local_path", out var localPathNode) || localPathNode.GetString() is not { } localPath ||
                            !check.TryGetProperty("expected_sha256", out var expectedNode) || expectedNode.GetString() is not { } expectedHash)
                        {
                            continue;
                        }

                        var normalized = localPath.Replace('\\', '/');
                        if (!normalized.StartsWith("docs/authority/", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        checkedArtifacts++;
                        if (!TryResolve(repository, normalized, out var fullPath))
                        {
                            findings.Add($"authority path escapes repository: {normalized}");
                            continue;
                        }

                        if (!File.Exists(fullPath))
                        {
                            findings.Add($"authority artifact is missing: {normalized}");
                            continue;
                        }

                        if (!StringComparer.OrdinalIgnoreCase.Equals(HashFile(fullPath), expectedHash))
                        {
                            findings.Add($"authority hash mismatch: {normalized}");
                        }
                    }
                }
            }
            catch (JsonException exception)
            {
                findings.Add($"bootstrap authority integrity evidence is invalid JSON: {exception.Message}");
            }
            catch (IOException exception)
            {
                findings.Add($"bootstrap authority integrity evidence could not be read: {exception.Message}");
            }
        }

        if (checkedArtifacts == 0)
        {
            findings.Add("bootstrap authority integrity evidence contains no authority artifacts");
        }

        if (!File.Exists(currentAuthorityPath))
        {
            findings.Add("current authority summary is missing");
        }
        else
        {
            try
            {
                using var currentAuthority = JsonDocument.Parse(File.ReadAllText(currentAuthorityPath));
                if (!currentAuthority.RootElement.TryGetProperty("effective_authority", out var effectiveAuthority) || string.IsNullOrWhiteSpace(effectiveAuthority.GetString()))
                {
                    findings.Add("current authority summary is missing effective_authority");
                }
            }
            catch (JsonException exception)
            {
                findings.Add($"current authority summary is invalid JSON: {exception.Message}");
            }
            catch (IOException exception)
            {
                findings.Add($"current authority summary could not be read: {exception.Message}");
            }
        }

        var outcome = findings.Count == 0 ? "VERIFIED" : "INVALID";
        return new CommandResult(findings.Count == 0 ? 0 : 1, "authority verify", new AuthorityPayload(outcome, checkedArtifacts, findings));
    }

    private static bool TryResolve(DirectoryInfo repository, string relativePath, out string fullPath)
    {
        fullPath = Path.GetFullPath(Path.Combine(repository.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(repository.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string HashFile(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}

public sealed record AuthorityPayload(string Outcome, int CheckedArtifacts, IReadOnlyList<string> Findings);