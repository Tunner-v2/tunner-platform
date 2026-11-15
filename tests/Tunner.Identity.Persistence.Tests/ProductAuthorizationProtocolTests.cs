using System.Collections.Immutable;
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
    private const string Product = "PRD-SYNTHETIC-PROTOCOL-ONLY";
    private const string EnvironmentName = "SANDBOX";
    private const string Redirect = "https://product.protocol.example.test/callback";
    private const string Resource = "https://product.protocol.example.test/api";
    private static readonly ImmutableArray<string> Scopes = ["openid", "product.read"];
    private static readonly ImmutableArray<string> Resources = [Resource];

    private sealed class Owners(IdentityTestDatabase database) : IProductAuthorizationPolicySource,
        IProductAuthorizationRegistrySource, IProductAuthorizationRelationshipSource
    {
        internal string ClientId { get; } = "synthetic-product-client-" + Guid.NewGuid().ToString("N");
        internal string ClientCredential { get; } = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        internal string NativeApplicationId { get; set; } = string.Empty;
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
        public ProductAuthorizationPolicy RequireCurrent(string origin, DateTimeOffset at) => new(
            new("fixture-product-policy-v1", "ACTIVE", Origin, interval, null, [Evidence]), Origin, Evidence,
            "fixture-product-processing", "fixture-product-retention", "RESTRICTED_SECURITY", ["pwd"],
            ["fixture-password-assurance"], TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5), false, null, 256);
        public ValueTask<ProductAuthorizationRegistration> RequireCurrentAsync(ProductAuthorizationRequest request, DateTimeOffset at, CancellationToken ct)
        {
            if (NativeApplicationId.Length == 0) throw new InvalidOperationException("The exact synthetic native application must be seeded first.");
            // Only the digest binds caller input. Every authorized owner fact is the fixed fixture snapshot.
            return ValueTask.FromResult(new ProductAuthorizationRegistration(request.Digest, Product, 1, "ACTIVE", EnvironmentName,
                1, "ACTIVE", ClientId, 1, "ACTIVE", NativeApplicationId, OpenIddictConstants.ClientTypes.Confidential, Redirect,
                Scopes, Resources, "fixture-product-registry-v1", Evidence, true, interval));
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
            RequireFixedOwner(request); await using var owner = database.Context();
            var subject = await owner.Database.SqlQuery<string>($"""
                SELECT subject AS "Value" FROM public.product_authorization_relationship_fixture WHERE request_digest={request.Digest} AND version=1
                """).SingleOrDefaultAsync(ct);
            if (subject is null || subject != Subject(request.AccountId)) throw new InvalidOperationException("An independently committed qualified Relationship is required.");
            return new(request.Digest, "fixture-relationship-" + AuthenticationIdentity.Digest(request.AccountId + "|" + Product)[..24], 1,
                true, true, "fixture-relationship-policy-v1", Evidence, subject, "fixture-subject-mapping-v1", "fixture-subject-mapping-evidence", interval);
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
    private async Task<Scenario> Create()
    {
        var database = await fixture.CreateDatabaseAsync();
        await using (var setup = database.Context())
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TABLE public.product_authorization_relationship_fixture (
                    request_digest text PRIMARY KEY, relationship_id text NOT NULL, subject text NOT NULL,
                    version bigint NOT NULL CHECK(version=1), committed_at timestamp with time zone NOT NULL)
                """);
        var owners = new Owners(database); var signing = RSA.Create(2048); var encryption = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(signing) { KeyId = "fixture-distinct-signing" };
        var encryptionKey = new RsaSecurityKey(encryption) { KeyId = "fixture-distinct-encryption" };
        var host = await Start(database, services =>
        {
            // Keep native artifacts out of incidental framework request/response logs.
            services.AddLogging(logging => logging.ClearProviders());
            services.AddSingleton<IProductAuthorizationPolicySource>(owners);
            services.AddSingleton<IProductAuthorizationRegistrySource>(owners);
            services.AddSingleton<IProductAuthorizationRelationshipSource>(owners);
            services.AddTunnerProductAuthorization(new(Origin, AuthorizationPath, TokenPath, signingKey, encryptionKey, Scopes, Resources, 32768, 16, 64, 16384));
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
                OpenIddictConstants.Permissions.Prefixes.Scope + "product.read",
                OpenIddictConstants.Permissions.Prefixes.Resource + Resource]);
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
    private sealed record NativeCode(string Value, string Verifier)
    { public override string ToString() => nameof(NativeCode); }
    private static async Task<NativeCode> Authorize(Scenario test)
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))); var state = Guid.NewGuid().ToString("N");
        using var query = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = test.Owners.ClientId, ["redirect_uri"] = Redirect, ["response_type"] = "code", ["response_mode"] = "form_post",
            ["scope"] = string.Join(' ', Scopes), ["resource"] = Resource, ["product_id"] = Product, ["environment"] = EnvironmentName,
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["state"] = state, ["nonce"] = Guid.NewGuid().ToString("N")
        });
        HttpResponseMessage received;
        test.Owners.ObserveAuthorization = true;
        try { received = await test.Host.Get(AuthorizationPath + "?" + await query.ReadAsStringAsync(), test.Cookies); }
        finally { test.Owners.ObserveAuthorization = false; }
        using var response = received;
        if (response.StatusCode != HttpStatusCode.OK) Assert.Fail(await NativeErrorDiagnostic(response, test.Owners));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var form = ReadNativeForm(await response.Content.ReadAsStringAsync()); Assert.Equal(Redirect, form.Action);
        Assert.False(form.Values.ContainsKey("access_token") || form.Values.ContainsKey("id_token") || form.Values.ContainsKey("refresh_token"));
        Assert.True(form.Values.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code), "The native form_post response must contain an authorization code.");
        Assert.Equal(state, form.Values.GetValueOrDefault("state")); return new(code!, verifier);
    }
    private static async Task<HttpResponseMessage> Redeem(Scenario test, NativeCode original)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = original.Value, ["code_verifier"] = original.Verifier,
            ["client_id"] = test.Owners.ClientId, ["client_secret"] = test.Owners.ClientCredential, ["redirect_uri"] = Redirect
        });
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
