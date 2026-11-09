using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Accounts;
using Tunner.Platform.Policies;
using Tunner.User.Bff;

namespace Tunner.Identity.Persistence.Tests;

internal sealed class AuthenticationTestHost : IAsyncDisposable
{
    internal const string Origin = "https://authentication.example.test";
    internal const string Password = "fixture passphrase accepted as an existing credential";
    private readonly WebApplication application;
    private readonly HttpClient client;
    private readonly X509Certificate2 certificate;
    internal TestClock Clock { get; } = new();
    internal TestAccess Access { get; } = new();
    internal TestPolicies Policies { get; } = new();
    internal IServiceProvider Services => application.Services;
    private AuthenticationTestHost(WebApplication application, HttpClient client, X509Certificate2 certificate, TestClock clock, TestAccess access, TestPolicies policies)
    { this.application = application; this.client = client; this.certificate = certificate; Clock = clock; Access = access; Policies = policies; }
    internal static async Task<AuthenticationTestHost> Start(IdentityTestDatabase database)
    {
        using var rsa = RSA.Create(2048); var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(2));
        var bytes = generated.Export(X509ContentType.Pkcs12); X509Certificate2 certificate;
        try { certificate = X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.DefaultKeySet); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        var clock = new TestClock(); var access = new TestAccess(); var policies = new TestPolicies();
        var application = TunnerAuthenticatedUserBffHost.CreateApplication([], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Bff:BrowserOrigin"] = Origin });
            builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            builder.Services.AddTunnerIdentityPersistence(database.ConnectionString);
            builder.Services.AddSingleton<TimeProvider>(clock);
            builder.Services.AddSingleton<IAuthenticationPolicySource>(policies);
            builder.Services.AddSingleton<IAccountAuthenticationAccessSource>(access);
            builder.Services.AddSingleton<IAuthenticationPresentationSource, TestPresentation>();
            builder.Services.AddSingleton(new AuthenticationPartitionKey(RandomNumberGenerator.GetBytes(32)));
        });
        await application.StartAsync();
        var address = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, presented, _, _) => presented is not null && presented.GetCertHashString(HashAlgorithmName.SHA256) == certificate.GetCertHashString(HashAlgorithmName.SHA256) };
        return new(application, new HttpClient(handler) { BaseAddress = new Uri(address) }, certificate, clock, access, policies);
    }
    internal async Task<string> SeedAsync(IdentityTestDatabase database, AccountState state = AccountState.Active, bool confirmed = true, string password = Password, bool minor = false)
    {
        await using var context = database.Context(); var id = "ACC-" + Guid.NewGuid().ToString("N");
        context.Accounts.Add(new AccountRow { AccountId = id, State = state, Version = 1, RecordedAtUtc = Clock.GetUtcNow(),
            OperationId = "OP-fixture-seed", CorrelationId = "fixture-correlation", EvidenceReference = "fixture-account",
            AgeEligibilityStatus = minor ? "INELIGIBLE" : null, AgeEvaluatedAtUtc = minor ? Clock.GetUtcNow() : null,
            AgePolicyVersion = minor ? "fixture-age" : null, AgeEvidenceReference = minor ? "fixture-age-evidence" : null });
        var user = new IdentityUserAdapter { AccountId = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), Email = "person@example.test",
            NormalizedEmail = "PERSON@EXAMPLE.TEST", EmailConfirmed = confirmed, SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N") };
        user.PasswordHash = AuthenticationPasswordWork.Hasher(Policies.Policy).HashPassword(user, password); context.Users.Add(user);
        context.VerifiedContacts.Add(new VerifiedContactRow { ContactId = Guid.NewGuid(), AccountId = id, NormalizedEmail = user.NormalizedEmail,
            State = confirmed ? ContactVerificationState.Verified : ContactVerificationState.Unverified, Version = 1, EvidenceReference = "fixture-contact" });
        await context.SaveChangesAsync(); return id;
    }
    internal async Task<HttpResponseMessage> Get(string path, string? cookies = null)
    { using var request = Request(HttpMethod.Get, path, cookies); return await client.SendAsync(request); }
    internal async Task<HttpResponseMessage> Post(string path, object body, string? cookies, string? csrf, string origin = Origin)
    {
        using var request = Request(HttpMethod.Post, path, cookies); request.Content = JsonContent.Create(body); request.Headers.TryAddWithoutValidation("Origin", origin);
        if (csrf is not null) request.Headers.TryAddWithoutValidation("X-Tunner-CSRF", csrf); return await client.SendAsync(request);
    }
    internal async Task<HttpResponseMessage> RawPost(string path, string body, string cookies, string csrf)
    {
        using var request = Request(HttpMethod.Post, path, cookies); request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Origin", Origin); request.Headers.TryAddWithoutValidation("X-Tunner-CSRF", csrf); return await client.SendAsync(request);
    }
    private static HttpRequestMessage Request(HttpMethod method, string path, string? cookies)
    {
        var request = new HttpRequestMessage(method, path); request.Headers.Host = new Uri(Origin).Host;
        if (cookies is not null) request.Headers.TryAddWithoutValidation("Cookie", cookies); return request;
    }
    internal async Task<Bootstrap> Options(string? existing = null)
    {
        using var response = await Get("/authentication/options", existing); response.EnsureSuccessStatusCode(); var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new(json.GetProperty("idempotencyKey").GetGuid(), json.GetProperty("csrfToken").GetString()!, Merge(existing, response));
    }
    internal static string Merge(string? prior, HttpResponseMessage response)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in (prior ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries)) { var pair = part.Trim().Split('=', 2); map[pair[0]] = pair[1]; }
        if (response.Headers.TryGetValues("Set-Cookie", out var cookies)) foreach (var value in cookies) { var pair = value.Split(';')[0].Split('=', 2); if (pair[1].Length == 0) map.Remove(pair[0]); else map[pair[0]] = pair[1]; }
        return string.Join("; ", map.Select(x => x.Key + "=" + x.Value));
    }
    internal static PasswordSignInCommand Command(Guid key, string password = Password, string email = "person@example.test", bool remember = false) => new()
        { IdempotencyKey = key, Email = email, Password = password, RememberMe = remember };
    public async ValueTask DisposeAsync() { client.Dispose(); await application.StopAsync(); await application.DisposeAsync(); certificate.Dispose(); }
    internal sealed record Bootstrap(Guid Key, string Csrf, string Cookies);
    internal sealed class TestPresentation : IAuthenticationPresentationSource
    {
        public AuthenticationPresentation RequirePublished(AuthenticationPolicy policy)
        {
            using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "authentication-content.proposal.json")));
            var values = source.RootElement.GetProperty("content").EnumerateObject().Select(p => new RegistrationPublishedText(p.Name, "fixture-variant", "fixture-version", "fixture-only-publication", "en", "PUBLISHED", p.Value.GetString()!));
            var uri = new Uri("https://fixture.example.test/legal"); return new("en", values, uri, uri, uri);
        }
    }
    internal sealed class TestClock : TimeProvider
    {
        private long ticks = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero).Ticks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        internal void Advance(TimeSpan span) => Interlocked.Add(ref ticks, span.Ticks);
    }
    internal sealed class TestPolicies : IAuthenticationPolicySource
    {
        // Synthetic test-only values; never an approved production policy.
        internal AuthenticationPolicy Policy { get; set; } = new("fixture-auth-v1", "fixture-processing", "fixture-retention", "fixture-password-assurance", 128, 10_000,
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5), TimeSpan.FromHours(8), TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1), 100, 20);
        internal bool Available { get; set; } = true;
        public AuthenticationPolicy RequireEffective(DateTimeOffset effectiveAt) => Available ? Policy : throw new InvalidOperationException("Fixture policy unavailable.");
    }
    internal sealed class TestAccess : IAccountAuthenticationAccessSource
    {
        internal bool Allowed { get; set; } = true;
        internal bool Stale { get; set; }
        public ValueTask<AccountAuthenticationAccessDecision> RequireAsync(AccountAuthenticationAccessRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AccountAuthenticationAccessDecision(request.AccountId, request.StateVersion + (Stale ? 1 : 0), request.Operation, Allowed,
                "fixture-access-v1", "fixture-access-evidence", new EffectivePolicyInterval(request.EffectiveAt.AddHours(-1), true, request.EffectiveAt.AddHours(1), false)));
    }
}
