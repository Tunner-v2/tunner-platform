> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 19 — Connected Product, Environment, Client & SDK Lifecycle FRD

## 1. Authority and traceability

Fulfills FigJam **128–135, 128A, 128B**, plus integration portions of 05/07/16–18. Tunner owns Product registration/integration contracts and evidence. The Product owns Product organizations/workspaces/memberships/roles/entitlements/usage/operational state.

## 2. Product and environment model

`ConnectedProduct` states:

```text
DRAFT -> ACTIVE -> SUSPENDED -> RETIRED
```

Each Product has isolated environments:

```text
SANDBOX
STAGING
PRODUCTION
```

`ProductEnvironment` states:

```text
PROVISIONING -> ACTIVE -> RESTRICTED | SUSPENDED -> RETIRED
```

Environment credentials, OAuth clients, webhook endpoints, signing secrets, R2 prefixes/buckets where applicable, provider mappings, rate/protection policy and event deliveries are isolated. Production credentials never work in Sandbox/Staging.

## 3. Registration and activation

Product activation requires:

- Product administrative/contact metadata required for Tunner service operation;
- environment provisioning;
- client/credential setup;
- redirect/webhook validation;
- assigned capabilities;
- API/SDK compatibility;
- MarketEligibility, ProductKycCase/payment-service eligibility and Tunner provider-route readiness where commercial capabilities are enabled;
- provider readiness for enabled payment/notification capabilities;
- synthetic readiness tests and no blocking integration incident.

Activation produces immutable readiness evidence. It does not create a Product organization/workspace or Product user entitlements.

## 4. OAuth clients

Client classes:

- browser/public authorization-code client with PKCE;
- confidential web/backend client;
- machine/workload client where an approved machine-to-machine flow exists.

Redirect URIs are exact-match registered values. Arbitrary request return URLs are prohibited. Credential rotation supports bounded overlap with explicit current/next versions and revocation.

## 5. Capabilities

Capabilities are versioned assignments per Product environment, such as:

- Identity/SSO;
- Billing Account;
- Billing Subscription;
- Payments;
- Notifications;
- verification/risk integration;
- events/webhooks;
- API protection profile.

Capability assignment is not business-state authority and does not activate a Product feature automatically.

## 6. API/SDK compatibility

OpenAPI 3.1 is the executable public API contract on .NET 10. SDKs consume generated contract clients plus Tunner ergonomic/version-safety layers.

Compatibility states:

```text
SUPPORTED
DEPRECATED
BLOCKED_IN_SANDBOX_ONLY_TEST
RETIRED
```

A breaking contract requires a new public API/schema version. Deprecation has explicit published dates/policy; production requests are never silently routed to an incompatible contract.

## 7. Event subscription and delivery

Product registers versioned event subscriptions with event type/version ranges, filter, endpoint, signing configuration and retry policy.

Every delivery has stable `event_id`, `subscription_id`, `delivery_id`, attempt lineage and endpoint/config version. At-least-once delivery is assumed. Redrive preserves the same business event identity and cannot recreate originating billing/payment work.

Product Application Result is distinct from HTTP acknowledgement. A Product may acknowledge transport and later report local application failure; Tunner records the integration exception without rolling back successful owner-domain state.

## 8. API protection

`ProductApiProtectionPolicy` defines rate/capacity/abuse protection by Product/environment/client/capability. These counters are technical protection telemetry only and never Product usage/billing metering.

Limit decisions are versioned and return standard retry/problem information. Security abuse policy may impose restriction independently of commercial rate limits.

## 9. Self-service integration readiness

Sandbox provides:

- credentials/client registration;
- webhook signing test;
- synthetic event delivery;
- OAuth redirect validation;
- contract-version test;
- provider/capability readiness where non-production provider credentials exist;
- diagnostics with correlation/event/delivery IDs;
- promotion checklist to Staging/Production.

Promotion copies no secrets and no business data. Configuration is re-created/versioned per environment.

## 10. SDK policy

Initial SDKs:

- .NET reference;
- TypeScript parallel/next.

SDKs must provide version headers, correlation/idempotency helpers, webhook verification, typed errors and safe retry guidance. SDK helpers cannot hide `UNKNOWN_EXTERNAL_STATE` or retry unsafe side effects.

## 11. Suspension/retirement

Product/environment suspension blocks new eligible API operations according to capability policy, but does not rewrite committed financial/identity history. Event deliveries already committed follow the suspension/delivery contract (for example continue security/financial mandatory events while optional Product events may pause).

Retirement requires credential revocation, webhook/event disposition, active Billing Relationship/Subscription review, retention/export evidence and immutable integration history.

## 12. Acceptance cases

- Sandbox token cannot call Production;
- redirect mismatch is rejected before authorization code issuance;
- duplicate event redelivery maps to existing Product application lineage;
- one Product delivery failure cannot block another subscriber;
- protection counters cannot become billing usage;
- Product suspension does not rewrite subscription/payment finality;
- SDK version incompatibility produces deterministic upgrade/error guidance.
