using Tunner.Platform.Secrets;

namespace Tunner.Installer;

/// <summary>Resolve existing credentials or create an absent record once; retries never overwrite a record.</summary>
public static class LocalR2Setup
{
    internal static async Task<LocalR2Result> RunAsync(R2InstallationProfile profile, ILocalOpenBaoSession session,
        Func<Dictionary<string, string>> requestCredentials,
        Func<R2InstallationProfile, ISecretStore, CancellationToken, Task<R2ProbeResult>> probe,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(probe);
        R2ProbeResult? result = null;
        var bootstrapCleaned = false;
        try
        {
            await EnsureCredentialsAsync(profile, session.Secrets, requestCredentials, cancellationToken).ConfigureAwait(false);
            result = await probe(profile, session.Secrets, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
                bootstrapCleaned = true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException && result is not null)
            {
                // Preserve the object key and original failure for recovery, but never report acceptance success.
                bootstrapCleaned = false;
            }
        }
        return new LocalR2Result(result.ObjectKey, result.ContentVerified, result.CleanupVerified,
            result.FailureCode, bootstrapCleaned);
    }

    public static async Task<bool> EnsureCredentialsAsync(R2InstallationProfile profile, OpenBaoSecretStore store,
        Func<Dictionary<string, string>> requestCredentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(requestCredentials);
        var reference = profile.Validate();
        using (var existing = await store.ReadIfPresentAsync(reference, cancellationToken).ConfigureAwait(false))
        {
            if (existing is not null)
            {
                _ = existing.GetRequired(profile.AccessKeyField);
                _ = existing.GetRequired(profile.SecretKeyField);
                return false;
            }
        }
        var values = requestCredentials();
        try
        {
            using (var input = new SecretValues(values))
            {
                _ = input.GetRequired(profile.AccessKeyField);
                _ = input.GetRequired(profile.SecretKeyField);
                if (values.Count != 2) throw new InvalidOperationException("LOCAL_CREDENTIAL_FIELDS_INVALID");
            }
            await store.CreateAsync(reference, values, cancellationToken).ConfigureAwait(false);
        }
        finally { values.Clear(); }
        using var saved = await store.ReadAsync(reference, cancellationToken).ConfigureAwait(false);
        _ = saved.GetRequired(profile.AccessKeyField);
        _ = saved.GetRequired(profile.SecretKeyField);
        return true;
    }
}

internal sealed record LocalR2Result(string ObjectKey, bool ContentVerified, bool CleanupVerified,
    string? ProbeFailureCode, bool BootstrapCleanupVerified)
{
    public string? FailureCode => BootstrapCleanupVerified ? ProbeFailureCode : "BOOTSTRAP_CLEANUP_FAILED";
    public bool Passed => ContentVerified && CleanupVerified && BootstrapCleanupVerified && FailureCode is null;
}
