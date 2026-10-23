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
if (args.Length != 2 || args[0] != "--local-browser-testing" ||
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development")
{
    Console.Error.WriteLine("Use the isolated local browser-testing starter. Production execution is refused.");
    return 2;
}
var directory = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
try
{
    var config = JsonSerializer.Deserialize<BrowserTestConfiguration>(await File.ReadAllTextAsync(args[1]))
        ?? throw new InvalidOperationException("Missing test configuration.");
    config.Validate();
    using var certificates = new X509Store(StoreName.My, StoreLocation.CurrentUser);
    certificates.Open(OpenFlags.ReadOnly);
    using var certificate = certificates.Certificates.Find(X509FindType.FindByThumbprint, config.CertificateThumbprint, false)
        .OfType<X509Certificate2>().Single(c => c.HasPrivateKey && c.NotAfter > DateTime.Now);
    var origin = "https://localhost:" + config.ApplicationPort;
    var connectionString = "Host=127.0.0.1;Port=" + config.PostgresPort + ";Database=tunner_browser_testing;Username=tunner;Include Error Detail=false";
    var policy = new RegistrationPolicy("browser-test-registration-v2", "PDR-0011", "test-only-processing", "test-only-retention",
        "test-only-terms", "test-only-privacy", "test-only-proof", "test-only-existing", "PASSWORD_AND_VERIFIED_EMAIL",
        15, 128, 10000, 6, TimeSpan.FromMinutes(5), 2, TimeSpan.FromSeconds(30), 2, TimeSpan.FromMinutes(1),
        1000, 1000, 1000, 1000, TimeSpan.FromMinutes(30), TimeSpan.FromHours(1));
    var metadata = new PolicyVersionCandidate<string>(policy.Version, "ACTIVE", "REGISTRATION",
        new EffectivePolicyInterval(DateTimeOffset.UtcNow.AddDays(-1), true, null, false), null, ["SYNTHETIC-LOCAL-TEST-ONLY"]);
    var age = new BrowserTestAgePolicies();
    var authenticationPolicy = new AuthenticationPolicy("browser-test-auth-v1", "test-only-processing", "test-only-retention", "test-only-password-assurance", 128, 10000,
        TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5), TimeSpan.FromHours(8), TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(1), 1000, 100);
    var authenticationMetadata = new PolicyVersionCandidate<string>(authenticationPolicy.Version, "ACTIVE", "AUTHENTICATION",
        new EffectivePolicyInterval(DateTimeOffset.UtcNow.AddDays(-1), true, null, false), null, ["SYNTHETIC-LOCAL-TEST-ONLY"]);
    var app = TunnerAuthenticatedUserBffHost.CreateApplication([], builder =>
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Bff:BrowserOrigin"] = origin });
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, config.ApplicationPort, endpoint => endpoint.UseHttps(certificate)));
        var protection = builder.Services.AddDataProtection().SetApplicationName("Tunner.IsolatedBrowserTesting")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(directory, "keys")));
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
        builder.Services.AddTunnerIdentityPersistence(connectionString);
        builder.Services.AddSingleton<IAuthenticationPolicySource>(new AuthenticationPolicyCatalog([new(authenticationMetadata, authenticationPolicy)]));
        builder.Services.AddSingleton<IAccountAuthenticationAccessSource, BrowserTestAuthenticationAccess>();
        builder.Services.AddSingleton<IAuthenticationPresentationSource>(new BrowserTestAuthenticationPresentation(origin));
        builder.Services.AddSingleton(new AuthenticationPartitionKey(RandomNumberGenerator.GetBytes(32)));
        builder.Services.AddSingleton<IRegistrationPolicySource>(new RegistrationPolicyCatalog([new(metadata, policy)]));
        builder.Services.AddSingleton<IAccountAgePolicySource>(age);
        builder.Services.AddSingleton<IAccountConsentEvidenceSource>(age);
        builder.Services.AddSingleton<IRegistrationPasswordBlocklist, BrowserTestBlocklist>();
        builder.Services.AddSingleton<IRegistrationNotificationDispatcher>(new BrowserTestMailpit(config.SmtpPort));
        builder.Services.AddSingleton<IRegistrationPresentationSource>(new BrowserTestPresentation(origin));
        builder.Services.AddSingleton(provider =>
        {
            var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("LocalBrowserTesting.NetworkKey.v1");
            var keyPath = Path.Combine(directory, "network-key.protected");
            byte[] key;
            if (File.Exists(keyPath)) key = protector.Unprotect(File.ReadAllBytes(keyPath));
            else { key = RandomNumberGenerator.GetBytes(32); File.WriteAllBytes(keyPath, protector.Protect(key)); }
            try { return new RegistrationNetworkPartitionKey(key); }
            finally { CryptographicOperations.ZeroMemory(key); }
        });
    });
    await using (app)
    {
        await using (var platform = new TunnerMigrationsDbContext(new DbContextOptionsBuilder<TunnerMigrationsDbContext>()
            .UseNpgsql(connectionString, database => database.MigrationsAssembly(typeof(TunnerMigrationsDbContextFactory).Assembly.FullName)).Options))
            await platform.Database.MigrateAsync();
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>().Database.MigrateAsync();
        app.MapGet("/browser-testing/status", () => new { mode = config.Mode, instanceId = config.InstanceId, ready = true }).AllowAnonymous();
        // Explicit local test notices; these are not published legal or support documents.
        app.MapGet("/browser-testing/{notice}", () => Results.Text("Isolated browser test fixture only. No legal document or production policy is published here.")).AllowAnonymous();
        app.MapGet("/", () => Results.Redirect("/registration")).AllowAnonymous();
        await app.RunAsync();
    }
    return 0;
}
catch (Exception error)
{
    // Do not log raw connection strings, addresses, passwords, verification codes or DOB.
    await File.WriteAllTextAsync(Path.Combine(directory, "host-error.log"), "Browser test host could not start: " + error.GetType().Name + ". Check prerequisites, local ports, migrations and certificate readiness.");
    return 1;
}

internal sealed record BrowserTestConfiguration(string Mode, string InstanceId, int ApplicationPort, int PostgresPort, int SmtpPort, string CertificateThumbprint)
{
    public void Validate()
    {
        if (Mode != "ISOLATED_LOCAL_BROWSER_TESTING" || !Guid.TryParse(InstanceId, out _) ||
            new[] { ApplicationPort, PostgresPort, SmtpPort }.Any(p => p < 1024 || p > 65535) ||
            new[] { ApplicationPort, PostgresPort, SmtpPort }.Distinct().Count() != 3 ||
            CertificateThumbprint.Length != 40 || !CertificateThumbprint.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Invalid isolated browser test configuration.");
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
internal sealed class BrowserTestAuthenticationAccess : IAccountAuthenticationAccessSource
{
    public ValueTask<AccountAuthenticationAccessDecision> RequireAsync(AccountAuthenticationAccessRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new AccountAuthenticationAccessDecision(request.AccountId, request.StateVersion, request.Operation, request.State == AccountState.Active,
            "test-only-access", "SYNTHETIC-LOCAL-TEST-ONLY", new EffectivePolicyInterval(request.EffectiveAt.AddMinutes(-1), true, request.EffectiveAt.AddMinutes(1), false)));
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
