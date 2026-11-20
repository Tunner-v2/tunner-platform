using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Tunner.Identity.Persistence;

/// <summary>Only an actual committed winner constructs this closed response. Reads cannot accept a carrier.</summary>
internal sealed class RecoveryCodeProofCompletedResult : IResult, IDisposable
{
    private RecoveryCodeFirstDelivery? delivery;
    private readonly StepUpBrowserOperation frame;
    private readonly NativeProofCompletion? native;
    internal RecoveryCodeProofCompletedResult(HttpContext http, StepUpBrowserOperation frame, RecoveryCodeFirstDelivery delivery, NativeProofCompletion? native)
    {
        if (!delivery.Matches(frame.Caller, frame.Operation.Binding) || frame.Browser.RequestId == Guid.Empty ||
            native is not null && (native.RequestId != frame.Browser.RequestId || native.AttemptId == Guid.Empty || native.CeremonyId == Guid.Empty))
            throw new InvalidOperationException("Recovery delivery must match the exact winning proof frame.");
        this.frame = frame; this.delivery = delivery; this.native = native;
        // Secondary ownership if a later middleware failure prevents IResult execution; ExecuteAsync finally is primary.
        http.Response.RegisterForDispose(this);
    }
    public async Task ExecuteAsync(HttpContext http)
    {
        var owned = Interlocked.Exchange(ref delivery, null) ?? throw new InvalidOperationException("Recovery response is one-use.");
        try
        {
            http.RequestAborted.ThrowIfCancellationRequested();
            if (http.Response.HasStarted || !owned.Matches(frame.Caller, frame.Operation.Binding)) throw new InvalidOperationException("Recovery response frame is no longer writable.");
            http.Response.Headers.CacheControl = "no-store"; http.Response.Headers["Referrer-Policy"] = "no-referrer";
            http.Response.ContentType = "application/json; charset=utf-8"; http.Response.StatusCode = StatusCodes.Status200OK;
            using var writer = new Utf8JsonWriter(http.Response.BodyWriter);
            writer.WriteStartObject(); writer.WriteString("status", "COMPLETED"); writer.WriteString("operationReference", frame.Operation.Binding.CorrelationId);
            writer.WriteBoolean("signInRequired", false);
            if (native is not null) { writer.WriteString("requestId", native.RequestId); writer.WriteString("attemptId", native.AttemptId); writer.WriteString("ceremonyId", native.CeremonyId); }
            writer.WritePropertyName("recoveryCodeFirstDelivery"); owned.Write(writer, frame.Browser.RequestId); writer.WriteEndObject(); writer.Flush();
            await http.Response.BodyWriter.FlushAsync(http.RequestAborted).ConfigureAwait(false);
        }
        finally { owned.Dispose(); }
    }
    public void Dispose() => Interlocked.Exchange(ref delivery, null)?.Dispose();
    public override string ToString() => nameof(RecoveryCodeProofCompletedResult);
}
