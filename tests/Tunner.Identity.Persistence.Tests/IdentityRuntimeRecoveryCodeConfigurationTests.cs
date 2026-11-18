using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Xunit;
using static Tunner.Identity.Persistence.Tests.AdminIdentityConfigurationHttpTests;

namespace Tunner.Identity.Persistence.Tests;

public sealed class IdentityRuntimeRecoveryCodeConfigurationTests(SessionListingPostgresFixture fixture) : IClassFixture<SessionListingPostgresFixture>
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(20_000L)]
    [InlineData(9_007_199_254_740_993L)]
    [InlineData(long.MaxValue)]
    public async Task AllNineteenAndNineInputsRoundTripThroughApprovalWithoutLosingInt64Precision(long work)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var candidate = Candidate(env, "fixture-recovery-v1", work);
        var shape = JsonSerializer.SerializeToElement(candidate.Values); Assert.Equal(19, shape.EnumerateObject().Count()); Assert.Equal(9, shape.GetProperty("Crypto").EnumerateObject().Count());
        Assert.Equal(work.ToString(CultureInfo.InvariantCulture), shape.GetProperty("Crypto").GetProperty("MaximumHashWork").GetString());
        await using var owner = env.Open(); var p = await owner.Application.CreateRecoveryCodeDraftAsync(candidate, "OP-create", "fixture-correlation");
        Assert.Throws<IdentityConfigurationUnavailableException>(() => Runtime(env)); var preview = await owner.Application.PreviewAsync(env.Command(p)); Assert.NotNull(preview.RecoveryCodes);
        Assert.Equal(JsonSerializer.Serialize(candidate.Values), JsonSerializer.Serialize(preview.RecoveryCodes.Values));
        p = await owner.Application.ValidateAsync(env.Command(p)); await Assert.ThrowsAsync<IdentityConfigurationForbiddenException>(() => owner.Application.ApproveAsync(env.Command(p)));
        env.Authorization.Actor = "fixture-security-approver"; p = await owner.Application.ApproveAsync(env.Command(p)); env.Authorization.Actor = "fixture-security-publisher";
        var publication = await owner.Application.PublishAsync(env.Publication(p)); var runtime = Runtime(env); Assert.Equal(work, runtime.Values.Crypto.MaximumHashWork);
        Assert.Equal(JsonSerializer.Serialize(candidate.Values.ToPolicy()), JsonSerializer.Serialize(runtime.Values));
        var history = await owner.Application.HistoryPreviewAsync(new(IdentityConfigurationFamily.RecoveryCodes), publication.PublicationReference, "OP-history", "fixture-correlation");
        Assert.NotNull(history.RecoveryCodes); Assert.Equal(work, history.RecoveryCodes.Values.Crypto.MaximumHashWork);
        using var native = new RecoveryCodeNative(candidate.Values.Crypto.ToProfile()); Assert.True(native.Supports(runtime.Values.Crypto));
        await using var platform = env.Platform(); Assert.All(await platform.AuditEvents.ToArrayAsync(), x => Assert.True(x.VerifyIntegrity()));
        Assert.All(await platform.OutboxMessages.ToArrayAsync(), x => Assert.DoesNotContain("HashIterations", x.Payload, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("format")]
    [InlineData("native")]
    [InlineData("generation")]
    [InlineData("replacement")]
    [InlineData("session")]
    [InlineData("delivery")]
    public async Task ExistingSupportedMechanismsAndNativeVersionRemainAuthoritative(string field)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var c = Candidate(env, "fixture-invalid");
        c = c with { Values = field switch
        {
            "format" => c.Values with { FormatProfileVersion = "unsupported" }, "native" => c.Values with { Crypto = c.Values.Crypto with { NativeIdentityVersion = "unsupported" } },
            "generation" => c.Values with { GenerationAdmission = (RecoveryCodeGenerationAdmission)0 }, "replacement" => c.Values with { ReplacementMechanism = (RecoveryCodeReplacementMechanism)0 },
            "session" => c.Values with { SessionEffect = (RecoveryCodeSessionEffect)0 }, _ => c.Values with { DeliveryMechanism = (RecoveryCodeDeliveryMechanism)0 }
        } };
        await using var owner = env.Open();
        if (field == "native") await Assert.ThrowsAsync<ArgumentException>(() => owner.Application.CreateRecoveryCodeDraftAsync(c, "OP-invalid", "fixture-correlation"));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Application.CreateRecoveryCodeDraftAsync(c, "OP-invalid", "fixture-correlation"));
        await AssertNoEffects(env);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("lifetime")]
    [InlineData("work")]
    [InlineData("codes")]
    public async Task ExistingPolicyAndCryptoBoundsRejectInvalidInputsBeforeWrites(string field)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var c = Candidate(env, "fixture-invalid");
        c = c with { Values = field switch { "origin" => c.Values with { Origin = "http://unsafe.example.test" }, "lifetime" => c.Values with { OperationLifetime = TimeSpan.Zero },
            "work" => c.Values with { Crypto = c.Values.Crypto with { MaximumHashWork = 1 } }, _ => c.Values with { Crypto = c.Values.Crypto with { CodesPerSet = c.Values.Crypto.MaximumCodesPerSet + 1 } } } };
        await using var owner = env.Open();
        if (field is "work" or "codes") await Assert.ThrowsAsync<ArgumentException>(() => owner.Application.CreateRecoveryCodeDraftAsync(c, "OP-invalid", "fixture-correlation"));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => owner.Application.CreateRecoveryCodeDraftAsync(c, "OP-invalid", "fixture-correlation"));
        await AssertNoEffects(env);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("scope")]
    [InlineData("status")]
    public async Task MetadataMustMatchExactPolicyVersionAndActiveRecoveryScope(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var c = Candidate(env, "fixture-recovery-v1");
        c = c with { Metadata = new(failure == "version" ? "other" : c.Metadata.VersionId, failure == "status" ? "DRAFT" : "ACTIVE", failure == "scope" ? "AUTHENTICATION" : "RECOVERY_CODES", env.Interval, null, ["SYNTHETIC-ONLY"]) };
        await using var owner = env.Open(); await Assert.ThrowsAsync<IdentityConfigurationUnavailableException>(() => owner.Application.CreateRecoveryCodeDraftAsync(c, "OP-invalid", "fixture-correlation")); await AssertNoEffects(env);
    }

    [Fact]
    public async Task ActualAdminLifecycleReplayAndRollbackFeedExistingExplicitlyAdmittedUserOptions()
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var admin = await Host.Start(db, additionalScopes: [new(IdentityConfigurationFamily.RecoveryCodes)]);
        var env = admin.Environment; var author = await admin.Browser("fixture-security-author"); var approver = await admin.Browser("fixture-security-approver"); var publisher = await admin.Browser("fixture-security-publisher");
        var command = new { idempotencyKey = Guid.NewGuid(), candidate = Candidate(env, "fixture-recovery-v1") }; JsonElement p;
        using (var create = await admin.Post("/recovery-codes/draft", command, author)) { Assert.Equal(HttpStatusCode.OK, create.StatusCode); p = (await Body(create)).GetProperty("result").Clone(); }
        admin.Owners.Denied = IdentityConfigurationOperation.CreateDraft;
        using (var denied = await admin.Post("/recovery-codes/draft", command, author)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); admin.Owners.Denied = null;
        using (var replay = await admin.Post("/recovery-codes/draft", command, author)) { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.True((await Body(replay)).GetProperty("replayed").GetBoolean()); }
        using (var conflict = await admin.Post("/recovery-codes/draft", new { command.idempotencyKey, candidate = Candidate(env, "fixture-changed") }, author)) Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using (var update = await admin.Post("/recovery-codes/draft", new { idempotencyKey = Guid.NewGuid(), candidate = Candidate(env, "fixture-recovery-v2"), proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64() }, author))
        { Assert.Equal(HttpStatusCode.OK, update.StatusCode); p = (await Body(update)).GetProperty("result").Clone(); }
        using (var preview = await admin.Post("/proposals/preview", new { proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64() }, author))
        { Assert.Equal(HttpStatusCode.OK, preview.StatusCode); Assert.Equal("20000", (await Body(preview)).GetProperty("recoveryCodes").GetProperty("values").GetProperty("crypto").GetProperty("maximumHashWork").GetString()); }
        p = await Step(admin, author, "validate", p); p = await Step(admin, approver, "approve", p); var first = await Publish(admin, publisher, p);
        var profile = new RecoveryCodePrivateIntegrationTests.Profile(); await using var user = await AuthenticationTestHost.Start(db, services => ComposeUser(services, env, profile, profile.Crypto));
        Assert.Equal(user.Clock.GetUtcNow(), env.Clock.GetUtcNow()); await user.SeedAsync(db); var cookies = await TotpTests.Login(user);
        using (var options = await user.Get("/account/recovery-codes/options", cookies))
        { Assert.Equal(HttpStatusCode.OK, options.StatusCode); var body = await Body(options); Assert.Equal("fixture-recovery-v2", body.GetProperty("policyVersion").GetString()); Assert.Equal(profile.Crypto.CodesPerSet, body.GetProperty("codesPerSet").GetInt32()); }
        env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var create = await admin.Post("/recovery-codes/draft", new { idempotencyKey = Guid.NewGuid(), candidate = Candidate(env, "fixture-recovery-v3") }, author))
        { Assert.Equal(HttpStatusCode.OK, create.StatusCode); p = (await Body(create)).GetProperty("result").Clone(); }
        p = await Step(admin, author, "validate", p); p = await Step(admin, approver, "approve", p); var latest = await Publish(admin, publisher, p, first.GetProperty("publicationReference").GetGuid()); Assert.Equal("fixture-recovery-v3", Runtime(env).Values.Version);
        env.Clock.Advance(TimeSpan.FromSeconds(1)); user.Clock.Advance(TimeSpan.FromSeconds(1));
        using (var rollback = await admin.Post("/rollback", new { idempotencyKey = Guid.NewGuid(), approvedVersionReference = first.GetProperty("versionReference").GetGuid(), supersedesPublicationReference = latest.GetProperty("publicationReference").GetGuid(), effectiveInterval = Interval(env.Clock.GetUtcNow()) }, publisher)) Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        Assert.Equal("fixture-recovery-v2", Runtime(env).Values.Version);
        using (var history = await admin.Post("/history/preview", new { scope = new { family = "RecoveryCodes" }, publicationReference = first.GetProperty("publicationReference").GetGuid() }, publisher))
        { Assert.Equal(HttpStatusCode.OK, history.StatusCode); Assert.Equal("20000", (await Body(history)).GetProperty("recoveryCodes").GetProperty("values").GetProperty("crypto").GetProperty("maximumHashWork").GetString()); }
        await AssertNoRecoveryMutation(db);
    }

    [Theory]
    [InlineData(9_007_199_254_740_993L)]
    [InlineData(long.MaxValue)]
    public async Task ActualHttpCandidatePreviewAndHistoryPreserveMaximumHashWorkAsDecimalString(long work)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var admin = await Host.Start(db, additionalScopes: [new(IdentityConfigurationFamily.RecoveryCodes)]);
        var author = await admin.Browser("fixture-security-author"); var approver = await admin.Browser("fixture-security-approver"); var publisher = await admin.Browser("fixture-security-publisher"); JsonElement p;
        using (var create = await admin.Post("/recovery-codes/draft", new { idempotencyKey = Guid.NewGuid(), candidate = Candidate(admin.Environment, "fixture-precise", work) }, author))
        { Assert.Equal(HttpStatusCode.OK, create.StatusCode); p = (await Body(create)).GetProperty("result").Clone(); }
        using (var preview = await admin.Post("/proposals/preview", new { proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64() }, author))
        { Assert.Equal(HttpStatusCode.OK, preview.StatusCode); Assert.Equal(work.ToString(CultureInfo.InvariantCulture), (await Body(preview)).GetProperty("recoveryCodes").GetProperty("values").GetProperty("crypto").GetProperty("maximumHashWork").GetString()); }
        p = await Step(admin, author, "validate", p); p = await Step(admin, approver, "approve", p); var publication = await Publish(admin, publisher, p);
        using (var history = await admin.Post("/history/preview", new { scope = new { family = "RecoveryCodes" }, publicationReference = publication.GetProperty("publicationReference").GetGuid() }, publisher))
        { Assert.Equal(HttpStatusCode.OK, history.StatusCode); Assert.Equal(work.ToString(CultureInfo.InvariantCulture), (await Body(history)).GetProperty("recoveryCodes").GetProperty("values").GetProperty("crypto").GetProperty("maximumHashWork").GetString()); }
        Assert.Equal(work, Runtime(admin.Environment).Values.Crypto.MaximumHashWork);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("native")]
    [InlineData("overflow")]
    public async Task InvalidHttpPolicyValuesAre400WithNoOwnerEffects(string failure)
    {
        await using var db = await fixture.CreateDatabaseAsync(); await using var admin = await Host.Start(db, additionalScopes: [new(IdentityConfigurationFamily.RecoveryCodes)]); var actor = await admin.Browser("fixture-security-author");
        await using var before = admin.Environment.Platform();
        var outboxBefore = JsonSerializer.Serialize(await before.OutboxMessages.AsNoTracking().OrderBy(x => x.EventId).ToArrayAsync());
        var auditBefore = JsonSerializer.Serialize(await before.AuditEvents.AsNoTracking().OrderBy(x => x.EventId).ToArrayAsync());
        var body = JsonSerializer.SerializeToNode(new { idempotencyKey = Guid.NewGuid(), candidate = Candidate(admin.Environment, "fixture-invalid") }, WebJson)!;
        if (failure == "format") body["candidate"]!["values"]!["formatProfileVersion"] = "unsupported";
        else body["candidate"]!["values"]!["crypto"]![failure == "native" ? "nativeIdentityVersion" : "maximumHashWork"] = failure == "native" ? "unsupported" : "9223372036854775808";
        using var response = await admin.Post("/recovery-codes/draft", body, actor); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var after = admin.Environment.Platform(); var messages = await after.OutboxMessages.AsNoTracking().OrderBy(x => x.EventId).ToArrayAsync();
        Assert.Equal(outboxBefore, JsonSerializer.Serialize(messages));
        Assert.Equal(auditBefore, JsonSerializer.Serialize(await after.AuditEvents.AsNoTracking().OrderBy(x => x.EventId).ToArrayAsync()));
        // Configuration command receipts live in the existing atomic outbox payload, not a separate receipt table.
        foreach (var message in messages) { using var payload = JsonDocument.Parse(message.Payload); Assert.False(payload.RootElement.TryGetProperty("commandReceipt", out _)); }
        await using var configuration = db.Context(); Assert.Empty(await configuration.Set<IdentityConfigurationProposalRow>().ToArrayAsync());
        Assert.Empty(await configuration.Set<IdentityConfigurationVersionRow>().ToArrayAsync()); Assert.Empty(await configuration.Set<IdentityConfigurationPublicationRow>().ToArrayAsync());
    }

    [Fact]
    public async Task MissingExplicitNativeAdmissionRejectsHostComposition()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var profile = new RecoveryCodePrivateIntegrationTests.Profile();
        await Assert.ThrowsAsync<InvalidOperationException>(() => AuthenticationTestHost.Start(db, services => ComposeUser(services, env, profile, null)));
        await AssertNoRecoveryMutation(db);
    }

    [Fact]
    public async Task RuntimePolicyCannotUseADifferentAdmittedNativeProfile()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); _ = await PublishOwner(env, Candidate(env, "fixture-recovery-v1"));
        var profile = new RecoveryCodePrivateIntegrationTests.Profile(); var crypto = Candidate(env, "fixture-only").Values.Crypto with { MaximumConcurrentWork = 2 };
        await using var host = await AuthenticationTestHost.Start(db, services => ComposeUser(services, env, profile, crypto.ToProfile())); await host.SeedAsync(db); var cookies = await TotpTests.Login(host);
        using var response = await host.Get("/account/recovery-codes/options", cookies); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(); Assert.Contains("DEPENDENCY_UNAVAILABLE", body, StringComparison.Ordinal); Assert.DoesNotContain("fixture-recovery-crypto", body, StringComparison.Ordinal); await AssertNoRecoveryMutation(db);
    }

    [Fact]
    public void ExistingNativeAdmissionMatchesEveryCryptoInputWithoutRunningHashWork()
    {
        var c = Crypto(30_000); using var native = new RecoveryCodeNative(c.ToProfile()); Assert.True(native.Supports(c.ToProfile()));
        var variations = new[] { c with { Version = "other" }, c with { CodesPerSet = 1 }, c with { MaximumCodesPerSet = 3 },
            c with { HashIterations = 9_999 }, c with { MaximumHashWork = 20_001 }, c with { MaximumGenerationAttempts = 9 }, c with { MaximumConcurrentWork = 2 }, c with { MaximumWorkDuration = TimeSpan.FromSeconds(11) } };
        foreach (var changed in variations) Assert.False(native.Supports(changed.ToProfile()));
        Assert.Throws<ArgumentException>(() => (c with { NativeIdentityVersion = "unsupported" }).ToProfile());
        var differentSupportedVersion = c with { NativeIdentityVersion = c.NativeIdentityVersion == "10.0.13" ? "10.0.15" : "10.0.13" };
        Assert.False(native.Supports(differentSupportedVersion.ToProfile()));
        Assert.Throws<InvalidOperationException>(() => new RecoveryCodeNative(differentSupportedVersion.ToProfile()));
    }

    [Fact]
    public async Task ExpiredPublicationCannotSupplyRuntimePolicy()
    {
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var interval = new EffectivePolicyInterval(env.Clock.GetUtcNow(), true, env.Clock.GetUtcNow().AddSeconds(1), false);
        _ = await PublishOwner(env, Candidate(env, "fixture-expiring"), interval); Assert.Equal("fixture-expiring", Runtime(env).Values.Version);
        env.Clock.Advance(TimeSpan.FromSeconds(1)); Assert.Throws<IdentityConfigurationUnavailableException>(() => Runtime(env));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public async Task ReconstructedTenFamilyPayloadAndScopeBytesRemainUnchanged(int ordinal)
    {
        // RECONSTRUCTED from checkpoint afd86dd3 format with explicit fixture values; not retained historical runtime bytes.
        await using var db = await fixture.CreateDatabaseAsync(); var env = new RuntimeConfigurationTestEnvironment(db); var registration = IdentityRuntimeAdditionalConfigurationTests.Registration(env, "fixture-registration-v2");
        var legacy = new LegacyDocument(ordinal == 0 ? env.Authentication("fixture-old") : null, ordinal == 1 ? env.Assurance("fixture-old") : null, ordinal == 2 ? env.Binding("fixture-old") : null,
            ordinal == 3 ? registration : null, ordinal == 4 ? IdentityRuntimeAdditionalConfigurationTests.Totp(env, "fixture-old") : null,
            ordinal == 5 ? new(registration.Values.Version, registration.Values.TermsVersion, registration.Values.PrivacyVersion, "fixture-old", "fixture-only", "fixture-content", "fixture-variant", env.Interval, new("https://fixture.example.test/terms"), new("https://fixture.example.test/privacy"), null) : null,
            ordinal == 6 ? new(new("fixture-passkey-v1", "ACTIVE", "PASSKEY", env.Interval, null, ["SYNTHETIC-ONLY"]), PasskeyValues()) : null,
            ordinal == 7 ? new("fixture-totp-v1", "fixture-old", "fixture-only", "fixture-content", "fixture-variant", env.Interval) : null,
            ordinal == 8 ? new("fixture-passkey-v1", "fixture-old", "fixture-only", "fixture-content", "fixture-variant", env.Interval) : null,
            ordinal == 9 ? new(StepUpBrowserTests.Scope, "fixture-step-v1", "fixture-old", "fixture-only", "fixture-content", "fixture-variant", env.Interval) : null);
        var golden = JsonSerializer.Serialize(legacy); var current = IdentityConfigurationDocument.Read(golden); Assert.Equal(Encoding.UTF8.GetBytes(golden), Encoding.UTF8.GetBytes(current.Snapshot)); Assert.Equal(Sha(golden), current.Digest);
        var s = current.Scope; var oldScope = JsonSerializer.Serialize(new LegacyScope(ordinal, s.AssuranceScope, s.AuthenticationPolicyVersion, s.RegistrationPolicyVersion, s.TotpPolicyVersion, s.PasskeyPolicyVersion, s.AssurancePolicyVersion));
        Assert.Equal(oldScope, s.Key); Assert.Equal(Sha(oldScope), Sha(s.Key)); Assert.Equal(ordinal, (int)s.Family); Assert.Null(current.RecoveryCodes); Assert.Equal(10, (int)IdentityConfigurationFamily.RecoveryCodes);
    }

    [Fact]
    public void SeparatePolicyOptInDoesNotSelectNativeAdmissionOrPresentation()
    {
        var old = new ServiceCollection(); old.AddTunnerPersistedIdentityRuntimeConfiguration(); Assert.DoesNotContain(old, x => x.ServiceType == typeof(IRecoveryCodePolicySource));
        var services = new ServiceCollection(); services.AddTunnerPersistedRecoveryCodeConfiguration(); Assert.Contains(services, x => x.ServiceType == typeof(IRecoveryCodePolicySource));
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(RecoveryCodeCryptoProfile) || x.ServiceType == typeof(RecoveryCodeNative) || x.ServiceType == typeof(IRecoveryCodePresentationSource) || x.ServiceType == typeof(IRecoveryCodeMutationAdmissionSource) || x.ServiceType == typeof(RecoveryCodeApplication));
        Assert.Throws<InvalidOperationException>(() => services.AddTunnerPersistedRecoveryCodeConfiguration());
    }

    private sealed record LegacyDocument(AuthenticationPolicyVersion? Authentication, AssurancePolicyVersion? Assurance, AuthenticationContentBinding? ContentBinding,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RegistrationPolicyVersion? Registration,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TotpPolicyVersion? Totp,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RegistrationContentBinding? RegistrationContent,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PasskeyConfigurationVersion? Passkey,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TotpContentBinding? TotpContent,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PasskeyContentBinding? PasskeyContent,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StepUpContentBinding? StepUpContent);
    private sealed record LegacyScope(int Family, AssurancePolicyScope? AssuranceScope, string? AuthenticationPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RegistrationPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TotpPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PasskeyPolicyVersion,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AssurancePolicyVersion);
    private static PasskeyConfigurationValues PasskeyValues() => new("fixture-passkey-v1", "authentication.example.test", AuthenticationTestHost.Origin, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), PasskeyRevocationSessionEffect.PreserveExistingSessions,
        "required", new[] { -7 }, 32, 32768, 4, 4, "fixture-enrollment-v1", "fixture-revocation-v1", "fixture-processing", "fixture-retention", "required", "none",
        new Dictionary<string, string> { [PasskeyPolicy.AssuranceKey(false, false)] = "fixture-passkey-assurance", [PasskeyPolicy.AssuranceKey(true, false)] = "fixture-syncable-unbacked-assurance", [PasskeyPolicy.AssuranceKey(true, true)] = "fixture-syncable-backed-assurance" });
    private static RecoveryCodeConfigurationCrypto Crypto(long work) => new("fixture-recovery-crypto-v1", Tunner.Testing.Totp.NativeTotpCodeFixture.IdentityVersion, 2, 2, 10_000, work, 8, 1, TimeSpan.FromSeconds(10));
    private static RecoveryCodeConfigurationVersion Candidate(RuntimeConfigurationTestEnvironment env, string version, long work = 20_000) => new(
        new(version, "ACTIVE", "RECOVERY_CODES", env.Interval, null, ["client-proposed-evidence-is-not-authority"]),
        new(version, AuthenticationTestHost.Origin, Crypto(work), "NATIVE_HEX32_V1", "fixture-recovery-delivery-v1", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), 2, 8,
            RecoveryCodeGenerationAdmission.WithinApprovedActiveSetCapacity, RecoveryCodeReplacementMechanism.RetireExactExpectedSetsAtIssueCommit, RecoveryCodeSessionEffect.NoSessionMutation,
            RecoveryCodeDeliveryMechanism.OneVolatileViewWithOptionalSingleLocalDownload, "fixture-recovery-proof", "fixture-recovery-proof", 20, 100, "fixture-processing", "fixture-retention"));
    private static RecoveryCodePolicyVersion Runtime(RuntimeConfigurationTestEnvironment env) => ((IRecoveryCodePolicySource)env.Source).RequireEffective(env.Clock.GetUtcNow());
    private static void ComposeUser(IServiceCollection services, RuntimeConfigurationTestEnvironment env, RecoveryCodePrivateIntegrationTests.Profile profile, RecoveryCodeCryptoProfile? admitted)
    {
        services.AddSingleton<TimeProvider>(env.Clock); services.AddTunnerPersistedRecoveryCodeConfiguration(); if (admitted is not null) services.AddSingleton(admitted);
        // Only this isolated fixture supplies the existing native admission and local impact publication owner.
        services.AddSingleton<IRecoveryCodeMutationAdmissionSource>(profile); services.AddSingleton<IRecoveryCodePresentationSource>(profile); services.AddSingleton<IAssurancePolicySource>(profile); services.AddSingleton<IStepUpPresentationSource>(profile);
    }
    private static async Task<IdentityConfigurationHistory> PublishOwner(RuntimeConfigurationTestEnvironment env, RecoveryCodeConfigurationVersion candidate, EffectivePolicyInterval? interval = null)
    {
        await using var owner = env.Open(); env.Authorization.Actor = "fixture-security-author"; var p = await owner.Application.CreateRecoveryCodeDraftAsync(candidate, "OP-create", "fixture-correlation"); p = await owner.Application.ValidateAsync(env.Command(p));
        env.Authorization.Actor = "fixture-security-approver"; p = await owner.Application.ApproveAsync(env.Command(p)); env.Authorization.Actor = "fixture-security-publisher";
        return await owner.Application.PublishAsync(env.Publication(p) with { EffectiveInterval = interval ?? new(env.Clock.GetUtcNow(), true, null, false) });
    }
    private static async Task AssertNoEffects(RuntimeConfigurationTestEnvironment env)
    { await using var platform = env.Platform(); Assert.Empty(await platform.OutboxMessages.ToArrayAsync()); Assert.Empty(await platform.AuditEvents.ToArrayAsync()); }
    private static async Task AssertNoRecoveryMutation(IdentityTestDatabase db)
    { await using var context = db.Context(); Assert.Empty(await context.Set<RecoveryCodeSetRow>().ToArrayAsync()); Assert.Empty(await context.Set<RecoveryCodeEntryRow>().ToArrayAsync()); Assert.Empty(await context.Set<RecoveryCodeOperationRow>().ToArrayAsync()); }
    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static object Interval(DateTimeOffset at) => new { from = at, includesFrom = true, until = (DateTimeOffset?)null, includesUntil = false };
    private static async Task<JsonElement> Body(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    private static async Task<JsonElement> Step(Host h, Browser actor, string operation, JsonElement p)
    {
        using var response = await h.Post("/proposals/" + operation, new { idempotencyKey = Guid.NewGuid(), proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64() }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
    private static async Task<JsonElement> Publish(Host h, Browser actor, JsonElement p, Guid? supersedes = null)
    {
        using var response = await h.Post("/proposals/publish", new { idempotencyKey = Guid.NewGuid(), proposalReference = p.GetProperty("proposalReference").GetGuid(), expectedRevision = p.GetProperty("revision").GetInt64(), effectiveInterval = Interval(h.Environment.Clock.GetUtcNow()), supersedesPublicationReference = supersedes }, actor);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await Body(response)).GetProperty("result").Clone();
    }
}
