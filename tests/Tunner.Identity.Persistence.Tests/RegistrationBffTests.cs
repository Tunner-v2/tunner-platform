using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Policies;
using Tunner.User.Bff;
using Xunit;

namespace Tunner.Identity.Persistence.Tests;

public sealed class RegistrationBffTests(IdentityPostgresFixture fixture) : IClassFixture<IdentityPostgresFixture>
{
    [Fact]
    public async Task ActualHttpsBffKeepsRegistrationAnonymousAndRequiresOriginCsrfAndProtectedBrowserContext()
    {
        await using var db=await fixture.CreateDatabaseAsync();await using var host=await Host.Start(db);
        using var options=await host.Get("/registration/options");Assert.Equal(HttpStatusCode.OK,options.StatusCode);
        var body=await options.Content.ReadFromJsonAsync<JsonElement>();var token=body.GetProperty("csrfToken").GetString()!;
        var wires=options.Headers.GetValues("Set-Cookie").ToArray();var cookie=string.Join("; ",wires.Select(x=>x.Split(';')[0]));
        var browser=Assert.Single(wires,x=>x.StartsWith(RegistrationEndpoints.BrowserCookieName+"=",StringComparison.Ordinal));
        Assert.Contains("secure",browser,StringComparison.OrdinalIgnoreCase);Assert.Contains("httponly",browser,StringComparison.OrdinalIgnoreCase);Assert.Contains("samesite=strict",browser,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(wires,x=>x.StartsWith("__Host-Tunner.User.Session=",StringComparison.Ordinal));
        using(var wrong=await host.Post(Command(),cookie,token,"https://outside.example.test"))Assert.Equal(HttpStatusCode.Forbidden,wrong.StatusCode);
        using(var missing=await host.Post(Command(),cookie,null))Assert.Equal(HttpStatusCode.BadRequest,missing.StatusCode);
        var csrfOnly=string.Join("; ",wires.Where(x=>!x.StartsWith(RegistrationEndpoints.BrowserCookieName+"=",StringComparison.Ordinal)).Select(x=>x.Split(';')[0]));
        using(var missingBrowser=await host.Post(Command(),csrfOnly,token))Assert.Equal(HttpStatusCode.Forbidden,missingBrowser.StatusCode);
        await using(var check=db.Context())Assert.False(await check.Accounts.AnyAsync());
        var command=Command();using var pending=await host.Post(command,cookie,token);Assert.Equal(HttpStatusCode.Accepted,pending.StatusCode);
        Assert.False(pending.Headers.Contains("Set-Cookie"));var safe=await pending.Content.ReadAsStringAsync();
        Assert.DoesNotContain(command.Password,safe);Assert.DoesNotContain(command.Email,safe);Assert.DoesNotContain("1990-01-01",safe);
        using var recovery=await host.Get("/registration/status/"+command.IdempotencyKey,cookie);Assert.Equal(HttpStatusCode.Accepted,recovery.StatusCode);
        using var other=await host.Get("/registration/status/"+command.IdempotencyKey);Assert.Equal(HttpStatusCode.Forbidden,other.StatusCode);
        using var http=await host.Get("/registration/options",http:true);Assert.Equal(HttpStatusCode.BadRequest,http.StatusCode);
        Assert.True(pending.Headers.CacheControl?.NoStore);
    }
    [Fact]
    public async Task OversizedMalformedAndUnderageRequestsHaveSafeFailuresWithoutCanonicalEffects()
    {
        await using var db=await fixture.CreateDatabaseAsync();await using var host=await Host.Start(db);
        using var options=await host.Get("/registration/options");var payload=await options.Content.ReadFromJsonAsync<JsonElement>();
        var token=payload.GetProperty("csrfToken").GetString()!;var cookie=string.Join("; ",options.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0]));
        using(var oversized=await host.Post(new { padding=new string('x',10000) },cookie,token))Assert.Equal(HttpStatusCode.RequestEntityTooLarge,oversized.StatusCode);
        using(var malformed=await host.Post(new { email="applicant@example.test" },cookie,token))Assert.Equal(HttpStatusCode.BadRequest,malformed.StatusCode);
        using var underage=await host.Post(Command(new(2015,1,1)),cookie,token);Assert.Equal(HttpStatusCode.BadRequest,underage.StatusCode);
        var text=await underage.Content.ReadAsStringAsync();Assert.Contains("AGE_INELIGIBLE",text);Assert.DoesNotContain("2015-01-01",text);
        await using var check=db.Context();Assert.False(await check.Accounts.AnyAsync());Assert.Empty(host.Notices.Messages);
    }
    [Fact]
    public async Task PublishedContentAndExplicitSecurityProvidersAreMandatory()
    {
        var proposal=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"registration-content.proposal.json")));
        var values=proposal.RootElement.GetProperty("content").EnumerateObject().Select(x=>new RegistrationPublishedText(x.Name,"fixture-variant","fixture-version","fixture-publication","en","PUBLISHED",x.Value.GetString()!)).ToArray();
        var uri=new Uri("https://fixture.example.test/legal");_ = new RegistrationPresentation("en",values,uri,uri,null);
        Assert.Throws<ArgumentException>(()=>new RegistrationPresentation("en",values.Skip(1),uri,uri,null));
        Assert.Throws<ArgumentException>(()=>new RegistrationPresentation("en",values.Select(x=>x with {State="DRAFT"}),uri,uri,null));
        Assert.Throws<InvalidOperationException>(()=>new ServiceCollection().AddTunnerRegistrationApplication());
        Assert.Throws<ArgumentException>(()=>new RegistrationNetworkPartitionKey(new byte[8]));
        using var key=new RegistrationNetworkPartitionKey(RandomNumberGenerator.GetBytes(32));
        Assert.Equal(key.Partition(IPAddress.Loopback),key.Partition(IPAddress.Loopback.MapToIPv6()));
    }
    private static RegisterAccountCommand Command(DateOnly? birth=null)=>new(){IdempotencyKey=Guid.NewGuid(),Email="applicant@example.test",Password="a unique long fixture passphrase",DateOfBirth=birth??new(1990,1,1),TermsVersion="fixture-terms",PrivacyVersion="fixture-privacy",AcknowledgedNotices=true};
    private sealed class Host : IAsyncDisposable
    {
        private const string Origin="https://registration.example.test";
        private readonly WebApplication app;private readonly X509Certificate2 certificate;private readonly Uri http;
        private readonly HttpClient client;
        public NoticeSink Notices {get;}
        private Host(WebApplication app,X509Certificate2 certificate,HttpClient client,Uri http,NoticeSink notices){this.app=app;this.certificate=certificate;this.client=client;this.http=http;Notices=notices;}
        public static async Task<Host> Start(IdentityTestDatabase database)
        {
            using var rsa=RSA.Create(2048);var request=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            using var generated=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));var bytes=generated.Export(X509ContentType.Pkcs12);
            X509Certificate2 certificate;try{certificate=X509CertificateLoader.LoadPkcs12(bytes,null,X509KeyStorageFlags.DefaultKeySet);}finally{CryptographicOperations.ZeroMemory(bytes);}
            var notices=new NoticeSink();var policy=new RegistrationPolicy("fixture-registration","fixture-age","fixture-processing","fixture-retention","fixture-terms","fixture-privacy","fixture-proof","fixture-existing","PASSWORD_AND_VERIFIED_EMAIL",15,128,10000,6,TimeSpan.FromMinutes(5),2,TimeSpan.FromSeconds(30),2,TimeSpan.FromMinutes(1),1000,1000,1000,1000,TimeSpan.FromMinutes(30),TimeSpan.FromHours(1));
            var version=new RegistrationPolicyVersion(new(policy.Version,"ACTIVE","REGISTRATION",new EffectivePolicyInterval(DateTimeOffset.UtcNow.AddDays(-1),true,null,false),null,["fixture-only-approval"]),policy);
            var app=TunnerUserBffHost.CreateApplication([],builder=>{
                builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["Bff:BrowserOrigin"]=Origin});
                builder.WebHost.ConfigureKestrel(server=>{server.Listen(IPAddress.Loopback,0);server.Listen(IPAddress.Loopback,0,listen=>listen.UseHttps(certificate));});
                builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
                builder.Services.AddTunnerIdentityPersistence(database.ConnectionString);
                builder.Services.AddSingleton<IRegistrationPolicySource>(new RegistrationPolicyCatalog([version]));
                builder.Services.AddSingleton<IRegistrationPasswordBlocklist,Blocklist>();
                builder.Services.AddSingleton<IRegistrationNotificationDispatcher>(notices);
                builder.Services.AddSingleton<IRegistrationPresentationSource,Presentation>();
                builder.Services.AddSingleton(new RegistrationNetworkPartitionKey(RandomNumberGenerator.GetBytes(32)));
            });
            await app.StartAsync();var addresses=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            var handler=new HttpClientHandler{UseCookies=false,AllowAutoRedirect=false,ServerCertificateCustomValidationCallback=(_,presented,_,_)=>presented is not null && presented.GetCertHashString(HashAlgorithmName.SHA256)==certificate.GetCertHashString(HashAlgorithmName.SHA256)};
            return new(app,certificate,new HttpClient(handler){BaseAddress=new Uri(addresses.Single(x=>x.StartsWith("https://",StringComparison.Ordinal)))},new Uri(addresses.Single(x=>x.StartsWith("http://",StringComparison.Ordinal))),notices);
        }
        public async Task<HttpResponseMessage> Get(string path,string? cookie=null,bool http=false){using var request=new HttpRequestMessage(HttpMethod.Get,http?new Uri(this.http,path):new Uri(client.BaseAddress!,path));request.Headers.Host=new Uri(Origin).Host;if(cookie is not null)request.Headers.TryAddWithoutValidation("Cookie",cookie);return await client.SendAsync(request);}
        public async Task<HttpResponseMessage> Post(object body,string? cookie,string? csrf,string origin=Origin)
        {using var request=new HttpRequestMessage(HttpMethod.Post,"/registration/register"){Content=JsonContent.Create(body)};request.Headers.Host=new Uri(Origin).Host;request.Headers.TryAddWithoutValidation("Origin",origin);if(cookie is not null)request.Headers.TryAddWithoutValidation("Cookie",cookie);if(csrf is not null)request.Headers.TryAddWithoutValidation("X-Tunner-CSRF",csrf);return await client.SendAsync(request);}
        public async ValueTask DisposeAsync(){client.Dispose();await app.StopAsync();await app.DisposeAsync();certificate.Dispose();}
    }
    private sealed class Presentation : IRegistrationPresentationSource
    {
        public RegistrationPresentation RequirePublished(RegistrationPolicy policy)
        {using var proposal=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"registration-content.proposal.json")));var values=proposal.RootElement.GetProperty("content").EnumerateObject().Select(x=>new RegistrationPublishedText(x.Name,"fixture-variant","fixture-version","fixture-publication","en","PUBLISHED",x.Value.GetString()!)).ToArray();var uri=new Uri("https://fixture.example.test/legal");return new("en",values,uri,uri,null);}
    }
    private sealed class Blocklist : IRegistrationPasswordBlocklist {public ValueTask<bool> IsBlockedAsync(string password,CancellationToken cancellationToken)=>ValueTask.FromResult(false);}
    private sealed class NoticeSink : IRegistrationNotificationDispatcher
    {public List<RegistrationNotification> Messages {get;}=[];public Task QueueAsync(RegistrationNotification notification,CancellationToken cancellationToken){Messages.Add(notification);return Task.CompletedTask;}}
}
