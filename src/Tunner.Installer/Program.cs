using System.Text;
using System.Text.Json;
using Tunner.Platform.Secrets;

namespace Tunner.Installer;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || args[0] is not ("setup-r2" or "test-r2"))
        {
            Console.WriteLine("Usage: Tunner.Installer setup-r2|test-r2 <operator-selected-profile-path>");
            return 2;
        }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            if (Console.IsInputRedirected) throw new InvalidOperationException("INTERACTIVE_PRIVATE_INPUT_REQUIRED");
            if (args[0] == "setup-r2")
                return await SetupAsync(args[1], cancellation.Token).ConfigureAwait(false);
            var profile = await R2InstallationProfile.LoadAsync(args[1], cancellation.Token).ConfigureAwait(false);
            if (!profile.DedicatedNonProductionTestBucket)
                throw new InvalidOperationException("DEDICATED_NON_PRODUCTION_BUCKET_REQUIRED");
            Console.WriteLine("This creates and deletes one unique test object. Confirm this is the dedicated non-production test bucket.");
            if (Prompt("Type DEDICATED NON-PRODUCTION") != "DEDICATED NON-PRODUCTION") return 2;
            using var secrets = new OpenBaoSecretStore(new Uri(profile.OpenBaoEndpoint), profile.SecretStoreReference, Secret("Short-lived scoped OpenBao token"));
            using var configured = await ConfiguredR2Storage.CreateAsync(profile, secrets, cancellation.Token).ConfigureAwait(false);
            var result = await R2ContractProbe.RunAsync(profile, configured.Storage, configured.IsAbsentAsync, cancellation.Token).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(result));
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
        var mode = Prompt("Type EXISTING to resolve credentials or CREATE to create an absent vault record");
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
