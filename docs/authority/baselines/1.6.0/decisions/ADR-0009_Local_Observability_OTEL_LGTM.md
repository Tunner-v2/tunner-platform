# ADR-0009 — Local Observability Reference: OpenTelemetry + Grafana OTEL-LGTM

**Status:** ACCEPTED  
**Date:** 2026-09-21

## Decision
Application instrumentation uses OpenTelemetry/OTLP. Development/test uses Grafana OTEL-LGTM as the local visualization/reference stack. Production observability backend remains a deployment/operations choice and must consume the same OpenTelemetry contract.

## Revisit trigger
Production provider selection or local stack operational limitations.
