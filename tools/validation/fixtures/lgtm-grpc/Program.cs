using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

try { return RunFixture(args); }
catch (Exception error)
{
    Console.Error.WriteLine("FAIL: synthetic gRPC fixture: " + error.Message);
    return 1;
}

static int RunFixture(string[] args)
{
    if (args.Length != 2 || !Uri.TryCreate(args[0], UriKind.Absolute, out var endpoint) ||
        endpoint.Scheme != "http" || !endpoint.IsLoopback || endpoint.Port < 1 ||
        endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.AbsolutePath != "/" ||
        !Guid.TryParseExact(args[1], "N", out _))
    {
        Console.Error.WriteLine("A loopback OTLP endpoint and synthetic fixture ID are required.");
        return 1;
    }

    const string service = "tunner-grpc-security-fixture";
    const string sourceName = "Tunner.Security.GrpcFixture";
    const string metricName = "tunner_grpc_security_fixture";
    var marker = "grpc-fixture-" + args[1];
    var resource = ResourceBuilder.CreateEmpty().AddService(service);
    void Configure(OtlpExporterOptions options)
    {
        options.Endpoint = endpoint;
        options.Protocol = OtlpExportProtocol.Grpc;
        options.Headers = string.Empty;
        options.TimeoutMilliseconds = 10000;
        options.ExportProcessorType = ExportProcessorType.Simple;
    }

    using var traces = Sdk.CreateTracerProviderBuilder().SetResourceBuilder(resource)
        .AddSource(sourceName).AddOtlpExporter(Configure).Build();
    using var metrics = Sdk.CreateMeterProviderBuilder().SetResourceBuilder(resource)
        .AddMeter(sourceName).AddOtlpExporter(Configure).Build();
    using var services = new ServiceCollection().AddLogging(logging => logging.AddOpenTelemetry(options =>
    {
        options.SetResourceBuilder(resource);
        options.IncludeFormattedMessage = true;
        options.AddOtlpExporter(Configure);
    })).BuildServiceProvider();
    using var source = new ActivitySource(sourceName);
    using var meter = new Meter(sourceName);
    var gauge = meter.CreateObservableGauge(metricName,
        () => new Measurement<long>(1, new KeyValuePair<string, object?>("fixture.id", args[1])));
    string traceId;
    using (var span = source.StartActivity(marker, ActivityKind.Internal))
    {
        if (span is null) { Console.Error.WriteLine("Synthetic trace was not sampled."); return 1; }
        traceId = span.TraceId.ToHexString();
    }
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(sourceName);
    var writeLog = LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, "SyntheticFixture"), "Synthetic fixture {FixtureId}");
    writeLog(logger, marker, null);
    if (!traces.ForceFlush(10000) || !metrics.ForceFlush(10000))
    {
        Console.Error.WriteLine("Synthetic OTLP/gRPC flush did not complete.");
        return 1;
    }
    GC.KeepAlive(gauge);
    Console.WriteLine(JsonSerializer.Serialize(new { protocol = "OTLP_GRPC", service, marker, trace_id = traceId, metric = metricName, fixture_id = args[1] }));
    return 0;

}
