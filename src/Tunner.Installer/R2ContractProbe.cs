using System.Security.Cryptography;
using Tunner.Platform.ObjectStorage;

namespace Tunner.Installer;

public sealed record R2ProbeResult(string ObjectKey, bool ContentVerified, bool CleanupVerified, string? FailureCode)
{
    public bool Passed => ContentVerified && CleanupVerified && FailureCode is null;
}

/// <summary>Tests only a new random object in an explicitly attested dedicated non-production bucket.</summary>
public static class R2ContractProbe
{
    public static async Task<R2ProbeResult> RunAsync(R2InstallationProfile profile, IObjectStorage storage,
        Func<ObjectStorageAddress, CancellationToken, Task<bool>> isAbsent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(isAbsent);
        profile.Validate();
        if (!profile.DedicatedNonProductionTestBucket)
            throw new InvalidOperationException("DEDICATED_NON_PRODUCTION_BUCKET_REQUIRED");
        cancellationToken.ThrowIfCancellationRequested();
        var key = "tunner-contract-tests/" + Guid.NewGuid().ToString("N");
        var address = new ObjectStorageAddress(profile.Bucket, key);
        var payload = RandomNumberGenerator.GetBytes(256);
        bool verified = false, cleaned = false;
        string? failure = null;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            using var content = new MemoryStream(payload, writable: false);
            await storage.PutAsync(new ObjectStorageWriteRequest(address, content), deadline.Token).ConfigureAwait(false);
            using var read = await storage.OpenReadAsync(address, deadline.Token).ConfigureAwait(false);
            var actual = new byte[payload.Length];
            await read.Content.ReadExactlyAsync(actual, deadline.Token).ConfigureAwait(false);
            var extra = new byte[1];
            verified = await read.Content.ReadAsync(extra, deadline.Token).ConfigureAwait(false) == 0 &&
                CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), SHA256.HashData(actual));
            if (!verified) failure = "CONTENT_MISMATCH";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Provider exceptions can contain request details. Never return or log them.
            failure = exception is OperationCanceledException ? "PROBE_CANCELLED_OR_TIMED_OUT" : "PROBE_FAILED";
        }
        finally
        {
            // A PUT timeout can still have created the object. Cleanup has its own bounded token.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try
            {
                await storage.DeleteAsync(address, cleanup.Token).ConfigureAwait(false);
                cleaned = await isAbsent(address, cleanup.Token).ConfigureAwait(false);
                if (!cleaned) failure = "CLEANUP_NOT_VERIFIED";
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { failure = "CLEANUP_FAILED"; }
        }
        return new R2ProbeResult(key, verified, cleaned, failure);
    }
}
