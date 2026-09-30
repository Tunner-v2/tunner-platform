# Local Docker dependency foundation

## Scope

This is the P0-006 local dependency foundation. It supplies PostgreSQL 18, RabbitMQ 4.3 with the local management UI, Redis 8, OpenBao, Mailpit, and Grafana OTEL-LGTM. OTEL-LGTM includes the local OpenTelemetry Collector and development/test viewers.

It is not a Product runtime, a production deployment, an object-store emulator, or a provider-credential environment. The API and Worker entries are opt-in health placeholders only; they do not represent a Tunner application implementation.

## Prerequisites

- Docker Desktop is running.
- Docker Compose is available.
- Ports in `infra/docker/.env.example` are free, or an ignored `infra/docker/.env.local` overrides only the conflicting port values.

Do not put passwords, API keys, R2 credentials, OpenBao tokens, or other secrets in `.env.local`. P0-007 owns local OpenBao initialization, least-privilege policy, and application secret injection.

## Commands

From the repository root in PowerShell:

```powershell
./tools/dev/tunner-dev.ps1 doctor
./tools/dev/tunner-dev.ps1 setup
./tools/dev/tunner-dev.ps1 start
./tools/dev/tunner-dev.ps1 health
./tools/dev/tunner-dev.ps1 logs
./tools/dev/tunner-dev.ps1 stop
```

`doctor` performs only Docker/Compose and static configuration checks. `start` pulls the pinned images and starts long-lived containers; run it only when you intend to use the local dependency environment. `stop` preserves named volumes. Docker Compose may write normal status/progress lines to standard error even when it succeeds; the wrapper preserves that output and treats the native Docker exit code as the success/failure boundary.

## Local-only exposure

Every published port binds to `127.0.0.1` by default. The RabbitMQ management UI, Mailpit UI, Grafana UI, and OpenBao API must never be treated as public endpoints. Mailpit is a local/test email sink; it does not deliver production email.

OpenBao's P0-006 health check establishes that the service responds in one of its documented states: uninitialized, sealed, or unsealed. It does not claim vault readiness. P0-007 must complete before an application receives secrets from OpenBao.

## Image policy

The Compose file uses exact version tags captured in `governance/evidence/TUN-P0-006-RD-SERVICES.json`; it does not use `latest` or `edge`. Tag review is point-in-time evidence, not a production image-digest approval.
