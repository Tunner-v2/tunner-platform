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
    private static readonly string[] InitialRequiredFields=["acknowledgedNotices","email","idempotencyKey","password","passwordConfirmation","privacyVersion","termsVersion"];
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
    public async Task OversizedMalformedAndObsoleteDobRequestsHaveSafeFailuresWithoutCanonicalEffects()
    {
        await using var db=await fixture.CreateDatabaseAsync();await using var host=await Host.Start(db);
        using var options=await host.Get("/registration/options");var payload=await options.Content.ReadFromJsonAsync<JsonElement>();
        var token=payload.GetProperty("csrfToken").GetString()!;var cookie=string.Join("; ",options.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0]));
        using(var oversized=await host.Post(new { padding=new string('x',10000) },cookie,token))Assert.Equal(HttpStatusCode.RequestEntityTooLarge,oversized.StatusCode);
        using(var malformed=await host.Post(new { email="applicant@example.test" },cookie,token))Assert.Equal(HttpStatusCode.BadRequest,malformed.StatusCode);
        var command=Command();using var obsolete=await host.Post(new {command.IdempotencyKey,command.Email,command.Password,command.PasswordConfirmation,command.TermsVersion,command.PrivacyVersion,command.AcknowledgedNotices,dateOfBirth="2015-01-01"},cookie,token);
        Assert.Equal(HttpStatusCode.BadRequest,obsolete.StatusCode);Assert.DoesNotContain("2015-01-01",await obsolete.Content.ReadAsStringAsync());
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
    [Fact]
    public async Task ActualHttpsFlowVerifiesContactBeforePersonalInformationAndActivatesUnder18WithoutSession()
    {
        await using var db=await fixture.CreateDatabaseAsync();await using var host=await Host.Start(db);
        using var options=await host.Get("/registration/options");var boot=await options.Content.ReadFromJsonAsync<JsonElement>();
        var token=boot.GetProperty("csrfToken").GetString()!;var cookie=string.Join("; ",options.Headers.GetValues("Set-Cookie").Select(x=>x.Split(';')[0]));
        using var registered=await host.Post(Command(),cookie,token);Assert.Equal(HttpStatusCode.Accepted,registered.StatusCode);
        var operation=await registered.Content.ReadFromJsonAsync<RegistrationResult>();Assert.NotNull(operation);
        using(var premature=await host.Post(new CompletePersonalInformationCommand{OperationId=operation.OperationId,IdempotencyKey=Guid.NewGuid(),ExpectedVersion=operation.Version,DateOfBirth=new(2015,1,1)},cookie,token,path:"/registration/personal-information"))Assert.Equal(HttpStatusCode.BadRequest,premature.StatusCode);
        var code=host.Notices.Messages.Single().Code!;using var proof=await host.Post(new VerifyRegistrationCommand(operation.OperationId,Guid.NewGuid(),operation.Version,code),cookie,token,path:"/registration/verify");
        Assert.Equal(HttpStatusCode.Accepted,proof.StatusCode);var verified=await proof.Content.ReadFromJsonAsync<RegistrationResult>();Assert.NotNull(verified);Assert.Equal("PERSONAL_INFORMATION",verified.NextAction);
        using(var forged=await host.Post(new{operationId=verified.OperationId,idempotencyKey=Guid.NewGuid(),expectedVersion=verified.Version,dateOfBirth="2015-01-01",consentSatisfied=true,independentCommercialEligible=true},cookie,token,path:"/registration/personal-information"))
        {Assert.Equal(HttpStatusCode.BadRequest,forged.StatusCode);Assert.DoesNotContain("2015-01-01",await forged.Content.ReadAsStringAsync());}
        using var profile=await host.Post(new CompletePersonalInformationCommand{OperationId=verified.OperationId,IdempotencyKey=Guid.NewGuid(),ExpectedVersion=verified.Version,DateOfBirth=new(2015,1,1),FullName="Fixture Name",DisplayName="Fixture"},cookie,token,path:"/registration/personal-information");
        Assert.Equal(HttpStatusCode.OK,profile.StatusCode);var complete=await profile.Content.ReadFromJsonAsync<RegistrationResult>();Assert.NotNull(complete);Assert.Equal("SUCCEEDED",complete.Status);Assert.Equal("UNDER_18",complete.AgeCapabilities!.AgeClassification);Assert.False(complete.AgeCapabilities.IndependentCommercialEligible);
        Assert.False(proof.Headers.Contains("Set-Cookie"));Assert.False(profile.Headers.Contains("Set-Cookie"));Assert.DoesNotContain("2015-01-01",await profile.Content.ReadAsStringAsync());
        await using var check=db.Context();Assert.Equal(Tunner.Accounts.AccountState.Active,(await check.Accounts.SingleAsync()).State);
    }

    [Fact]
    public async Task OpenApiSeparatesInitialRegistrationAndVerifiedPersonalInformation()
    {
        using var contract=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"registration-onboarding.openapi.json")));
        var schemas=contract.RootElement.GetProperty("components").GetProperty("schemas");
        var initial=schemas.GetProperty("RegisterAccount");
        Assert.False(initial.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(InitialRequiredFields,initial.GetProperty("required").EnumerateArray().Select(x=>x.GetString()).OrderBy(x=>x,StringComparer.Ordinal));
        Assert.False(initial.GetProperty("properties").TryGetProperty("dateOfBirth",out _));
        var personal=schemas.GetProperty("CompletePersonalInformation");
        Assert.Contains(personal.GetProperty("required").EnumerateArray(),x=>x.GetString()=="dateOfBirth");
        Assert.True(personal.GetProperty("properties").GetProperty("dateOfBirth").GetProperty("writeOnly").GetBoolean());
        Assert.False(schemas.GetProperty("OperationResult").GetProperty("properties").TryGetProperty("dateOfBirth",out _));
    }
    private static RegisterAccountCommand Command()=>new(){IdempotencyKey=Guid.NewGuid(),Email="applicant@example.test",Password="a unique long fixture passphrase",PasswordConfirmation="a unique long fixture passphrase",TermsVersion="fixture-terms",PrivacyVersion="fixture-privacy",AcknowledgedNotices=true};
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
            var notices=new NoticeSink();var policy=new RegistrationPolicy("fixture-registration-v2","PDR-0011","fixture-processing","fixture-retention","fixture-terms","fixture-privacy","fixture-proof","fixture-existing","PASSWORD_AND_VERIFIED_EMAIL",15,128,10000,6,TimeSpan.FromMinutes(5),2,TimeSpan.FromSeconds(30),2,TimeSpan.FromMinutes(1),1000,1000,1000,1000,TimeSpan.FromMinutes(30),TimeSpan.FromHours(1));
            var version=new RegistrationPolicyVersion(new(policy.Version,"ACTIVE","REGISTRATION",new EffectivePolicyInterval(DateTimeOffset.UtcNow.AddDays(-1),true,null,false),null,["fixture-only-approval"]),policy);
            var agePolicies=new FixtureAccountAgePolicies();
            var app=TunnerUserBffHost.CreateApplication([],builder=>{
                builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["Bff:BrowserOrigin"]=Origin});
                builder.WebHost.ConfigureKestrel(server=>{server.Listen(IPAddress.Loopback,0);server.Listen(IPAddress.Loopback,0,listen=>listen.UseHttps(certificate));});
                builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
                builder.Services.AddTunnerIdentityPersistence(database.ConnectionString);
                builder.Services.AddSingleton<IRegistrationPolicySource>(new RegistrationPolicyCatalog([version]));
                builder.Services.AddSingleton<IAccountAgePolicySource>(agePolicies);builder.Services.AddSingleton<IAccountConsentEvidenceSource>(agePolicies);
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
        public async Task<HttpResponseMessage> Post(object body,string? cookie,string? csrf,string origin=Origin,string path="/registration/register")
        {using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};request.Headers.Host=new Uri(Origin).Host;request.Headers.TryAddWithoutValidation("Origin",origin);if(cookie is not null)request.Headers.TryAddWithoutValidation("Cookie",cookie);if(csrf is not null)request.Headers.TryAddWithoutValidation("X-Tunner-CSRF",csrf);return await client.SendAsync(request);}
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
