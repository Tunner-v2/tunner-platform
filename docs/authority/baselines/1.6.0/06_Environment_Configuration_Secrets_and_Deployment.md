> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 06 — Environments, Configuration, Secrets & Deployment

## 1. Environment classes

### Engineering environments

- `LOCAL_DEVELOPMENT`
- `EPHEMERAL_TEST`

### Connected Product environments

- `SANDBOX`
- `STAGING`
- `PRODUCTION`

A developer laptop is not Product Sandbox.

## 2. Product environment isolation

Each Connected Product environment has independent:

- environment ID;
- OAuth clients/credentials;
- redirect URIs;
- webhook endpoints;
- signing keys/secrets;
- event subscriptions;
- API protection policy;
- SDK/API compatibility posture;
- provider test/live configuration;
- operational state.

No Production credential can authenticate against Sandbox or Staging.

No environment’s webhook signing secret may be reused in another environment.

## 3. Data isolation

- Production customer data must not be copied into non-production by default.
- Sanitized/synthetic fixtures are preferred.
- Any approved production-derived test data requires governed anonymization/minimization.
- Sandbox financial/provider actions use test/sandbox provider capabilities.
- Staging approximates production topology without assuming access to live customer funds.

## 4. Configuration policy

No business policy or environment-specific setting is hardcoded into application code.

Configuration categories:

1. immutable code constants/protocol rules;
2. non-secret deploy-time configuration;
3. versioned business policy/configuration;
4. secrets/credentials;
5. dynamic runtime state.

Only category 1 belongs directly in code.

## 5. Secrets

All secrets are resolved via secret-store abstraction.

Local/reference implementation: OpenBao.

Production secret provider remains cloud-neutral through adapters.

Prohibited:

- secrets in `appsettings*.json`;
- `.env` committed secrets;
- secrets in source code;
- provider API keys in application tables;
- secrets in logs;
- secret values in issue/PR text;
- secrets in screenshots/evidence.

Application storage holds references/fingerprints/metadata only where needed.

## 6. Bootstrap problem

A process needs a minimal identity/bootstrap method to access the vault.

That bootstrap must use environment/workload identity or an approved short-lived credential mechanism.

Do not solve vault access by putting a permanent vault root token in config.

## 7. Default local ports

Defaults are documented and configurable.

| Component | Default |
|---|---:|
| Tunner API | 7100 |
| User web | 7110 |
| Admin/Support web | 7120 |
| Worker health/diagnostic | 7130 |
| PostgreSQL | 5432 |
| RabbitMQ AMQP | 5672 |
| RabbitMQ Management (local only) | 15672 |
| Redis | 6379 |
| OpenBao | 8200 |
| OTLP gRPC | 4317 |
| OTLP HTTP | 4318 |

If a dependency already uses a conventional standard port, retain it unless conflict requires override.

## 8. Deployment promotion

Build once; promote immutable artifact.

`CI Build → Sandbox → Staging → Production`

Environment configuration changes; application artifact does not.

Release manifest records image digest.

## 9. Health endpoints

Separate:

- liveness;
- readiness;
- protected detailed diagnostics.

Public health responses must not expose secrets, topology or sensitive dependency detail.

## 10. Example — Product Acme

```text
ACME / SANDBOX
 client: acme-sbx-...
 webhook: https://sandbox.acme/...
 provider mode: test

ACME / STAGING
 client: acme-stg-...
 webhook: https://staging.acme/...

ACME / PRODUCTION
 client: acme-prod-...
 webhook: https://api.acme/...
 provider mode: live
```

No credentials are interchangeable.

## 11. Acceptance tests

| ID | Scenario | Expected |
|---|---|---|
| ENV-001 | Sandbox credential against Prod | authentication rejected |
| ENV-002 | repo secret scan | zero committed secrets |
| ENV-003 | API starts | secret references resolved from vault |
| ENV-004 | production image promoted | identical digest used across staging/prod |
| ENV-005 | port conflict | supported config override changes port without code change |

## 12. Local Docker dependency topology

The reference local environment includes:

- PostgreSQL 18;
- RabbitMQ 4.3 current supported patch with management UI enabled locally;
- Redis 8.x;
- OpenBao;
- OpenTelemetry Collector;
- selected local observability viewers;
- Tunner API and Worker.

User/Admin web hosts are included when the applicable UI plane starts. Provider simulators/stubs are added per milestone.

Local management ports must bind to loopback by default and must not imply production exposure. All ports remain configurable.

## 13. Infrastructure configuration boundaries

Connection endpoints are non-secret configuration. Credentials, passwords, signing material and object-store keys are secret references resolved through the vault.

Examples:

- RabbitMQ: host/vhost/topology name may be config; credentials are vault secrets.
- Redis: endpoint/instance prefix may be config; credentials/TLS client secrets are vault-managed.
- Cloudflare R2: account/endpoint, bucket name, jurisdiction/location policy reference and public/custom-domain configuration may be non-secret config; R2 access-key ID/secret and any signing credentials are vault-managed. Do not assume AWS KMS or S3 Object Lock APIs are available.
- SignalR/gRPC-Web: route/path and feature enablement are configuration; authentication remains ASP.NET Core security policy.

## Transactional email environments

- Local/Test: Mailpit captures email without external delivery.
- Sandbox/Staging/Production: `IEmailProvider` selects the configured provider; Resend is the primary production-capable adapter through the official .NET SDK.
- Resend API credentials and webhook signing secrets live only in the vault.
- Mailgun is an approved alternate provider but is not implemented in the MVP unless a governed resiliency/commercial/regional trigger is approved.
- Delivery webhooks are authenticated/verified, normalized into Tunner notification-delivery evidence and processed idempotently.
- Transactional/security email is separated from marketing communication consent/preferences.

## Cloudflare R2 environments

Cloudflare R2 is an external managed dependency and is not emulated as an authoritative local Docker service.

- unit/component tests use a deterministic `FakeObjectStorage` only where no real provider behavior is under test;
- R2 integration/contract tests use dedicated non-production R2 buckets and scoped credentials;
- Sandbox, Staging and Production use separate buckets and credentials; Production buckets are never shared with lower environments;
- R2 location hints are performance placement hints, not residency guarantees;
- when residency must be guaranteed, use an approved R2 Jurisdictional Restriction where available;
- if an approved `data_region`/residency policy requires a jurisdiction R2 cannot guarantee, activation is blocked and another `IObjectStorage` adapter must be approved rather than pretending compliance;
- R2 lifecycle and bucket-lock policy is provisioned through governed infrastructure configuration. PostgreSQL remains authoritative for object ownership, retention/hold intent and lifecycle state.

## Data-region and residency capability

Tunner does not promise strict U.S. or Canadian data residency before a deployment/cloud region is approved. Every Connected Product environment nevertheless carries governed residency metadata such as `data_region`, residency policy/version and applicable storage/processing constraints so future enforcement does not require redesigning Product/environment identity.

## Local observability environment

Local/Test uses Grafana `otel-lgtm` as the default OpenTelemetry backend. Default local ports include Grafana UI `3000`, OTLP/gRPC `4317` and OTLP/HTTP `4318`; ports remain configurable. Production observability backend is selected with the eventual cloud/operations architecture and does not change application instrumentation contracts.
