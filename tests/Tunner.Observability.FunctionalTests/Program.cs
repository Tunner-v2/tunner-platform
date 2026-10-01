using System.Diagnostics;
using Tunner.Observability;

var failures = new List<string>();

try
{
    var scope = new TunnerTelemetryScope("governance", "operation-001", "cause-001", "product-001", "environment-001");
    var attributes = scope.ToAttributes();
    Expect(attributes.Count == 5, "Approved correlation values must become five safe attributes.", failures);
    Expect(attributes.All(attribute => TelemetryAttributePolicy.IsAllowed(attribute.Key)), "Correlation must emit only allow-listed keys.", failures);

    var filtered = TelemetryAttributePolicy.Filter(
    new KeyValuePair<string, object?>[]
    {
        new("tunner.module", "governance"),
        new("password", "not-allowed"),
        new("tunner.operation_id", "operation-001"),
        new("tunner.product_id", "customer@example.test")
    });
    Expect(filtered.Count == 2, "The policy must reject unapproved keys and non-opaque values.", failures);

    ExpectThrows(() => new TunnerTelemetryScope("governance", "operation with spaces").ToAttributes(), "Unsafe correlation values must be rejected.", failures);
    ExpectThrows(() => new TunnerObservabilityOptions { OtlpEndpoint = new Uri("https://collector.example.test") }.Validate(), "P0 must reject non-loopback exporter endpoints.", failures);

    using var listener = new ActivityListener
    {
        ShouldListenTo = source => source.Name == TunnerTelemetry.ActivitySourceName,
        Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
    };
    ActivitySource.AddActivityListener(listener);
    using var activity = TunnerTelemetry.StartOperation("operation-001", scope);
    Expect(activity is not null, "The Tunner ActivitySource must create a sampled operation.", failures);
    Expect(activity?.GetTagItem("tunner.operation_id")?.ToString() == "operation-001", "Sampled operations must include the safe operation ID.", failures);

    using var redactionActivity = new Activity("redaction-test");
    redactionActivity.SetTag("password", "not-allowed");
    redactionActivity.SetTag("tunner.module", "governance");
    redactionActivity.Start();
    new TunnerActivityRedactionProcessor().OnEnd(redactionActivity);
    Expect(redactionActivity.GetTagItem("password") is null, "Activity redaction must remove unapproved attributes.", failures);
    Expect(redactionActivity.GetTagItem("tunner.module")?.ToString() == "governance", "Activity redaction must retain approved attributes.", failures);
    redactionActivity.Stop();
}
finally
{
    if (failures.Count > 0)
    {
        foreach (var failure in failures)
        {
            Console.Error.WriteLine(failure);
        }
    }
}

if (failures.Count == 0)
{
    Console.WriteLine("PASS: Tunner.Observability functional checks");
    return 0;
}

return 1;

static void Expect(bool condition, string message, ICollection<string> failures)
{
    if (!condition)
    {
        failures.Add(message);
    }
}

static void ExpectThrows(Action action, string message, ICollection<string> failures)
{
    try
    {
        action();
        failures.Add(message);
    }
    catch (ArgumentException)
    {
    }
    catch (InvalidOperationException)
    {
    }
}
