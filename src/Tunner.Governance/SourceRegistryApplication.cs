using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tunner.Governance;

public static class SourceRegistryApplication
{
    private const string RegistryPath = "governance/rd/source-registry.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static CommandResult Check(DirectoryInfo repository, string? scopeId, string asOfText, int? maxAgeDays)
    {
        if (!DateOnly.TryParse(asOfText, out var asOf) || maxAgeDays is null || maxAgeDays < 0)
        {
            return Rejected(scopeId, ["--as-of must be an ISO-8601 date and --max-age-days must be non-negative"]);
        }

        var authority = AuthorityApplication.Verify(repository);
        if (authority.ExitCode != 0)
        {
            return new CommandResult(1, "sources check", new SourceCheckPayload("AUTHORITY_INVALID", scopeId, asOfText, maxAgeDays, [], ((AuthorityPayload)authority.Payload).Findings));
        }

        var registryFile = Path.Combine(repository.FullName, RegistryPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(registryFile))
        {
            return Rejected(scopeId, [$"source registry is missing: {RegistryPath}"]);
        }

        try
        {
            var registry = JsonSerializer.Deserialize<SourceRegistryDocument>(File.ReadAllText(registryFile), JsonOptions);
            if (registry is null || registry.SchemaVersion != 1 || registry.Entries.Count == 0)
            {
                return Rejected(scopeId, ["source registry must be a non-empty schema_version 1 document"]);
            }

            var entries = registry.Entries.Where(entry => scopeId is null || StringComparer.Ordinal.Equals(entry.ScopeId, scopeId)).OrderBy(entry => entry.ScopeType, StringComparer.Ordinal).ThenBy(entry => entry.ScopeId, StringComparer.Ordinal).ToArray();
            if (entries.Length == 0)
            {
                return Rejected(scopeId, ["no registered source evidence exists for the requested scope"]);
            }

            var findings = new List<string>();
            var checkedEntries = new List<SourceCheckEntry>();
            foreach (var entry in entries)
            {
                CheckEntry(repository, entry, asOf, maxAgeDays.Value, checkedEntries, findings);
            }

            return new CommandResult(findings.Count == 0 ? 0 : 1, "sources check", new SourceCheckPayload(findings.Count == 0 ? "CURRENT" : "NOT_CURRENT", scopeId, asOfText, maxAgeDays, checkedEntries, findings));
        }
        catch (JsonException exception)
        {
            return Rejected(scopeId, [$"source registry is invalid JSON: {exception.Message}"]);
        }
    }

    private static void CheckEntry(DirectoryInfo repository, SourceRegistryEntry entry, DateOnly asOf, int maxAgeDays, List<SourceCheckEntry> checkedEntries, List<string> findings)
    {
        var prefix = $"{entry.ScopeType}:{entry.ScopeId}";
        if (!StringComparer.Ordinal.Equals(entry.ScopeType, "WORK_ITEM") && !StringComparer.Ordinal.Equals(entry.ScopeType, "DECISION"))
        {
            findings.Add($"{prefix}: scope_type must be WORK_ITEM or DECISION");
            return;
        }

        var scopePath = StringComparer.Ordinal.Equals(entry.ScopeType, "WORK_ITEM") ? $"governance/work-items/{entry.ScopeId}.yaml" : $"governance/decisions/{entry.ScopeId}.yaml";
        if (!File.Exists(Path.Combine(repository.FullName, scopePath.Replace('/', Path.DirectorySeparatorChar))))
        {
            findings.Add($"{prefix}: registered scope does not exist");
            return;
        }

        if (!StringComparer.Ordinal.Equals(entry.SourceAuthority, "PRIMARY"))
        {
            findings.Add($"{prefix}: source_authority must be PRIMARY");
            return;
        }

        var evidencePath = Path.Combine(repository.FullName, entry.EvidencePath.Replace('/', Path.DirectorySeparatorChar));
        if (!IsRepositoryRelative(repository, entry.EvidencePath) || !File.Exists(evidencePath))
        {
            findings.Add($"{prefix}: source evidence is missing or escapes the repository");
            return;
        }

        var actualHash = HashFile(evidencePath);
        if (!StringComparer.Ordinal.Equals(actualHash, entry.EvidenceSha256))
        {
            findings.Add($"{prefix}: source evidence hash mismatch");
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(evidencePath));
            var root = document.RootElement;
            if (!root.TryGetProperty("scope_type", out var evidenceScopeType) || !root.TryGetProperty("scope_id", out var evidenceScopeId) || !StringComparer.Ordinal.Equals(evidenceScopeType.GetString(), entry.ScopeType) || !StringComparer.Ordinal.Equals(evidenceScopeId.GetString(), entry.ScopeId))
            {
                findings.Add($"{prefix}: source evidence scope linkage is invalid");
                return;
            }

            if (!TryReadReviewedDate(root, out var reviewedDate) || !DateOnly.TryParse(entry.ReviewedOn, out var registeredDate) || reviewedDate != registeredDate)
            {
                findings.Add($"{prefix}: reviewed date is missing or does not match the registry");
                return;
            }

            var age = asOf.DayNumber - reviewedDate.DayNumber;
            if (age < 0 || age > maxAgeDays)
            {
                findings.Add($"{prefix}: source evidence age {age} day(s) is outside the requested 0..{maxAgeDays} day window");
                return;
            }

            if (!root.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() == 0 || sources.EnumerateArray().Any(source => !HasPrimaryMetadata(source)))
            {
                findings.Add($"{prefix}: source evidence must contain non-empty primary-source metadata with HTTPS URLs");
                return;
            }

            checkedEntries.Add(new SourceCheckEntry(entry.ScopeType, entry.ScopeId, entry.EvidencePath, actualHash, reviewedDate.ToString("O", CultureInfo.InvariantCulture), age));
        }
        catch (JsonException exception)
        {
            findings.Add($"{prefix}: source evidence is invalid JSON: {exception.Message}");
        }
    }

    private static bool HasPrimaryMetadata(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var url = source.TryGetProperty("url", out var urlValue) ? urlValue.GetString() : source.TryGetProperty("source", out var sourceValue) ? sourceValue.GetString() : null;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && StringComparer.OrdinalIgnoreCase.Equals(uri.Scheme, Uri.UriSchemeHttps) && (source.TryGetProperty("finding", out _) || source.TryGetProperty("selection", out _));
    }

    private static bool TryReadReviewedDate(JsonElement root, out DateOnly date)
    {
        date = default;
        var text = root.TryGetProperty("reviewed_at", out var reviewed) ? reviewed.GetString() : root.TryGetProperty("retrieved_at", out var retrieved) ? retrieved.GetString() : null;
        if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)) { date = DateOnly.FromDateTime(timestamp.DateTime); return true; }
        return false;
    }

    private static bool IsRepositoryRelative(DirectoryInfo repository, string path)
    {
        if (Path.IsPathRooted(path)) return false;
        var fullPath = Path.GetFullPath(Path.Combine(repository.FullName, path));
        var root = Path.GetFullPath(repository.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static CommandResult Rejected(string? scopeId, IReadOnlyList<string> findings)
        => new(2, "sources check", new SourceCheckPayload("REJECTED", scopeId, string.Empty, null, [], findings));

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

public sealed record SourceRegistryDocument(int SchemaVersion, IReadOnlyList<SourceRegistryEntry> Entries);
public sealed record SourceRegistryEntry(string ScopeType, string ScopeId, string EvidencePath, string EvidenceSha256, string ReviewedOn, string SourceAuthority);
public sealed record SourceCheckEntry(string ScopeType, string ScopeId, string EvidencePath, string EvidenceSha256, string ReviewedOn, int AgeDays);
public sealed record SourceCheckPayload(string Outcome, string? ScopeId, string AsOf, int? MaxAgeDays, IReadOnlyList<SourceCheckEntry> Entries, IReadOnlyList<string> Findings);