using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tunner.PresentationContent;

namespace Tunner.Admin.Bff;

internal sealed class AdminContentAuthorization(IHttpContextAccessor http, IAdminContentSessionSource sessions,
    IAdminContentAuthorizationSource authorization, IAdminContentHttpProfileSource profiles, TimeProvider clock)
    : IPresentationContentAuthorizationSource
{
    internal const string Scheme = "Tunner.Admin.Bff.Session";
    internal static readonly object ProfileItem = new();
    internal static async ValueTask<AdminContentSession> CurrentSession(HttpContext context, IAdminContentSessionSource sessions,
        TimeProvider clock, CancellationToken ct)
    {
        var identities = context.User.Identities.Where(x => x.IsAuthenticated).ToArray();
        if (identities.Length == 0 || identities.Any(x => x.AuthenticationType != Scheme)) throw new ContentForbiddenException();
        var session = await sessions.RequireCurrentAsync(context.User, clock.GetUtcNow(), ct).ConfigureAwait(false);
        if (session is null || new[] { session.ActorReference, session.SessionReference, session.QualificationReference }.Any(string.IsNullOrWhiteSpace) ||
            !Enum.IsDefined(session.ActorKind) || session.EffectiveInterval is null || !session.EffectiveInterval.Contains(clock.GetUtcNow())) throw new ContentForbiddenException();
        return session;
    }
    public async ValueTask<ContentAuthorizationDecision> AuthorizeAsync(ContentAuthorizationRequest request, CancellationToken cancellationToken)
    {
        var context = http.HttpContext ?? throw new ContentForbiddenException(); var ct = cancellationToken;
        var profile = profiles.RequireEffective(clock.GetUtcNow()); profile.Require(clock.GetUtcNow());
        var profileSnapshot = JsonSerializer.Serialize(profile);
        if (!context.Items.TryGetValue(ProfileItem, out var admittedProfile) || admittedProfile is not string snapshot || snapshot != profileSnapshot)
            throw new ContentForbiddenException();
        if (!profile.Scopes.Contains(request.Scope, StringComparer.Ordinal)) throw new ContentForbiddenException();
        var session = await CurrentSession(context, sessions, clock, ct).ConfigureAwait(false);
        var sessionSnapshot = JsonSerializer.Serialize(session);
        var decision = await authorization.AuthorizeAsync(session, request, ct).ConfigureAwait(false);
        var final = await CurrentSession(context, sessions, clock, ct).ConfigureAwait(false);
        var finalProfile = profiles.RequireEffective(clock.GetUtcNow()); finalProfile.Require(clock.GetUtcNow());
        if (decision is null || decision.ActorReference != session.ActorReference || decision.ActorKind != session.ActorKind ||
            JsonSerializer.Serialize(final) != sessionSnapshot || JsonSerializer.Serialize(finalProfile) != profileSnapshot)
            throw new ContentForbiddenException();
        return decision;
    }
}

public static class AdminContentRegistration
{
    /// <summary>Opt-in protected adapters only. Existing Admin cookie/session/sign-in ownership is unchanged.</summary>
    public static IServiceCollection AddTunnerAdminContent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var required in new[] { typeof(PresentationContentApplication), typeof(IAdminContentSessionSource), typeof(IAdminContentAuthorizationSource),
            typeof(IAdminContentHttpProfileSource), typeof(IContentBrowseBudgetSource), typeof(TimeProvider) })
            if (!services.Any(x => x.ServiceType == required)) throw new InvalidOperationException("Explicit qualified Admin content dependencies are required.");
        if (services.Any(x => x.ServiceType == typeof(IPresentationContentAuthorizationSource)))
            throw new InvalidOperationException("Content authorization may be composed only once.");
        services.AddHttpContextAccessor();
        services.AddScoped<IPresentationContentAuthorizationSource, AdminContentAuthorization>();
        return services;
    }
}
