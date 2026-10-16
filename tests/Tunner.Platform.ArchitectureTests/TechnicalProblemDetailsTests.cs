using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Tunner.Platform.Operations;
using Xunit;

namespace Tunner.Platform.ArchitectureTests;

public sealed class TechnicalProblemDetailsTests
{
    private static readonly string[] MinimalFields = ["correlation_id", "error_code", "retryability", "status", "title", "type"];

    private static TechnicalProblemMetadata Metadata() => new(TechnicalProblemCodes.VersionConflict, " Exact-Correlation ", "owner-classification");

    private static ProblemDetails Create(TechnicalProblemMetadata? metadata = null, int status = 409, string type = "urn:test:conflict", string title = "Owner-approved conflict") =>
        TechnicalProblemDetails.Create(status, type, title, metadata ?? Metadata());

    [Theory]
    [InlineData(400)] [InlineData(409)] [InlineData(500)] [InlineData(599)]
    public async Task RealHttpResultUsesMatchingStatusAndCanonicalMediaType(int status)
    {
        var result = TechnicalProblemDetails.HttpResult(status, "urn:test:conflict", "Owner-approved conflict", Metadata());
        Assert.Equal(status, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal(TechnicalProblemDetails.MediaType, Assert.IsAssignableFrom<IContentTypeHttpResult>(result).ContentType);
        Assert.False(result is IValueHttpResult);
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var stream = new MemoryStream();
        context.Response.Body = stream;
        await result.ExecuteAsync(context);
        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith(TechnicalProblemDetails.MediaType, context.Response.ContentType);
        using var json = JsonDocument.Parse(stream.ToArray());
        Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("urn:test:conflict", json.RootElement.GetProperty("type").GetString());
        Assert.Equal(TechnicalProblemCodes.VersionConflict, json.RootElement.GetProperty("error_code").GetString());
        Assert.Equal(" Exact-Correlation ", json.RootElement.GetProperty("correlation_id").GetString());
        Assert.Equal("owner-classification", json.RootElement.GetProperty("retryability").GetString());
    }

    [Fact]
    public void OptionalSafeMetadataHasCanonicalFieldsAndExactOwnerValues()
    {
        var metadata = Metadata() with { OperationId = "OP-owner", ResourceId = "owner-resource", CurrentVersion = 12, ExpectedVersion = 10, BlockerOwner = "owner", BlockerReference = "blocker-reference" };
        var body = TechnicalProblemDetails.Create(409, "/problem-types/owner", "Safe title", metadata, "Safe detail\nwith context", "/occurrences/owner");
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(body));
        var root = json.RootElement;
        Assert.Equal("Safe detail\nwith context", root.GetProperty("detail").GetString());
        Assert.Equal("/occurrences/owner", root.GetProperty("instance").GetString());
        Assert.Equal("OP-owner", root.GetProperty("operation_id").GetString());
        Assert.Equal("owner-resource", root.GetProperty("resource_id").GetString());
        Assert.Equal(12, root.GetProperty("current_version").GetInt64());
        Assert.Equal(10, root.GetProperty("expected_version").GetInt64());
        Assert.Equal("owner", root.GetProperty("blocker_owner").GetString());
        Assert.Equal("blocker-reference", root.GetProperty("blocker_ref").GetString());
        Assert.DoesNotContain("Extensions", root.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void AbsentMetadataIsOmittedWithoutManufacturingContextOrVersions()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Create()));
        var fields = json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(MinimalFields, fields);
    }

    [Fact]
    public void DomainMeaningAndVersionsAreNotReinterpreted()
    {
        var body = Create(new("FUTURE_OWNER_CODE", "correlation", "future-owner-classification") { ExpectedVersion = -1 }, status: 502);
        Assert.Equal(502, body.Status);
        Assert.Equal("FUTURE_OWNER_CODE", body.Extensions["error_code"]);
        Assert.Equal(-1L, body.Extensions["expected_version"]);
        Assert.False(body.Extensions.ContainsKey("current_version"));
        Assert.False(TechnicalProblemDetails.KnownErrorCodes.Contains("FUTURE_OWNER_CODE"));
    }

    [Fact]
    public void CanonicalClassRecognitionReusesExistingCodesWithoutClosedEnums()
    {
        Assert.Equal(13, TechnicalProblemDetails.KnownErrorCodes.Count);
        Assert.Contains(TechnicalProblemCodes.AuthenticationRequired, TechnicalProblemDetails.KnownErrorCodes);
        Assert.Contains(TechnicalProblemCodes.ForbiddenScope, TechnicalProblemDetails.KnownErrorCodes);
        Assert.Contains(TechnicalProblemCodes.VersionConflict, TechnicalProblemDetails.KnownErrorCodes);
        Assert.Contains("UNKNOWN_EXTERNAL_STATE", TechnicalProblemDetails.KnownErrorCodes);
        Assert.Contains("NOT_FOUND_OR_NOT_DISCLOSABLE", TechnicalProblemDetails.KnownErrorCodes);
        Assert.False(TechnicalProblemDetails.KnownErrorCodes.Contains("version_conflict"));
    }

    [Theory]
    [InlineData(0)] [InlineData(200)] [InlineData(399)] [InlineData(600)]
    public void WriterRequiresAnExplicitErrorStatus(int status) => Assert.Throws<ArgumentOutOfRangeException>(() => Create(status: status));

    [Theory]
    [InlineData("type", null)] [InlineData("type", "")] [InlineData("type", "bad uri")]
    [InlineData("type", "uri\n")]
    [InlineData("title", null)] [InlineData("title", " ")] [InlineData("title", "text\n")]
    [InlineData("code", "")] [InlineData("correlation", " ")] [InlineData("retryability", "owner\n")]
    [InlineData("operation", " ")] [InlineData("resource", "ref\n")]
    [InlineData("blockerOwner", "")] [InlineData("blockerRef", "ref\n")]
    [InlineData("instance", "bad uri")]
    public void InvalidOwnerMetadataIsRejectedWithoutDisclosingInput(string field, string? value)
    {
        var metadata = Metadata();
        metadata = field switch
        {
            "code" => metadata with { ErrorCode = value! }, "correlation" => metadata with { CorrelationId = value! },
            "retryability" => metadata with { Retryability = value! }, "operation" => metadata with { OperationId = value },
            "resource" => metadata with { ResourceId = value }, "blockerOwner" => metadata with { BlockerOwner = value },
            "blockerRef" => metadata with { BlockerReference = value }, _ => metadata
        };
        var error = Assert.Throws<ArgumentException>(() => TechnicalProblemDetails.Create(409,
            field == "type" ? value! : "urn:test:conflict", field == "title" ? value! : "Safe title", metadata,
            instance: field == "instance" ? value : null));
        if (!string.IsNullOrWhiteSpace(value)) Assert.DoesNotContain(value, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpSnapshotDoesNotCopyAmbientSecretsOrRunProblemServiceCustomization()
    {
        var customized = false;
        var collection = new ServiceCollection().AddLogging();
        collection.AddProblemDetails(options => options.CustomizeProblemDetails = context => { customized = true; context.ProblemDetails.Detail = "private-context"; });
        using var services = collection.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services, TraceIdentifier = "private-context" };
        context.Request.Path = "/private-context";
        context.Request.QueryString = new QueryString("?diagnostic=private");
        context.Items["exception"] = new InvalidOperationException("private-context");
        using var stream = new MemoryStream(); context.Response.Body = stream;
        await TechnicalProblemDetails.HttpResult(503, "urn:test:unavailable", "Safe title", Metadata()).ExecuteAsync(context);
        Assert.False(customized);
        Assert.DoesNotContain("private-context", System.Text.Encoding.UTF8.GetString(stream.ToArray()), StringComparison.Ordinal);
        Assert.Equal(503, context.Response.StatusCode);
    }

    [Fact]
    public async Task MutableCallerBodiesCannotRewriteAnIndependentHttpSnapshot()
    {
        var metadata = Metadata();
        var callerBody = Create(metadata);
        var result = TechnicalProblemDetails.HttpResult(409, "urn:test:conflict", "Safe title", metadata);
        callerBody.Status = 500; callerBody.Extensions["correlation_id"] = "changed";
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var stream = new MemoryStream(); context.Response.Body = stream;
        await result.ExecuteAsync(context);
        using var json = JsonDocument.Parse(stream.ToArray());
        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal(409, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(metadata.CorrelationId, json.RootElement.GetProperty("correlation_id").GetString());
    }

    [Fact]
    public void ReaderPreservesFutureExtensionsAfterSourceDocumentDisposal()
    {
        ProblemDetails body;
        using (var document = JsonDocument.Parse("""{"type":"https://unreachable.invalid/owner","status":503,"error_code":"FUTURE_CODE","retryability":"FUTURE_CLASS","future":{"values":[1,true,null]},"detail":"Do not parse for retry"}"""))
            body = TechnicalProblemDetails.Read(document.RootElement);
        Assert.Equal("https://unreachable.invalid/owner", body.Type);
        Assert.Equal(503, body.Status);
        Assert.Equal("FUTURE_CODE", Assert.IsType<JsonElement>(body.Extensions["error_code"]).GetString());
        Assert.Equal("FUTURE_CLASS", Assert.IsType<JsonElement>(body.Extensions["retryability"]).GetString());
        Assert.Equal(JsonValueKind.Array, Assert.IsType<JsonElement>(body.Extensions["future"]).GetProperty("values").ValueKind);
    }

    [Theory]
    [InlineData("{\"type\":8,\"title\":false,\"status\":\"409\",\"detail\":[],\"instance\":{},\"future\":true}")]
    [InlineData("{\"type\":null,\"title\":null,\"status\":null,\"detail\":null,\"instance\":null}")]
    public void ReaderIgnoresWrongTypedStandardMembers(string content)
    {
        using var document = JsonDocument.Parse(content);
        var body = TechnicalProblemDetails.Read(document.RootElement);
        Assert.Equal("about:blank", body.Type);
        Assert.Null(body.Title); Assert.Null(body.Status); Assert.Null(body.Detail); Assert.Null(body.Instance);
    }

    [Theory]
    [InlineData("99")] [InlineData("600")] [InlineData("409.5")] [InlineData("999999999999")]
    public void ReaderIgnoresInvalidHttpStatusNumbersWithoutCoercion(string number)
    {
        using var document = JsonDocument.Parse("{\"status\":" + number + "}");
        Assert.Null(TechnicalProblemDetails.Read(document.RootElement).Status);
    }

    [Fact]
    public void ReaderKeepsAdvisoryStatusAndCaseSensitiveExtensionsWithoutInferringMeaning()
    {
        using var document = JsonDocument.Parse("""{"status":200,"Type":"future-case-extension","correlation_id":" Exact ","error_code":null}""");
        var body = TechnicalProblemDetails.Read(document.RootElement);
        Assert.Equal(200, body.Status);
        Assert.Equal("about:blank", body.Type);
        Assert.True(body.Extensions.ContainsKey("Type"));
        Assert.Equal(" Exact ", Assert.IsType<JsonElement>(body.Extensions["correlation_id"]).GetString());
        Assert.Equal(JsonValueKind.Null, Assert.IsType<JsonElement>(body.Extensions["error_code"]).ValueKind);
    }

    [Theory]
    [InlineData("[]")] [InlineData("null")] [InlineData("7")] [InlineData("\"private-input\"")]
    public void ReaderRejectsNonObjectsWithoutEchoingContent(string content)
    {
        using var document = JsonDocument.Parse(content);
        var error = Assert.Throws<ArgumentException>(() => TechnicalProblemDetails.Read(document.RootElement));
        Assert.DoesNotContain("private-input", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EachBodyHasItsOwnFiniteExtensionDictionary()
    {
        var first = Create(); first.Extensions["extra"] = "caller-value";
        Assert.False(Create().Extensions.ContainsKey("extra"));
        Assert.Throws<ArgumentNullException>(() => TechnicalProblemDetails.Create(409, "urn:test:conflict", "Safe title", null!));
    }
}
