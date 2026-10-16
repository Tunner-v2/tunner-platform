# P1 gRPC/gRPC-Web protocol eligibility audit

## Existing authority

This is a protocol-eligibility audit, not a gRPC implementation. ADR-0004 and Document 15 already set the default transport policy:

- Tunner browser commands and queries use REST/JSON through the applicable BFF.
- SignalR is Tunner browser realtime delivery.
- Connected Products use REST/OpenAPI and signed webhook/event contracts.
- Same-process modules use typed in-process application contracts.

gRPC-Web is selectable only for a documented Tunner-owned browser capability with material typed/binary or server-streaming value. Native gRPC is selectable only across a real justified process/service boundary. Neither is a Product integration contract or a generic foundation to add speculatively.

## Current trigger evaluation

No currently authorized Tunner feature demonstrates a material gRPC-Web requirement. No User/Admin BFF, browser client, high-frequency typed read stream, or ADR-approved browser capability exists in the repository.

No current Tunner component is deliberately separated into a process/service that needs synchronous typed RPC. The current application consists of the Platform library, API host, Worker host, governance tool, and their in-process module contracts. The OpenTelemetry exporter’s `HttpProtobuf` setting is telemetry configuration, not application gRPC.

**Selective activation trigger: NOT_PRESENT.**

## Result

P1-011 adds no `Grpc.*` package, `.proto` file, endpoint, generated client, channel, or generic scaffolding. This is conformant with the selected protocol policy, not a missing implementation.

A future gRPC-Web adoption requires a feature/capability ADR that documents why REST is insufficient, unary versus streaming need, browser/proxy compatibility, authorization, observability/error mapping, generated-client lifecycle, and fallback/degradation. A future native-gRPC adoption requires an ADR justifying a real process/service boundary and why in-process calls are insufficient.

Until then, ordinary browser behavior remains BFF REST/JSON or SignalR as applicable; Connected Product behavior remains REST/webhook based.