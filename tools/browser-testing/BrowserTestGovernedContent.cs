using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Identity.Persistence;
using Tunner.Platform.Policies;
using Tunner.PresentationContent;

// Explicit persistent engineering fixture. No Admin session, HTTP publication endpoint or production policy is introduced.
internal sealed class BrowserTestGovernedContent : IContentTransactionLocalProfileSource, IAuthenticationContentBindingSource
{
    private const string Scope = "synthetic.local.identity.login", Variant = "synthetic-local-en", Evidence = "SYNTHETIC-LOCAL-TEST-ONLY";
    private static readonly EffectivePolicyInterval Interval = new(new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), true, null, false);
    private readonly BrowserTestHostOptions options;
    private readonly IReadOnlyDictionary<string, string> wording;
    private readonly ContentGovernanceProfile profile;
    private string? authenticationVersion;

    internal BrowserTestGovernedContent(BrowserTestHostOptions options)
    {
        this.options = options; RequireIsolated();
        using var proposal = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "browser-test-authentication-content.json")));
        wording = proposal.RootElement.GetProperty("content").EnumerateObject().ToDictionary(x => x.Name,
            x => x.Value.GetString() ?? throw new InvalidOperationException("Synthetic Login wording is incomplete."), StringComparer.Ordinal);
        profile = new(new("synthetic-local-content-v1", "ACTIVE", Scope, Interval, null, [Evidence]), "Synthetic local Login", Evidence,
            "synthetic-local-processing", "synthetic-local-retention", "synthetic-local-audit-retention", "RESTRICTED_SECURITY", 4096, true, true, true,
            wording.Select(x => new ContentDefinition(x.Key, "synthetic-sensitive", true, true, true, [], []) { Purpose = "Synthetic local wording: " + x.Key }).ToArray(),
            [new(Variant, "en", new Dictionary<string, string> { ["page"] = "login" })], [new(Variant, [Variant])],
            Enum.GetValues<ContentOperation>().ToDictionary(operation => operation, operation => new ContentEffectContract(
                "synthetic-local-content-" + operation, "fixture-completed", "fixture/presentation-content", "1.0",
                operation is ContentOperation.Browse or ContentOperation.Preview or ContentOperation.History ? null : "fixture.local.content." + operation,
                operation is ContentOperation.Browse or ContentOperation.Preview or ContentOperation.History ? null : "1")));
    }

    private void RequireIsolated()
    {
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") != "Development" || options.Mode != "ISOLATED_LOCAL_BROWSER_TESTING" ||
            options.PersistentDirectory is null || options.Totp is not null || options.Recovery is not null || options.RecoveryCase is not null ||
            options.Origin != "https://localhost:" + options.Port)
            throw new InvalidOperationException("Governed Content requires the explicit persistent isolated browser mode.");
    }

    internal void ConfigureUser(IServiceCollection services, AuthenticationPolicy policy)
    {
        RequireIsolated(); policy.Validate(); authenticationVersion = policy.Version;
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IPresentationContentProfileSource>(this);
        services.AddTunnerPresentationContent(options.ConnectionString);
        services.AddSingleton<IPresentationContentAuthorizationSource>(_ => new FixtureAuthorization(this, false));
        services.AddSingleton<IAuthenticationContentBindingSource>(this);
        services.AddTunnerPublishedAuthenticationPresentation();
    }

    public ContentGovernanceProfile RequireEffective(string scope, DateTimeOffset at) => RequireCurrentLocal(scope, at);
    public ContentGovernanceProfile RequireCurrentLocal(string scope, DateTimeOffset at)
    {
        RequireIsolated();
        return scope == Scope && Interval.Contains(at) ? profile : throw new ContentUnavailableException();
    }

    public AuthenticationContentBinding RequireQualified(AuthenticationPolicy policy, DateTimeOffset at)
    {
        RequireIsolated();
        if (authenticationVersion is null || policy.Version != authenticationVersion || !Interval.Contains(at)) throw new ContentUnavailableException();
        return new(policy.Version, "synthetic-local-login-binding-v1", Evidence, Scope, Variant, Interval,
            new(options.Origin + "/browser-testing/terms"), new(options.Origin + "/browser-testing/privacy"), new(options.Origin + "/browser-testing/support"));
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        RequireIsolated();
        var databaseOptions = new DbContextOptionsBuilder<PresentationContentDbContext>().UseNpgsql(options.ConnectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "presentation_content")).Options;
        await using var database = new PresentationContentDbContext(databaseOptions);
        await database.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        var application = new PresentationContentApplication(database, this, new FixtureAuthorization(this, true), TimeProvider.System);
        var stored = await database.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT 1 FROM presentation_content.profiles WHERE \"Scope\"={Scope} AND \"Version\"={profile.Metadata.VersionId}) AS \"Value\"")
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset? lastStart = null;
        foreach (var (key, text) in wording)
        {
            if (stored)
            {
                var history = await application.HistoryAsync(Scope, key, Variant, "fixture-content-history", "fixture-content-seed", cancellationToken).ConfigureAwait(false);
                if (history.Count != 0)
                {
                    var now = DateTimeOffset.UtcNow;
                    if (!history.Any(entry => entry.EffectiveInterval.Contains(now)))
                    {
                        var pending = history.Where(entry => entry.EffectiveInterval.From > now && entry.EffectiveInterval.From <= now.AddSeconds(5))
                            .Select(entry => (DateTimeOffset?)entry.EffectiveInterval.From).OrderBy(from => from).FirstOrDefault();
                        if (pending is { } activation && (lastStart is null || activation > lastStart)) lastStart = activation;
                    }
                    continue; // Restart preserves history, including committed bootstrap publications still awaiting activation.
                }
            }
            var proposal = await application.CreateDraftAsync(new(Scope, key, Variant, text, "fixture-content-draft", "fixture-content-seed"), cancellationToken).ConfigureAwait(false);
            stored = true;
            proposal = await application.ValidateAsync(new(proposal.ProposalReference, proposal.Revision, "fixture-content-validate", "fixture-content-seed"), cancellationToken).ConfigureAwait(false);
            proposal = await application.ApproveAsync(new(proposal.ProposalReference, proposal.Revision, "fixture-content-approve", "fixture-content-seed"), cancellationToken).ConfigureAwait(false);
            var ticks = DateTimeOffset.UtcNow.AddSeconds(5).UtcTicks;
            var from = new DateTimeOffset(ticks + (10 - ticks % 10) % 10, TimeSpan.Zero);
            _ = await application.PublishAsync(new(proposal.ProposalReference, proposal.Revision, new(from, true, null, false), null,
                "fixture-content-publish", "fixture-content-seed"), cancellationToken).ConfigureAwait(false);
            if (lastStart is null || from > lastStart) lastStart = from;
        }
        if (lastStart is { } start)
        {
            var delay = start - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        _ = await application.ResolveAsync(new(Scope, Variant, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    private sealed class FixtureAuthorization(BrowserTestGovernedContent owner, bool seed) : IPresentationContentAuthorizationSource
    {
        public ValueTask<ContentAuthorizationDecision> AuthorizeAsync(ContentAuthorizationRequest request, CancellationToken cancellationToken)
        {
            owner.RequireIsolated(); cancellationToken.ThrowIfCancellationRequested();
            var actor = request.Operation switch { ContentOperation.Approve => "synthetic-local-approver", ContentOperation.Publish or ContentOperation.Rollback => "synthetic-local-publisher", _ => "synthetic-local-author" };
            return ValueTask.FromResult(new ContentAuthorizationDecision(request.Digest, seed && request.Scope == Scope, actor,
                ContentActorKind.Human, "synthetic-local-fixture", "synthetic-local-content-authorization-v1", Evidence, Interval));
        }
    }
}
