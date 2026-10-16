using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Tunner.Platform.Operations;

/// <summary>Owner-approved disclosure only. A retryability label never authorizes an automatic retry.</summary>
public sealed record TechnicalProblemMetadata(string ErrorCode, string CorrelationId, string Retryability)
{
    public string? OperationId { get; init; }
    public string? ResourceId { get; init; }
    public long? CurrentVersion { get; init; }
    public long? ExpectedVersion { get; init; }
    public string? BlockerOwner { get; init; }
    public string? BlockerReference { get; init; }
}

/// <summary>Document 32 representation mechanics. Status, disclosure, finality and retry policy belong to owners.</summary>
public static class TechnicalProblemDetails
{
    public const string MediaType = "application/problem+json";

    // Recognition only: future approved values are not rejected or mapped to implicit behavior.
    public static IReadOnlySet<string> KnownErrorCodes { get; } = new[]
    {
        "VALIDATION_ERROR", TechnicalProblemCodes.AuthenticationRequired, TechnicalProblemCodes.ForbiddenScope,
        "STEP_UP_REQUIRED", "APPROVAL_REQUIRED", TechnicalProblemCodes.VersionConflict, "BUSINESS_RULE_BLOCKED",
        "DEPENDENCY_UNAVAILABLE", "ROUTING_UNAVAILABLE", "UNKNOWN_EXTERNAL_STATE", "NOT_FOUND_OR_NOT_DISCLOSABLE",
        "RATE_OR_ABUSE_LIMITED", "CONTRACT_VERSION_UNSUPPORTED"
    }.ToFrozenSet(StringComparer.Ordinal);

    public static ProblemDetails Create(int status, string type, string safeTitle, TechnicalProblemMetadata metadata,
        string? safeDetail = null, string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (status is < 400 or > 599) throw new ArgumentOutOfRangeException(nameof(status), "An explicit HTTP error status is required.");
        RequireReference(type);
        RequireText(safeTitle);
        RequireText(metadata.ErrorCode);
        RequireText(metadata.CorrelationId);
        RequireText(metadata.Retryability);
        if (instance is not null) RequireReference(instance);
        var problem = new ProblemDetails { Status = status, Type = type, Title = safeTitle, Detail = safeDetail, Instance = instance };
        problem.Extensions.Add("error_code", metadata.ErrorCode);
        problem.Extensions.Add("correlation_id", metadata.CorrelationId);
        problem.Extensions.Add("retryability", metadata.Retryability);
        AddReference(problem, "operation_id", metadata.OperationId);
        AddReference(problem, "resource_id", metadata.ResourceId);
        if (metadata.CurrentVersion is { } current) problem.Extensions.Add("current_version", current);
        if (metadata.ExpectedVersion is { } expected) problem.Extensions.Add("expected_version", expected);
        AddReference(problem, "blocker_owner", metadata.BlockerOwner);
        AddReference(problem, "blocker_ref", metadata.BlockerReference);
        return problem;
    }

    /// <summary>Owns a private snapshot; no mutable body is exposed and no ambient request/exception metadata is copied.</summary>
    public static IResult HttpResult(int status, string type, string safeTitle, TechnicalProblemMetadata metadata,
        string? safeDetail = null, string? instance = null) => new SnapshotHttpResult(Create(status, type, safeTitle, metadata, safeDetail, instance));

    /// <summary>RFC-compatible consumption without URI lookup, human-detail parsing, closed enums or retry execution.</summary>
    public static ProblemDetails Read(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object) throw new ArgumentException("A problem JSON object is required.", nameof(document));
        var problem = new ProblemDetails
        {
            Type = ReadString(document, "type") ?? "about:blank",
            Title = ReadString(document, "title"), Detail = ReadString(document, "detail"), Instance = ReadString(document, "instance")
        };
        if (document.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Number
            && status.TryGetInt32(out var code) && code is >= 100 and <= 599) problem.Status = code;
        foreach (var property in document.EnumerateObject())
            if (property.Name is not ("type" or "title" or "status" or "detail" or "instance"))
                problem.Extensions[property.Name] = property.Value.Clone();
        return problem;
    }

    private static string? ReadString(JsonElement document, string name) =>
        document.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void AddReference(ProblemDetails problem, string name, string? value)
    {
        if (value is null) return;
        RequireText(value);
        problem.Extensions.Add(name, value);
    }

    private static void RequireText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            throw new ArgumentException("Problem metadata requires nonempty text without control characters.");
    }

    private static void RequireReference(string? value)
    {
        RequireText(value);
        if (value!.Any(char.IsWhiteSpace) || !Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out _))
            throw new ArgumentException("Problem type and instance require URI references.");
    }

    private sealed class SnapshotHttpResult(ProblemDetails snapshot) : IResult, IStatusCodeHttpResult, IContentTypeHttpResult
    {
        public int? StatusCode => snapshot.Status;
        public string ContentType => MediaType;
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            return Results.Json(snapshot, contentType: MediaType, statusCode: snapshot.Status).ExecuteAsync(httpContext);
        }
    }
}
