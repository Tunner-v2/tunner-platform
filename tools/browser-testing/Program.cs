using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Tunner.Accounts;
using Tunner.Database;
using Tunner.Database.Migrations;
using Tunner.Identity.Persistence;
using Tunner.Platform.Policies;
using Tunner.User.Bff;

// This separate tool is never composed into the production BFF. Values are explicit synthetic fixtures.
if (args.Length == 1 && args[0] == "--finite-recovery-protocol-canary")
    return await FiniteRecoveryCoordinator.RunPublicProtocolAsync();
if (args.Length == 1 && args[0] == "--sensitive-native-totp-oracle")
    return await BrowserTestTotpOracle.RunAsync();
if (args.Length == 2 && args[0] is "--finite-totp-browser-testing" or "--finite-totp-public-fault")
    return await FiniteTotpCoordinator.RunAsync(args[1], args[0] == "--finite-totp-public-fault");
if (args.Length == 2 && args[0] == "--finite-totp-resource-canary")
    return await FiniteTotpCoordinator.RunAsync(args[1], false, true);
if (args.Length == 2 && args[0] is "--finite-recovery-browser-testing" or "--finite-recovery-public-fault" or "--finite-recovery-resource-canary")
    return await FiniteRecoveryCoordinator.RunAsync(args[1], args[0] switch { "--finite-recovery-public-fault" => "public-fault", "--finite-recovery-resource-canary" => "resource-canary", _ => "browser" });
if (args.Length != 2 || args[0] != "--local-browser-testing" ||
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development")
{
    Console.Error.WriteLine("Use the isolated local browser-testing starter. Production execution is refused.");
    return 2;
}
var directory = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
var startupStage = "READING_CONFIGURATION";
async Task MarkStartupStageAsync(string stage)
{
    startupStage = stage;
    try
    {
        await File.WriteAllTextAsync(Path.Combine(directory, "host-startup-stage.json"),
            JsonSerializer.Serialize(new { stage, at = DateTimeOffset.UtcNow })).ConfigureAwait(false);
    }
    catch (IOException) { } // Optional local diagnostics must not determine application readiness.
    catch (UnauthorizedAccessException) { }
}
try
{
    await MarkStartupStageAsync("READING_CONFIGURATION");
    var config = JsonSerializer.Deserialize<BrowserTestConfiguration>(await File.ReadAllTextAsync(args[1]))
        ?? throw new InvalidOperationException("Missing test configuration.");
    config.Validate();
    await MarkStartupStageAsync("OPENING_CERTIFICATE_STORE");
    using var certificates = new X509Store(StoreName.My, StoreLocation.CurrentUser);
    certificates.Open(OpenFlags.ReadOnly);
    await MarkStartupStageAsync("SELECTING_CERTIFICATE");
    using var certificate = certificates.Certificates.Find(X509FindType.FindByThumbprint, config.CertificateThumbprint, false)
        .OfType<X509Certificate2>().Single(c => c.HasPrivateKey && c.NotAfter > DateTime.Now);
    var origin = "https://localhost:" + config.ApplicationPort;
    var connectionString = "Host=127.0.0.1;Port=" + config.PostgresPort + ";Database=tunner_browser_testing;Username=tunner;Include Error Detail=false";
    var options = new BrowserTestHostOptions(origin, config.ApplicationPort, connectionString, certificate,
        config.SmtpPort, directory, config.Mode, config.InstanceId);
    await MarkStartupStageAsync("COMPOSING_HOST");
    if (config.GovernedContent) options = options with { GovernedContent = new BrowserTestGovernedContent(options, config.SyntheticAdmin ? config.AdminPort : null) };
    var app = BrowserTestHost.Create(options);
    await using (app)
    {
        await MarkStartupStageAsync("MIGRATING_DATABASE");
        await BrowserTestHost.MigrateAndMapAsync(app, options);
        if (config.SyntheticAdmin)
        {
            await MarkStartupStageAsync("STARTING_SYNTHETIC_ADMIN_HTTPS");
            await using var admin = options.GovernedContent!.CreateAdmin(app.Services.GetRequiredService<IAuthenticationPolicySource>());
            await admin.StartAsync();
            try
            {
                await MarkStartupStageAsync("STARTING_HTTPS");
                await app.RunAsync();
            }
            finally { await admin.StopAsync(); }
        }
        else
        {
            await MarkStartupStageAsync("STARTING_HTTPS");
            await app.RunAsync();
        }
    }
    return 0;
}
catch (Exception error)
{
    // Do not log raw connection strings, addresses, passwords, verification codes or DOB.
    await File.WriteAllTextAsync(Path.Combine(directory, "host-error.log"), "Browser test host could not start at " + startupStage + ": " + error.GetType().Name + ". Check prerequisites, local ports, migrations and certificate readiness.");
    return 1;
}

internal sealed record BrowserTestConfiguration(string Mode, string InstanceId, int ApplicationPort, int PostgresPort, int SmtpPort, string CertificateThumbprint,
    bool GovernedContent = false, bool SyntheticAdmin = false, int? AdminPort = null)
{
    public void Validate()
    {
        if (Mode != "ISOLATED_LOCAL_BROWSER_TESTING" || !Guid.TryParse(InstanceId, out _) ||
            new[] { ApplicationPort, PostgresPort, SmtpPort }.Any(p => p < 1024 || p > 65535) ||
            new[] { ApplicationPort, PostgresPort, SmtpPort }.Distinct().Count() != 3 ||
            CertificateThumbprint.Length != 40 || !CertificateThumbprint.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Invalid isolated browser test configuration.");
        if (SyntheticAdmin && (!GovernedContent || AdminPort is not { } port || port is < 1024 or > 65535 ||
            new[] { ApplicationPort, PostgresPort, SmtpPort }.Contains(port)))
            throw new InvalidOperationException("Synthetic Admin requires explicit governed Content and a distinct allocated local HTTPS port.");
        if (AdminPort is { } saved && saved != 0 && (saved is < 1024 or > 65535 || new[] { ApplicationPort, PostgresPort, SmtpPort }.Contains(saved)))
            throw new InvalidOperationException("Invalid saved isolated Admin port.");
    }
}
internal sealed class BrowserTestAgePolicies : IAccountAgePolicySource, IAccountConsentEvidenceSource, IAccountJurisdictionSource
{
    public string RequireJurisdictionReference(string accountId) => "SYNTHETIC-LOCAL-TEST";
    public AccountPersonalInformationPolicy RequireEffective(string accountId, DateTimeOffset now)
    {
        var policy = new AccountPersonalInformationPolicy(new("browser-test-age-v2", RequireJurisdictionReference(accountId), "test-only-consent", 0, false, false),
            "test-only-profile-processing", "test-only-profile-retention", TimeSpan.FromDays(365), 100, 80);
        return new AccountAgePolicyCatalog(this, [new(new(policy.Age.Version, "ACTIVE", policy.Age.JurisdictionReference,
            new EffectivePolicyInterval(now.AddDays(-1), true, null, false), null, ["SYNTHETIC-LOCAL-TEST-ONLY"]), policy)]).RequireEffective(accountId, now);
    }
    public Task<IReadOnlyList<AccountConsentEvidence>> ReadAsync(string accountId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AccountConsentEvidence>>(Array.Empty<AccountConsentEvidence>());
}
internal sealed class BrowserTestBlocklist : IRegistrationPasswordBlocklist
{
    public ValueTask<bool> IsBlockedAsync(string password, CancellationToken cancellationToken) =>
        ValueTask.FromResult(password.Contains("password", StringComparison.OrdinalIgnoreCase));
}
internal sealed class BrowserTestPresentation(string origin) : IRegistrationPresentationSource
{
    public RegistrationPresentation RequirePublished(RegistrationPolicy policy)
    {
        using var proposal = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "browser-test-content.json")));
        var content = proposal.RootElement.GetProperty("content").EnumerateObject().Select(p =>
            new RegistrationPublishedText(p.Name, "synthetic-test-variant", "synthetic-test-version", "LOCAL-TEST-NOT-A-PUBLICATION", "en", "PUBLISHED", p.Value.GetString()!));
        return new("en", content, new(origin + "/browser-testing/terms"), new(origin + "/browser-testing/privacy"), new(origin + "/browser-testing/support"));
    }
}
internal sealed class BrowserTestMailpit(int smtpPort) : IRegistrationNotificationDispatcher, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<Guid> delivered = [];
    public async Task QueueAsync(RegistrationNotification notification, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (delivered.Contains(notification.DeliveryId)) return;
            using var message = new MailMessage("tunner@browser-testing.test", notification.Address)
            { Subject = "Tunner local browser test", Body = notification.Code is null ? "This test address already has an account." : "Your local test verification code is: " + notification.Code };
            message.Headers.Add("X-Tunner-Test-Delivery", notification.DeliveryId.ToString("N"));
            using var client = new SmtpClient("127.0.0.1", smtpPort) { DeliveryMethod = SmtpDeliveryMethod.Network, UseDefaultCredentials = false };
            await client.SendMailAsync(message, cancellationToken);
            delivered.Add(notification.DeliveryId);
        }
        catch (SmtpException) { throw new RegistrationNotificationUnavailableException(); }
        finally { gate.Release(); }
    }
    public void Dispose() => gate.Dispose();
}

// Synthetic Account access and content providers are confined to this explicitly Development-only executable.
internal sealed class BrowserTestAuthenticationAccess(IEnumerable<BrowserTestRecoveryCodes> recoveryProfiles) : IAccountAuthenticationAccessSource
{
    public ValueTask<AccountAuthenticationAccessDecision> RequireAsync(AccountAuthenticationAccessRequest request, CancellationToken cancellationToken)
    {
        recoveryProfiles.SingleOrDefault()?.BeforeAccessDecision(request);
        return ValueTask.FromResult(new AccountAuthenticationAccessDecision(request.AccountId, request.StateVersion, request.Operation, request.State == AccountState.Active,
            "test-only-access", "SYNTHETIC-LOCAL-TEST-ONLY", new EffectivePolicyInterval(request.EffectiveAt.AddMinutes(-1), true, request.EffectiveAt.AddMinutes(1), false)));
    }
}
internal sealed class BrowserTestAuthenticationPresentation(string origin) : IAuthenticationPresentationSource
{
    public AuthenticationPresentation RequirePublished(AuthenticationPolicy policy)
    {
        using var proposal = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "browser-test-authentication-content.json")));
        var values = proposal.RootElement.GetProperty("content").EnumerateObject().Select(p => new RegistrationPublishedText(p.Name,
            "synthetic-test-variant", "synthetic-test-version", "LOCAL-TEST-NOT-A-PUBLICATION", "en", "PUBLISHED", p.Value.GetString()!));
        return new("en", values, new(origin + "/browser-testing/terms"), new(origin + "/browser-testing/privacy"), new(origin + "/browser-testing/support"));
    }
}
