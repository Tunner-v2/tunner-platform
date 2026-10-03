using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Tunner.Platform.Realtime;

/// <summary>
/// Tunner's technical browser-realtime hub. It accepts no client subscription
/// input and exposes no business operations or authoritative domain objects.
/// </summary>
[Authorize(Policy = TunnerRealtimePolicies.AuthenticatedConnect)]
public sealed partial class TunnerRealtimeHub(
    IRealtimeAudienceResolver audienceResolver,
    IAuthorizationService authorizationService,
    ILogger<TunnerRealtimeHub> logger) : Hub<ITunnerRealtimeClient>
{
    public override async Task OnConnectedAsync()
    {
        var principal = Context.User;
        if (principal?.Identity?.IsAuthenticated is not true)
        {
            Context.Abort();
            return;
        }

        var resolution = await audienceResolver.ResolveAsync(
            new RealtimeConnectionAuthorizationContext(principal, authorizationService, Context.ConnectionId),
            Context.ConnectionAborted);

        if (!resolution.IsAuthorized)
        {
            LogScopeRejected(logger);
            Context.Abort();
            return;
        }

        foreach (var audience in resolution.Audiences)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, audience.GroupName, Context.ConnectionAborted);
        }

        await base.OnConnectedAsync();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tunner realtime connection was rejected by server-side scope resolution.")]
    private static partial void LogScopeRejected(ILogger logger);
}

/// <summary>
/// SignalR implementation of the generic foundation publisher. The published
/// message deliberately contains no owner-domain state or sensitive fields.
/// </summary>
public sealed class SignalRRealtimeHintPublisher(IHubContext<TunnerRealtimeHub, ITunnerRealtimeClient> hubContext) : IRealtimeHintPublisher
{
    public Task PublishResynchronizationHintAsync(RealtimeAudience audience, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audience);
        return hubContext.Clients.Group(audience.GroupName).ResynchronizeAsync(new RealtimeResynchronizationHint());
    }
}