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
                var primaryChecksPresent = false;
                foreach (var groupName in new[] { "checks", "amendment_checks" })
                {
                    if (!integrity.RootElement.TryGetProperty(groupName, out var checks) || checks.ValueKind != JsonValueKind.Array)
                    {
                        if (StringComparer.Ordinal.Equals(groupName, "checks"))
                        {
                            findings.Add("bootstrap authority integrity evidence does not contain a checks array");
                        }

                        continue;
                    }

                    primaryChecksPresent |= StringComparer.Ordinal.Equals(groupName, "checks");
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

                if (!primaryChecksPresent)
                {
                    findings.Add("bootstrap authority integrity evidence does not contain a checks array");
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

    public static CommandResult Status(DirectoryInfo repository)
    {
        var verification = Verify(repository);
        var activeAmendments = new List<string>();
        var effectiveAuthority = string.Empty;
        var currentAuthorityPath = Path.Combine(repository.FullName, "docs", "authority", "current-authority.json");
        try
        {
            using var authority = JsonDocument.Parse(File.ReadAllText(currentAuthorityPath));
            effectiveAuthority = authority.RootElement.TryGetProperty("effective_authority", out var effective) ? effective.GetString() ?? string.Empty : string.Empty;
            if (authority.RootElement.TryGetProperty("active_amendments", out var amendments) && amendments.ValueKind == JsonValueKind.Array)
            {
                activeAmendments.AddRange(amendments.EnumerateArray().Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null).Where(id => !string.IsNullOrWhiteSpace(id))!);
            }
        }
        catch (IOException exception)
        {
            return new CommandResult(1, "authority status", new AuthorityStatusPayload("INVALID", string.Empty, [], 0, [exception.Message]));
        }
        catch (JsonException exception)
        {
            return new CommandResult(1, "authority status", new AuthorityStatusPayload("INVALID", string.Empty, [], 0, [exception.Message]));
        }

        var verified = verification.Payload as AuthorityPayload;
        return new CommandResult(verification.ExitCode, "authority status", new AuthorityStatusPayload(verification.ExitCode == 0 ? "VERIFIED" : "INVALID", effectiveAuthority, activeAmendments.OrderBy(item => item, StringComparer.Ordinal).ToArray(), verified?.CheckedArtifacts ?? 0, verified?.Findings ?? []));
    }

    public static CommandResult Import(DirectoryInfo repository)
    {
        var verification = Verify(repository);
        var payload = verification.Payload as AuthorityPayload;
        var findings = verification.ExitCode == 0
            ? new[] { "The repository authority mirror is already imported and hash-verified. Import is idempotent and does not fetch or choose remote authority." }
            : payload?.Findings ?? ["authority verification failed"];
        return new CommandResult(verification.ExitCode, "authority import", new AuthorityImportPayload(verification.ExitCode == 0 ? "IMPORTED_MIRROR_VERIFIED" : "REJECTED", payload?.CheckedArtifacts ?? 0, findings));
    }

    public static CommandResult Diff(DirectoryInfo repository)
    {
        var verification = Verify(repository);
        var payload = verification.Payload as AuthorityPayload;
        var findings = verification.ExitCode == 0
            ? new[] { "No remote source was read. The offline mirror matches its recorded SHA-256 evidence; a canonical-source comparison requires an explicitly fetched, hash-recorded candidate and cannot be silently resolved." }
            : payload?.Findings ?? ["authority verification failed"];
        return new CommandResult(verification.ExitCode, "authority diff", new AuthorityDiffPayload(verification.ExitCode == 0 ? "MIRROR_CURRENT_OFFLINE" : "DRIFT_OR_INVALID", payload?.CheckedArtifacts ?? 0, findings));
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
public sealed record AuthorityStatusPayload(string Outcome, string EffectiveAuthority, IReadOnlyList<string> ActiveAmendments, int CheckedArtifacts, IReadOnlyList<string> Findings);
public sealed record AuthorityImportPayload(string Outcome, int CheckedArtifacts, IReadOnlyList<string> Findings);
public sealed record AuthorityDiffPayload(string Outcome, int CheckedArtifacts, IReadOnlyList<string> Findings);
