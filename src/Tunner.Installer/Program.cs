using System.Text;
using System.Text.Json;
using Tunner.Platform.Secrets;

namespace Tunner.Installer;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || args[0] is not ("setup-r2" or "test-r2" or "local-r2"))
        {
            Console.WriteLine("Usage: Tunner.Installer setup-r2|test-r2|local-r2 <operator-selected-profile-path>");
            return 2;
        }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            if (Console.IsInputRedirected) throw new InvalidOperationException("INTERACTIVE_PRIVATE_INPUT_REQUIRED");
            if (args[0] == "local-r2")
                return await LocalAsync(args[1], cancellation.Token).ConfigureAwait(false);
            if (args[0] == "setup-r2")
                return await SetupAsync(args[1], cancellation.Token).ConfigureAwait(false);
            var profile = await R2InstallationProfile.LoadAsync(args[1], cancellation.Token).ConfigureAwait(false);
            if (!profile.DedicatedNonProductionTestBucket)
                throw new InvalidOperationException("DEDICATED_NON_PRODUCTION_BUCKET_REQUIRED");
            Console.WriteLine("This creates and deletes one unique test object. Confirm this is the dedicated non-production test bucket.");
            if (Prompt("Type DEDICATED NON-PRODUCTION") != "DEDICATED NON-PRODUCTION") return 2;
            using var secrets = new OpenBaoSecretStore(new Uri(profile.OpenBaoEndpoint), profile.SecretStoreReference, Secret("Short-lived scoped OpenBao token"));
            using var configured = await ConfiguredR2Storage.CreateAsync(profile, secrets, cancellation.Token).ConfigureAwait(false);
            var started = DateTimeOffset.UtcNow;
            var result = await R2ContractProbe.RunAsync(profile, configured.Storage, configured.IsAbsentAsync, cancellation.Token).ConfigureAwait(false);
            var receipt = R2ExecutionReceipt.Create(profile, started, DateTimeOffset.UtcNow, result.ObjectKey, result.ContentVerified, result.CleanupVerified, null, result.FailureCode);
            await receipt.SaveAsync(args[1], CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(receipt));
            return result.Passed ? 0 : 1;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("INSTALLATION_CANCELLED"); return 1; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Stable output only: exceptions may contain remote response bodies, input or SDK credentials.
            Console.Error.WriteLine("INSTALLATION_FAILED: check the non-secret profile, scoped token permissions and provider availability.");
            return 1;
        }
        finally { Console.CancelKeyPress -= cancelHandler; }
    }

    private static async Task<int> SetupAsync(string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path)) throw new IOException("PROFILE_ALREADY_EXISTS");
        var profile = new R2InstallationProfile
        {
            EnvironmentClass = Prompt("Engineering environment class (LOCAL_DEVELOPMENT or EPHEMERAL_TEST)"),
            EnvironmentReference = Prompt("Existing environment reference"),
            Endpoint = Prompt("R2 S3 endpoint base URL (no bucket path)"),
            Bucket = Prompt("Existing bucket name"),
            SecretStoreReference = Prompt("Existing logical secret-store reference"),
            OpenBaoEndpoint = Prompt("OpenBao base URL"),
            CredentialResourceReference = Prompt("Exact existing/approved KV-v2 API resource reference (mount/data/path)"),
            AccessKeyField = Prompt("Exact access-key-ID field name"),
            SecretKeyField = Prompt("Exact secret-access-key field name"),
            DedicatedNonProductionTestBucket = Prompt("If confirmed dedicated to non-production tests, type DEDICATED NON-PRODUCTION") == "DEDICATED NON-PRODUCTION"
        };
        var reference = profile.Validate();
        var mode = Prompt("Type EXISTING to resolve credentials or CREATE to create an absent vault record").ToUpperInvariant();
        if (mode is not ("EXISTING" or "CREATE")) throw new InvalidOperationException("SETUP_MODE_INVALID");
        using var secrets = new OpenBaoSecretStore(new Uri(profile.OpenBaoEndpoint), profile.SecretStoreReference, Secret("Short-lived scoped OpenBao token"));
        if (mode == "CREATE")
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                values.Add(profile.AccessKeyField, Secret("Bucket-scoped R2 access key ID"));
                values.Add(profile.SecretKeyField, Secret("Bucket-scoped R2 secret access key"));
                await secrets.CreateAsync(reference, values, cancellationToken).ConfigureAwait(false);
                Console.WriteLine("Vault record created. It is retained if subsequent profile saving fails; retry with EXISTING.");
            }
            finally { values.Clear(); }
        }
        using (var values = await secrets.ReadAsync(reference, cancellationToken).ConfigureAwait(false))
        {
            _ = values.GetRequired(profile.AccessKeyField);
            _ = values.GetRequired(profile.SecretKeyField);
        }
        await profile.SaveNewAsync(path, cancellationToken).ConfigureAwait(false);
        Console.WriteLine("Profile saved; credentials resolved. Live R2 validation has not run.");
        return 0;
    }

    private static async Task<int> LocalAsync(string path, CancellationToken cancellationToken)
    {
        var profile = await R2InstallationProfile.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        if (profile.EnvironmentClass != "LOCAL_DEVELOPMENT" || !new Uri(profile.OpenBaoEndpoint).IsLoopback)
            throw new InvalidOperationException("LOCAL_LOOPBACK_BOOTSTRAP_REQUIRED");
        if (!profile.DedicatedNonProductionTestBucket)
            throw new InvalidOperationException("DEDICATED_NON_PRODUCTION_BUCKET_REQUIRED");
        Console.WriteLine($"Using bucket {profile.Bucket}; OpenBao {profile.OpenBaoEndpoint}; resource {profile.CredentialResourceReference}.");
        Console.WriteLine("This creates and deletes one unique R2 test object.");
        if (Prompt("Type DEDICATED NON-PRODUCTION") != "DEDICATED NON-PRODUCTION") return 2;
        var session = await LocalOpenBaoSession.CreateAsync(profile,
            Secret("Local OpenBao bootstrap/root token (private; used only to issue a restricted identity)"), cancellationToken).ConfigureAwait(false);
        var started = DateTimeOffset.UtcNow;
        var result = await LocalR2Setup.RunAsync(profile, session, () =>
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [profile.AccessKeyField] = Secret("Bucket-scoped R2 access key ID (first setup only)"),
                    [profile.SecretKeyField] = Secret("Bucket-scoped R2 secret access key (first setup only)")
                }, async (configuredProfile, secrets, operationToken) =>
        {
            Console.WriteLine("Credentials resolved; future runs reuse the existing record.");
            using var configured = await ConfiguredR2Storage.CreateAsync(configuredProfile, secrets, operationToken).ConfigureAwait(false);
            return await R2ContractProbe.RunAsync(configuredProfile, configured.Storage, configured.IsAbsentAsync, operationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        var receipt = R2ExecutionReceipt.Create(profile, started, DateTimeOffset.UtcNow, result.ObjectKey, result.ContentVerified, result.CleanupVerified, result.BootstrapCleanupVerified, result.FailureCode);
        await receipt.SaveAsync(path, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(receipt));
        return result.Passed ? 0 : 1;
    }

    private static string Prompt(string label)
    {
        Console.Write(label + ": ");
        return Console.ReadLine()?.Trim() ?? throw new InvalidOperationException("INPUT_REQUIRED");
    }

    private static string Secret(string label)
    {
        Console.Write(label + ": ");
        var value = new StringBuilder();
        try
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; }
                else if (!char.IsControl(key.KeyChar)) value.Append(key.KeyChar);
            }
            Console.WriteLine();
            if (value.Length == 0) throw new InvalidOperationException("PRIVATE_INPUT_REQUIRED");
            return value.ToString();
        }
        finally { value.Clear(); }
    }
}
