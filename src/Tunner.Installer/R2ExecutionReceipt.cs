using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tunner.Installer;

/// <summary>Non-secret execution provenance for the exact running installer artifact.</summary>
internal sealed record R2ExecutionReceipt(
    int SchemaVersion, string RunId, string BuildSourceRevision, string ToolSha256,
    DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, string EngineeringEnvironment,
    string Endpoint, string Bucket, string ObjectKey, bool ContentVerified, bool CleanupVerified,
    bool? BootstrapCleanupVerified, string? FailureCode, bool Passed)
{
    internal static R2ExecutionReceipt Create(R2InstallationProfile profile, DateTimeOffset started, DateTimeOffset completed,
        string objectKey, bool contentVerified, bool cleanupVerified, bool? bootstrapCleanupVerified, string? failureCode)
    {
        _ = profile.Validate();
        if (!profile.DedicatedNonProductionTestBucket || completed < started)
            throw new InvalidOperationException("RECEIPT_CONTEXT_INVALID");
        var assembly = typeof(R2ExecutionReceipt).Assembly;
        var information = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var match = Regex.Match(information, "[a-f0-9]{40}", RegexOptions.CultureInvariant);
        return new R2ExecutionReceipt(1, Guid.NewGuid().ToString("N"),
            match.Success ? match.Value : "UNAVAILABLE",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(),
            started.ToUniversalTime(), completed.ToUniversalTime(), profile.EnvironmentClass,
            profile.Endpoint, profile.Bucket, objectKey, contentVerified, cleanupVerified, bootstrapCleanupVerified,
            failureCode, contentVerified && cleanupVerified && bootstrapCleanupVerified is not false && failureCode is null);
    }

    internal async Task<string> SaveAsync(string profilePath, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(profilePath))!, "receipts");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, RunId + ".json");
        await using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, this, cancellationToken: cancellationToken).ConfigureAwait(false);
        return destination;
    }
}
