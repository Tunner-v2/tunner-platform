using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Tunner.Platform.Bff;

/// <summary>No session authority exists in P1. An owner must later provide a governed, authoritative server store.</summary>
public sealed class UnavailableBffTicketStore : ITicketStore
{
    public Task<string> StoreAsync(AuthenticationTicket ticket) => throw new InvalidOperationException("Authoritative browser session integration is not activated.");
    public Task RenewAsync(string key, AuthenticationTicket ticket) => throw new InvalidOperationException("Authoritative browser session integration is not activated.");
    public Task<AuthenticationTicket?> RetrieveAsync(string key) => Task.FromResult<AuthenticationTicket?>(null);
    public Task RemoveAsync(string key) => Task.CompletedTask;
}
