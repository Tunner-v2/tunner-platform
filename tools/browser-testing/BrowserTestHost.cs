using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tunner.Accounts;
using Tunner.Database;
using Tunner.Database.Migrations;
using Tunner.Identity.Persistence;
using Tunner.Platform.Policies;
using Tunner.User.Bff;

internal sealed record BrowserTestHostOptions(string Origin, int Port, string ConnectionString, X509Certificate2 Certificate,
    int SmtpPort, string? PersistentDirectory, string Mode, string RunId, BrowserTestTotp? Totp = null, BrowserTestRecoveryCodes? Recovery = null, BrowserTestRecoveryCase? RecoveryCase = null,
    BrowserTestGovernedContent? GovernedContent = null);

/// <summary>The existing test executable's single BFF composition, with explicit persistent or finite ownership.</summary>
internal static class BrowserTestHost
{
    internal static WebApplication Create(BrowserTestHostOptions options)
    {
        var sessions = options.Mode == "ISOLATED_LOCAL_BROWSER_TESTING" && options.PersistentDirectory is not null;
        if (options.GovernedContent is not null && (!sessions || options.Totp is not null || options.Recovery is not null || options.RecoveryCase is not null))
            throw new InvalidOperationException("Governed browser Content cannot be composed into finite fixture modes.");
        var registration = new RegistrationPolicy("browser-test-registration-v2", "PDR-0011", "test-only-processing", "test-only-retention",
            "test-only-terms", "test-only-privacy", "test-only-proof", "test-only-existing", "PASSWORD_AND_VERIFIED_EMAIL",
            15, 128, 10000, 6, TimeSpan.FromMinutes(5), 2, TimeSpan.FromSeconds(30), 2, TimeSpan.FromMinutes(1),
            1000, 1000, 1000, 1000, TimeSpan.FromMinutes(30), TimeSpan.FromHours(1));
        var registrationMetadata = new PolicyVersionCandidate<string>(registration.Version, "ACTIVE", "REGISTRATION", Interval(), null, ["SYNTHETIC-LOCAL-TEST-ONLY"]);
        var authentication = new AuthenticationPolicy("browser-test-auth-v1", "test-only-processing", "test-only-retention", "test-only-password-assurance", 128, 10000,
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5), TimeSpan.FromHours(8), TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(1), 1000, 100);
        var authenticationMetadata = new PolicyVersionCandidate<string>(authentication.Version, "ACTIVE", "AUTHENTICATION", Interval(), null, ["SYNTHETIC-LOCAL-TEST-ONLY"]);
        var age = new BrowserTestAgePolicies();
        var app = TunnerAuthenticatedUserBffHost.CreateApplication([], builder =>
        {
            if (options.Recovery is not null || options.RecoveryCase is not null) builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Bff:BrowserOrigin"] = options.Origin });
            builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, options.Port, endpoint => endpoint.UseHttps(options.Certificate)));
            var protection = builder.Services.AddDataProtection().SetApplicationName("Tunner.IsolatedBrowserTesting");
            if (options.PersistentDirectory is { } directory)
            {
                protection.PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(directory, "keys")));
                if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
            }
            else protection.UseEphemeralDataProtectionProvider();
            builder.Services.AddTunnerIdentityPersistence(options.ConnectionString);
            builder.Services.AddSingleton<IAuthenticationPolicySource>(new AuthenticationPolicyCatalog([new(authenticationMetadata, authentication)]));
            builder.Services.AddSingleton<IAccountAuthenticationAccessSource, BrowserTestAuthenticationAccess>();
            if (options.GovernedContent is { } governed) governed.ConfigureUser(builder.Services, authentication);
            else builder.Services.AddSingleton<IAuthenticationPresentationSource>(new BrowserTestAuthenticationPresentation(options.Origin));
            builder.Services.AddScoped<IUserStepUpOperationSource, BrowserTestStepUpOperations>();
            builder.Services.AddSingleton<IStepUpPresentationSource, BrowserTestStepUpPresentation>();
            var required = options.Totp is null ? "test-only-password-assurance" : BrowserTestTotp.OperationAssurance;
            var methods = new Dictionary<string, string> { ["pwd"] = required };
            if (options.Totp is not null) methods.Add("totp", required);
            var ordinaryProof = new AssurancePolicyVersion(new(BrowserTestStepUpOperations.PolicyVersion, "ACTIVE", BrowserTestStepUpOperations.Scope,
                Interval(), null, ["SYNTHETIC-LOCAL-TEST-ONLY"]), required, TimeSpan.FromMinutes(1), methods);
            var proofPolicies = new List<AssurancePolicyVersion> { ordinaryProof };
            foreach (var version in BrowserTestPasskeys.AssurancePolicies())
            {
                var mapping = version.MethodAssurances.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                if (options.Totp is not null) mapping.Add("totp", version.RequiredAssurance);
                proofPolicies.Add(new(version.Metadata, version.RequiredAssurance, version.ProofLifetime, mapping));
            }
            if (options.Totp is not null) proofPolicies.AddRange(BrowserTestTotp.AssurancePolicies());
            if (options.Recovery is not null) proofPolicies.AddRange(options.Recovery.AssurancePolicies());
            if (sessions)
            {
                proofPolicies.Add(BrowserTestSessions.AssurancePolicy());
                proofPolicies.Add(BrowserTestSessions.BulkAssurancePolicy());
                proofPolicies.Add(BrowserTestDevices.AssurancePolicy());
            }
            builder.Services.AddSingleton<IAssurancePolicySource>(new AssurancePolicyCatalog(proofPolicies));
            BrowserTestPasskeys.Configure(builder.Services, options.Origin);
            if (sessions)
            {
                BrowserTestSessions.Configure(builder.Services);
                BrowserTestDevices.Configure(builder.Services, options.Origin);
            }
            builder.Services.AddSingleton(new AuthenticationPartitionKey(RandomNumberGenerator.GetBytes(32)));
            builder.Services.AddSingleton<IRegistrationPolicySource>(new RegistrationPolicyCatalog([new(registrationMetadata, registration)]));
            builder.Services.AddSingleton<IAccountAgePolicySource>(age);
            builder.Services.AddSingleton<IAccountConsentEvidenceSource>(age);
            builder.Services.AddSingleton<IRegistrationPasswordBlocklist, BrowserTestBlocklist>();
            builder.Services.AddSingleton<IRegistrationNotificationDispatcher>(new BrowserTestMailpit(options.SmtpPort));
            builder.Services.AddSingleton<IRegistrationPresentationSource>(new BrowserTestPresentation(options.Origin));
            builder.Services.AddSingleton(provider =>
            {
                byte[] key;
                if (options.PersistentDirectory is { } persistent)
                {
                    var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("LocalBrowserTesting.NetworkKey.v1");
                    var path = Path.Combine(persistent, "network-key.protected");
                    if (File.Exists(path)) key = protector.Unprotect(File.ReadAllBytes(path));
                    else { key = RandomNumberGenerator.GetBytes(32); File.WriteAllBytes(path, protector.Protect(key)); }
                }
                else key = RandomNumberGenerator.GetBytes(32);
                try { return new RegistrationNetworkPartitionKey(key); }
                finally { CryptographicOperations.ZeroMemory(key); }
            });
            options.Totp?.Configure(builder.Services);
            options.Recovery?.Configure(builder.Services);
            options.RecoveryCase?.Configure(builder.Services);
        });
        options.Totp?.MapResponseFaults(app);
        options.Recovery?.MapResponseFaults(app);
        return app;
    }

    internal static async Task MigrateAndMapAsync(WebApplication app, BrowserTestHostOptions options, CancellationToken cancellationToken = default)
    {
        await using (var platform = new TunnerMigrationsDbContext(new DbContextOptionsBuilder<TunnerMigrationsDbContext>()
            .UseNpgsql(options.ConnectionString, database => database.MigrationsAssembly(typeof(TunnerMigrationsDbContextFactory).Assembly.FullName)).Options))
            await platform.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
            await database.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            if (database.Database.HasPendingModelChanges()) throw new InvalidOperationException("FINITE_MIGRATIONS_INCOMPLETE");
            await database.Database.ExecuteSqlRawAsync(BrowserTestStepUpOperations.Schema, cancellationToken).ConfigureAwait(false);
        }
        if (options.GovernedContent is { } governed) await governed.InitializeAsync(cancellationToken).ConfigureAwait(false);
        BrowserTestStepUpAssets.Map(app);
        if (options.Recovery is { } recovery)
            app.MapGet("/browser-testing/status", () => new { mode = options.Mode, journeyId = recovery.JourneyId, runId = options.RunId, ready = true }).AllowAnonymous();
        else if (options.RecoveryCase is not null)
            app.MapGet("/browser-testing/status", () => new { mode = options.Mode, journeyId = BrowserTestRecoveryCase.JourneyId, runId = options.RunId, ready = true }).AllowAnonymous();
        else if (options.Totp is { } profile)
            app.MapGet("/browser-testing/status", () => new { mode = options.Mode, journeyId = profile.JourneyId, runId = options.RunId, ready = true }).AllowAnonymous();
        else app.MapGet("/browser-testing/status", () => new { mode = options.Mode, instanceId = options.RunId, governedContent = options.GovernedContent is not null, ready = true }).AllowAnonymous();
        app.MapGet("/browser-testing/{notice}", () => Results.Text("Isolated browser test fixture only. No legal document or production policy is published here.")).AllowAnonymous();
        app.MapGet("/", () => Results.Redirect("/registration")).AllowAnonymous();
    }
    private static EffectivePolicyInterval Interval() => new(DateTimeOffset.UtcNow.AddDays(-1), true, null, false);
}
