using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Bff;
using Xunit;

namespace Tunner.Platform.ArchitectureTests;

public sealed class BffProtocolCallbackTests(BffFoundationFixture fixture) : IClassFixture<BffFoundationFixture>
{
    private const string Callback = "/_fixture/protocol/callback";
    private static BffProtocolCallback Registration(Func<HttpContext, Task> handler) => new(Callback, 128, 4, 32, 96, handler);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactCallbackAcceptsBoundedFormPostWithoutWeakeningOrdinaryOrigin(bool chunked)
    {
        var calls = 0;
        await using var host = await BffTestHost.Start(BffSurface.User, fixture.Certificate, configure: builder =>
            builder.Services.AddSingleton(Registration(context => { calls++; return context.Response.WriteAsync("terminal"); })));
        using var request = host.Request(HttpMethod.Post, Callback, origin: "https://provider.example.test");
        request.Content = new StringContent("state=synthetic&code=synthetic", Encoding.UTF8, "application/x-www-form-urlencoded");
        request.Headers.TransferEncodingChunked = chunked;
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("terminal", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, calls);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        using var ordinary = await host.Send(HttpMethod.Post, "/_fixture/anonymous", origin: "https://provider.example.test");
        Assert.Equal(HttpStatusCode.Forbidden, ordinary.StatusCode);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("GET", "", "application/x-www-form-urlencoded", "", false, 400)]
    [InlineData("POST", "?state=other", "application/x-www-form-urlencoded", "state=x", false, 400)]
    [InlineData("POST", "", "application/json", "{}", false, 400)]
    [InlineData("POST", "", "application/x-www-form-urlencoded", "state=x&state=y", false, 400)]
    [InlineData("POST", "", "application/x-www-form-urlencoded", "a=1&b=2&c=3&d=4&e=5", false, 400)]
    [InlineData("POST", "", "application/x-www-form-urlencoded", "oversized", false, 413)]
    [InlineData("POST", "", "application/x-www-form-urlencoded", "oversized", true, 413)]
    public async Task MalformedTransportNeverInvokesTheHandler(string method, string query, string contentType, string body, bool chunked, int status)
    {
        var calls = 0;
        await using var host = await BffTestHost.Start(BffSurface.User, fixture.Certificate, configure: builder =>
            builder.Services.AddSingleton(Registration(_ => { calls++; return Task.CompletedTask; })));
        using var request = host.Request(new(method), Callback + query, origin: "https://provider.example.test");
        if (method != "GET") request.Content = new StringContent(body == "oversized" ? new string('x', 129) : body, Encoding.UTF8, contentType);
        request.Headers.TransferEncodingChunked = chunked;
        using var response = await host.Client.SendAsync(request);
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal(0, calls);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task HttpsAndTargetHostChecksStillPrecedeCallback()
    {
        var calls = 0;
        await using var host = await BffTestHost.Start(BffSurface.User, fixture.Certificate, configure: builder =>
            builder.Services.AddSingleton(Registration(_ => { calls++; return Task.CompletedTask; })));
        using var http = host.Request(HttpMethod.Post, Callback, http: true);
        http.Content = new StringContent("state=x", Encoding.UTF8, "application/x-www-form-urlencoded");
        using var httpResponse = await host.Client.SendAsync(http);
        Assert.Equal(HttpStatusCode.BadRequest, httpResponse.StatusCode);
        using var wrongHost = host.Request(HttpMethod.Post, Callback);
        wrongHost.Headers.Host = "wrong.example.test";
        wrongHost.Content = new StringContent("state=x", Encoding.UTF8, "application/x-www-form-urlencoded");
        using var hostResponse = await host.Client.SendAsync(wrongHost);
        Assert.Equal(HttpStatusCode.Forbidden, hostResponse.StatusCode);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void AdminAndOverlappingRegistrationsAreRefused()
    {
        Assert.Throws<InvalidOperationException>(() => BffTestHost.Create(BffSurface.Admin, builder =>
            builder.Services.AddSingleton(Registration(_ => Task.CompletedTask))));
        Assert.Throws<InvalidOperationException>(() => BffTestHost.Create(BffSurface.User, builder =>
        {
            builder.Services.AddSingleton(Registration(_ => Task.CompletedTask));
            builder.Services.AddSingleton(Registration(_ => Task.CompletedTask));
        }));
    }

    [Fact]
    public async Task NativeHandlerCaseAliasAndEncodedAliasCannotEscapeTheBoundedBranch()
    {
        var calls = 0;
        await using var host = await BffTestHost.Start(BffSurface.User, fixture.Certificate, configure: builder =>
            builder.Services.AddSingleton(Registration(_ => { calls++; return Task.CompletedTask; })));
        foreach (var path in new[] { "/_fixture/Protocol/callback", "/_fixture/protocol/%63allback" })
        {
            using var request = host.Request(HttpMethod.Post, path, origin: host.Origin);
            request.Content = new StringContent("state=x", Encoding.UTF8, "application/x-www-form-urlencoded");
            using var response = await host.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task ConflictingCallbackRegistrationCannotInheritHealthExemptions(string method)
    {
        var calls = 0;
        await using var host = await BffTestHost.Start(BffSurface.User, fixture.Certificate, configure: builder =>
            builder.Services.AddSingleton(new BffProtocolCallback("/health", 128, 4, 32, 96,
                _ => { calls++; return Task.CompletedTask; })));
        using var request = host.Request(new(method), "/health");
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, calls);
        using var wrongHost = host.Request(new(method), "/health");
        wrongHost.Headers.Host = "wrong.example.test";
        using var rejectedHost = await host.Client.SendAsync(wrongHost);
        Assert.Equal(HttpStatusCode.Forbidden, rejectedHost.StatusCode);
        Assert.Equal(0, calls);
    }
}
