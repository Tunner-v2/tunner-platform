using System.Net;
using System.Text.Json;
using Tunner.Platform.Secrets;
using Xunit;

namespace Tunner.Installer.Tests;

public sealed class CredentialSecretVersionTests
{
    private static readonly CredentialSecretReference Reference = new("fixture-vault", "fixture/data/credential/generation", 1);
    [Fact]
    public async Task CreateOnlyAndVersionSelectedReadShareScopedTransportWithoutChangingEngineeringContract()
    {
        var handler = new Handler(); using var store = new OpenBaoSecretStore(new Uri("http://127.0.0.1:8200"), Reference.StoreReference, "fixture-scoped-token", handler);
        await store.CreateCredentialVersionAsync(Reference, new Dictionary<string, string> { ["seed"] = "NONFUNCTIONAL-PUBLIC-FIXTURE" });
        Assert.Equal(0, handler.Cas); using var values = await store.ReadCredentialVersionAsync(Reference); Assert.NotNull(values);
        Assert.Equal("?version=1", handler.Query); Assert.DoesNotContain("NONFUNCTIONAL-PUBLIC-FIXTURE", values.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, handler.LookupCount); Assert.Equal(1, handler.Writes);
    }
    [Theory]
    [InlineData("{}")] [InlineData("[]")] [InlineData("{\"data\":[]}")]
    [InlineData("{\"data\":{\"data\":{\"seed\":\"DO-NOT-ECHO-SENTINEL\"},\"metadata\":{\"version\":2,\"destroyed\":false,\"deletion_time\":\"\"}}}")]
    [InlineData("{\"data\":{\"data\":{\"seed\":\"DO-NOT-ECHO-SENTINEL\"},\"metadata\":{\"version\":9999999999999999999999999999999,\"destroyed\":false,\"deletion_time\":\"\"}}}")]
    [InlineData("{\"data\":{\"data\":{\"seed\":\"DO-NOT-ECHO-SENTINEL\"},\"metadata\":{\"version\":1,\"destroyed\":true,\"deletion_time\":\"\"}}}")]
    [InlineData("{\"data\":{\"data\":{\"seed\":4},\"metadata\":{\"version\":1,\"destroyed\":false,\"deletion_time\":\"\"}}}")]
    [InlineData("DO-NOT-ECHO-SENTINEL")]
    public async Task MalformedWrongVersionOrRetiredCredentialResponseHasFixedRedactedFailure(string response)
    {
        var handler = new Handler { ReadResponse = response }; using var store = new OpenBaoSecretStore(new Uri("http://127.0.0.1:8200"), Reference.StoreReference, "fixture-scoped-token", handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadCredentialVersionAsync(Reference));
        Assert.Equal("SECRET_RESPONSE_INVALID", error.Message); Assert.DoesNotContain("DO-NOT-ECHO-SENTINEL", error.ToString(), StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("{}")] [InlineData("{\"data\":{\"version\":2}}")]
    [InlineData("{\"data\":{\"version\":\"DO-NOT-ECHO-SENTINEL\"}}")]
    [InlineData("{\"data\":{\"version\":999999999999999999999999999999}}")]
    public async Task MalformedCreateVersionCannotBeReportedAsSuccessfulOrOverwrite(string response)
    {
        var handler = new Handler { CreateResponse = response }; using var store = new OpenBaoSecretStore(new Uri("http://127.0.0.1:8200"), Reference.StoreReference, "fixture-scoped-token", handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateCredentialVersionAsync(Reference, new Dictionary<string, string> { ["seed"] = "PUBLIC-FIXTURE" }));
        Assert.Equal("SECRET_RESPONSE_INVALID", error.Message); Assert.Equal(0, handler.Cas); Assert.Equal(1, handler.Writes);
    }
    [Fact]
    public async Task InvalidReferenceCannotSendAProviderRequest()
    {
        var handler = new Handler(); using var store = new OpenBaoSecretStore(new Uri("http://127.0.0.1:8200"), Reference.StoreReference, "fixture-scoped-token", handler);
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCredentialVersionAsync(Reference with { ResourceReference = "fixture/data/path?version=2" }));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadCredentialVersionAsync(Reference with { StoreReference = "another-owner" }));
        Assert.Equal(0, handler.LookupCount);
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal string ReadResponse { get; init; } = "{\"data\":{\"data\":{\"seed\":\"NONFUNCTIONAL-PUBLIC-FIXTURE\"},\"metadata\":{\"version\":1,\"destroyed\":false,\"deletion_time\":\"\"}}}";
        internal string CreateResponse { get; init; } = "{\"data\":{\"version\":1}}";
        internal int LookupCount; internal int Writes; internal int? Cas; internal string? Query;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("lookup-self", StringComparison.Ordinal)) { LookupCount++; return Json("{\"data\":{\"ttl\":60,\"policies\":[\"fixture-credential-only\"]}}"); }
            if (request.Method == HttpMethod.Post)
            {
                Writes++; using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); Cas = body.RootElement.GetProperty("options").GetProperty("cas").GetInt32(); return Json(CreateResponse);
            }
            Query = request.RequestUri.Query; return Json(ReadResponse);
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
