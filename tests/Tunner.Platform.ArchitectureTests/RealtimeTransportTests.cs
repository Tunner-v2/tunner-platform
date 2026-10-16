using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Bff;
using Tunner.Platform.Realtime;
using Xunit;

namespace Tunner.Platform.ArchitectureTests;

public sealed class RealtimeTransportTests(BffFoundationFixture fixture) : IClassFixture<BffFoundationFixture>
{
    [Fact]
    public async Task RealTransportRejectsAnonymousAndForeignOriginRequests()
    {
        await using var host = await Start();
        using var anonymous = await host.Send(HttpMethod.Get, "/_fixture/realtime", origin: host.Origin);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var cookie = await host.ServerCookie();
        using var foreign = await host.Send(HttpMethod.Get, "/_fixture/realtime", cookie, "https://outside.example.test");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }

    [Fact]
    public async Task ReconnectReauthorizesAndHintCarriesNoDomainStateBeforeAuthoritativeRead()
    {
        await using var host = await Start();
        var cookie = await host.ServerCookie();
        using (var socket = await Connect(host, cookie))
            await AssertHint(host, socket);
        using (var reconnected = await Connect(host, cookie))
        {
            await AssertHint(host, reconnected);
            using var read = await host.Send(HttpMethod.Get, "/_fixture/read", cookie);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal("fixture-read", await read.Content.ReadAsStringAsync());
        }
        var key = host.CookieOptions.TicketDataFormat.Unprotect(cookie[(cookie.IndexOf('=') + 1)..])!.Principal.Claims.Single().Value;
        await host.Store.RemoveAsync(key);
        using var revoked = await host.Send(HttpMethod.Get, "/_fixture/realtime", cookie, host.Origin);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task RealConnectionRatePolicyRejectsExcessConnections()
    {
        await using var host = await Start(1);
        var cookie = await host.ServerCookie();
        using var socket = await Connect(host, cookie);
        using var excess = await host.Send(HttpMethod.Get, "/_fixture/realtime", cookie, host.Origin);
        Assert.Equal(HttpStatusCode.TooManyRequests, excess.StatusCode);
    }

    private Task<BffTestHost> Start(int permits = 20) => BffTestHost.Start(BffSurface.User, fixture.Certificate,
        configure: builder =>
        {
            builder.Services.AddTunnerRealtimeFoundation();
            // Synthetic scope and thresholds belong only to this isolated test host.
            builder.Services.AddSingleton<IRealtimeAudienceResolver, FixtureAudienceResolver>();
            builder.Services.AddCors(options => options.AddPolicy("fixture.origin",
                policy => policy.WithOrigins("https://user-bff.example.test").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
            builder.Services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddFixedWindowLimiter("fixture.connections", limiter =>
                {
                    limiter.PermitLimit = permits;
                    limiter.Window = TimeSpan.FromHours(1);
                    limiter.QueueLimit = 0;
                    limiter.AutoReplenishment = false;
                });
            });
        },
        map: app =>
        {
            app.UseCors();
            app.UseRateLimiter();
            app.MapTunnerRealtimeFoundation(new("/_fixture/realtime", "fixture.origin", "fixture.connections"));
        });

    private async Task<ClientWebSocket> Connect(BffTestHost host, string cookie)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Host", new Uri(host.Origin).Authority);
        socket.Options.SetRequestHeader("Origin", host.Origin);
        socket.Options.SetRequestHeader("Cookie", cookie);
        socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            certificate is not null && certificate.GetCertHashString(HashAlgorithmName.SHA256) == fixture.Certificate.GetCertHashString(HashAlgorithmName.SHA256);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var endpoint = new UriBuilder(new Uri(host.Client.BaseAddress!, "/_fixture/realtime")) { Scheme = "wss" }.Uri;
            await socket.ConnectAsync(endpoint, timeout.Token);
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e").AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
            using var handshake = JsonDocument.Parse(await Receive(socket));
            Assert.Empty(handshake.RootElement.EnumerateObject());
            // A completion frame proves OnConnectedAsync has finished before the group hint is sent.
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":1,\"invocationId\":\"fixture-ready\",\"target\":\"NoSuchFixtureMethod\",\"arguments\":[]}\u001e").AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
            using var ready = JsonDocument.Parse(await Receive(socket));
            Assert.Equal(3, ready.RootElement.GetProperty("type").GetInt32());
            Assert.Equal("fixture-ready", ready.RootElement.GetProperty("invocationId").GetString());
            return socket;
        }
        catch { socket.Dispose(); throw; }
    }

    private static async Task AssertHint(BffTestHost host, ClientWebSocket socket)
    {
        await host.Services.GetRequiredService<IRealtimeHintPublisher>().PublishResynchronizationHintAsync(new RealtimeAudience("fixture:authorized"));
        using var message = JsonDocument.Parse(await Receive(socket));
        Assert.Equal(1, message.RootElement.GetProperty("type").GetInt32());
        Assert.Equal("ResynchronizeAsync", message.RootElement.GetProperty("target").GetString());
        var arguments = message.RootElement.GetProperty("arguments").EnumerateArray().ToArray();
        Assert.Single(arguments);
        Assert.Empty(arguments[0].EnumerateObject());
    }

    private static async Task<string> Receive(ClientWebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var payload = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
            Assert.NotEqual(WebSocketMessageType.Close, read.MessageType);
            payload.Write(buffer, 0, read.Count);
            var bytes = payload.ToArray();
            var separator = Array.IndexOf(bytes, (byte)0x1e);
            if (separator >= 0) return Encoding.UTF8.GetString(bytes, 0, separator);
            Assert.True(bytes.Length < 65536, "SignalR fixture response exceeded the bounded test buffer.");
        }
    }

    private sealed class FixtureAudienceResolver : IRealtimeAudienceResolver
    {
        public Task<RealtimeAudienceResolution> ResolveAsync(RealtimeConnectionAuthorizationContext context, CancellationToken cancellationToken) =>
            Task.FromResult(context.Principal.Identity?.IsAuthenticated == true
                ? RealtimeAudienceResolution.Allow([new RealtimeAudience("fixture:authorized")])
                : RealtimeAudienceResolution.Deny());
    }
}
