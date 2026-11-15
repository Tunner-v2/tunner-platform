using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
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
    private const string ResumePath = "/product-authorization/continuation/resume";
    private const string ReviewPath = "/product-authorization";
    private const string ReviewOptionsPath = "/product-authorization/options";
    private const string ReviewDecisionPath = "/product-authorization/decision";
    private static readonly string[] AnonymousProductContextKeys = ["content", "locale", "productName", "scopeDescriptions"];
    private const string Product = "PRD-SYNTHETIC-PROTOCOL-ONLY";
    private const string EnvironmentName = "SANDBOX";
    private const string Redirect = "https://product.protocol.example.test/callback";
    private const string Resource = "https://product.protocol.example.test/api";
    private const string OtherResource = "https://other.protocol.example.test/api";
    private const string PasswordAcr = "urn:fixture:product:acr:password";
    private const string StrongAcr = "urn:fixture:product:acr:strong";
    private static readonly ImmutableArray<string> Scopes = ["openid", "product.read"];
    private static readonly ImmutableArray<string> Resources = [Resource];
    private static readonly AssurancePolicyScope ProductProofScope = new("AUTHORIZE_PRODUCT", "fixture-product-authorization-proof");
    private const string OtherAssuranceVersion = "fixture-unrelated-proof-policy-v1";
    private static readonly AssurancePolicyScope OtherProofScope = new("fixture.unrelated-operation", "fixture-unrelated-owner");

    private sealed class Owners(IdentityTestDatabase database) : IProductAuthorizationPolicySource,
        IProductAuthorizationRegistrySource, IProductAuthorizationRelationshipSource, IProductAuthorizationLifecycleSource,
        IProductAuthorizationContinuationPolicySource, IAccountAuthenticationAccessSource, IProductAuthorizationPresentationSource,
        IAssurancePolicySource, IUserStepUpOperationSource
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
        internal bool ContinuationPolicyAvailable { get; set; } = true;
        internal string ContinuationPolicyVersion { get; set; } = "fixture-continuation-policy-v1";
        internal ProductAuthorizationInteractionQualification? InteractionQualification { get; set; }
        internal IAccountAuthenticationAccessSource? AuthenticationAccess { get; set; }
        internal string PresentationVersion { get; set; } = "fixture-product-presentation-v1";
        internal bool PresentationAvailable { get; set; } = true;
        internal string AssuranceVersion { get; set; } = "fixture-product-proof-policy-v1";
        internal ProductAuthorizationStepUpQualification? StepUpQualification { get; set; }
        internal bool FallbackSelected { get; set; }
        internal Action<AssurancePolicyScope>? BeforeAssuranceRead { get; set; }
        internal ImmutableArray<string> GrantedScopes => LifecycleEnabled ? ["offline_access", "openid", "product.read"] : Scopes;
        internal DateTimeOffset? IssuancePolicyExpiresAt { get; set; }
        internal ProductAuthorizationArtifactEffect RevocationEffect { get; set; } = ProductAuthorizationArtifactEffect.PresentedToken;
        internal bool DenyLifecycleArtifact { get; set; }
        internal int LifecycleDecisions { get; private set; }
        internal Func<CancellationToken, Task>? BeforeTokenOutput { get; set; }
        internal Func<CancellationToken, Task>? BeforeRelationshipEnsure { get; set; }
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
        public AssurancePolicyVersion RequireEffective(AssurancePolicyScope scope, DateTimeOffset executedAt, string? expectedVersion = null)
        {
            BeforeAssuranceRead?.Invoke(scope);
            return new AssurancePolicyCatalog([new(new(AssuranceVersion, "ACTIVE", ProductProofScope, interval, null, [Evidence]),
                "fixture-product-proof-assurance", TimeSpan.FromMinutes(1), new Dictionary<string, string> { ["pwd"] = "fixture-product-proof-assurance" }),
                new(new(OtherAssuranceVersion, "ACTIVE", OtherProofScope, interval, null, [Evidence]),
                "fixture-product-proof-assurance", TimeSpan.FromMinutes(1), new Dictionary<string, string> { ["pwd"] = "fixture-product-proof-assurance" })])
                .RequireEffective(scope, executedAt, expectedVersion);
        }
        Task<UserStepUpOperationContext?> IUserStepUpOperationSource.ResolveAsync(UserStepUpActor actor, CancellationToken ct) =>
            Task.FromResult<UserStepUpOperationContext?>(FallbackSelected ? new(actor, "OP-fixture-unrelated-operation", "fixture-unrelated-correlation",
                OtherProofScope, "fixture-unrelated-resource", 1, new string('D', 64), OtherAssuranceVersion) : null);
        Task<bool> IUserStepUpOperationSource.ApplyAsync(UserStepUpActor actor, UserStepUpOperationContext operation, IdentityPersistenceDbContext context, CancellationToken ct) => Task.FromResult(false);
        public ValueTask<ProductAuthorizationPresentation> RequirePublishedAsync(ProductAuthorizationPresentationFacts facts, DateTimeOffset at, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!PresentationAvailable) throw new InvalidOperationException("Synthetic Product publication unavailable.");
            string[] keys = ["brand.tunner", "authorization.heading", "authorization.description", "authorization.product", "authorization.account",
                "authorization.permissions", "authorization.assurance", "authorization.callback", "authorization.allow", "authorization.deny",
                "authorization.switchAccount", "authorization.working", "authorization.changed", "authorization.unavailable", "authorization.proofRequired",
                "authorization.loginRequired", "authorization.boundary", "authorization.footer.privacy", "authorization.footer.terms", "authorization.footer.support",
                "authorization.loginDescription", "authorization.loginPermissions", "authorization.loginSummary", "authorization.verify"];
            RegistrationPublishedText Published(string key) => new(key, "fixture-review-variant", PresentationVersion,
                "fixture-review-publication", "en", "PUBLISHED", "Synthetic review text: " + key);
            var legal = new Uri("https://fixture.example.test/legal");
            return ValueTask.FromResult(new ProductAuthorizationPresentation(new(PresentationVersion, "ACTIVE", Origin, interval, null, [Evidence]),
                facts.Digest, "Synthetic Product", facts.LoginOnly ? null : "Synthetic current Account",
                facts.RegisteredReturnOrigin, facts.RequestedAcrValues.Length > 0 ? "Synthetic requested assurance" : null, "en",
                facts.Scopes.Select(Published).ToImmutableArray(), keys.Select(Published).ToImmutableArray(), legal, legal, legal,
                Evidence, "fixture-review-processing", "fixture-review-retention", "RESTRICTED_SECURITY", TimeSpan.FromSeconds(30), 32768, 16384));
        }
        async ValueTask<AccountAuthenticationAccessDecision> IAccountAuthenticationAccessSource.RequireAsync(AccountAuthenticationAccessRequest request, CancellationToken ct)
        {
            var source = AuthenticationAccess ?? throw new InvalidOperationException("The existing qualified authentication access fixture is required.");
            var decision = await source.RequireAsync(request, ct);
            // Keep actual TestAccess denial/staleness/action behavior; this owner qualifies one stable
            // policy interval instead of manufacturing a new authority snapshot on every clock sample.
            return decision with { EffectiveInterval = interval };
        }
        ProductAuthorizationContinuationPolicy IProductAuthorizationContinuationPolicySource.RequireCurrent(string origin, DateTimeOffset at) =>
            !ContinuationPolicyAvailable ? throw new InvalidOperationException("Synthetic continuation owner unavailable.") : new(
                new(ContinuationPolicyVersion, "ACTIVE", Origin, interval, null, [Evidence]), Origin, Evidence,
                "fixture-continuation-processing", "fixture-continuation-retention", "RESTRICTED_SECURITY", TimeSpan.FromMinutes(2), 16384, 4096);
        public ProductAuthorizationPolicy RequireCurrent(string origin, DateTimeOffset at) => !PolicyAvailable
            ? throw new InvalidOperationException("Synthetic Product policy owner unavailable.") : new(
            new(PolicyVersion, "ACTIVE", Origin, IssuancePolicyExpiresAt is { } expires
                ? new EffectivePolicyInterval(interval.From, true, expires, false) : interval, null, [Evidence]), Origin, Evidence,
            "fixture-product-processing", "fixture-product-retention", "RESTRICTED_SECURITY", ["pwd"],
            ["fixture-password-assurance"], TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5), LifecycleEnabled, LifecycleEnabled ? TimeSpan.FromHours(1) : null, 256)
            { InteractionQualification = InteractionQualification, StepUpQualification = StepUpQualification };
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
            if (BeforeRelationshipEnsure is { } beforeEnsure) await beforeEnsure(ct);
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
        internal string Cookies { get; set; } = cookies;
        internal RsaSecurityKey SigningKey { get; } = signingKey;
        internal RsaSecurityKey EncryptionKey { get; } = encryptionKey;
        public async ValueTask DisposeAsync()
        {
            try { await Host.DisposeAsync(); }
            finally
            {
                signing.Dispose(); encryption.Dispose();
                try { await Database.DisposeAsync(); }
                finally
                {
                    // Every scenario has its own database/connection string. Disposing EF
                    // providers leaves the driver's implicit pool alive across scenarios.
                    // Clear only this owned pool, never pools belonging to other tests/apps.
                    using var connection = new Npgsql.NpgsqlConnection(Database.ConnectionString);
                    Npgsql.NpgsqlConnection.ClearPool(connection);
                }
            }
        }
        public override string ToString() => nameof(Scenario);
    }
    private async Task<Scenario> Create(bool nativeResourcePermission = true, bool broaderNativeQualification = false, bool lifecycle = false,
        bool continuation = false, bool anonymous = false, bool interaction = false, bool review = false, bool stepUp = false)
    {
        review |= stepUp; interaction |= stepUp;
        var database = await fixture.CreateDatabaseAsync();
        await using (var setup = database.Context())
            await setup.Database.ExecuteSqlRawAsync("""
                CREATE TABLE public.product_authorization_relationship_fixture (
                    request_digest text PRIMARY KEY, relationship_id text NOT NULL, subject text NOT NULL,
                    version bigint NOT NULL CHECK(version>0), committed_at timestamp with time zone NOT NULL)
                """);
        var owners = new Owners(database) { LifecycleEnabled = lifecycle, InteractionQualification = interaction ? new(true,
            [new(PasswordAcr, "fixture-password-assurance", "pwd", PasswordAcr, ["pwd"]),
             new(StrongAcr, "fixture-strong-assurance", "hwk", StrongAcr, ["hwk"])]) : null };
        if (stepUp) owners.StepUpQualification = new([new(StrongAcr, "pwd", "fixture-password-assurance", "pwd",
            "fixture-product-proof-assurance", ProductProofScope, owners.AssuranceVersion, StrongAcr, ["pwd"])]);
        var signing = RSA.Create(2048); var encryption = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(signing) { KeyId = "fixture-distinct-signing" };
        var encryptionKey = new RsaSecurityKey(encryption) { KeyId = "fixture-distinct-encryption" };
        var host = await Start(database, services =>
        {
            // Keep native artifacts out of incidental framework request/response logs.
            services.AddLogging(logging => logging.ClearProviders());
            if (interaction || review)
            {
                owners.AuthenticationAccess = services.Last(service => service.ServiceType == typeof(IAccountAuthenticationAccessSource))
                    .ImplementationInstance as IAccountAuthenticationAccessSource
                    ?? throw new InvalidOperationException("The existing authentication access singleton must be qualified before decoration.");
                services.AddSingleton<IAccountAuthenticationAccessSource>(owners);
            }
            services.AddSingleton<IProductAuthorizationPolicySource>(owners);
            services.AddSingleton<IProductAuthorizationRegistrySource>(owners);
            services.AddSingleton<IProductAuthorizationRelationshipSource>(owners);
            if (lifecycle) services.AddSingleton<IProductAuthorizationLifecycleSource>(owners);
            if (continuation || review) services.AddSingleton<IProductAuthorizationContinuationPolicySource>(owners);
            if (review) services.AddSingleton<IProductAuthorizationPresentationSource>(owners);
            if (stepUp)
            {
                services.AddSingleton<IAssurancePolicySource>(owners);
                services.AddSingleton<IUserStepUpOperationSource>(owners);
                services.AddSingleton<IStepUpPresentationSource, StepUpBrowserTests.Presentation>();
            }
            var nativeScopes = broaderNativeQualification ? ImmutableArray.Create("openid", "product.other", "product.read") : owners.GrantedScopes;
            var nativeResources = broaderNativeQualification ? ImmutableArray.Create(OtherResource, Resource).Sort(StringComparer.Ordinal) : Resources;
            services.AddTunnerProductAuthorization(new(Origin, AuthorizationPath, TokenPath, signingKey, encryptionKey, nativeScopes, nativeResources, 32768, 16, 64, 16384,
                lifecycle ? new(IntrospectionPath, RevocationPath, RotateRefreshTokens: true, SlidingRefreshExpiration: false, RefreshReuseLeeway: TimeSpan.Zero) : null,
                enableContinuation: continuation || review, requireAuthorizationReview: review, enableOperationStepUp: stepUp));
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
        var account = await host.SeedAsync(database);
        if (anonymous) return new(database, host, owners, signing, encryption, signingKey, encryptionKey, account, string.Empty);
        var bootstrap = await host.Options();
        using var signed = await host.Post("/authentication/sign-in", Command(bootstrap.Key), bootstrap.Cookies, bootstrap.Csrf);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode); var cookies = Merge(bootstrap.Cookies, signed);
        using var current = await host.Get("/authentication/session", cookies); Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        return new(database, host, owners, signing, encryption, signingKey, encryptionKey, account, cookies);
    }
    private sealed record NativeCode(string Value, string Verifier, string Nonce)
    { public override string ToString() => nameof(NativeCode); }
    private static async Task<NativeCode> Authorize(Scenario test, IReadOnlyDictionary<string, string>? controls = null)
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var state = Guid.NewGuid().ToString("N"); var nonce = Guid.NewGuid().ToString("N");
        using var response = await RequestAuthorization(test, verifier, state, nonce, controls);
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

    private sealed record PendingRequest(string Verifier, string State, string Nonce, string TokenId, string OperationId, string CorrelationId)
    { public override string ToString() => nameof(PendingRequest); }

    private static async Task<PendingRequest> BeginContinuation(Scenario test, IReadOnlyDictionary<string, string>? controls = null,
        bool existingSession = false, string expectedRedirect = "/login")
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var state = Guid.NewGuid().ToString("N"); var nonce = Guid.NewGuid().ToString("N");
        using var response = await RequestAuthorization(test, verifier, state, nonce, controls);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(expectedRedirect, response.Headers.Location?.OriginalString);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(ProductAuthorizationContinuation.CookieName + "=", StringComparison.Ordinal));
        Assert.True(cookie.Length <= 4096 && cookie.Contains("; path=/", StringComparison.OrdinalIgnoreCase) &&
            cookie.Contains("; secure", StringComparison.OrdinalIgnoreCase) && cookie.Contains("; httponly", StringComparison.OrdinalIgnoreCase) &&
            cookie.Contains("; samesite=strict", StringComparison.OrdinalIgnoreCase), "Continuation transport must use its bounded private host cookie.");
        Assert.True(!cookie.Contains(state, StringComparison.Ordinal) && !cookie.Contains(nonce, StringComparison.Ordinal) &&
            !cookie.Contains(verifier, StringComparison.Ordinal), "The original request must remain protected.");
        test.Cookies = Merge(test.Cookies, response);
        await using var scope = test.Host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var pending = new List<PendingRequest>();
        await foreach (var token in manager.FindByApplicationIdAsync(test.Owners.NativeApplicationId))
        {
            if (await manager.GetTypeAsync(token) != OpenIddictConstants.TokenTypeIdentifiers.Private.RequestToken) continue;
            Assert.Equal(OpenIddictConstants.Statuses.Valid, await manager.GetStatusAsync(token));
            var properties = await manager.GetPropertiesAsync(token);
            var metadata = properties["tunner.product.continuation.v1"];
            var payload = await manager.GetPayloadAsync(token);
            Assert.True(!string.IsNullOrEmpty(payload) && !payload.Contains(state, StringComparison.Ordinal) &&
                !payload.Contains(nonce, StringComparison.Ordinal) && !payload.Contains(verifier, StringComparison.Ordinal),
                "The native pending row must protect original protocol material.");
            pending.Add(new(verifier, state, nonce, (await manager.GetIdAsync(token))!,
                metadata.GetProperty("OperationId").GetString()!, metadata.GetProperty("CorrelationId").GetString()!));
        }
        await AssertEffects(test, 0, verifier, state, nonce);
        await using var read = test.Database.Context();
        if (existingSession) Assert.Single(await read.Set<AuthenticationSessionRow>().ToArrayAsync());
        else Assert.Empty(await read.Set<AuthenticationSessionRow>().ToArrayAsync());
        return Assert.Single(pending);
    }

    private static async Task<string> SignInContinuationBrowser(Scenario test, bool pending = true)
    {
        var originalPointer = CookieValue(test.Cookies, ProductAuthorizationContinuation.CookieName);
        var bootstrap = await test.Host.Options(test.Cookies);
        using var signed = await test.Host.Post("/authentication/sign-in", Command(bootstrap.Key), bootstrap.Cookies, bootstrap.Csrf);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode); test.Cookies = Merge(bootstrap.Cookies, signed);
        Assert.True(CookieValue(test.Cookies, ProductAuthorizationContinuation.CookieName) == originalPointer,
            "The independent continuation must survive actual authentication-cookie rotation.");
        return await SessionContinuation(test, pending);
    }

    private static async Task<string> SessionContinuation(Scenario test, bool pending)
    {
        using var current = await test.Host.Get("/authentication/session", test.Cookies);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var json = await current.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("authenticated").GetBoolean());
        if (pending) Assert.True(json.GetProperty("productAuthorizationPending").GetBoolean());
        else Assert.False(json.TryGetProperty("productAuthorizationPending", out _));
        test.Cookies = Merge(test.Cookies, current);
        return json.GetProperty("csrfToken").GetString()!;
    }

    private static async Task PrepareContinuation(Scenario test, string csrf)
    {
        using var response = await test.Host.Post(ResumePath, new { }, test.Cookies, csrf);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("resumePath", Assert.Single(json.EnumerateObject().Select(property => property.Name)));
        Assert.Equal(AuthorizationPath, json.GetProperty("resumePath").GetString());
        test.Cookies = Merge(test.Cookies, response);
    }

    private static string? CookieValue(string cookies, string name) => cookies.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Trim().Split('=', 2)).Where(pair => pair[0] == name).Select(pair => pair[1]).SingleOrDefault();

    private static async Task AssertPendingStatus(Scenario test, PendingRequest pending, string status)
    {
        await using var scope = test.Host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var token = await manager.FindByIdAsync(pending.TokenId); Assert.NotNull(token);
        Assert.Equal(status, await manager.GetStatusAsync(token!));
    }

    private static async Task AssertInteractionClaims(Scenario test, string identity, DateTimeOffset authenticatedAt, string? acr = PasswordAcr)
    {
        var validated = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(identity, new TokenValidationParameters
        {
            ValidIssuer = new Uri(Origin).AbsoluteUri, ValidAudience = test.Owners.ClientId, IssuerSigningKey = test.SigningKey,
            TokenDecryptionKey = test.EncryptionKey, ValidateLifetime = true, ClockSkew = TimeSpan.Zero
        });
        Assert.True(validated.IsValid, validated.Exception?.GetType().Name ?? "Native artifact validation failed.");
        Assert.True(validated.ClaimsIdentity.FindFirst("auth_time")?.Value == authenticatedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            "Authentication time must remain the actual original authentication instant.");
        Assert.True(validated.ClaimsIdentity.FindFirst("acr")?.Value == acr, "Only the exact qualified actual-session ACR may be disclosed.");
        Assert.True(validated.ClaimsIdentity.Claims.Where(claim => claim.Type == "amr").Select(claim => claim.Value).SequenceEqual(["pwd"]),
            "Issued methods must use the exact explicit actual-session mapping.");
    }

    private static async Task AssertNoInteractionRelationship(Scenario test)
    {
        await AssertEffects(test, 0);
        await using var read = test.Database.Context();
        Assert.Equal(0, await read.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM public.product_authorization_relationship_fixture").SingleAsync());
    }

    private sealed record ReviewFrame(string Reference, string Csrf)
    { public override string ToString() => nameof(ReviewFrame); }

    private static async Task<ReviewFrame> ReviewOptions(Scenario test, PendingRequest pending)
    {
        using var response = await test.Host.Get(ReviewOptionsPath, test.Cookies); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        var content = await response.Content.ReadAsStringAsync(); using var document = JsonDocument.Parse(content); var json = document.RootElement;
        Assert.True(json.GetProperty("canAllow").GetBoolean()); Assert.Equal("Satisfied", json.GetProperty("interactionStatus").GetString());
        Assert.Equal("Synthetic Product", json.GetProperty("productName").GetString());
        Assert.Equal("Synthetic current Account", json.GetProperty("accountLabel").GetString());
        Assert.Equal(new Uri(Redirect).GetLeftPart(UriPartial.Authority), json.GetProperty("registeredReturnOrigin").GetString());
        Assert.True(Scopes.SequenceEqual(json.GetProperty("scopeDescriptions").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)));
        Assert.True(new[] { pending.Verifier, pending.State, pending.Nonce, test.Owners.ClientCredential }
            .All(secret => !content.Contains(secret, StringComparison.Ordinal)), "Review options must not expose original private protocol material.");
        test.Cookies = Merge(test.Cookies, response);
        var reference = json.GetProperty("reference").GetString(); var csrf = json.GetProperty("csrfToken").GetString();
        Assert.True(!string.IsNullOrWhiteSpace(reference) && !string.IsNullOrWhiteSpace(csrf));
        return new(reference!, csrf!);
    }

    private static Task<HttpResponseMessage> ReviewPost(Scenario test, ReviewFrame frame, string decision, string? cookies = null, string? csrf = null) =>
        test.Host.Post(ReviewDecisionPath, new { reference = frame.Reference, decision }, cookies ?? test.Cookies, csrf ?? frame.Csrf);

    private static async Task ReviewApply(Scenario test, ReviewFrame frame, string decision)
    {
        using var response = await ReviewPost(test, frame, decision); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("resumePath", Assert.Single(json.EnumerateObject().Select(property => property.Name)));
        Assert.Equal(decision == "SWITCH_ACCOUNT" ? "/login" : AuthorizationPath, json.GetProperty("resumePath").GetString());
        test.Cookies = Merge(test.Cookies, response);
    }

    private sealed record ProofSelection(int Generation, ProductAuthorizationPreparedProof Proof, ProductAuthorizationProofBinding Binding)
    { public override string ToString() => nameof(ProofSelection); }

    private static async Task<PendingRequest> PreparePasswordProductProof(Scenario test)
    {
        // Persistence precision must not round a decision behind a freshly touched session.
        if (test.Host.Clock.GetUtcNow().UtcTicks % TimeSpan.TicksPerMicrosecond == 0)
            test.Host.Clock.Advance(TimeSpan.FromTicks(1));
        var original = await ReadSourceSession(test);
        var pending = await BeginContinuation(test, new Dictionary<string, string> { ["acr_values"] = StrongAcr }, existingSession: true, expectedRedirect: ReviewPath);
        using (var review = await test.Host.Get(ReviewOptionsPath, test.Cookies))
        {
            Assert.Equal(HttpStatusCode.OK, review.StatusCode); var json = await review.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(json.GetProperty("canAllow").GetBoolean()); Assert.Equal("RequiresFreshProof", json.GetProperty("interactionStatus").GetString());
            Assert.True(json.GetProperty("stepUpAvailable").GetBoolean()); test.Cookies = Merge(test.Cookies, review);
        }
        Guid request; string csrf;
        using (var options = await test.Host.Get("/authentication/step-up/options", test.Cookies))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode); var json = await options.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("PENDING", json.GetProperty("status").GetString()); Assert.True(json.GetProperty("passwordAvailable").GetBoolean());
            request = json.GetProperty("requestId").GetGuid(); csrf = json.GetProperty("csrfToken").GetString()!;
            test.Cookies = Merge(test.Cookies, options);
        }
        using (var verified = await test.Host.Post("/authentication/step-up/password", new { requestId = request, password = Password }, test.Cookies, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, verified.StatusCode); var content = await verified.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(content); var json = document.RootElement;
            Assert.Equal("PROOF_READY", json.GetProperty("status").GetString()); Assert.False(json.TryGetProperty("signInRequired", out _));
            Assert.False(json.TryGetProperty("proofKey", out _) || json.TryGetProperty("proofId", out _) || json.TryGetProperty("access_token", out _) || json.TryGetProperty("refresh_token", out _));
            test.Cookies = Merge(test.Cookies, verified);
        }
        await AssertNoInteractionRelationship(test);
        await using var read = test.Database.Context(); var proof = await read.Set<StepUpProofRow>().SingleAsync();
        Assert.Equal("ACTIVE", proof.State); Assert.Null(proof.EndedAt);
        Assert.Equal(0, await read.Set<OutboxMessage>().CountAsync(message => message.EventType == "StepUpProofConsumed"));
        var current = await ReadSourceSession(test);
        Assert.Equal(original.SessionId, current.SessionId); Assert.Equal(original.AuthenticatedAt, current.AuthenticatedAt);
        Assert.Equal(original.Assurance, current.Assurance); Assert.Equal(original.AuthenticationMethods, current.AuthenticationMethods);
        return pending;
    }

    private static async Task<ProofSelection> ReadPrivatePreparedProof(Scenario test, PendingRequest pending)
    {
        await using var scope = test.Host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var token = await manager.FindByIdAsync(pending.TokenId); Assert.NotNull(token);
        var properties = await manager.GetPropertiesAsync(token!);
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Tunner.Identity.ProductAuthorization.NativeProof.v1");
        var selected = JsonSerializer.Deserialize<ProofSelection>(protector.Unprotect(properties["tunner.product.continuation.proof.v1"].GetString()!));
        Assert.NotNull(selected); return selected!;
    }

    private static StepUpCaller PersistedProofCaller(IServiceProvider services, AuthenticationSessionRow session)
    {
        var protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Tunner.User.Session.Ticket.v1");
        var ticket = TicketSerializer.Default.Deserialize(Convert.FromBase64String(protector.Unprotect(session.ProtectedTicket)));
        Assert.NotNull(ticket); return StepUpCaller.FromPrincipal(ticket!.Principal, Origin, new string('A', 64));
    }

    [Fact]
    public async Task StepUpUnrelatedOwnerRemainsAvailableWithoutProductCookieDuringContinuationPolicyOutage()
    {
        await using var test = await Create(stepUp: true);
        Assert.Null(CookieValue(test.Cookies, ProductAuthorizationContinuation.CookieName));
        test.Owners.FallbackSelected = true; test.Owners.ContinuationPolicyAvailable = false;
        using var response = await test.Host.Get("/authentication/step-up/options", test.Cookies);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PENDING", json.GetProperty("status").GetString());
        Assert.Equal("fixture-unrelated-correlation", json.GetProperty("operationReference").GetString());
        Assert.True(json.GetProperty("passwordAvailable").GetBoolean());
        await AssertNoRequestTokens(test); await AssertNoInteractionRelationship(test);
    }

    [Fact]
    public async Task StepUpOwnerConflictCreatedByVerifiedProofIssuancePreventsProductHandoff()
    {
        await using var test = await Create(stepUp: true);
        var pending = await BeginContinuation(test, new Dictionary<string, string> { ["acr_values"] = StrongAcr }, existingSession: true, expectedRedirect: ReviewPath);
        Guid request; string csrf;
        using (var options = await test.Host.Get("/authentication/step-up/options", test.Cookies))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode); var json = await options.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("PENDING", json.GetProperty("status").GetString()); request = json.GetProperty("requestId").GetGuid();
            csrf = json.GetProperty("csrfToken").GetString()!; test.Cookies = Merge(test.Cookies, options);
        }
        var hookAttached = false; var proofCreated = false;
        test.Owners.BeforeAssuranceRead = scope =>
        {
            if (scope != ProductProofScope || hookAttached) return;
            var http = test.Host.Services.GetRequiredService<IHttpContextAccessor>().HttpContext;
            Assert.NotNull(http);
            var database = http!.RequestServices.GetRequiredService<IdentityPersistenceDbContext>();
            database.ChangeTracker.Tracked += (_, tracked) =>
            {
                if (tracked.Entry.State != EntityState.Added || tracked.Entry.Entity is not StepUpProofRow
                    { State: "ACTIVE", AuthenticationMethod: "pwd" }) return;
                proofCreated = true; test.Owners.FallbackSelected = true;
            };
            hookAttached = true;
        };
        using var response = await test.Host.Post("/authentication/step-up/password", new { requestId = request, password = Password }, test.Cookies, csrf);
        Assert.True(proofCreated, "The fallback selection must appear only after actual password verification creates its proof.");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var read = test.Database.Context(); Assert.Equal("ACTIVE", (await read.Set<StepUpProofRow>().SingleAsync()).State);
        Assert.Equal(0, await read.Set<OutboxMessage>().CountAsync(message => message.EventType == "StepUpProofConsumed"));
        await using var nativeScope = test.Host.Services.CreateAsyncScope(); var manager = nativeScope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var token = await manager.FindByIdAsync(pending.TokenId); Assert.NotNull(token);
        Assert.False((await manager.GetPropertiesAsync(token!)).ContainsKey("tunner.product.continuation.proof.v1"));
        await AssertNoInteractionRelationship(test);
    }

    [Fact]
    public async Task StepUpPasswordProofReadinessSurvivesResponseLossAndRequiresExplicitAllowBeforeNativeConsumption()
    {
        await using var test = await Create(stepUp: true); var original = await ReadSourceSession(test);
        var pending = await PreparePasswordProductProof(test);
        // A fresh read reconciles the same durable proof after a lost password-verification response.
        using (var recovered = await test.Host.Get("/authentication/step-up/options", test.Cookies))
        {
            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
            Assert.Equal("PROOF_READY", (await recovered.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
            test.Cookies = Merge(test.Cookies, recovered);
        }
        var frame = await ReviewOptions(test, pending); await AssertNoInteractionRelationship(test);
        await using (var before = test.Database.Context())
        { Assert.Equal("ACTIVE", (await before.Set<StepUpProofRow>().SingleAsync()).State); Assert.Equal(0, await before.Set<OutboxMessage>().CountAsync(message => message.EventType == "StepUpProofConsumed")); }
        await ReviewApply(test, frame, "ALLOW");
        using var authorized = await test.Host.Get(AuthorizationPath, test.Cookies); Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var form = ReadNativeForm(await authorized.Content.ReadAsStringAsync()); Assert.True(form.Values.GetValueOrDefault("state") == pending.State);
        Assert.True(form.Values.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code));
        await using (var after = test.Database.Context())
        {
            var proof = await after.Set<StepUpProofRow>().SingleAsync(); Assert.Equal("CONSUMED", proof.State); Assert.Empty(proof.ProtectedKey);
            Assert.Equal(1, await after.Set<OutboxMessage>().CountAsync(message => message.EventType == "StepUpProofConsumed"));
        }
        using var redeemed = await Redeem(test, new(code!, pending.Verifier, pending.Nonce)); Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        var tokens = await redeemed.Content.ReadFromJsonAsync<JsonElement>();
        await AssertInteractionClaims(test, tokens.GetProperty("id_token").GetString()!, original.AuthenticatedAt, StrongAcr);
        var current = await ReadSourceSession(test); Assert.Equal(original.AuthenticatedAt, current.AuthenticatedAt); Assert.Equal(original.Assurance, current.Assurance);
    }

    [Fact]
    public async Task StepUpSameTransactionRejectsSecondConsumptionAndPreparationAndRollbackRestoresProof()
    {
        await using var test = await Create(stepUp: true); var pending = await PreparePasswordProductProof(test);
        var selected = await ReadPrivatePreparedProof(test, pending); var session = await ReadSourceSession(test);
        await using (var scope = test.Host.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
            var application = scope.ServiceProvider.GetRequiredService<StepUpApplication>(); var caller = PersistedProofCaller(scope.ServiceProvider, session);
            var policy = test.Owners.RequireCurrent(Origin, test.Host.Clock.GetUtcNow());
            var row = await database.Set<StepUpProofRow>().SingleAsync();
            var key = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Tunner.User.StepUp.Proof.RetryKey.v1").Unprotect(row.ProtectedKey);
            await using var transaction = await database.Database.BeginTransactionAsync();
            var first = await application.ConsumeProductAuthorizationProofAsync(caller, selected.Binding, policy, selected.Proof.ProofId, default); Assert.NotNull(first);
            Assert.Null(await application.ConsumeProductAuthorizationProofAsync(caller, selected.Binding, policy, selected.Proof.ProofId, default));
            Assert.Null(await application.PrepareProductAuthorizationProofAsync(caller, selected.Binding, policy, key, default));
            Assert.Null(await application.ReadPreparedProductAuthorizationProofAsync(caller, selected.Binding, policy, selected.Proof.ProofId, default));
            Assert.True(await application.ProductAuthorizationProofCurrentAsync(caller, selected.Binding, policy, first!, default));
            Assert.Single(database.ChangeTracker.Entries<OutboxMessage>(), entry => entry.Entity.EventType == "StepUpProofConsumed");
            await transaction.RollbackAsync(); database.ChangeTracker.Clear();
        }
        await using var read = test.Database.Context(); var restored = await read.Set<StepUpProofRow>().SingleAsync();
        Assert.Equal("ACTIVE", restored.State); Assert.Null(restored.EndedAt); Assert.True(restored.ProtectedKey.Length > 0);
        Assert.Equal(0, await read.Set<OutboxMessage>().CountAsync(message => message.EventType == "StepUpProofConsumed"));
        await AssertNoInteractionRelationship(test); await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Valid);
    }

    [Theory]
    [InlineData("Request")]
    [InlineData("Generation")]
    [InlineData("Session")]
    [InlineData("ProductPolicy")]
    [InlineData("Factor")]
    [InlineData("AssurancePolicy")]
    public async Task StepUpWrongBindingOrChangedCurrentAuthorityCannotConsumePreparedProof(string mutation)
    {
        await using var test = await Create(stepUp: true); var pending = await PreparePasswordProductProof(test);
        var selected = await ReadPrivatePreparedProof(test, pending); var originalSession = await ReadSourceSession(test);
        if (mutation == "Generation")
        {
            var oldReview = await ReviewOptions(test, pending); await ReviewApply(test, oldReview, "SWITCH_ACCOUNT");
            var csrf = await SessionContinuation(test, pending: true);
            using (var rebound = await test.Host.Post(ResumePath, new { }, test.Cookies, csrf))
            { Assert.Equal(HttpStatusCode.OK, rebound.StatusCode); test.Cookies = Merge(test.Cookies, rebound); }
            using (var changed = await test.Host.Get(ReviewOptionsPath, test.Cookies))
            {
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                Assert.False((await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("canAllow").GetBoolean());
            }
            using var rejected = await ReviewPost(test, oldReview, "ALLOW", csrf: csrf); Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        }
        else
        {
            if (mutation == "ProductPolicy") test.Owners.PolicyVersion = "fixture-product-policy-v2";
            if (mutation == "AssurancePolicy") test.Owners.AssuranceVersion = "fixture-product-proof-policy-v2";
            if (mutation == "Factor")
            {
                await using var write = test.Database.Context(); var user = await write.Users.SingleAsync(user => user.AccountId == test.AccountId);
                user.SecurityStamp = Guid.NewGuid().ToString("N"); await write.SaveChangesAsync();
            }
            var binding = mutation == "Request" ? selected.Binding with { RequestDigest = new string('B', 64) }
                : mutation == "Session" ? selected.Binding with { SessionId = Guid.NewGuid() } : selected.Binding;
            await using var scope = test.Host.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<IdentityPersistenceDbContext>();
            var application = scope.ServiceProvider.GetRequiredService<StepUpApplication>(); var caller = PersistedProofCaller(scope.ServiceProvider, originalSession);
            await using var transaction = await database.Database.BeginTransactionAsync();
            var policy = test.Owners.RequireCurrent(Origin, test.Host.Clock.GetUtcNow());
            if (mutation is "Session" or "ProductPolicy")
                await Assert.ThrowsAsync<InvalidOperationException>(() => application.ConsumeProductAuthorizationProofAsync(caller, binding, policy, selected.Proof.ProofId, default));
            else if (mutation == "AssurancePolicy")
                await Assert.ThrowsAsync<AssurancePolicyVersionConflictException>(() => application.ConsumeProductAuthorizationProofAsync(caller, binding, policy, selected.Proof.ProofId, default));
            else Assert.Null(await application.ConsumeProductAuthorizationProofAsync(caller, binding, policy, selected.Proof.ProofId, default));
            await transaction.RollbackAsync(); database.ChangeTracker.Clear();
        }
        await using var read = test.Database.Context(); Assert.Equal("ACTIVE", (await read.Set<StepUpProofRow>().SingleAsync()).State);
        Assert.Equal(0, await read.Set<OutboxMessage>().CountAsync(message => message.EventType == "StepUpProofConsumed"));
        await AssertNoInteractionRelationship(test);
    }

    [Fact]
    public async Task StepUpAssuranceDriftAfterProofConsumptionPreventsNativePublication()
    {
        await using var test = await Create(stepUp: true); var pending = await PreparePasswordProductProof(test);
        var frame = await ReviewOptions(test, pending); await ReviewApply(test, frame, "ALLOW");
        var boundaryReached = false;
        test.Owners.BeforeRelationshipEnsure = ct =>
        {
            ct.ThrowIfCancellationRequested(); boundaryReached = true; test.Owners.AssuranceVersion = "fixture-product-proof-policy-v2";
            return Task.CompletedTask;
        };
        using var response = await test.Host.Get(AuthorizationPath, test.Cookies);
        Assert.True(boundaryReached, "The assurance mutation must follow proof consumption and precede native output.");
        await AssertRejected(response, "access_denied", HttpStatusCode.OK); await AssertEffects(test, 0);
    }

    [Fact]
    public async Task StepUpPromptNoneCannotOpenReviewOrProofUi()
    {
        await using var test = await Create(stepUp: true);
        using var response = await RequestAuthorization(test, overrides: new Dictionary<string, string> { ["prompt"] = "none", ["acr_values"] = StrongAcr });
        await AssertRejected(response, "interaction_required", HttpStatusCode.OK);
        Assert.Null(response.Headers.Location); Assert.Null(CookieValue(Merge(test.Cookies, response), ProductAuthorizationContinuation.CookieName));
        await AssertNoRequestTokens(test); await AssertNoInteractionRelationship(test);
        await using var read = test.Database.Context(); Assert.Empty(await read.Set<StepUpProofRow>().ToArrayAsync());
    }

    [Fact]
    public async Task StepUpProofReadyThenDenyNeverConsumesProofOrCreatesGrant()
    {
        await using var test = await Create(stepUp: true); var pending = await PreparePasswordProductProof(test);
        var frame = await ReviewOptions(test, pending); await ReviewApply(test, frame, "DENY");
        using var response = await test.Host.Get(AuthorizationPath, test.Cookies); await AssertRejected(response, "access_denied", HttpStatusCode.OK);
        await AssertNoInteractionRelationship(test);
        await using var read = test.Database.Context(); var proof = await read.Set<StepUpProofRow>().SingleAsync();
        Assert.Equal("ACTIVE", proof.State); Assert.Null(proof.EndedAt);
        Assert.Equal(0, await read.Set<OutboxMessage>().CountAsync(message => message.EventType == "StepUpProofConsumed"));
        var decline = await read.Set<OutboxMessage>().SingleAsync(message => message.EventType == "ProductAuthorizationDeclined");
        using var payload = JsonDocument.Parse(decline.Payload);
        Assert.Equal(test.Owners.PresentationVersion, payload.RootElement.GetProperty("presentationVersion").GetString());
        Assert.Equal(pending.OperationId, decline.OperationId); Assert.Equal(pending.CorrelationId, decline.CorrelationId);
        Assert.True(new[] { frame.Reference, frame.Csrf, pending.State, pending.Nonce, pending.Verifier, test.Owners.ClientCredential }
            .All(secret => !decline.Payload.Contains(secret, StringComparison.Ordinal)), "Proof-ready denial evidence must exclude browser and protocol secrets.");
    }

    [Theory]
    [InlineData("Changed")]
    [InlineData("Unavailable")]
    public async Task ReviewPublicationDriftDuringRelationshipActivationPreventsNativeCodePublication(string drift)
    {
        await using var test = await Create(review: true);
        var pending = await BeginContinuation(test, existingSession: true, expectedRedirect: ReviewPath);
        var frame = await ReviewOptions(test, pending); await ReviewApply(test, frame, "ALLOW");
        var relationshipBoundaryReached = false;
        test.Owners.BeforeRelationshipEnsure = ct =>
        {
            ct.ThrowIfCancellationRequested(); relationshipBoundaryReached = true;
            if (drift == "Changed") test.Owners.PresentationVersion = "fixture-product-presentation-v2";
            else test.Owners.PresentationAvailable = false;
            return Task.CompletedTask;
        };
        using var response = await test.Host.Get(AuthorizationPath, test.Cookies);
        Assert.True(relationshipBoundaryReached, "The publication mutation must occur after approval validation inside Relationship activation.");
        await AssertRejected(response, "access_denied", HttpStatusCode.OK);
        await AssertEffects(test, 0, pending.State, pending.Nonce, pending.Verifier, frame.Reference, frame.Csrf);
        await using var read = test.Database.Context();
        Assert.Equal(1, await read.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM public.product_authorization_relationship_fixture").SingleAsync());
        Assert.Equal(0, await read.Set<OutboxMessage>().CountAsync(message => message.EventType == "ProductAuthorizationDeclined"));
    }

    [Fact]
    public async Task ReviewDenialPublicationDriftCannotAttributeDeclineToUnreviewedVersion()
    {
        await using var test = await Create(review: true);
        var pending = await BeginContinuation(test, existingSession: true, expectedRedirect: ReviewPath);
        var frame = await ReviewOptions(test, pending); await ReviewApply(test, frame, "DENY");
        test.Owners.PresentationVersion = "fixture-product-presentation-v2";
        using var response = await test.Host.Get(AuthorizationPath, test.Cookies);
        await AssertRejected(response, "access_denied", HttpStatusCode.OK);
        await AssertNoInteractionRelationship(test);
        await using var read = test.Database.Context();
        Assert.Equal(0, await read.Set<OutboxMessage>().CountAsync(message => message.EventType == "ProductAuthorizationDeclined"));
        await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Valid);
    }

    [Fact]
    public async Task ReviewSwitchRejectsOldReferenceWithNewGenerationPointerReboundToSameSession()
    {
        await using var test = await Create(review: true);
        var pending = await BeginContinuation(test, existingSession: true, expectedRedirect: ReviewPath);
        var oldReview = await ReviewOptions(test, pending);
        await ReviewApply(test, oldReview, "SWITCH_ACCOUNT");
        var currentCsrf = await SessionContinuation(test, pending: true);
        using (var rebound = await test.Host.Post(ResumePath, new { }, test.Cookies, currentCsrf))
        {
            Assert.Equal(HttpStatusCode.OK, rebound.StatusCode);
            Assert.Equal(ReviewPath, (await rebound.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resumePath").GetString());
            test.Cookies = Merge(test.Cookies, rebound);
        }
        using var staleDecision = await ReviewPost(test, oldReview, "ALLOW", csrf: currentCsrf);
        Assert.Equal(HttpStatusCode.Conflict, staleDecision.StatusCode);
        await AssertNoInteractionRelationship(test);
        await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Valid);
    }

    [Fact]
    public async Task ReviewActualCurrentSessionRequiresExplicitAllowBeforeNativeGrantAndRedemption()
    {
        await using var test = await Create(review: true);
        var pending = await BeginContinuation(test, existingSession: true, expectedRedirect: ReviewPath);
        using (var unapproved = await test.Host.Get(AuthorizationPath, test.Cookies))
        { Assert.Equal(HttpStatusCode.Found, unapproved.StatusCode); Assert.Equal(ReviewPath, unapproved.Headers.Location?.OriginalString); }
        await AssertNoInteractionRelationship(test);
        var frame = await ReviewOptions(test, pending); await ReviewApply(test, frame, "ALLOW");
        await AssertNoInteractionRelationship(test);
        using var authorized = await test.Host.Get(AuthorizationPath, test.Cookies); Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var form = ReadNativeForm(await authorized.Content.ReadAsStringAsync()); Assert.Equal(Redirect, form.Action);
        Assert.True(form.Values.GetValueOrDefault("state") == pending.State);
        Assert.True(form.Values.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code));
        test.Cookies = Merge(test.Cookies, authorized); Assert.Null(CookieValue(test.Cookies, ProductAuthorizationContinuation.CookieName));
        await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Redeemed);
        using var redeemed = await Redeem(test, new(code!, pending.Verifier, pending.Nonce)); Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        var tokens = await redeemed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(tokens.TryGetProperty("access_token", out _) && tokens.TryGetProperty("id_token", out _));
        using var replay = await test.Host.Get(AuthorizationPath, test.Cookies); await AssertRejected(replay, "invalid_request", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ReviewDenyProducesNativeOriginalStateDenialAndOnePrivateDeclinedEffect()
    {
        await using var test = await Create(review: true); var pending = await BeginContinuation(test, existingSession: true, expectedRedirect: ReviewPath);
        var frame = await ReviewOptions(test, pending); await ReviewApply(test, frame, "DENY");
        using var response = await test.Host.Get(AuthorizationPath, test.Cookies); await AssertRejected(response, "access_denied", HttpStatusCode.OK);
        var form = ReadNativeForm(await response.Content.ReadAsStringAsync()); Assert.True(form.Values.GetValueOrDefault("state") == pending.State);
        await AssertNoInteractionRelationship(test); await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Redeemed);
        await using var read = test.Database.Context(); var declined = await read.Set<OutboxMessage>().SingleAsync(message => message.EventType == "ProductAuthorizationDeclined");
        Assert.Equal(pending.OperationId, declined.OperationId); Assert.Equal(pending.CorrelationId, declined.CorrelationId);
        Assert.Equal("RESTRICTED_SECURITY", declined.DataClassification);
        Assert.True(new[] { pending.Verifier, pending.State, pending.Nonce, frame.Reference, frame.Csrf, test.Owners.ClientCredential }
            .All(secret => !declined.Payload.Contains(secret, StringComparison.Ordinal)), "Declined evidence must exclude private browser and protocol material.");
        using var replay = await test.Host.Get(AuthorizationPath, test.Cookies); await AssertRejected(replay, "invalid_request", HttpStatusCode.BadRequest);
        Assert.Equal(1, await read.Set<OutboxMessage>().CountAsync(message => message.EventType == "ProductAuthorizationDeclined"));
    }

    [Fact]
    public async Task ReviewSwitchAccountInvalidatesPriorPointerAndReferenceWithoutSigningOut()
    {
        await using var test = await Create(review: true); var pending = await BeginContinuation(test, existingSession: true, expectedRedirect: ReviewPath);
        var frame = await ReviewOptions(test, pending); var priorCookies = test.Cookies;
        await ReviewApply(test, frame, "SWITCH_ACCOUNT");
        using (var staleOptions = await test.Host.Get(ReviewOptionsPath, priorCookies)) Assert.Equal(HttpStatusCode.Conflict, staleOptions.StatusCode);
        using (var staleDecision = await ReviewPost(test, frame, "ALLOW", priorCookies)) Assert.Equal(HttpStatusCode.Conflict, staleDecision.StatusCode);
        using (var staleNative = await test.Host.Get(AuthorizationPath, priorCookies)) await AssertRejected(staleNative, "invalid_request", HttpStatusCode.BadRequest);
        _ = await SessionContinuation(test, pending: true);
        await AssertNoInteractionRelationship(test); await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Valid);
        using var unsignedReview = await test.Host.Get(ReviewOptionsPath, test.Cookies); Assert.Equal(HttpStatusCode.Conflict, unsignedReview.StatusCode);
    }

    [Theory]
    [InlineData("Authentication", HttpStatusCode.Unauthorized)]
    [InlineData("Client", HttpStatusCode.Conflict)]
    [InlineData("Policy", HttpStatusCode.Conflict)]
    [InlineData("Publication", HttpStatusCode.Conflict)]
    [InlineData("Expiry", HttpStatusCode.Conflict)]
    public async Task ReviewCurrentAuthorityOrExpiredReferencePreventsDecision(string mutation, HttpStatusCode status)
    {
        await using var test = await Create(review: true); var pending = await BeginContinuation(test, existingSession: true, expectedRedirect: ReviewPath);
        var frame = await ReviewOptions(test, pending);
        switch (mutation)
        {
            case "Authentication":
                await using (var write = test.Database.Context())
                {
                    var user = await write.Users.SingleAsync(user => user.AccountId == test.AccountId);
                    user.SecurityStamp = Guid.NewGuid().ToString("N"); await write.SaveChangesAsync();
                }
                break;
            case "Client":
                await using (var scope = test.Host.Services.CreateAsyncScope())
                {
                    var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
                    var client = await manager.FindByClientIdAsync(test.Owners.ClientId); Assert.NotNull(client);
                    var descriptor = new OpenIddictApplicationDescriptor(); await manager.PopulateAsync(descriptor, client!);
                    Assert.True(descriptor.Permissions.Remove(OpenIddictConstants.Permissions.Prefixes.Resource + Resource)); await manager.UpdateAsync(client!, descriptor);
                }
                break;
            case "Policy": test.Owners.PolicyVersion = "fixture-product-policy-v2"; break;
            case "Publication": test.Owners.PresentationVersion = "fixture-product-presentation-v2"; break;
            default: test.Host.Clock.Advance(TimeSpan.FromSeconds(31)); break;
        }
        using var response = await ReviewPost(test, frame, "ALLOW"); Assert.Equal(status, response.StatusCode);
        await AssertNoInteractionRelationship(test); await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Valid);
    }

    [Fact]
    public async Task ReviewDecisionRejectsWrongCsrfAndTamperedReferenceWithoutGrant()
    {
        await using var test = await Create(review: true); var pending = await BeginContinuation(test, existingSession: true, expectedRedirect: ReviewPath);
        var frame = await ReviewOptions(test, pending);
        using (var wrongCsrf = await ReviewPost(test, frame, "ALLOW", csrf: "synthetic-invalid-csrf")) Assert.Equal(HttpStatusCode.BadRequest, wrongCsrf.StatusCode);
        var tampered = frame with { Reference = (frame.Reference[0] == 'A' ? "B" : "A") + frame.Reference[1..] };
        using (var wrongReference = await ReviewPost(test, tampered, "ALLOW")) Assert.Equal(HttpStatusCode.Conflict, wrongReference.StatusCode);
        using (var unknown = await test.Host.Post(ReviewDecisionPath, new { reference = frame.Reference, decision = "ALLOW", returnUrl = "/account" }, test.Cookies, frame.Csrf))
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        await AssertNoInteractionRelationship(test); await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Valid);
    }

    [Fact]
    public async Task ReviewAnonymousProductLoginPublishesSafeContextAndActualSignInStillRequiresReview()
    {
        await using var test = await Create(review: true, anonymous: true); var pending = await BeginContinuation(test);
        using (var options = await test.Host.Get("/authentication/options", test.Cookies))
        {
            Assert.Equal(HttpStatusCode.OK, options.StatusCode); var content = await options.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(content); var context = document.RootElement.GetProperty("productAuthorization");
            Assert.Equal("Synthetic Product", context.GetProperty("productName").GetString());
            Assert.Equal(AnonymousProductContextKeys, context.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.True(new[] { pending.State, pending.Nonce, pending.Verifier, test.Owners.ClientCredential }
                .All(secret => !content.Contains(secret, StringComparison.Ordinal)), "Anonymous login context must expose only safe published Product descriptions.");
            test.Cookies = Merge(test.Cookies, options);
        }
        var csrf = await SignInContinuationBrowser(test);
        using (var resume = await test.Host.Post(ResumePath, new { }, test.Cookies, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, resume.StatusCode);
            Assert.Equal(ReviewPath, (await resume.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resumePath").GetString());
            test.Cookies = Merge(test.Cookies, resume);
        }
        using (var bypass = await test.Host.Get(AuthorizationPath, test.Cookies))
        { Assert.Equal(HttpStatusCode.Found, bypass.StatusCode); Assert.Equal(ReviewPath, bypass.Headers.Location?.OriginalString); }
        _ = await ReviewOptions(test, pending); await AssertNoInteractionRelationship(test);
        await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Valid);
    }

    [Fact]
    public async Task ReviewConcurrentIdenticalAllowIsIdempotentAndNativeCodeCannotReplay()
    {
        await using var test = await Create(review: true); var pending = await BeginContinuation(test, existingSession: true, expectedRedirect: ReviewPath);
        var frame = await ReviewOptions(test, pending);
        var decisions = await Task.WhenAll(ReviewPost(test, frame, "ALLOW"), ReviewPost(test, frame, "ALLOW"));
        foreach (var decision in decisions)
        {
            using (decision)
            {
                Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
                Assert.Equal(AuthorizationPath, (await decision.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resumePath").GetString());
                test.Cookies = Merge(test.Cookies, decision);
            }
        }
        await AssertNoInteractionRelationship(test);
        using (var issued = await test.Host.Get(AuthorizationPath, test.Cookies))
        {
            Assert.Equal(HttpStatusCode.OK, issued.StatusCode); var form = ReadNativeForm(await issued.Content.ReadAsStringAsync());
            Assert.True(form.Values.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code));
        }
        using var replay = await test.Host.Get(AuthorizationPath, test.Cookies); await AssertRejected(replay, "invalid_request", HttpStatusCode.BadRequest);
        await AssertEffects(test, 1); await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Redeemed);
    }

    [Fact]
    public async Task ReviewOppositeDecisionCannotReplaceCommittedAllow()
    {
        await using var test = await Create(review: true); var pending = await BeginContinuation(test, existingSession: true, expectedRedirect: ReviewPath);
        var frame = await ReviewOptions(test, pending); await ReviewApply(test, frame, "ALLOW");
        using var denied = await ReviewPost(test, frame, "DENY"); Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        using var switched = await ReviewPost(test, frame, "SWITCH_ACCOUNT"); Assert.Equal(HttpStatusCode.Conflict, switched.StatusCode);
        await AssertNoInteractionRelationship(test);
        using var authorized = await test.Host.Get(AuthorizationPath, test.Cookies); Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        Assert.True(ReadNativeForm(await authorized.Content.ReadAsStringAsync()).Values.ContainsKey("code"));
        await AssertEffects(test, 1);
    }

    [Fact]
    public async Task InteractionMappedActualPasswordSessionDisclosesOnlyQualifiedIdentityClaims()
    {
        await using var test = await Create(interaction: true);
        var session = await ReadSourceSession(test); test.Host.Clock.Advance(TimeSpan.FromSeconds(10));
        var code = await Authorize(test, new Dictionary<string, string> { ["acr_values"] = PasswordAcr });
        using var response = await Redeem(test, code); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        await AssertInteractionClaims(test, json.GetProperty("id_token").GetString()!, session.AuthenticatedAt);
        await using var read = test.Database.Context();
        var issued = await read.Set<OutboxMessage>().SingleAsync(message => message.EventType == "ProductAuthorizationGranted");
        await using var scope = test.Host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
        var authorization = await manager.FindByIdAsync(issued.Subject); Assert.NotNull(authorization);
        var lineage = ProductAuthorizationApplication.ReadLineage((await manager.GetPropertiesAsync(authorization!))[ProductAuthorizationApplication.LineageProperty]);
        Assert.NotNull(lineage); Assert.Equal(session.AuthenticatedAt, lineage!.AuthenticationClaims!.AuthenticationTime);
        Assert.Equal(session.SessionId, lineage.Interaction!.OriginalSessionId);
        Assert.True(lineage.Request.AuthenticationContext!.RequestedAcrValues.SequenceEqual([PasswordAcr]));
    }

    [Theory]
    [InlineData("urn:fixture:product:acr:unsupported", "access_denied")]
    [InlineData(StrongAcr, "interaction_required")]
    public async Task InteractionUnsupportedOrUnmetAcrNeverActivatesRelationship(string requestedAcr, string error)
    {
        await using var test = await Create(continuation: true, interaction: true);
        using var response = await RequestAuthorization(test, overrides: new Dictionary<string, string> { ["acr_values"] = requestedAcr });
        await AssertRejected(response, error, HttpStatusCode.OK);
        Assert.Null(response.Headers.Location); await AssertNoRequestTokens(test); await AssertNoInteractionRelationship(test);
    }

    [Fact]
    public async Task InteractionMaxAgeRequiresExplicitAuthenticationTimeDisclosureQualification()
    {
        await using var test = await Create(continuation: true, interaction: true);
        test.Owners.InteractionQualification = test.Owners.InteractionQualification! with { EmitAuthenticationTime = false };
        using var response = await RequestAuthorization(test, overrides: new Dictionary<string, string> { ["max_age"] = "60" });
        await AssertRejected(response, "access_denied", HttpStatusCode.OK);
        await AssertNoRequestTokens(test); await AssertNoInteractionRelationship(test);
    }

    [Theory]
    [InlineData("StaleMaxAge")]
    [InlineData("PromptLogin")]
    [InlineData("ZeroMaxAge")]
    public async Task InteractionForcedFreshRequestRequiresNewActualSessionAndPreservesOriginalContext(string control)
    {
        await using var test = await Create(continuation: true, interaction: true);
        var original = await ReadSourceSession(test);
        var controls = new Dictionary<string, string> { ["acr_values"] = PasswordAcr };
        if (control == "PromptLogin") controls["prompt"] = "login";
        else controls["max_age"] = control == "ZeroMaxAge" ? "0" : "1";
        if (control == "StaleMaxAge") test.Host.Clock.Advance(TimeSpan.FromSeconds(2));
        var receivedAt = test.Host.Clock.GetUtcNow();
        var pending = await BeginContinuation(test, controls, existingSession: true);
        await PrepareContinuation(test, await SessionContinuation(test, pending: true));
        using (var oldSession = await test.Host.Get(AuthorizationPath, test.Cookies))
        {
            Assert.Equal(HttpStatusCode.Found, oldSession.StatusCode); Assert.Equal("/login", oldSession.Headers.Location?.OriginalString);
            test.Cookies = Merge(test.Cookies, oldSession);
        }
        await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Valid); await AssertNoInteractionRelationship(test);
        // The owner clock remains unchanged: a new actual session ID is required even at the same instant.
        await PrepareContinuation(test, await SignInContinuationBrowser(test));
        using var response = await test.Host.Get(AuthorizationPath, test.Cookies);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var form = ReadNativeForm(await response.Content.ReadAsStringAsync()); Assert.Equal(Redirect, form.Action);
        Assert.True(form.Values.GetValueOrDefault("state") == pending.State);
        Assert.True(form.Values.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code));
        await using var read = test.Database.Context();
        var current = await read.Set<AuthenticationSessionRow>().SingleAsync(session => session.State == "ACTIVE");
        Assert.NotEqual(original.SessionId, current.SessionId); Assert.Equal(receivedAt.ToUnixTimeSeconds(), current.AuthenticatedAt.ToUnixTimeSeconds());
        var issued = await read.Set<OutboxMessage>().SingleAsync(message => message.EventType == "ProductAuthorizationGranted");
        Assert.Equal(pending.OperationId, issued.OperationId); Assert.Equal(pending.CorrelationId, issued.CorrelationId);
        await using (var scope = test.Host.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            var authorization = await manager.FindByIdAsync(issued.Subject); Assert.NotNull(authorization);
            var lineage = ProductAuthorizationApplication.ReadLineage((await manager.GetPropertiesAsync(authorization!))[ProductAuthorizationApplication.LineageProperty]);
            Assert.NotNull(lineage); Assert.Equal(original.SessionId, lineage!.Interaction!.OriginalSessionId);
            Assert.Equal(receivedAt.ToUnixTimeSeconds(), lineage.Interaction.ReceivedAt.ToUnixTimeSeconds());
            Assert.Equal(current.SessionId, lineage.SessionId);
            Assert.True(lineage.Request.AuthenticationContext!.RequestedAcrValues.SequenceEqual([PasswordAcr]));
            Assert.True(lineage.Request.AuthenticationContext.Prompt == controls.GetValueOrDefault("prompt"));
            Assert.True(lineage.Request.AuthenticationContext.MaxAgeSeconds == (controls.TryGetValue("max_age", out var age) ? (long?)long.Parse(age, CultureInfo.InvariantCulture) : null));
            var tokenManager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>(); var requests = 0;
            await foreach (var token in tokenManager.FindByApplicationIdAsync(test.Owners.NativeApplicationId))
                if (await tokenManager.GetTypeAsync(token) == OpenIddictConstants.TokenTypeIdentifiers.Private.RequestToken)
                { requests++; Assert.True(await tokenManager.GetIdAsync(token) == pending.TokenId); }
            Assert.Equal(1, requests);
        }
        await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Redeemed);
        using var redeemed = await Redeem(test, new(code!, pending.Verifier, pending.Nonce)); Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        await AssertInteractionClaims(test, (await redeemed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id_token").GetString()!, current.AuthenticatedAt);
    }

    [Fact]
    public async Task InteractionDifferentSessionAuthenticatedOneMicrosecondBeforeRequestCannotSatisfyForcedLogin()
    {
        await using var test = await Create(continuation: true, interaction: true);
        var alpha = await ReadSourceSession(test);
        // Choose an instant inside one whole second so the one-microsecond difference cannot
        // be reconstructed from the public, second-precision authentication properties.
        var initial = test.Host.Clock.GetUtcNow();
        test.Host.Clock.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond - initial.Ticks % TimeSpan.TicksPerSecond)
            + TimeSpan.FromMilliseconds(100));
        var betaAuthenticatedAt = test.Host.Clock.GetUtcNow();
        // An independent anonymous browser has no previous session to replace: Alpha remains live.
        var betaBootstrap = await test.Host.Options();
        string betaCookies;
        using (var betaSignIn = await test.Host.Post("/authentication/sign-in", Command(betaBootstrap.Key), betaBootstrap.Cookies, betaBootstrap.Csrf))
        {
            Assert.Equal(HttpStatusCode.OK, betaSignIn.StatusCode); betaCookies = Merge(betaBootstrap.Cookies, betaSignIn);
        }
        await using (var read = test.Database.Context())
        {
            var beta = await read.Set<AuthenticationSessionRow>().SingleAsync(session => session.SessionId != alpha.SessionId);
            Assert.NotEqual(alpha.SessionId, beta.SessionId);
            Assert.Equal(2, await read.Set<AuthenticationSessionRow>().CountAsync(session => session.State == "ACTIVE"));
            Assert.Equal(betaAuthenticatedAt.ToUnixTimeSeconds(), beta.AuthenticatedAt.ToUnixTimeSeconds());
        }
        test.Host.Clock.Advance(TimeSpan.FromMicroseconds(1));
        var originalReceivedAt = test.Host.Clock.GetUtcNow();
        Assert.Equal(TimeSpan.FromMicroseconds(1), originalReceivedAt - betaAuthenticatedAt);
        Assert.Equal(betaAuthenticatedAt.ToUnixTimeSeconds(), originalReceivedAt.ToUnixTimeSeconds());
        using (var original = await RequestAuthorization(test, overrides: new Dictionary<string, string> { ["prompt"] = "login" }))
        {
            Assert.Equal(HttpStatusCode.Found, original.StatusCode); Assert.Equal("/login", original.Headers.Location?.OriginalString);
            test.Cookies = Merge(betaCookies, original);
        }
        await PrepareContinuation(test, await SessionContinuation(test, pending: true));
        using var replay = await test.Host.Get(AuthorizationPath, test.Cookies);
        Assert.Equal(HttpStatusCode.Found, replay.StatusCode); Assert.Equal("/login", replay.Headers.Location?.OriginalString);
        await AssertNoInteractionRelationship(test);
        await using var scope = test.Host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        var pending = 0;
        await foreach (var token in manager.FindByApplicationIdAsync(test.Owners.NativeApplicationId))
            if (await manager.GetTypeAsync(token) == OpenIddictConstants.TokenTypeIdentifiers.Private.RequestToken)
            { pending++; Assert.Equal(OpenIddictConstants.Statuses.Valid, await manager.GetStatusAsync(token)); }
        Assert.Equal(1, pending);
    }

    [Fact]
    public async Task InteractionPromptNoneUnmetFreshnessOrAssuranceNeverCreatesUiContinuation()
    {
        await using var test = await Create(continuation: true, interaction: true);
        foreach (var assurance in new[] { false, true })
        {
            var controls = new Dictionary<string, string> { ["prompt"] = "none" };
            if (assurance) controls["acr_values"] = StrongAcr; else controls["max_age"] = "0";
            using var response = await RequestAuthorization(test, overrides: controls);
            await AssertRejected(response, assurance ? "interaction_required" : "login_required", HttpStatusCode.OK);
            Assert.Null(response.Headers.Location);
            Assert.Null(CookieValue(Merge(test.Cookies, response), ProductAuthorizationContinuation.CookieName));
        }
        await AssertNoRequestTokens(test); await AssertNoInteractionRelationship(test);
    }

    [Theory]
    [InlineData("Policy")]
    [InlineData("Factor")]
    public async Task InteractionPolicyOrCurrentFactorDriftDeniesBeforeGrant(string drift)
    {
        await using var test = await Create(continuation: true, anonymous: true, interaction: true);
        var pending = await BeginContinuation(test, new Dictionary<string, string> { ["acr_values"] = PasswordAcr });
        await PrepareContinuation(test, await SignInContinuationBrowser(test));
        if (drift == "Policy") test.Owners.PolicyVersion = "fixture-product-policy-v2";
        else
        {
            await using var write = test.Database.Context(); var user = await write.Users.SingleAsync(user => user.AccountId == test.AccountId);
            user.SecurityStamp = Guid.NewGuid().ToString("N"); await write.SaveChangesAsync();
        }
        using var response = await test.Host.Get(AuthorizationPath, test.Cookies);
        await AssertRejected(response, drift == "Policy" ? "access_denied" : "invalid_request",
            drift == "Policy" ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Valid); await AssertNoInteractionRelationship(test);
    }

    [Fact]
    public async Task InteractionAuthenticationTimeRemainsActualThroughCodeAndRefresh()
    {
        await using var test = await Create(lifecycle: true, interaction: true); var original = await ReadSourceSession(test);
        test.Host.Clock.Advance(TimeSpan.FromSeconds(10));
        var code = await Authorize(test, new Dictionary<string, string> { ["max_age"] = "120", ["acr_values"] = PasswordAcr });
        test.Host.Clock.Advance(TimeSpan.FromSeconds(10));
        string refresh;
        using (var redeemed = await Redeem(test, code))
        {
            Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode); var json = await redeemed.Content.ReadFromJsonAsync<JsonElement>();
            await AssertInteractionClaims(test, json.GetProperty("id_token").GetString()!, original.AuthenticatedAt);
            refresh = json.GetProperty("refresh_token").GetString()!;
        }
        var beforeRefresh = await ReadSourceSession(test);
        Assert.Equal(original.AuthenticatedAt, beforeRefresh.AuthenticatedAt);
        test.Host.Clock.Advance(TimeSpan.FromSeconds(10));
        using var response = await Refresh(test, refresh); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refreshed = await response.Content.ReadFromJsonAsync<JsonElement>();
        await AssertInteractionClaims(test, refreshed.GetProperty("id_token").GetString()!, original.AuthenticatedAt);
        await AssertSourceSessionUnchanged(test, beforeRefresh);
    }

    [Fact]
    public async Task InteractionUnsupportedEssentialClaimsAreExplicitlyRejectedBeforeRelationship()
    {
        await using var test = await Create(continuation: true, interaction: true);
        using var response = await RequestAuthorization(test, overrides: new Dictionary<string, string>
            { ["claims"] = "{\"id_token\":{\"acr\":{\"essential\":true,\"values\":[\"urn:fixture:product:acr:strong\"]}}}" });
        await AssertRejected(response, "interaction_required", HttpStatusCode.OK);
        await AssertNoRequestTokens(test); await AssertNoInteractionRelationship(test);
    }

    [Fact]
    public async Task AnonymousRequestSurvivesActualPasswordSignInAndRedeemsItsExactOriginalBindings()
    {
        await using var test = await Create(continuation: true, anonymous: true);
        var pending = await BeginContinuation(test); var csrf = await SignInContinuationBrowser(test);
        await PrepareContinuation(test, csrf);
        using var authorized = await test.Host.Get(AuthorizationPath, test.Cookies);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        var form = ReadNativeForm(await authorized.Content.ReadAsStringAsync()); Assert.Equal(Redirect, form.Action);
        Assert.True(form.Values.GetValueOrDefault("state") == pending.State, "The exact original protocol state must survive sign-in.");
        Assert.True(form.Values.TryGetValue("code", out var code) && !string.IsNullOrWhiteSpace(code));
        Assert.False(form.Values.ContainsKey("access_token") || form.Values.ContainsKey("id_token") || form.Values.ContainsKey("refresh_token"));
        test.Cookies = Merge(test.Cookies, authorized);
        Assert.Null(CookieValue(test.Cookies, ProductAuthorizationContinuation.CookieName));
        await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Redeemed);
        _ = await SessionContinuation(test, pending: false);
        await using (var read = test.Database.Context())
        {
            var issuance = await read.Set<OutboxMessage>().SingleAsync(message => message.EventType == "ProductAuthorizationGranted");
            Assert.Equal(pending.OperationId, issuance.OperationId); Assert.Equal(pending.CorrelationId, issuance.CorrelationId);
            await using var scope = test.Host.Services.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            var authorization = await manager.FindByIdAsync(issuance.Subject); Assert.NotNull(authorization);
            var properties = await manager.GetPropertiesAsync(authorization!);
            var lineage = ProductAuthorizationApplication.ReadLineage(properties[ProductAuthorizationApplication.LineageProperty]); Assert.NotNull(lineage);
            var expected = new ProductAuthorizationRequest(Product, EnvironmentName, test.Owners.ClientId, Redirect, Scopes, Resources,
                Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(pending.Verifier))), "S256", "code", "form_post", pending.State, pending.Nonce);
            Assert.True(lineage!.Request.Digest == expected.Digest, "Restoration must preserve every original native authorization binding.");
            Assert.Equal(pending.OperationId, lineage.OperationId); Assert.Equal(pending.CorrelationId, lineage.CorrelationId);
            Assert.Equal(test.AccountId, lineage.AccountId);
            Assert.Equal((await read.Set<AuthenticationSessionRow>().SingleAsync()).SessionId, lineage.SessionId);
        }
        using var redeemed = await Redeem(test, new(code!, pending.Verifier, pending.Nonce));
        Assert.Equal(HttpStatusCode.OK, redeemed.StatusCode);
        var tokens = await redeemed.Content.ReadFromJsonAsync<JsonElement>();
        var identity = tokens.GetProperty("id_token").GetString(); var access = tokens.GetProperty("access_token").GetString();
        Assert.True(!string.IsNullOrWhiteSpace(identity) && !string.IsNullOrWhiteSpace(access));
        var validated = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(identity!, new TokenValidationParameters
        {
            ValidIssuer = new Uri(Origin).AbsoluteUri, ValidAudience = test.Owners.ClientId, IssuerSigningKey = test.SigningKey,
            TokenDecryptionKey = test.EncryptionKey, ValidateLifetime = true, ClockSkew = TimeSpan.Zero
        });
        Assert.True(validated.IsValid, validated.Exception?.GetType().Name ?? "Native artifact validation failed.");
        Assert.True(validated.ClaimsIdentity.FindFirst("nonce")?.Value == pending.Nonce, "The ID token must bind the original nonce.");
        await using var evidence = test.Database.Context();
        foreach (var effect in await evidence.Set<OutboxMessage>().Where(message => message.EventType.StartsWith("ProductAuthorization")).ToListAsync())
            Assert.True(new[] { code!, identity!, access!, pending.Verifier, pending.State, pending.Nonce, test.Owners.ClientCredential }
                .All(secret => !effect.Payload.Contains(secret, StringComparison.Ordinal)), "Continuation and protocol artifacts must remain outside durable effect payloads.");
    }

    [Fact]
    public async Task ConsumedContinuationCannotReplayWithItsOriginalReadyPointer()
    {
        await using var test = await Create(continuation: true, anonymous: true); var pending = await BeginContinuation(test);
        await PrepareContinuation(test, await SignInContinuationBrowser(test)); var readyCookies = test.Cookies;
        using (var first = await test.Host.Get(AuthorizationPath, readyCookies)) Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var replay = await test.Host.Get(AuthorizationPath, readyCookies)) await AssertRejected(replay, "invalid_request", HttpStatusCode.BadRequest);
        await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Redeemed);
        _ = await SessionContinuation(test, pending: false); await AssertEffects(test, 1, pending.Verifier, pending.State, pending.Nonce);
    }

    [Fact]
    public async Task RestoredRequestRerunsCurrentNativeClientPermissions()
    {
        await using var test = await Create(continuation: true, anonymous: true); var pending = await BeginContinuation(test);
        await PrepareContinuation(test, await SignInContinuationBrowser(test));
        await using (var scope = test.Host.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var application = await manager.FindByClientIdAsync(test.Owners.ClientId); Assert.NotNull(application);
            var descriptor = new OpenIddictApplicationDescriptor(); await manager.PopulateAsync(descriptor, application!);
            Assert.True(descriptor.Permissions.Remove(OpenIddictConstants.Permissions.Prefixes.Resource + Resource));
            await manager.UpdateAsync(application!, descriptor);
        }
        using var response = await test.Host.Get(AuthorizationPath, test.Cookies);
        await AssertRejected(response, "invalid_request", HttpStatusCode.BadRequest, "ID2192");
        await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Valid); await AssertEffects(test, 0);
    }

    [Theory]
    [InlineData("Policy")]
    [InlineData("Expiry")]
    [InlineData("TamperedPointer")]
    public async Task ChangedExpiredOrTamperedContinuationCannotRestoreOriginalRequest(string change)
    {
        await using var test = await Create(continuation: true, anonymous: true); var pending = await BeginContinuation(test);
        await PrepareContinuation(test, await SignInContinuationBrowser(test));
        if (change == "Policy") test.Owners.ContinuationPolicyVersion = "fixture-continuation-policy-v2";
        else if (change == "Expiry") test.Host.Clock.Advance(TimeSpan.FromMinutes(3));
        else
        {
            var pointer = CookieValue(test.Cookies, ProductAuthorizationContinuation.CookieName)!;
            var replacement = (pointer[0] == 'A' ? "B" : "A") + pointer[1..];
            test.Cookies = test.Cookies.Replace(ProductAuthorizationContinuation.CookieName + "=" + pointer,
                ProductAuthorizationContinuation.CookieName + "=" + replacement, StringComparison.Ordinal);
        }
        using var response = await test.Host.Get(AuthorizationPath, test.Cookies);
        await AssertRejected(response, "invalid_request", HttpStatusCode.BadRequest);
        _ = await SessionContinuation(test, pending: false);
        await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Valid); await AssertEffects(test, 0);
    }

    [Fact]
    public async Task AnonymousPromptNoneReturnsLoginRequiredWithoutCreatingContinuation()
    {
        await using var test = await Create(continuation: true, anonymous: true);
        using var response = await RequestAuthorization(test, overrides: new Dictionary<string, string> { ["prompt"] = "none" });
        await AssertRejected(response, "login_required", HttpStatusCode.OK);
        Assert.Null(response.Headers.Location); Assert.Null(CookieValue(Merge(test.Cookies, response), ProductAuthorizationContinuation.CookieName));
        await AssertNoRequestTokens(test); await AssertEffects(test, 0);
    }

    [Theory]
    [InlineData("Unauthenticated", HttpStatusCode.Unauthorized)]
    [InlineData("WrongCsrf", HttpStatusCode.BadRequest)]
    [InlineData("UnknownBody", HttpStatusCode.BadRequest)]
    [InlineData("ForeignOrigin", HttpStatusCode.Forbidden)]
    public async Task ResumeRequiresActualCurrentUserCsrfSameOriginAndExactBody(string rejection, HttpStatusCode status)
    {
        await using var test = await Create(continuation: true, anonymous: true); var pending = await BeginContinuation(test);
        var csrf = rejection == "Unauthenticated" ? string.Empty : await SignInContinuationBrowser(test);
        using var response = await test.Host.Post(ResumePath, rejection == "UnknownBody" ? new { returnUrl = "/account" } : (object)new { },
            test.Cookies, rejection == "WrongCsrf" ? "synthetic-invalid-csrf" : csrf,
            rejection == "ForeignOrigin" ? "https://foreign.example.test" : Origin);
        Assert.Equal(status, response.StatusCode); Assert.Null(response.Headers.Location);
        await AssertPendingStatus(test, pending, OpenIddictConstants.Statuses.Valid); await AssertEffects(test, 0);
        if (rejection != "Unauthenticated")
        {
            // Rejected preparation must leave the private pointer unready, even with a valid authenticated cookie.
            using var unprepared = await test.Host.Get(AuthorizationPath, test.Cookies);
            await AssertRejected(unprepared, "invalid_request", HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task InvalidAnonymousProductCannotCreateLoginRedirectOrPendingRequest()
    {
        await using var test = await Create(continuation: true, anonymous: true);
        using var response = await RequestAuthorization(test, overrides: new Dictionary<string, string> { ["product_id"] = "PRD-UNQUALIFIED-SYNTHETIC" });
        await AssertRejected(response, "access_denied", HttpStatusCode.OK); Assert.Null(response.Headers.Location);
        Assert.Null(CookieValue(Merge(test.Cookies, response), ProductAuthorizationContinuation.CookieName));
        await AssertNoRequestTokens(test); await AssertEffects(test, 0);
    }

    [Fact]
    public async Task UnavailableContinuationPolicyDeniesContinuationAndPreservesOrdinarySignInWithoutPointer()
    {
        await using var test = await Create(continuation: true, anonymous: true); test.Owners.ContinuationPolicyAvailable = false;
        using (var rejected = await RequestAuthorization(test))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, rejected.StatusCode); Assert.Null(rejected.Headers.Location);
            Assert.Null(CookieValue(Merge(test.Cookies, rejected), ProductAuthorizationContinuation.CookieName));
        }
        _ = await SignInContinuationBrowser(test, pending: false);
        await AssertNoRequestTokens(test); await AssertEffects(test, 0);
    }

    private static async Task AssertNoRequestTokens(Scenario test)
    {
        await using var scope = test.Host.Services.CreateAsyncScope(); var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        await foreach (var token in manager.FindByApplicationIdAsync(test.Owners.NativeApplicationId))
            Assert.False(await manager.GetTypeAsync(token) == OpenIddictConstants.TokenTypeIdentifiers.Private.RequestToken);
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
