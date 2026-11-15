using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Tunner.Database.Outbox;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AuthenticationTestHost;

namespace Tunner.Identity.Persistence.Tests;

/// <summary>Real native server processing over the existing qualified HTTPS/PostgreSQL facilities.</summary>
public sealed class ProductAuthorizationProtocolTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    private const string AuthorizationPath = "/protocol/product/authorize";
    private const string TokenPath = "/protocol/product/token";
    private const string IntrospectionPath = "/protocol/product/introspect";
    private const string RevocationPath = "/protocol/product/revoke";
    private const string Product = "PRD-SYNTHETIC-PROTOCOL-ONLY";
    private const string EnvironmentName = "SANDBOX";
    private const string Redirect = "https://product.protocol.example.test/callback";
    private const string Resource = "https://product.protocol.example.test/api";
    private const string OtherResource = "https://other.protocol.example.test/api";
    private static readonly ImmutableArray<string> Scopes = ["openid", "product.read"];
    private static readonly ImmutableArray<string> Resources = [Resource];

    private sealed class Owners(IdentityTestDatabase database) : IProductAuthorizationPolicySource,
        IProductAuthorizationRegistrySource, IProductAuthorizationRelationshipSource, IProductAuthorizationLifecycleSource
    {
        internal string ClientId { get; } = "synthetic-product-client-" + Guid.NewGuid().ToString("N");
        internal string ClientCredential { get; } = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        internal string NativeApplicationId { get; set; } = string.Empty;
        internal string PolicyVersion { get; set; } = "fixture-product-policy-v1";
        internal long RegistryVersion { get; set; } = 1;
        internal bool PolicyAvailable { get; set; } = true;
        internal bool RegistryAvailable { get; set; } = true;
        internal bool RelationshipAvailable { get; set; } = true;
        internal bool LifecycleEnabled { get; init; }
        internal ImmutableArray<string> GrantedScopes => LifecycleEnabled ? ["offline_access", "openid", "product.read"] : Scopes;
        internal DateTimeOffset? IssuancePolicyExpiresAt { get; set; }
        internal ProductAuthorizationArtifactEffect RevocationEffect { get; set; } = ProductAuthorizationArtifactEffect.PresentedToken;
        internal bool DenyLifecycleArtifact { get; set; }
        internal int LifecycleDecisions { get; private set; }
        internal Func<CancellationToken, Task>? BeforeTokenOutput { get; set; }
        internal bool NativeArtifactsPrepared { get; private set; }
        internal void ObservePreparedArtifacts(OpenIddictResponse response) => NativeArtifactsPrepared =
            !string.IsNullOrWhiteSpace(response.AccessToken) && !string.IsNullOrWhiteSpace(response.IdToken);
        internal bool ObserveAuthorization { get; set; }
        internal bool NativeSeen { get; private set; }
        internal bool RawAuthorizationPath { get; private set; }
        internal bool AuthorizationPathMatched { get; private set; }
        internal bool EmptyPathBase { get; private set; }
        internal bool GetMethod { get; private set; }
        internal string NativeError { get; set; } = "unavailable";
        internal string NativeErrorIdentifier { get; set; } = "unavailable";
        internal void ObserveRequest(HttpRequest? request)
        {
            if (!ObserveAuthorization) return;
            NativeSeen = true;
            var raw = request?.HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget;
            var queryStart = raw?.IndexOf('?') ?? -1;
            RawAuthorizationPath = raw is not null && raw.AsSpan(0, queryStart < 0 ? raw.Length : queryStart).SequenceEqual(AuthorizationPath);
            AuthorizationPathMatched = request?.Path == AuthorizationPath;
            EmptyPathBase = request is not null && !request.PathBase.HasValue;
            GetMethod = request is not null && HttpMethods.IsGet(request.Method);
        }
        private readonly EffectivePolicyInterval interval = new(DateTimeOffset.UtcNow.AddDays(-1), true, DateTimeOffset.UtcNow.AddHours(4), false);
        private const string Evidence = "SYNTHETIC-LOCAL-PROTOCOL-TEST-ONLY";
        public ProductAuthorizationPolicy RequireCurrent(string origin, DateTimeOffset at) => !PolicyAvailable
            ? throw new InvalidOperationException("Synthetic Product policy owner unavailable.") : new(
            new(PolicyVersion, "ACTIVE", Origin, IssuancePolicyExpiresAt is { } expires
                ? new EffectivePolicyInterval(interval.From, true, expires, false) : interval, null, [Evidence]), Origin, Evidence,
            "fixture-product-processing", "fixture-product-retention", "RESTRICTED_SECURITY", ["pwd"],
            ["fixture-password-assurance"], TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5), LifecycleEnabled, LifecycleEnabled ? TimeSpan.FromHours(1) : null, 256);
        public ValueTask<ProductAuthorizationRegistration> RequireCurrentAsync(ProductAuthorizationRequest request, DateTimeOffset at, CancellationToken ct)
        {
            if (!RegistryAvailable) throw new InvalidOperationException("Synthetic Registry owner unavailable.");
            if (NativeApplicationId.Length == 0) throw new InvalidOperationException("The exact synthetic native application must be seeded first.");
            // Only the digest binds caller input. Every authorized owner fact is the fixed fixture snapshot.
            return ValueTask.FromResult(new ProductAuthorizationRegistration(request.Digest, Product, RegistryVersion, "ACTIVE", EnvironmentName,
                1, "ACTIVE", ClientId, 1, "ACTIVE", NativeApplicationId, OpenIddictConstants.ClientTypes.Confidential, Redirect,
                GrantedScopes, Resources, "fixture-product-registry-v1", Evidence, true, interval));
        }
        public ValueTask<ProductAuthorizationLifecycleDecision> RequireCurrentAsync(ProductAuthorizationLifecycleFacts facts, DateTimeOffset at, CancellationToken ct)
        {
            LifecycleDecisions++;
            var exactOwner = LifecycleEnabled && facts.ProductId == Product && facts.Environment == EnvironmentName &&
                facts.Origin == Origin && facts.OriginalClientId == ClientId && facts.RequestingClientId == ClientId &&
                facts.RequestingNativeApplicationId == NativeApplicationId && facts.InitialNativeState.NativeApplicationId == NativeApplicationId &&
                facts.OriginalRegistration.NativeApplicationId == NativeApplicationId && facts.OriginalRegistration.ProductVersion == 1 &&
                facts.OriginalRegistration.EnvironmentVersion == 1 && facts.OriginalRegistration.ClientVersion == 1 &&
                facts.InitialNativeState.Subject == facts.OriginalRelationship.Subject && facts.OriginalRelationship.Version == 1 &&
                facts.OriginalScopes.SequenceEqual(GrantedScopes, StringComparer.Ordinal) && facts.OriginalResources.SequenceEqual(Resources, StringComparer.Ordinal) &&
                facts.RequestedScopes.All(scope => GrantedScopes.Contains(scope, StringComparer.Ordinal)) &&
                facts.RequestedResources.SequenceEqual(Resources, StringComparer.Ordinal);
            var external = facts.External;
            var currentOwner = external.Registration.ProductId == Product && external.Registration.ProductVersion == 1 &&
                external.Registration.Environment == EnvironmentName && external.Registration.EnvironmentVersion == 1 &&
                external.Registration.ClientId == ClientId && external.Registration.ClientVersion == 1 &&
                external.Registration.NativeApplicationId == NativeApplicationId && external.Registration.ProductState == "ACTIVE" &&
                external.Registration.EnvironmentState == "ACTIVE" && external.Registration.ClientState == "ACTIVE" &&
                external.Relationship.Active && external.Relationship.IndependentlyCommitted && external.Relationship.Version == 1 &&
                external.Relationship.Subject == Subject(facts.AccountId);
            var browserCurrent = external.Account.Exists && external.Account.State == "Active" && external.Account.Version == facts.OriginalAccountVersion &&
                external.Account.OperationAccess is { Allowed: true } && external.SourceSession is
                { State: "ACTIVE", BindingMatches: true, Expired: false, SecurityStampCurrent: true, VerifiedContactCurrent: true, MethodCurrent: true } session &&
                session.SessionId == facts.SourceSessionId && session.AccountId == facts.AccountId && session.Origin == Origin;
            var revocation = facts.Operation == ProductAuthorizationLifecycleOperation.Revocation;
            var allowed = exactOwner && currentOwner && (revocation || browserCurrent);
            var active = allowed && !revocation && !DenyLifecycleArtifact && facts.InitialNativeState.AuthorizationStatus == OpenIddictConstants.Statuses.Valid &&
                facts.InitialNativeState.TokenStatus == OpenIddictConstants.Statuses.Valid && facts.InitialNativeState.ExpiresAt > at;
            var refresh = facts.Operation == ProductAuthorizationLifecycleOperation.Refresh && active;
            return ValueTask.FromResult(new ProductAuthorizationLifecycleDecision(facts.Digest, "fixture-lifecycle-policy-v1", Evidence,
                "fixture-lifecycle-processing", "fixture-lifecycle-retention", "RESTRICTED_SECURITY", interval, allowed, active,
                refresh, refresh ? facts.ObservedAt.AddMinutes(5) : null,
                refresh && facts.RequestedScopes.Contains("openid", StringComparer.Ordinal) ? facts.ObservedAt.AddMinutes(5) : null,
                refresh ? facts.InitialNativeState.ExpiresAt : null, facts.Operation == ProductAuthorizationLifecycleOperation.Introspection && active ? ["sub"] : [],
                (revocation || DenyLifecycleArtifact) && allowed ? RevocationEffect : ProductAuthorizationArtifactEffect.None));
        }
        internal static string Subject(string accountId) => "fixture-product-subject-" + AuthenticationIdentity.Digest(accountId + "|" + Product)[..24];
        public async ValueTask<ProductRelationshipDecision> EnsureActiveAsync(ProductRelationshipRequest request, DateTimeOffset at, CancellationToken ct)
        {
            RequireFixedOwner(request);
            // Separate connection, no ambient/native Identity transaction and no Account foreign-key lock.
            // This is a durable synthetic owner acknowledgement, not a claimed rollback/enlistment.
            await using var owner = database.Context();
            if (owner.Database.CurrentTransaction is not null) throw new InvalidOperationException("The synthetic Relationship owner must commit independently.");
            var subject = Subject(request.AccountId); var relationship = "fixture-relationship-" + AuthenticationIdentity.Digest(request.AccountId + "|" + Product)[..24];
            await owner.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO public.product_authorization_relationship_fixture(request_digest,relationship_id,subject,version,committed_at)
                VALUES({request.Digest},{relationship},{subject},1,{at}) ON CONFLICT(request_digest) DO NOTHING
                """, ct);
            return await RequireCurrentAsync(request, at, ct);
        }
        public async ValueTask<ProductRelationshipDecision> RequireCurrentAsync(ProductRelationshipRequest request, DateTimeOffset at, CancellationToken ct)
        {
            if (!RelationshipAvailable) throw new InvalidOperationException("Synthetic Relationship owner unavailable.");
            RequireFixedOwner(request); await using var owner = database.Context();
            var subject = await owner.Database.SqlQuery<string>($"""
                SELECT subject AS "Value" FROM public.product_authorization_relationship_fixture WHERE request_digest={request.Digest}
                """).SingleOrDefaultAsync(ct);
            if (subject is null || subject != Subject(request.AccountId)) throw new InvalidOperationException("An independently committed qualified Relationship is required.");
            var version = await owner.Database.SqlQuery<long>($"""
                SELECT version AS "Value" FROM public.product_authorization_relationship_fixture WHERE request_digest={request.Digest}
                """).SingleAsync(ct);
            return new(request.Digest, "fixture-relationship-" + AuthenticationIdentity.Digest(request.AccountId + "|" + Product)[..24], version,
                true, true, "fixture-relationship-policy-v1", Evidence, subject, "fixture-subject-mapping-v1", "fixture-subject-mapping-evidence", interval);
        }
        internal async Task ChangeRelationshipAsync(CancellationToken ct = default)
        {
            await using var owner = database.Context();
            Assert.Null(owner.Database.CurrentTransaction);
            Assert.Equal(1, await owner.Database.ExecuteSqlRawAsync(
                "UPDATE public.product_authorization_relationship_fixture SET version=version+1", ct));
        }
        private void RequireFixedOwner(ProductRelationshipRequest request)
        {
            if (request.ProductId != Product || request.Environment != EnvironmentName || request.ClientId != ClientId ||
                request.ProductVersion != 1 || request.EnvironmentVersion != 1 || request.ClientVersion != 1)
                throw new InvalidOperationException("Only the independently qualified synthetic Product is admitted.");
        }
    }
    private sealed class Scenario(IdentityTestDatabase database, AuthenticationTestHost host, Owners owners, RSA signing, RSA encryption,
        RsaSecurityKey signingKey, RsaSecurityKey encryptionKey, string accountId, string cookies) : IAsyncDisposable
    {
        internal IdentityTestDatabase Database { get; } = database;
        internal AuthenticationTestHost Host { get; } = host;
        internal Owners Owners { get; } = owners;
        internal string AccountId { get; } = accountId;
        internal string Cookies { get; } = cookies;
        internal RsaSecurityKey SigningKey { get; } = signingKey;
        internal RsaSecurityKey EncryptionKey { get; } = encryptionKey;
        public async ValueTask DisposeAsync() { await Host.DisposeAsync(); signing.Dispose(); encryption.Dispose(); await Database.DisposeAsync(); }
        public override string ToString() => nameof(Scenario);
    }
    private async Task<Scenario> Create(bool nativeResourcePermission = true, bool broaderNativeQualification = false, bool lifecycle = false)
    {
        var database = await fixture.CreateDatabaseAsync();
        await using (var setup = database.Context())
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TABLE public.product_authorization_relationship_fixture (
                    request_digest text PRIMARY KEY, relationship_id text NOT NULL, subject text NOT NULL,
                    version bigint NOT NULL CHECK(version>0), committed_at timestamp with time zone NOT NULL)
                """);
        var owners = new Owners(database) { LifecycleEnabled = lifecycle }; var signing = RSA.Create(2048); var encryption = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(signing) { KeyId = "fixture-distinct-signing" };
        var encryptionKey = new RsaSecurityKey(encryption) { KeyId = "fixture-distinct-encryption" };
        var host = await Start(database, services =>
        {
            // Keep native artifacts out of incidental framework request/response logs.
            services.AddLogging(logging => logging.ClearProviders());
            services.AddSingleton<IProductAuthorizationPolicySource>(owners);
            services.AddSingleton<IProductAuthorizationRegistrySource>(owners);
            services.AddSingleton<IProductAuthorizationRelationshipSource>(owners);
            if (lifecycle) services.AddSingleton<IProductAuthorizationLifecycleSource>(owners);
            var nativeScopes = broaderNativeQualification ? ImmutableArray.Create("openid", "product.other", "product.read") : owners.GrantedScopes;
            var nativeResources = broaderNativeQualification ? ImmutableArray.Create(OtherResource, Resource).Sort(StringComparer.Ordinal) : Resources;
            services.AddTunnerProductAuthorization(new(Origin, AuthorizationPath, TokenPath, signingKey, encryptionKey, nativeScopes, nativeResources, 32768, 16, 64, 16384,
                lifecycle ? new(IntrospectionPath, RevocationPath, RotateRefreshTokens: true, SlidingRefreshExpiration: false, RefreshReuseLeeway: TimeSpan.Zero) : null));
            services.AddOpenIddict().AddServer(options =>
            {
                options.AddEventHandler<OpenIddictServerEvents.ProcessRequestContext>(builder => builder
                    .UseInlineHandler(context =>
                    {
                        owners.ObserveRequest(context.Transaction.GetHttpRequest());
                        return ValueTask.CompletedTask;
                    }).SetOrder(int.MinValue + 50_000));
                options.AddEventHandler<OpenIddictServerEvents.ProcessErrorContext>(builder => builder
                    .UseInlineHandler(context =>
                    {
                        if (owners.ObserveAuthorization)
                        {
                            owners.NativeError = SafeOAuthError(context.Error);
                            owners.NativeErrorIdentifier = SafeOpenIddictIdentifier(context.ErrorUri);
                        }
                        return ValueTask.CompletedTask;
                    }).SetOrder(int.MinValue + 50_000));
                options.AddEventHandler<OpenIddictServerEvents.ApplyTokenResponseContext>(builder => builder
                    .UseInlineHandler(async context =>
                    {
                        if (owners.BeforeTokenOutput is { } beforeOutput)
                        {
                            owners.ObservePreparedArtifacts(context.Response);
                            await beforeOutput(context.CancellationToken);
                        }
                    }).SetOrder(int.MinValue + 99_999));
            });
        });
        host.Clock.SynchronizeToSystem();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = owners.ClientId, ClientSecret = owners.ClientCredential, ClientType = OpenIddictConstants.ClientTypes.Confidential,
                ConsentType = OpenIddictConstants.ConsentTypes.Explicit
            };
            descriptor.RedirectUris.Add(new Uri(Redirect));
            descriptor.Permissions.UnionWith([OpenIddictConstants.Permissions.Endpoints.Authorization, OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode, OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Prefixes.Scope + "product.read"]);
            if (nativeResourcePermission) descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Resource + Resource);
            if (lifecycle) descriptor.Permissions.UnionWith([OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.Endpoints.Introspection, OpenIddictConstants.Permissions.Endpoints.Revocation]);
            if (broaderNativeQualification)
            {
                descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + "product.other");
                descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Resource + OtherResource);
            }
            descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
            var application = await manager.CreateAsync(descriptor);
            owners.NativeApplicationId = await manager.GetIdAsync(application) ?? throw new InvalidOperationException("The synthetic native client identity is required.");
        }
        var account = await host.SeedAsync(database); var bootstrap = await host.Options();
        using var signed = await host.Post("/authentication/sign-in", Command(bootstrap.Key), bootstrap.Cookies, bootstrap.Csrf);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode); var cookies = Merge(bootstrap.Cookies, signed);
        using var current = await host.Get("/authentication/session", cookies); Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        return new(database, host, owners, signing, encryption, signingKey, encryptionKey, account, cookies);
    }
    private sealed record NativeCode(string Value, string Verifier, string Nonce)
    { public override string ToString() => nameof(NativeCode); }
    private static async Task<NativeCode> Authorize(Scenario test)
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var state = Guid.NewGuid().ToString("N"); var nonce = Guid.NewGuid().ToString("N");
        using var response = await RequestAuthorization(test, verifier, state, nonce);
        if (response.StatusCode != HttpStatusCode.OK) Assert.Fail(await NativeErrorDiagnostic(response, test.Owners));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var form = ReadNativeForm(await response.Content.ReadAsStringAsync()); Assert.Equal(Redirect, form.Action);
        Assert.False(form.Values.ContainsKey("access_token") || form.Values.ContainsKey("id_token") || form.Values.ContainsKey("refresh_token"));
        Assert.True(form.Values.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code), "The native form_post response must contain an authorization code.");
        Assert.Equal(state, form.Values.GetValueOrDefault("state")); return new(code!, verifier, nonce);
    }
    private static async Task<HttpResponseMessage> RequestAuthorization(Scenario test, string? verifier = null,
        string? state = null, string? nonce = null, IReadOnlyDictionary<string, string>? overrides = null)
    {
        verifier ??= Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var values = new Dictionary<string, string>
        {
            ["client_id"] = test.Owners.ClientId, ["redirect_uri"] = Redirect, ["response_type"] = "code", ["response_mode"] = "form_post",
            ["scope"] = string.Join(' ', test.Owners.GrantedScopes), ["resource"] = Resource, ["product_id"] = Product, ["environment"] = EnvironmentName,
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["state"] = state ?? Guid.NewGuid().ToString("N"),
            ["nonce"] = nonce ?? Guid.NewGuid().ToString("N")
        };
        if (overrides is not null) foreach (var pair in overrides) values[pair.Key] = pair.Value;
        using var query = new FormUrlEncodedContent(values);
        HttpResponseMessage received;
        test.Owners.ObserveAuthorization = true;
        try { received = await test.Host.Get(AuthorizationPath + "?" + await query.ReadAsStringAsync(), test.Cookies); }
        finally { test.Owners.ObserveAuthorization = false; }
        return received;
    }
    private static async Task<HttpResponseMessage> Redeem(Scenario test, NativeCode original, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var values = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = original.Value, ["code_verifier"] = original.Verifier,
            ["client_id"] = test.Owners.ClientId, ["client_secret"] = test.Owners.ClientCredential, ["redirect_uri"] = Redirect
        };
        if (overrides is not null) foreach (var pair in overrides) values[pair.Key] = pair.Value;
        using var form = new FormUrlEncodedContent(values);
        return await test.Host.RawPost(TokenPath, await form.ReadAsStringAsync(), string.Empty, string.Empty, "application/x-www-form-urlencoded");
    }

    private static string SafeOAuthError(string? error) => error is
        "invalid_request" or "invalid_client" or "invalid_grant" or "unauthorized_client" or "unsupported_grant_type" or
        "invalid_scope" or "invalid_target" or "unsupported_response_type" or "access_denied" or "server_error" or "temporarily_unavailable" or
        "login_required" or "consent_required" or "interaction_required" ? error : "unrecognized";

    private static string SafeOpenIddictIdentifier(string? uriText)
    {
        if (uriText is not { Length: <= 512 } || !Uri.TryCreate(uriText, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.Host.Equals("documentation.openiddict.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith("/errors/", StringComparison.Ordinal)) return "unavailable";
        var last = uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf('/') + 1)..];
        return last.Length == 6 && last.StartsWith("ID", StringComparison.Ordinal) && last.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0
            ? last : "unavailable";
    }

    private static async Task<string> NativeErrorDiagnostic(HttpResponseMessage response, Owners owners)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var safeMediaType = mediaType is "application/json" or "application/problem+json" or "text/plain" or "text/html" ? mediaType : "unrecognized";
        var prefix = "Native authorization HTTP " + (int)response.StatusCode + "; ContentType=" + safeMediaType +
            "; NativeSeen=" + owners.NativeSeen + "; RawAuthorizationPath=" + owners.RawAuthorizationPath +
            "; AuthorizationPathMatched=" + owners.AuthorizationPathMatched + "; EmptyPathBase=" + owners.EmptyPathBase +
            "; GET=" + owners.GetMethod + "; NativeOAuth=" + owners.NativeError + "; NativeOpenIddict=" + owners.NativeErrorIdentifier + "; ";
        var bytes = new byte[8193];
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(); var count = 0;
            while (count < bytes.Length)
            { var read = await stream.ReadAsync(bytes.AsMemory(count)); if (read == 0) break; count += read; }
            if (count > 8192) return prefix + "bounded OAuth metadata unavailable";
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            if (mediaType == "text/plain")
            {
                foreach (var line in Encoding.UTF8.GetString(bytes, 0, count).Split('\n'))
                {
                    var colon = line.IndexOf(':'); if (colon < 0) continue;
                    var key = line[..colon].Trim();
                    if (key is "error" or "error_description" or "error_uri")
                        if (!fields.TryAdd(key, line[(colon + 1)..].Trim())) return prefix + "ambiguous OAuth metadata unavailable";
                }
            }
            else
            {
                using var document = JsonDocument.Parse(bytes.AsMemory(0, count)); var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return prefix + "OAuth metadata unavailable";
                foreach (var property in root.EnumerateObject())
                    if (property.Name is "error" or "error_description" or "error_uri" && property.Value.ValueKind == JsonValueKind.String)
                        if (!fields.TryAdd(property.Name, property.Value.GetString()!)) return prefix + "ambiguous OAuth metadata unavailable";
            }
            var description = fields.GetValueOrDefault("error_description");
            string[] labels = ["client_id", "redirect_uri", "response_type", "response_mode", "code_challenge", "code_challenge_method", "scope", "resource", "product_id", "environment", "nonce", "state"];
            var hints = labels.Select(label => label + "=" + (description?.Contains("'" + label + "'", StringComparison.Ordinal) == true));
            return prefix + "OAuth=" + SafeOAuthError(fields.GetValueOrDefault("error")) + "; OpenIddict=" +
                SafeOpenIddictIdentifier(fields.GetValueOrDefault("error_uri")) + "; parameter labels: " + string.Join(", ", hints);
        }
        catch (Exception error) when (error is JsonException or IOException or HttpRequestException or InvalidOperationException)
        { return prefix + "OAuth metadata unavailable"; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    [Fact]
    public async Task NativeHttpsFormPostCodeRedeemsOnceWithCausalNativeOutboxEvidence()
    {
        await using var test = await Create(); var code = await Authorize(test);
        await using (var issued = test.Database.Context())
        {
            Assert.Equal(1, await issued.Set<OutboxMessage>().CountAsync(x => x.EventType == "ProductAuthorizationGranted"));
            Assert.Equal(0, await issued.Set<OutboxMessage>().CountAsync(x => x.EventType == "ProductAuthorizationRedeemed"));
            Assert.Equal(1, await issued.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM public.product_authorization_relationship_fixture").SingleAsync());
        }
        using var response = await Redeem(test, code); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bearer", tokens.GetProperty("token_type").GetString()); Assert.True(tokens.GetProperty("expires_in").GetInt64() is > 0 and <= 300);
        Assert.False(tokens.TryGetProperty("refresh_token", out _));
        var access = tokens.GetProperty("access_token").GetString(); var identity = tokens.GetProperty("id_token").GetString();
        Assert.True(!string.IsNullOrWhiteSpace(access) && !string.IsNullOrWhiteSpace(identity), "Native code redemption must publish its qualified artifacts.");
        foreach (var artifact in new[] { (Token: access!, Audience: Resource), (Token: identity!, Audience: test.Owners.ClientId) })
        {
            var validated = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(artifact.Token, new TokenValidationParameters
            {
                ValidIssuer = new Uri(Origin).AbsoluteUri, ValidAudience = artifact.Audience, IssuerSigningKey = test.SigningKey,
                TokenDecryptionKey = test.EncryptionKey, ValidateLifetime = true, ClockSkew = TimeSpan.Zero
            });
            Assert.True(validated.IsValid, validated.Exception?.GetType().Name ?? "Native artifact validation failed.");
            Assert.True(validated.ClaimsIdentity.FindFirst("sub")?.Value == Owners.Subject(test.AccountId), "The independently qualified subject mapping must be preserved.");
            if (artifact.Audience == test.Owners.ClientId)
                Assert.True(validated.ClaimsIdentity.FindFirst("nonce")?.Value == code.Nonce, "The ID token must preserve the original protected nonce.");
            Assert.DoesNotContain(validated.ClaimsIdentity.Claims.Select(claim => claim.Type), type => type is "email" or "name" or "role" or "roles" or "account_id");
        }
        using var replay = await Redeem(test, code); Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        var rejected = await replay.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("invalid_grant", rejected.GetProperty("error").GetString());
        Assert.False(rejected.TryGetProperty("access_token", out _) || rejected.TryGetProperty("id_token", out _) || rejected.TryGetProperty("refresh_token", out _));
        await using var check = test.Database.Context();
        var effects = await check.Set<OutboxMessage>().Where(x => x.EventType == "ProductAuthorizationGranted" || x.EventType == "ProductAuthorizationRedeemed").ToArrayAsync();
        var issuance = Assert.Single(effects, x => x.EventType == "ProductAuthorizationGranted");
        var redemption = Assert.Single(effects, x => x.EventType == "ProductAuthorizationRedeemed");
        Assert.Equal(issuance.Subject, redemption.Subject); Assert.Equal(issuance.OperationId, redemption.OperationId); Assert.Equal(issuance.CorrelationId, redemption.CorrelationId);
        Assert.Equal("RESTRICTED_SECURITY", redemption.DataClassification);
        foreach (var effect in effects)
        {
            using var payload = JsonDocument.Parse(effect.Payload); Assert.Equal(Product, payload.RootElement.GetProperty("product").GetString());
            Assert.Equal(EnvironmentName, payload.RootElement.GetProperty("environment").GetString());
            Assert.False(effect.Payload.Contains(code.Value, StringComparison.Ordinal) || effect.Payload.Contains(access!, StringComparison.Ordinal) ||
                effect.Payload.Contains(identity!, StringComparison.Ordinal) || effect.Payload.Contains(test.Owners.ClientCredential, StringComparison.Ordinal), "Protocol artifacts must not enter outbox payloads.");
        }
        await using var scope = test.Host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await manager.FindByIdAsync(issuance.Subject); Assert.NotNull(authorization);
        var properties = await manager.GetPropertiesAsync(authorization!); Assert.True(properties.ContainsKey(ProductAuthorizationApplication.LineageProperty));
    }

    [Fact]
    public async Task MissingNativeResourcePermissionRejectsBeforeIdentityIssuance()
    {
        await using var test = await Create(nativeResourcePermission: false);
        using var response = await RequestAuthorization(test);
        await AssertRejected(response, "invalid_request", HttpStatusCode.BadRequest, "ID2192");
        await AssertEffects(test, grants: 0);
        await using var read = test.Database.Context();
        Assert.Equal(0, await read.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM public.product_authorization_relationship_fixture").SingleAsync());
    }

    [Theory]
    [InlineData("code_verifier", "invalid_grant")]
    [InlineData("client_id", "invalid_client")]
    [InlineData("client_secret", "invalid_client")]
    [InlineData("redirect_uri", "invalid_grant")]
    public async Task TokenRedemptionRequiresExactNativeBindings(string parameter, string error)
    {
        await using var test = await Create(); var original = await Authorize(test);
        var wrong = parameter switch
        {
            "code_verifier" => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)),
            "redirect_uri" => "https://product.protocol.example.test/other-callback",
            _ => "synthetic-unrecognized-binding"
        };
        using var response = await Redeem(test, original, new Dictionary<string, string> { [parameter] = wrong });
        await AssertRejected(response, error, error == "invalid_client" ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest);
        await AssertEffects(test, grants: 1, original.Value, test.Owners.ClientCredential);
    }

    [Theory]
    [InlineData("product_id", "PRD-UNQUALIFIED-SYNTHETIC")]
    [InlineData("environment", "PRODUCTION")]
    public async Task NativeClientCannotSelectAnUnqualifiedProductOrEnvironment(string parameter, string value)
    {
        await using var test = await Create();
        using var response = await RequestAuthorization(test, overrides: new Dictionary<string, string> { [parameter] = value });
        await AssertRejected(response, "access_denied", HttpStatusCode.OK);
        await AssertEffects(test, grants: 0);
    }

    [Fact]
    public async Task NativeScopeAndResourcePermissionsDoNotReplaceExactRegistryQualification()
    {
        await using var test = await Create(broaderNativeQualification: true);
        using (var scope = await RequestAuthorization(test, overrides: new Dictionary<string, string> { ["scope"] = "openid product.other" }))
            await AssertRejected(scope, "access_denied", HttpStatusCode.OK);
        using (var resource = await RequestAuthorization(test, overrides: new Dictionary<string, string> { ["resource"] = OtherResource }))
            await AssertRejected(resource, "access_denied", HttpStatusCode.OK);
        await AssertEffects(test, grants: 0);
    }

    [Fact]
    public async Task ExpiredOriginalCodeCannotRedeemEvenWhileNativeEnvelopeIsUnexpired()
    {
        await using var test = await Create(); var original = await Authorize(test);
        // The owner clock advances beyond the original two-minute deadline without sleeping
        // or changing the native token validator's system clock.
        test.Host.Clock.Advance(TimeSpan.FromMinutes(3));
        using var response = await Redeem(test, original);
        await AssertRejected(response, "invalid_grant", HttpStatusCode.BadRequest);
        await AssertEffects(test, grants: 1, original.Value);
    }

    [Fact]
    public async Task RevokedTunnerSessionCannotRedeemItsPreviouslyIssuedNativeCode()
    {
        await using var test = await Create(); var original = await Authorize(test);
        await using (var write = test.Database.Context())
        {
            var session = await write.Set<AuthenticationSessionRow>().SingleAsync(x => x.AccountId == test.AccountId);
            session.State = "REVOKED"; session.RevokedAt = test.Host.Clock.GetUtcNow(); session.Version++;
            session.ProtectedKey = ""; session.ProtectedTicket = ""; await write.SaveChangesAsync();
        }
        using var response = await Redeem(test, original);
        await AssertRejected(response, "invalid_grant", HttpStatusCode.BadRequest);
        await AssertEffects(test, grants: 1, original.Value);
    }

    [Theory]
    [InlineData("Policy")]
    [InlineData("Registry")]
    [InlineData("Relationship")]
    public async Task OwnerChangeUnavailabilityAndFinalRecheckPreventNativeTokenPublication(string owner)
    {
        // Fresh originals isolate denial stages; a failed original is never retried or replaced.
        foreach (var stage in new[] { "Changed", "Unavailable", "FinalOutput" })
        {
            await using var test = await Create(); var original = await Authorize(test); var finalBoundaryReached = false;
            if (stage == "FinalOutput")
                test.Owners.BeforeTokenOutput = async ct =>
                {
                    finalBoundaryReached = true;
                    await ChangeOwner(test.Owners, owner, unavailable: false, ct);
                };
            else await ChangeOwner(test.Owners, owner, stage == "Unavailable");
            using var response = await Redeem(test, original);
            if (stage == "Unavailable")
            {
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                await response.Content.LoadIntoBufferAsync(65536);
                var body = await response.Content.ReadAsStringAsync();
                Assert.True(!body.Contains(original.Value, StringComparison.Ordinal) &&
                    !body.Contains(test.Owners.ClientCredential, StringComparison.Ordinal), "Unavailable owners must not expose native credentials or codes.");
                Assert.True(!body.Contains("access_token", StringComparison.Ordinal) && !body.Contains("id_token", StringComparison.Ordinal) &&
                    !body.Contains("refresh_token", StringComparison.Ordinal), "Unavailable owners must not publish protocol artifacts.");
            }
            else await AssertRejected(response, "invalid_grant", HttpStatusCode.BadRequest);
            if (stage == "FinalOutput")
            {
                Assert.True(finalBoundaryReached, "The owner mutation must run at the native pre-output boundary.");
                Assert.True(test.Owners.NativeArtifactsPrepared, "Native artifacts must have been generated before the final authority denial removes them.");
                await using var read = test.Database.Context();
                var issuance = await read.Set<OutboxMessage>().SingleAsync(x => x.EventType == "ProductAuthorizationGranted");
                await using var scope = test.Host.Services.CreateAsyncScope();
                var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
                var authorization = await manager.FindByIdAsync(issuance.Subject); Assert.NotNull(authorization);
                Assert.Equal(OpenIddictConstants.Statuses.Revoked, await manager.GetStatusAsync(authorization!));
            }
            await AssertEffects(test, grants: 1, original.Value, test.Owners.ClientCredential);
        }
    }

    [Fact]
    public async Task LifecycleRefreshOutlivesCodeDeadlineRotatesAndRejectsReplayWithoutBrowserActivity()
    {
        await using var test = await Create(lifecycle: true); var original = await IssueLifecycleTokens(test);
        var before = await ReadSourceSession(test); var originalRefreshEnd = await RefreshExpiration(test, original.AuthorizationId);
        test.Host.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.True(test.Host.Clock.GetUtcNow() > original.CodeExpiresAt, "Refresh must exercise an expired original authorization-code deadline.");
        using var response = await Refresh(test, original.Refresh);
        var refreshed = await ReadLifecycleTokens(response, original.AuthorizationId, original.OriginalCode, original.CodeExpiresAt);
        Assert.True(refreshed.Refresh != original.Refresh, "Qualified rotation must replace the presented refresh token.");
        var validated = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(refreshed.Access, new TokenValidationParameters
        {
            ValidIssuer = new Uri(Origin).AbsoluteUri, ValidAudience = Resource, IssuerSigningKey = test.SigningKey,
            TokenDecryptionKey = test.EncryptionKey, ValidateLifetime = true, ClockSkew = TimeSpan.Zero
        });
        Assert.True(validated.IsValid, validated.Exception?.GetType().Name ?? "Refreshed native access validation failed.");
        Assert.True(validated.ClaimsIdentity.FindFirst("sub")?.Value == Owners.Subject(test.AccountId), "Refresh must preserve the independently mapped subject.");
        Assert.True(validated.ClaimsIdentity.FindFirst("exp")?.Value == test.Host.Clock.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            "Native generation must preserve the exact qualified access-token expiry.");
        Assert.True(await RefreshExpiration(test, original.AuthorizationId) == originalRefreshEnd, "Non-sliding rotation must preserve the original refresh expiry.");
        await AssertRefreshStatuses(test, original.AuthorizationId, valid: 1, redeemed: 1);
        await AssertLifecycleEffects(test, "ProductAuthorizationRefreshed", 1, original, refreshed);
        using var replay = await Refresh(test, original.Refresh);
        await AssertRejected(replay, "invalid_grant", HttpStatusCode.BadRequest);
        await AssertLifecycleEffects(test, "ProductAuthorizationRefreshed", 1, original, refreshed);
        await AssertSourceSessionUnchanged(test, before);
    }

    [Fact]
    public async Task LifecycleRefreshCanNarrowScopesWithoutIssuingAnUnrequestedIdentityToken()
    {
        await using var test = await Create(lifecycle: true); var original = await IssueLifecycleTokens(test);
        var before = await ReadSourceSession(test); var originalRefreshEnd = await RefreshExpiration(test, original.AuthorizationId);
        using var response = await Refresh(test, original.Refresh, new Dictionary<string, string> { ["scope"] = "offline_access product.read" });
        var narrowed = await ReadLifecycleTokens(response, original.AuthorizationId, original.OriginalCode, original.CodeExpiresAt, expectIdentity: false);
        var validated = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(narrowed.Access, new TokenValidationParameters
        {
            ValidIssuer = new Uri(Origin).AbsoluteUri, ValidAudience = Resource, IssuerSigningKey = test.SigningKey,
            TokenDecryptionKey = test.EncryptionKey, ValidateLifetime = true, ClockSkew = TimeSpan.Zero
        });
        Assert.True(validated.IsValid, validated.Exception?.GetType().Name ?? "Narrowed native access validation failed.");
        var scopes = validated.ClaimsIdentity.FindAll("scope").SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Order(StringComparer.Ordinal);
        Assert.True(scopes.SequenceEqual(["offline_access", "product.read"], StringComparer.Ordinal), "Native access scope must remain narrowed to the qualified request.");
        Assert.True(await RefreshExpiration(test, original.AuthorizationId) == originalRefreshEnd, "Narrowing must preserve the original non-sliding refresh bound.");
        await AssertLifecycleEffects(test, "ProductAuthorizationRefreshed", 1, original, narrowed);
        await AssertSourceSessionUnchanged(test, before);
    }

    [Theory]
    [InlineData("scope", "openid product.other", "invalid_scope")]
    [InlineData("client_id", "synthetic-unrecognized-binding", "invalid_client")]
    public async Task LifecycleRefreshRejectsScopeEscalationAndWrongNativeClient(string parameter, string value, string error)
    {
        await using var test = await Create(lifecycle: true); var original = await IssueLifecycleTokens(test);
        var before = await ReadSourceSession(test);
        using var response = await Refresh(test, original.Refresh, new Dictionary<string, string> { [parameter] = value });
        await AssertRejected(response, error, error == "invalid_client" ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest);
        await AssertLifecycleEffects(test, "ProductAuthorizationRefreshed", 0, original);
        await AssertSourceSessionUnchanged(test, before);
    }

    [Fact]
    public async Task LifecycleIntrospectionDisclosesOnlyQualifiedClaimsAndBecomesInactiveAfterOwnerChange()
    {
        await using var test = await Create(lifecycle: true); var original = await IssueLifecycleTokens(test);
        var before = await ReadSourceSession(test);
        using (var response = await Inspect(test, original.Access, "access_token"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(content.GetProperty("active").GetBoolean());
            Assert.True(content.GetProperty("sub").GetString() == Owners.Subject(test.AccountId), "Active introspection must retain only the qualified native subject.");
            Assert.All(content.EnumerateObject().Select(property => property.Name), name => Assert.True(name is "active" or "sub", "Introspection must not disclose unqualified fields."));
        }
        test.Owners.RegistryVersion++;
        using (var response = await Inspect(test, original.Access, "access_token")) await AssertInactive(response);
        Assert.True(test.Owners.LifecycleDecisions >= 3, "Both active revalidation and changed-owner denial must reach the qualified lifecycle source.");
        await AssertLifecycleEffects(test, "ProductAuthorizationIntrospected", 1, original);
        await AssertSourceSessionUnchanged(test, before);
    }

    [Theory]
    [InlineData(ProductAuthorizationArtifactEffect.PresentedToken)]
    [InlineData(ProductAuthorizationArtifactEffect.AuthorizationChain)]
    public async Task LifecycleRevocationHonorsPresentedTokenAndAuthorizationChainEffects(ProductAuthorizationArtifactEffect effect)
    {
        await using var test = await Create(lifecycle: true); var original = await IssueLifecycleTokens(test);
        var before = await ReadSourceSession(test); test.Owners.RevocationEffect = effect;
        using (var response = await Revoke(test, original.Access, "access_token")) await AssertRevocationSuccess(response);
        using (var response = await Inspect(test, original.Access, "access_token")) await AssertInactive(response);
        using (var response = await Inspect(test, original.Refresh, "refresh_token"))
        {
            if (effect == ProductAuthorizationArtifactEffect.AuthorizationChain) await AssertInactive(response);
            else
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var content = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(content.GetProperty("active").GetBoolean(), "Presented-token revocation must preserve the qualified sibling refresh token.");
            }
        }
        await using var scope = test.Host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await manager.FindByIdAsync(original.AuthorizationId); Assert.NotNull(authorization);
        Assert.Equal(effect == ProductAuthorizationArtifactEffect.AuthorizationChain ? OpenIddictConstants.Statuses.Revoked : OpenIddictConstants.Statuses.Valid,
            await manager.GetStatusAsync(authorization!));
        await using (var read = test.Database.Context())
        {
            var audit = await read.Set<OutboxMessage>().SingleAsync(message => message.EventType == "ProductAuthorizationRevoked");
            using var payload = JsonDocument.Parse(audit.Payload);
            Assert.Equal(effect.ToString(), payload.RootElement.GetProperty("effect").GetString());
        }
        await AssertLifecycleEffects(test, "ProductAuthorizationRevoked", 1, original);
        await AssertSourceSessionUnchanged(test, before);
    }

    [Fact]
    public async Task LifecycleRevocationOfUnknownNativeTokenSucceedsWithoutOwnerDecisionOrSuccessAudit()
    {
        await using var test = await Create(lifecycle: true); var before = await ReadSourceSession(test);
        var unrecognized = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        using var response = await Revoke(test, unrecognized, "access_token"); await AssertRevocationSuccess(response);
        Assert.Equal(0, test.Owners.LifecycleDecisions);
        await using var read = test.Database.Context();
        Assert.Equal(0, await read.Set<OutboxMessage>().CountAsync(message => message.EventType.StartsWith("ProductAuthorization")));
        await AssertSourceSessionUnchanged(test, before);
    }

    [Fact]
    public async Task LifecycleRevocationSurvivesEndedSourceSessionAndExpiredIssuancePolicy()
    {
        await using var test = await Create(lifecycle: true);
        test.Owners.IssuancePolicyExpiresAt = test.Host.Clock.GetUtcNow().AddMinutes(1);
        var original = await IssueLifecycleTokens(test);
        test.Host.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(test.Host.Clock.GetUtcNow() > test.Owners.IssuancePolicyExpiresAt, "The original issuance policy must have expired before revocation.");
        await using (var write = test.Database.Context())
        {
            var session = await write.Set<AuthenticationSessionRow>().SingleAsync(row => row.AccountId == test.AccountId);
            session.State = "REVOKED"; session.RevokedAt = test.Host.Clock.GetUtcNow(); session.Version++;
            session.ProtectedKey = ""; session.ProtectedTicket = ""; await write.SaveChangesAsync();
        }
        var before = await ReadSourceSession(test);
        test.Owners.PolicyAvailable = false; test.Host.Policies.Available = false; test.Host.Access.Allowed = false;
        test.Owners.RevocationEffect = ProductAuthorizationArtifactEffect.AuthorizationChain;
        using (var response = await Revoke(test, original.Refresh, "refresh_token")) await AssertRevocationSuccess(response);
        Assert.True(test.Owners.LifecycleDecisions >= 2, "A current explicit revocation qualification must govern despite ended browser eligibility.");
        await AssertLifecycleEffects(test, "ProductAuthorizationRevoked", 1, original);
        await AssertSourceSessionUnchanged(test, before);
        await using var scope = test.Host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await manager.FindByIdAsync(original.AuthorizationId); Assert.NotNull(authorization);
        Assert.Equal(OpenIddictConstants.Statuses.Revoked, await manager.GetStatusAsync(authorization!));
    }

    [Theory]
    [InlineData(ProductAuthorizationLifecycleOperation.Refresh)]
    [InlineData(ProductAuthorizationLifecycleOperation.Introspection)]
    public async Task LifecycleQualifiedArtifactDenialCommitsExplicitChainRevocation(ProductAuthorizationLifecycleOperation operation)
    {
        await using var test = await Create(lifecycle: true); var original = await IssueLifecycleTokens(test);
        var before = await ReadSourceSession(test);
        test.Owners.DenyLifecycleArtifact = true; test.Owners.RevocationEffect = ProductAuthorizationArtifactEffect.AuthorizationChain;
        if (operation == ProductAuthorizationLifecycleOperation.Refresh)
        {
            using var response = await Refresh(test, original.Refresh);
            await AssertRejected(response, "invalid_grant", HttpStatusCode.BadRequest);
        }
        else
        {
            using var response = await Inspect(test, original.Access, "access_token"); await AssertInactive(response);
        }
        await using var scope = test.Host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await manager.FindByIdAsync(original.AuthorizationId); Assert.NotNull(authorization);
        Assert.Equal(OpenIddictConstants.Statuses.Revoked, await manager.GetStatusAsync(authorization!));
        await AssertLifecycleEffects(test, "ProductAuthorizationRevoked", 1, original);
        await AssertLifecycleEffects(test, "ProductAuthorizationRefreshed", 0, original);
        await AssertSourceSessionUnchanged(test, before);
    }

    private sealed record LifecycleTokens(string Access, string Identity, string Refresh, string AuthorizationId, NativeCode OriginalCode, DateTimeOffset CodeExpiresAt)
    { public override string ToString() => nameof(LifecycleTokens); }

    private static async Task<LifecycleTokens> IssueLifecycleTokens(Scenario test)
    {
        var code = await Authorize(test);
        await using var read = test.Database.Context();
        var granted = await read.Set<OutboxMessage>().SingleAsync(message => message.EventType == "ProductAuthorizationGranted");
        await using var scope = test.Host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await manager.FindByIdAsync(granted.Subject); Assert.NotNull(authorization);
        var properties = await manager.GetPropertiesAsync(authorization!);
        var original = ProductAuthorizationApplication.ReadLineage(properties[ProductAuthorizationApplication.LineageProperty]); Assert.NotNull(original);
        using var response = await Redeem(test, code);
        return await ReadLifecycleTokens(response, granted.Subject, code, original!.CodeExpiresAt);
    }

    private static async Task<LifecycleTokens> ReadLifecycleTokens(HttpResponseMessage response, string authorizationId, NativeCode code, DateTimeOffset codeExpiresAt, bool expectIdentity = true)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bearer", content.GetProperty("token_type").GetString());
        var access = content.GetProperty("access_token").GetString();
        var identityPresent = content.TryGetProperty("id_token", out var identityValue);
        var identity = identityPresent ? identityValue.GetString() : null;
        var refresh = content.GetProperty("refresh_token").GetString();
        Assert.True(!string.IsNullOrWhiteSpace(access) && !string.IsNullOrWhiteSpace(refresh), "The qualified lifecycle must publish native access and refresh artifacts.");
        if (expectIdentity) Assert.True(identityPresent && !string.IsNullOrWhiteSpace(identity), "The qualified OpenID lifecycle must publish its native identity artifact.");
        else Assert.False(identityPresent, "A scope-narrowed refresh must not publish an unrequested identity artifact.");
        return new(access!, identity ?? string.Empty, refresh!, authorizationId, code, codeExpiresAt);
    }

    private static Task<HttpResponseMessage> Refresh(Scenario test, string token, IReadOnlyDictionary<string, string>? overrides = null) =>
        PostNativeForm(test, TokenPath, new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = token }, overrides);
    private static Task<HttpResponseMessage> Inspect(Scenario test, string token, string hint) =>
        PostNativeForm(test, IntrospectionPath, new Dictionary<string, string> { ["token"] = token, ["token_type_hint"] = hint });
    private static Task<HttpResponseMessage> Revoke(Scenario test, string token, string hint) =>
        PostNativeForm(test, RevocationPath, new Dictionary<string, string> { ["token"] = token, ["token_type_hint"] = hint });
    private static async Task<HttpResponseMessage> PostNativeForm(Scenario test, string path, Dictionary<string, string> values, IReadOnlyDictionary<string, string>? overrides = null)
    {
        values["client_id"] = test.Owners.ClientId; values["client_secret"] = test.Owners.ClientCredential;
        if (overrides is not null) foreach (var pair in overrides) values[pair.Key] = pair.Value;
        using var form = new FormUrlEncodedContent(values);
        return await test.Host.RawPost(path, await form.ReadAsStringAsync(), string.Empty, string.Empty, "application/x-www-form-urlencoded");
    }

    private static async Task AssertInactive(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(content.GetProperty("active").GetBoolean());
        Assert.Equal("active", Assert.Single(content.EnumerateObject().Select(property => property.Name)));
    }
    private static async Task AssertRevocationSuccess(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); await response.Content.LoadIntoBufferAsync(65536);
        var content = await response.Content.ReadAsStringAsync();
        if (content.Length == 0) return;
        using var document = JsonDocument.Parse(content);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Empty(document.RootElement.EnumerateObject().Select(property => property.Name));
    }

    private static async Task<AuthenticationSessionRow> ReadSourceSession(Scenario test)
    {
        await using var read = test.Database.Context();
        return await read.Set<AuthenticationSessionRow>().AsNoTracking().SingleAsync(row => row.AccountId == test.AccountId);
    }
    private static async Task AssertSourceSessionUnchanged(Scenario test, AuthenticationSessionRow before)
    {
        var after = await ReadSourceSession(test);
        Assert.True(after.Version == before.Version && after.LastSeenAt == before.LastSeenAt && after.State == before.State &&
            after.ExpiresAt == before.ExpiresAt && after.RevokedAt == before.RevokedAt, "Native token traffic must not renew, end or revive the browser session.");
    }
    private static async Task<DateTimeOffset> RefreshExpiration(Scenario test, string authorizationId)
    {
        await using var scope = test.Host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        DateTimeOffset? expires = null;
        await foreach (var token in manager.FindByAuthorizationIdAsync(authorizationId))
            if (await manager.GetTypeAsync(token) == OpenIddictConstants.TokenTypeIdentifiers.RefreshToken && await manager.GetStatusAsync(token) == OpenIddictConstants.Statuses.Valid)
            {
                Assert.Null(expires); expires = await manager.GetExpirationDateAsync(token);
            }
        Assert.NotNull(expires); return expires ?? throw new InvalidOperationException("The valid native refresh expiry is required.");
    }
    private static async Task AssertRefreshStatuses(Scenario test, string authorizationId, int valid, int redeemed)
    {
        await using var scope = test.Host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var statuses = new List<string?>();
        await foreach (var token in manager.FindByAuthorizationIdAsync(authorizationId))
            if (await manager.GetTypeAsync(token) == OpenIddictConstants.TokenTypeIdentifiers.RefreshToken) statuses.Add(await manager.GetStatusAsync(token));
        Assert.Equal(valid, statuses.Count(status => status == OpenIddictConstants.Statuses.Valid));
        Assert.Equal(redeemed, statuses.Count(status => status == OpenIddictConstants.Statuses.Redeemed));
        Assert.Equal(valid + redeemed, statuses.Count);
    }
    private static async Task AssertLifecycleEffects(Scenario test, string type, int count, params LifecycleTokens[] artifacts)
    {
        await using var read = test.Database.Context();
        var effects = await read.Set<OutboxMessage>().Where(message => message.EventType.StartsWith("ProductAuthorization")).ToListAsync();
        Assert.Equal(count, effects.Count(message => message.EventType == type));
        foreach (var effect in effects)
            foreach (var issued in artifacts)
                Assert.True(!effect.Payload.Contains(issued.Access, StringComparison.Ordinal) && (issued.Identity.Length == 0 || !effect.Payload.Contains(issued.Identity, StringComparison.Ordinal)) &&
                    !effect.Payload.Contains(issued.Refresh, StringComparison.Ordinal) && !effect.Payload.Contains(issued.OriginalCode.Value, StringComparison.Ordinal) &&
                    !effect.Payload.Contains(test.Owners.ClientCredential, StringComparison.Ordinal), "Native lifecycle artifacts and credentials must not enter outbox payloads.");
    }

    private static async Task ChangeOwner(Owners owners, string owner, bool unavailable, CancellationToken ct = default)
    {
        switch (owner)
        {
            case "Policy":
                if (unavailable) owners.PolicyAvailable = false; else owners.PolicyVersion = "fixture-product-policy-v2";
                break;
            case "Registry":
                if (unavailable) owners.RegistryAvailable = false; else owners.RegistryVersion++;
                break;
            case "Relationship":
                if (unavailable) owners.RelationshipAvailable = false; else await owners.ChangeRelationshipAsync(ct);
                break;
            default: throw new InvalidOperationException("An explicit synthetic owner is required.");
        }
    }

    private static async Task AssertEffects(Scenario test, int grants, params string[] excludedArtifacts)
    {
        await using var read = test.Database.Context();
        var effects = await read.Set<OutboxMessage>().Where(x => x.EventType == "ProductAuthorizationGranted" || x.EventType == "ProductAuthorizationRedeemed").ToListAsync();
        Assert.Equal(grants, effects.Count);
        Assert.DoesNotContain(effects.Select(x => x.EventType), type => type == "ProductAuthorizationRedeemed");
        foreach (var effect in effects)
        {
            Assert.Equal("ProductAuthorizationGranted", effect.EventType);
            foreach (var artifact in excludedArtifacts)
                Assert.False(effect.Payload.Contains(artifact, StringComparison.Ordinal), "Rejected native artifacts must not enter success outbox payloads.");
        }
    }

    private static async Task AssertRejected(HttpResponseMessage response, string expectedError, HttpStatusCode expectedStatus, string? expectedIdentifier = null)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        await response.Content.LoadIntoBufferAsync(65536);
        var body = await response.Content.ReadAsStringAsync(); var mediaType = response.Content.Headers.ContentType?.MediaType;
        Dictionary<string, string> fields;
        if (expectedError == "invalid_client")
        {
            Assert.Single(response.Headers.WwwAuthenticate.Select(header => header.Scheme), scheme => scheme == "Basic");
            var challenge = response.Headers.WwwAuthenticate.First(header => header.Scheme == "Basic");
            var safeHeaderError = "unrecognized";
            if (challenge.Parameter is { Length: <= 4096 } parameters)
                foreach (var parameter in parameters.Split(','))
                {
                    var separator = parameter.IndexOf('=');
                    if (separator >= 0 && parameter[..separator].Trim() == "error")
                        safeHeaderError = SafeOAuthError(parameter[(separator + 1)..].Trim().Trim('"'));
                }
            Assert.Equal(expectedError, safeHeaderError);
            if (body.Length == 0) return; // Native challenge responses can publish the error solely in WWW-Authenticate.
        }
        if (mediaType == "text/html")
        {
            var form = ReadNativeForm(body); Assert.Equal(Redirect, form.Action); fields = form.Values;
        }
        else if (mediaType == "text/plain")
        {
            fields = new(StringComparer.Ordinal);
            foreach (var line in body.Split('\n'))
            {
                var colon = line.IndexOf(':'); if (colon < 0) continue;
                var key = line[..colon].Trim();
                if (key is "error" or "error_uri" or "code" or "access_token" or "id_token" or "refresh_token")
                    Assert.True(fields.TryAdd(key, line[(colon + 1)..].Trim()), "Native error fields must be unique.");
            }
        }
        else
        {
            Assert.True(mediaType is "application/json" or "application/problem+json", "A native protocol error representation is required.");
            using var document = JsonDocument.Parse(body); fields = new(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name is "code" or "access_token" or "id_token" or "refresh_token")
                    Assert.True(property.Value.ValueKind == JsonValueKind.Null ||
                        property.Value.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(property.Value.GetString()),
                        "A protocol rejection must not publish a native artifact.");
                if (property.Value.ValueKind == JsonValueKind.String)
                    Assert.True(fields.TryAdd(property.Name, property.Value.GetString()!), "Native error fields must be unique.");
            }
        }
        Assert.Equal(expectedError, SafeOAuthError(fields.GetValueOrDefault("error")));
        foreach (var output in new[] { "code", "access_token", "id_token", "refresh_token" })
            Assert.True(string.IsNullOrEmpty(fields.GetValueOrDefault(output)), "A protocol rejection must not publish a native artifact.");
        if (expectedIdentifier is not null) Assert.Equal(expectedIdentifier, SafeOpenIddictIdentifier(fields.GetValueOrDefault("error_uri")));
    }

    private sealed record NativeForm(string Action, Dictionary<string, string> Values)
    { public override string ToString() => nameof(NativeForm); }
    /// <summary>Bounded test-only HTML tokenizer. It neither evaluates scripts nor treats a native response as executable content.</summary>
    private static NativeForm ReadNativeForm(string html)
    {
        Assert.True(html.Length is > 0 and <= 65536, "The native form response must be bounded.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal); string? action = null; var inside = false; var forms = 0; var tags = 0;
        for (var position = 0; position < html.Length;)
        {
            var start = html.IndexOf('<', position); if (start < 0) break;
            if (html.AsSpan(start).StartsWith("<!--", StringComparison.Ordinal))
            { var endComment = html.IndexOf("-->", start + 4, StringComparison.Ordinal); Assert.True(endComment >= 0); position = endComment + 3; continue; }
            var end = start + 1; char quote = '\0';
            for (; end < html.Length; end++)
            {
                var current = html[end]; if (quote != '\0') { if (current == quote) quote = '\0'; }
                else if (current is '\'' or '"') quote = current; else if (current == '>') break;
            }
            Assert.True(end < html.Length && ++tags <= 64, "The native HTML tag structure must be bounded.");
            var tag = html[(start + 1)..end].Trim(); position = end + 1; if (tag.Length == 0 || tag[0] is '!' or '?') continue;
            var closing = tag[0] == '/'; var offset = closing ? 1 : 0; var nameStart = offset;
            while (offset < tag.Length && char.IsAsciiLetterOrDigit(tag[offset])) offset++;
            var name = tag[nameStart..offset];
            if (closing) { if (name.Equals("form", StringComparison.OrdinalIgnoreCase)) inside = false; continue; }
            if (!name.Equals("form", StringComparison.OrdinalIgnoreCase) && !name.Equals("input", StringComparison.OrdinalIgnoreCase)) continue;
            var attributes = ReadAttributes(tag, offset);
            if (name.Equals("form", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(++forms == 1 && !inside, "Exactly one native form is expected.");
                Assert.True(attributes.GetValueOrDefault("method")?.Equals("post", StringComparison.OrdinalIgnoreCase) == true);
                action = attributes.GetValueOrDefault("action"); inside = true;
            }
            else if (inside && attributes.GetValueOrDefault("type")?.Equals("hidden", StringComparison.OrdinalIgnoreCase) == true)
            {
                var field = attributes.GetValueOrDefault("name"); Assert.True(!string.IsNullOrWhiteSpace(field) && field.Length <= 64 && values.Count < 16);
                Assert.True(attributes.TryGetValue("value", out var value) && value.Length <= 16384);
                Assert.True(values.TryAdd(field!, value!), "Native response fields must not be duplicated.");
            }
        }
        Assert.True(forms == 1 && !inside && !string.IsNullOrWhiteSpace(action), "A complete native form_post response is required."); return new(action!, values);
    }
    private static Dictionary<string, string> ReadAttributes(string tag, int offset)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (offset < tag.Length)
        {
            while (offset < tag.Length && char.IsWhiteSpace(tag[offset])) offset++;
            if (offset == tag.Length || tag[offset] == '/') break; var start = offset;
            while (offset < tag.Length && !char.IsWhiteSpace(tag[offset]) && tag[offset] is not '=' and not '/') offset++;
            Assert.True(offset > start); var name = tag[start..offset]; while (offset < tag.Length && char.IsWhiteSpace(tag[offset])) offset++;
            var value = string.Empty;
            if (offset < tag.Length && tag[offset] == '=')
            {
                offset++; while (offset < tag.Length && char.IsWhiteSpace(tag[offset])) offset++; Assert.True(offset < tag.Length);
                if (tag[offset] is '\'' or '"')
                { var quote = tag[offset++]; start = offset; while (offset < tag.Length && tag[offset] != quote) offset++; Assert.True(offset < tag.Length); value = tag[start..offset++]; }
                else { start = offset; while (offset < tag.Length && !char.IsWhiteSpace(tag[offset]) && tag[offset] != '/') offset++; value = tag[start..offset]; }
            }
            Assert.True(values.Count < 16 && values.TryAdd(name, WebUtility.HtmlDecode(value)), "Native response attributes must be unique and bounded.");
        }
        return values;
    }
}
