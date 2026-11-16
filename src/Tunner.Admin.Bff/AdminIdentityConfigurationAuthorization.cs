using System.Text.Json;
using Tunner.Identity.Persistence;

namespace Tunner.Admin.Bff;

internal sealed class AdminIdentityConfigurationAuthorization(IHttpContextAccessor http, IAdminIdentityConfigurationSessionSource sessions,
    IAdminIdentityConfigurationAuthorizationSource authorization, IAdminIdentityConfigurationHttpProfileSource profiles, TimeProvider clock)
    : IIdentityConfigurationAuthorizationSource
{
    internal const string Scheme = "Tunner.Admin.Bff.Session";
    internal static readonly object ProfileItem = new();
    internal static async ValueTask<AdminIdentityConfigurationSession> CurrentSession(HttpContext h, IAdminIdentityConfigurationSessionSource sessions, TimeProvider clock, CancellationToken ct)
    {
        var identities = h.User.Identities.Where(x => x.IsAuthenticated).ToArray();
        if (identities.Length == 0 || identities.Any(x => x.AuthenticationType != Scheme)) throw new IdentityConfigurationForbiddenException();
        var session = await sessions.RequireCurrentAsync(h.User, clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (session is null || new[] { session.ActorReference, session.SessionReference, session.QualificationReference }.Any(string.IsNullOrWhiteSpace) ||
            !Enum.IsDefined(session.ActorKind) || session.EffectiveInterval is null || !session.EffectiveInterval.Contains(clock.GetUtcNow())) throw new IdentityConfigurationForbiddenException();
        return session;
    }
    public async ValueTask<IdentityConfigurationAuthorizationDecision> AuthorizeAsync(IdentityConfigurationAuthorizationRequest request, CancellationToken cancellationToken)
    {
        var h = http.HttpContext ?? throw new IdentityConfigurationForbiddenException(); var ct = cancellationToken;
        var profile = profiles.RequireEffective(clock.GetUtcNow()); profile.Require(clock.GetUtcNow()); var snapshot = JsonSerializer.Serialize(profile);
        if (!h.Items.TryGetValue(ProfileItem, out var admitted) || admitted is not string prior || prior != snapshot || !profile.Scopes.Contains(request.Scope))
            throw new IdentityConfigurationForbiddenException();
        var session = await CurrentSession(h, sessions, clock, ct).ConfigureAwait(false); var sessionSnapshot = JsonSerializer.Serialize(session);
        var decision = await authorization.AuthorizeAsync(session, request, ct).ConfigureAwait(false);
        var final = await CurrentSession(h, sessions, clock, ct).ConfigureAwait(false); var finalProfile = profiles.RequireEffective(clock.GetUtcNow()); finalProfile.Require(clock.GetUtcNow());
        if (decision is null || decision.ActorReference != session.ActorReference || decision.ActorKind != session.ActorKind ||
            JsonSerializer.Serialize(final) != sessionSnapshot || JsonSerializer.Serialize(finalProfile) != snapshot) throw new IdentityConfigurationForbiddenException();
        return decision;
    }
}
public static class AdminIdentityConfigurationRegistration
{
    public static IServiceCollection AddTunnerAdminIdentityConfiguration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var type in new[] { typeof(IdentityRuntimeConfigurationApplication), typeof(IAdminIdentityConfigurationSessionSource),
            typeof(IAdminIdentityConfigurationAuthorizationSource), typeof(IAdminIdentityConfigurationHttpProfileSource), typeof(IIdentityConfigurationBrowseBudgetSource), typeof(TimeProvider) })
            if (!services.Any(x => x.ServiceType == type)) throw new InvalidOperationException("Explicit qualified Admin Identity configuration dependencies are required.");
        if (services.Any(x => x.ServiceType == typeof(IIdentityConfigurationAuthorizationSource))) throw new InvalidOperationException("Identity configuration authorization may be composed only once.");
        services.AddHttpContextAccessor(); services.AddScoped<IIdentityConfigurationAuthorizationSource, AdminIdentityConfigurationAuthorization>(); return services;
    }
}
