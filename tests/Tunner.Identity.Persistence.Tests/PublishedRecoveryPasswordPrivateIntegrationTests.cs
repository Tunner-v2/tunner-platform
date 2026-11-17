using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Database.Outbox;
using Tunner.PresentationContent;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

// PRIVATE FIXED COHORT ONLY. No xUnit attributes/reflection/discovery or output of code/credential/browser/row material.
internal sealed partial class RecoveryPasswordRepairPrivateIntegrationTests
{
    internal async Task PublishedPasswordRecoveryUsesActualContentAndPrivateNativeRepair()
    {
        var checkpoint = new RecoveryCodeFirstCaseCheckpoint();
        try
        {
            await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority();
            checkpoint.Mark(RecoveryCodeFirstCaseStage.HOST); var configured = await PublishedPasswordHost(db, authority, false); await using var host = configured.Host;
            checkpoint.Mark(RecoveryCodeFirstCaseStage.SEED); using var seed = await SeedAsync(db, host, authority);
            string priorHash; string priorStamp;
            await using (var before = db.Context()) { var user = await before.Users.SingleAsync(row => row.AccountId == seed.Account); priorHash = user.PasswordHash!; priorStamp = user.SecurityStamp!; }
            checkpoint.Mark(RecoveryCodeFirstCaseStage.OPTIONS_GET); using var bootstrap = await host.Get("/authentication/options"); Assert.True(bootstrap.StatusCode == HttpStatusCode.OK);
            var boot = await bootstrap.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(boot.GetProperty("recoveryAvailable").GetBoolean());
            var cookies = AuthenticationTestHost.Merge(null, bootstrap); var csrf = boot.GetProperty("csrfToken").GetString()!;
            using (var options = await host.Get("/recovery/options", cookies))
            {
                Assert.True(options.StatusCode == HttpStatusCode.OK && NoNewUserCookie(options)); var safe = await options.Content.ReadAsStringAsync(); var body = JsonSerializer.Deserialize<JsonElement>(safe);
                Assert.True(body.GetProperty("available").GetBoolean() && body.GetProperty("goals")[0].GetProperty("reference").GetString() == authority.Policy.GoalReference);
                var text = body.GetProperty("content").GetProperty("fixture.goal");
                Assert.True(text.GetProperty("publicationRef").GetString() == configured.Publications["fixture.goal"].PublicationReference.ToString("N") && text.GetProperty("versionRef").GetString() == configured.Publications["fixture.goal"].VersionReference.ToString("N"));
                Assert.True(!safe.Contains(seed.Account, StringComparison.Ordinal) && !safe.Contains("person@example.test", StringComparison.OrdinalIgnoreCase));
            }
            var request = Guid.NewGuid(); long expectedVersion;
            checkpoint.Mark(RecoveryCodeFirstCaseStage.START_POST);
            using (var started = await host.Post("/recovery/start", new { requestId = request, email = "person@example.test", goalReference = authority.Policy.GoalReference }, cookies, csrf))
            {
                Assert.True(started.StatusCode == HttpStatusCode.OK && NoNewUserCookie(started)); cookies = AuthenticationTestHost.Merge(cookies, started);
                var entry = (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operation"); expectedVersion = entry.GetProperty("version").GetInt64();
                Assert.True(entry.GetProperty("status").GetString() == "AWAITING_PROOF" && !entry.GetProperty("authenticated").GetBoolean());
            }
            await Unspent(db, seed, priorHash, priorStamp);
            checkpoint.Mark(RecoveryCodeFirstCaseStage.PASSWORD_PROOF_POST);
            using (var repaired = await host.Post("/recovery/repair", new { requestId = request, expectedVersion, code = seed.Code.NativeInput(), password = Replacement, passwordConfirmation = Replacement }, cookies, csrf))
            {
                Assert.True(repaired.StatusCode == HttpStatusCode.OK && NoNewUserCookie(repaired)); cookies = AuthenticationTestHost.Merge(cookies, repaired);
                var safe = await repaired.Content.ReadAsStringAsync(); var operation = JsonSerializer.Deserialize<JsonElement>(safe).GetProperty("operation");
                Assert.True(operation.GetProperty("status").GetString() == "PASSWORD_REPAIRED_NO_SESSION" && !operation.GetProperty("authenticated").GetBoolean());
                var result = operation.GetProperty("result");
                Assert.True(result.GetProperty("outcome").GetString() == "PASSWORD_REPAIRED_NO_SESSION");
                Assert.True(result.GetProperty("effects")[0].GetProperty("kind").GetString() == "TUNNER_SESSION_INVALIDATION");
                Assert.True(result.GetProperty("followUps").EnumerateArray().Select(value => value.GetProperty("reference").GetString()!).Order(StringComparer.Ordinal).SequenceEqual(authority.Policy.UnresolvedDownstreamEffects.Order(StringComparer.Ordinal), StringComparer.Ordinal));
                var expected = new Presentation().RequirePublished(authority.Policy, host.Clock.GetUtcNow());
                foreach (var follow in result.GetProperty("followUps").EnumerateArray())
                {
                    var qualified = expected.FollowUps.Single(value => value.Reference == follow.GetProperty("reference").GetString());
                    Assert.True(qualified.LabelKey == follow.GetProperty("labelKey").GetString() && qualified.ValueKey == follow.GetProperty("valueKey").GetString());
                }
                Assert.True(!safe.Contains(Replacement, StringComparison.Ordinal) && !safe.Contains(seed.Code.NativeInput(), StringComparison.Ordinal) && !safe.Contains(seed.Account, StringComparison.Ordinal));
            }
            checkpoint.Mark(RecoveryCodeFirstCaseStage.RESULT_POST);
            using (var original = await host.Post("/recovery/result", new { requestId = request }, cookies, csrf))
                Assert.True(original.StatusCode == HttpStatusCode.OK && NoNewUserCookie(original) && (await original.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("operation").GetProperty("requestId").GetGuid() == request);
            using (var anonymous = await host.Get("/authentication/session", cookies)) Assert.True(anonymous.StatusCode == HttpStatusCode.Unauthorized);
            using (var oldFirst = await host.Get("/authentication/session", seed.First.Cookies)) Assert.True(oldFirst.StatusCode == HttpStatusCode.Unauthorized);
            using (var oldSecond = await host.Get("/authentication/session", seed.Second.Cookies)) Assert.True(oldSecond.StatusCode == HttpStatusCode.Unauthorized);
            checkpoint.Mark(RecoveryCodeFirstCaseStage.CHECK_CONTEXT);
            await using var check = db.Context(); var changed = await check.Users.SingleAsync(row => row.AccountId == seed.Account); var native = AuthenticationPasswordWork.Hasher(host.Policies.Policy);
            Assert.True(changed.PasswordHash != priorHash && changed.SecurityStamp != priorStamp && native.VerifyHashedPassword(changed, changed.PasswordHash!, Replacement) != PasswordVerificationResult.Failed);
            Assert.True(await check.Set<AuthenticationSessionRow>().CountAsync() == 2 && await check.Set<RecoveryCodeEntryRow>().CountAsync(row => row.ConsumedAt != null) == 1 && await check.Set<RecoveryResultRow>().CountAsync() == 1);
            Assert.True(!await check.Set<OutboxMessage>().AnyAsync(row => row.EventType == "RecoveryCompleted"));
        }
        catch (Exception error) when (error is not OutOfMemoryException) { throw new RecoveryCodeFirstCaseFailure(checkpoint.Phase, checkpoint.Stage); }
    }

    internal async Task PublishedPasswordRecoveryPublicationExpiryLeavesCodeAndCredentialUnchanged()
    {
        var checkpoint = new RecoveryCodeFirstCaseCheckpoint();
        try
        {
            await using var db = await fixture.CreateFiniteDatabaseAsync(CancellationToken.None); var authority = new Authority();
            checkpoint.Mark(RecoveryCodeFirstCaseStage.HOST); var configured = await PublishedPasswordHost(db, authority, true); await using var host = configured.Host;
            checkpoint.Mark(RecoveryCodeFirstCaseStage.SEED); using var seed = await SeedAsync(db, host, authority); string hash; string stamp;
            await using (var before = db.Context()) { var user = await before.Users.SingleAsync(row => row.AccountId == seed.Account); hash = user.PasswordHash!; stamp = user.SecurityStamp!; }
            using var bootstrap = await host.Get("/authentication/options"); Assert.True(bootstrap.StatusCode == HttpStatusCode.OK); var boot = await bootstrap.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(boot.GetProperty("recoveryAvailable").GetBoolean()); var cookies = AuthenticationTestHost.Merge(null, bootstrap); var csrf = boot.GetProperty("csrfToken").GetString()!;
            var request = Guid.NewGuid();
            using (var started = await host.Post("/recovery/start", new { requestId = request, email = "person@example.test", goalReference = authority.Policy.GoalReference }, cookies, csrf))
            { Assert.True(started.StatusCode == HttpStatusCode.OK && NoNewUserCookie(started)); cookies = AuthenticationTestHost.Merge(cookies, started); }
            host.Clock.Advance(TimeSpan.FromSeconds(2)); checkpoint.Mark(RecoveryCodeFirstCaseStage.PASSWORD_PROOF_POST);
            using (var rejected = await host.Post("/recovery/repair", new { requestId = request, expectedVersion = 1, code = seed.Code.NativeInput(), password = Replacement, passwordConfirmation = Replacement }, cookies, csrf))
                Assert.True(rejected.StatusCode == HttpStatusCode.ServiceUnavailable && NoNewUserCookie(rejected));
            using (var options = await host.Get("/recovery/options", cookies)) Assert.True(options.StatusCode == HttpStatusCode.ServiceUnavailable && NoNewUserCookie(options));
            using (var primary = await host.Get("/authentication/options", cookies))
            { Assert.True(primary.StatusCode == HttpStatusCode.OK); Assert.True(!(await primary.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("recoveryAvailable", out _)); }
            using (var screen = await host.Get("/recovery", cookies)) Assert.True(screen.StatusCode == HttpStatusCode.Found && screen.Headers.Location?.OriginalString == "/login");
            checkpoint.Mark(RecoveryCodeFirstCaseStage.CHECK_CONTEXT); await Unspent(db, seed, hash, stamp);
            await using var check = db.Context(); Assert.True(await check.Set<RecoveryEntryRow>().AllAsync(row => row.State == "OPEN" && row.Version == 1) && !await check.Set<RecoveryResultRow>().AnyAsync());
            Assert.True(authority.RequireEffective(host.Clock.GetUtcNow()).Metadata.EffectiveInterval.Contains(host.Clock.GetUtcNow()));
        }
        catch (Exception error) when (error is not OutOfMemoryException) { throw new RecoveryCodeFirstCaseFailure(checkpoint.Phase, checkpoint.Stage); }
    }

    private static async Task<(AuthenticationTestHost Host, PresentationContentTestEnvironment Content, Dictionary<string, ContentHistoryEntry> Publications)> PublishedPasswordHost(
        IdentityTestDatabase db, Authority authority, bool expires)
    {
        PresentationContentTestEnvironment? content = null;
        var host = await Host(db, configure: services =>
        {
            var clock = (TimeProvider)services.Last(service => service.ServiceType == typeof(TimeProvider)).ImplementationInstance!;
            var semantic = new Presentation().RequirePublished(authority.RequireEffective(clock.GetUtcNow()).Values, clock.GetUtcNow());
            content = new(db, clock, semantic.Content.ToDictionary(pair => pair.Key, pair => pair.Value.Text, StringComparer.Ordinal));
            services.AddSingleton<IRecoveryPasswordPolicySource>(authority); services.AddSingleton<IRecoveryCaseHierarchySource>(authority); services.AddSingleton<IRegistrationPasswordBlocklist>(authority);
            services.AddSingleton<IPresentationContentProfileSource>(content); services.AddSingleton<IPresentationContentAuthorizationSource>(content.Authorization); services.AddTunnerPresentationContent(db.ConnectionString);
            services.AddSingleton<IRecoveryPasswordContentBindingSource>(new PublishedPasswordOwnerBinding(authority, content)); services.AddTunnerPublishedRecoveryPasswordPresentation();
        });
        try
        {
            host.Clock.SynchronizeToSystem(); var remainder = host.Clock.GetUtcNow().UtcTicks % TimeSpan.TicksPerMicrosecond;
            if (remainder != 0) host.Clock.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerMicrosecond - remainder));
            if (content is null || !ReferenceEquals(host.Clock, content.Clock)) throw new InvalidOperationException("FINITE_PUBLICATION_CLOCK_REQUIRED");
            await content.InitializeAsync(); var publications = new Dictionary<string, ContentHistoryEntry>(StringComparer.Ordinal);
            foreach (var definition in content.Profile.Definitions)
            {
                if (!expires) { publications.Add(definition.Key, await content.PublishTextAsync(definition.Key, "Synthetic published Recovery copy only")); continue; }
                await using var writer = content.Open(); content.Authorization.Actor = "fixture-author"; var proposal = await writer.Application.CreateDraftAsync(content.Draft("Synthetic published Recovery copy only", definition.Key));
                proposal = await writer.Application.ValidateAsync(content.Command(proposal)); content.Authorization.Actor = "fixture-approver"; proposal = await writer.Application.ApproveAsync(content.Command(proposal)); content.Authorization.Actor = "fixture-publisher";
                publications.Add(definition.Key, await writer.Application.PublishAsync(content.Publish(proposal) with { EffectiveInterval = new(host.Clock.GetUtcNow(), true, host.Clock.GetUtcNow().AddSeconds(1), false) }));
            }
            return (host, content, publications);
        }
        catch { await host.DisposeAsync(); throw; }
    }
    private sealed class PublishedPasswordOwnerBinding(Authority authority, PresentationContentTestEnvironment content) : IRecoveryPasswordContentBindingSource
    {
        public ValueTask<RecoveryPasswordContentBinding> RequireQualifiedAsync(RecoveryPasswordPolicy policy, DateTimeOffset effectiveAt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); var qualified = authority.RequireEffective(effectiveAt); var semantic = new Presentation().RequirePublished(qualified.Values, effectiveAt);
            return ValueTask.FromResult(new RecoveryPasswordContentBinding(qualified, semantic.Version, Public, content.Scope, content.Variant, semantic.EffectiveInterval,
                semantic.GoalLabelKey, semantic.GoalDescriptionKey, semantic.EffectLabelKey, semantic.EffectValueKey, semantic.FollowUps.ToImmutableArray()));
        }
    }
}
