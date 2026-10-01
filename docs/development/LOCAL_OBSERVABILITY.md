# Local observability baseline

## Scope

TUN-P0-009 supplies the reusable .NET foundation for OpenTelemetry traces, metrics, and logs. It exports through OTLP to the existing development/test Grafana OTEL-LGTM stack from P0-006. It does not add a Tunner API, Worker, Product metric, billing/usage telemetry, production backend selection, or deployment behavior.

## Local endpoint

The default exporter endpoint is loopback-only HTTP OTLP at `http://127.0.0.1:24318`, matching the configured Tunner local Compose mapping. `OTEL_EXPORTER_OTLP_ENDPOINT` may override it only with another loopback HTTP endpoint. This is an endpoint setting, not a secret; do not set OTLP headers, credentials, API keys, or tokens in repository configuration.

Grafana OTEL-LGTM is for development and test viewing only. Production operations must select a backend separately while preserving the OpenTelemetry/OTLP contract.

## Telemetry boundary

`TunnerTelemetryScope` accepts only opaque, bounded identifiers for module, operation, causation, and explicitly safe Product/environment scope IDs. The activity and log processors remove non-allow-listed attributes; log bodies and exceptions are replaced before export. Never pass secrets, raw payment data, unrestricted PII, email addresses, tokens, credentials, or free-form customer input through the P0 facade.

## Verification

After dependencies are restored, run:

```powershell
./tools/validation/Validate-TunnerObservabilitySource.ps1
dotnet run --project tests/Tunner.Observability.FunctionalTests --no-restore
```

These checks do not start Docker or send telemetry. Runtime viewing in Grafana is a later operator-run local acceptance step after a governed host exists.
