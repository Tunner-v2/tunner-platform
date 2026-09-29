> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Closure revision date:** 2026-09-29  

# 32 — Domain API, Event, Data & Policy Contract Catalog

## 1. Purpose

This is the canonical cross-domain contract catalog. It prevents implicit coupling, duplicated authority and AI-generated ad-hoc contracts. Detailed field schemas are generated/maintained in code/OpenAPI/JSON Schema during implementation, but every contract family and invariant is fixed here.

## 2. Contract rules

- public Product API: HTTPS REST/JSON, OpenAPI 3.1 executable baseline;
- browser: User/Admin BFF REST/JSON; SignalR only for realtime notifications/status/read-model changes;
- Product async: signed/versioned HTTPS webhook/events with CloudEvents-compatible envelope;
- internal same-process domain calls: typed application contracts;
- RabbitMQ: durable post-commit independent async transport;
- every mutating operation has stable `operation_id`; caller idempotency key where duplicate submission is plausible;
- `expected_version`/ETag-style concurrency is mandatory for versioned mutable resources where lost update matters;
- `correlation_id` follows the business operation across events/providers;
- provider external IDs are never primary domain IDs.

## 3. Canonical identifiers

Stable opaque IDs use type prefixes in human-visible contexts, without encoding mutable semantics:

```text
ACC-* Account
PRD-* Product
ENV-* Product Environment
BILL-* Billing Account
MEM-* Billing Membership
BAUTH-* Billing Authority
BREL-* Product Billing Relationship
SUB-* Billing Subscription
SUBOP-* Subscription Operation
SNAP-* Commercial Snapshot
INV-* Invoice
TXN-* Payment Transaction
PAYATT-* Payment Attempt
REF-* Refund
DISP-* Dispute
TAXDET-* Tax Determination
TAXREM-* Tax Remittance Obligation
JRN-* Journal
PCP-* Product Compliance Profile
PKYC-* Product KYC Case
PAB-* Provider Account Binding
PRR-* Provider Route Resolution
PRF-* Provider Object Reference
TTA-* Tunner Treasury Account
FXC-* FX Conversion
PPREF-* Product Payout Preference
PAYBL-* Product Payable
SET-* Settlement
PODV-* Product Payout Destination Version
PAYOUT-* Payout
POR-* Payout Receipt
POC-* Payout Processing Case
NTF-* Notification Intent
DLV-* Notification Delivery
SUP-* Support Case
RISK-* Risk Case
APR-* Approval Request
PRIV-* Privacy Request
INC-* Incident
OP-* Generic governed operation
EVT-* Domain event
```

IDs are never reused after deletion/closure.

## 4. Standard command envelope

Applicable API/application commands include:

```text
operation_id
idempotency_key (caller-facing where applicable)
correlation_id
actor/principal context (server-derived, never trusted from arbitrary payload)
resource_id
expected_version
requested_at
reason_code / reason text when required
policy-relevant context refs
command-specific payload
```

Authorization and actor identity are derived from authenticated context. Client cannot claim roles/account IDs outside authorized delegation.

## 5. Standard operation result

```text
operation_id
correlation_id
status
resource_id + version when created/changed
result refs
authoritative finality indicator
next_action when nonterminal
error/problem reference when failed
created_at / updated_at / terminal_at
```

Canonical operation statuses:

```text
CREATED
VALIDATING
BLOCKED
REQUIRES_ACTION
QUEUED
SUBMITTED
PROCESSING
WAITING_EXTERNAL
UNKNOWN_EXTERNAL_STATE
SUCCEEDED
FAILED
CANCELED
PARTIAL
```

Domain resources have their own state models; generic operation state must not be copied into domain enums blindly.

## 6. Error contract

HTTP/API errors use RFC-compatible Problem Details with Tunner extensions:

```text
type
title
status
detail (safe)
instance
error_code
correlation_id
operation_id when applicable
resource_id when safe
current_version / expected_version for conflict when safe
blocker_owner / blocker_ref when actionable
retryability classification
```

Canonical classes include:

- `VALIDATION_ERROR`;
- `AUTHENTICATION_REQUIRED`;
- `FORBIDDEN_SCOPE`;
- `STEP_UP_REQUIRED`;
- `APPROVAL_REQUIRED`;
- `VERSION_CONFLICT`;
- `BUSINESS_RULE_BLOCKED`;
- `DEPENDENCY_UNAVAILABLE`;
- `ROUTING_UNAVAILABLE`;
- `UNKNOWN_EXTERNAL_STATE`;
- `NOT_FOUND_OR_NOT_DISCLOSABLE` where enumeration resistance is required;
- `RATE_OR_ABUSE_LIMITED`;
- `CONTRACT_VERSION_UNSUPPORTED`.

## 7. Domain event envelope

Every domain event has:

```text
event_id
specversion/event envelope version
event_type
event_schema_version
occurred_at
published_at
source domain/resource
subject/resource_id
resource_version
operation_id
correlation_id
causation_event_id when applicable
Product/environment scope when applicable
data classification
payload
```

Business `event_id` is stable across broker retries and Product webhook redrives.

## 8. Event Subscription / delivery contract

`EventSubscription` is versioned and contains subscriber type, compatible event types/schema ranges, filters, handler generation, route, retry/DLQ policy and effective interval.

`EventDelivery` records:

- event/subscription IDs;
- attempt lineage;
- endpoint/handler/config version;
- status;
- authenticated acknowledgement;
- last error and next retry;
- DLQ/redrive evidence.

Consumer inbox uniqueness: at minimum `event_id + subscription_id + handler_generation`.

## 9. Product webhook signing

Outbound Product webhook includes:

- immutable event identity/version;
- timestamp;
- delivery identity;
- signature keyed by registered signing configuration version;
- endpoint/config version;
- correlation.

Secret rotation supports versioned overlap. Replay/redrive signs a new transport attempt but preserves the same business event identity.

## 10. Identity contracts

Public/user contracts:

- registration/verification/authentication/recovery;
- sessions/devices/authenticators;
- external identity link/unlink;
- Product OAuth/OIDC authorization.

Key invariants:

- Account identity != Product membership;
- Product session != Tunner session;
- verified contact mutation only via verification workflow;
- sensitive account existence not disclosed through arbitrary recovery/registration queries.

## 11. Billing Account contracts

- resolve/provision payer context;
- membership operations;
- capability authorization resolution;
- Billing Profile version;
- Payment Method Reference lifecycle;
- Product Billing Relationship;
- state/closure/migration.

Membership and authority are distinct resource/version families.

### Tunner Checkout contract

`CheckoutOperation` is a Tunner-owned customer journey and operation identity. It binds Product/environment, Billing Account/customer context, immutable CommercialSnapshot, explicit amount/presentment currency, TaxDetermination, consent/disclosure versions and the resulting PaymentTransaction.

`CheckoutPresentationMode` is `EMBEDDED_PROVIDER_COMPONENT | PROVIDER_HOSTED_REDIRECT` and is selected by the eligible provider adapter/route, not by the Connected Product.

`ProviderPaymentCollectionSession` is short-lived provider execution metadata created only after `ProviderRouteResolution`. It may expose only browser-safe initialization/redirect material required for the selected presentation mode. It is not Product authority, is not a portable cross-provider payment token and never contains raw PAN/CVV.

Provider/issuer authentication continuation is normalized through `REQUIRES_ACTION`. Tunner does not universally force 3-D Secure but must support authentication when the selected provider/network/issuer requires it. All continuations return/converge on the Tunner CheckoutOperation.

## 12. Commercial contracts

Product commercial source schema contains billing-safe projection only. Canonical commercial references include:

- plan/item source key/version;
- price model/version;
- currency/region/cadence;
- quantity/billable metric contract;
- Product tax classification ref;
- intro/discount ref;
- agreement/fee policy refs.

`CommercialSnapshot` is immutable and must be referenced by financial operations rather than reconstructed.

## 13. Subscription contracts

`SubscriptionIntent` requires explicit commercial action (`NEW_SUBSCRIPTION`, `REPLACE_PLAN`, `ADD_ITEM`, `REMOVE_ITEM`, `CHANGE_QUANTITY`, lifecycle action etc.) and cannot rely on server inference.

Product result contains Tunner billing state/financial refs only; Product entitlement application is Product-owned and separately acknowledged where contracted.

## 14. Payment/Invoice/Finance contracts

Payment transaction/attempt, Refund, Dispute, Invoice, Allocation, TaxDetermination, Journal, ProductPayable, Settlement, Payout, PayoutReceipt and exception-only PayoutProcessingCase maintain separate finality.

Canonical Product-finance/provider-separation requirements:

- `ProductKycCase` is the Tunner-owned Connected Product compliance/KYC record where required;
- ProviderAccountBinding belongs to Tunner ↔ Provider infrastructure and is not a Product financial-ownership relationship;
- ProductPayable ownership is Product + Product commercial transaction/payable currency; in the initial model payable currency equals the source presentment currency from the immutable CommercialSnapshot and is never changed by provider settlement currency;
- `ProductPayableBalance` is a derived Product + payable-currency liability view keyed by Product + payable currency (and as-of/read-model version where needed), not a separately mutable identified resource;
- approved Settlement is immutable; correction supersedes/compensates rather than edits;
- Finance records `TunnerTreasuryAccount`, `FxConversion` and versioned `ProductPayoutPreference`;
- each Settlement remains one Product + payable currency; initial cross-currency netting is disabled;
- each Payout stores immutable `ProductPayoutDestinationVersion` and requested payout currency; when currency differs from Settlement it references actual FxConversion evidence;
- any externally meaningful replacement payout uses a new payout identity/idempotency context and predecessor lineage;
- terminal Payout finality produces one immutable PayoutReceipt for that attempt;
- PayoutProcessingCase is created only for failure/return/unknown/mismatch/reconciliation work, not every success.

No contract may expose a single generic `payment_status` or `settlement_status` as a substitute for these distinct resources.

## 15. Provider adapter interfaces

Minimum provider-neutral interfaces include:

- `IPaymentProvider`;
- `IPayoutProvider` for external Product payout execution where a provider/bank adapter is enabled;
- `IProviderAccountAdapter` for provider-specific Tunner merchant/account capability, requirements and remediation behind Provider Operations;
- `ITaxEngine` — stable provider-neutral contract for approved managed tax services; adapters call external managed tax engines and must not embed Tunner-maintained tax-rate/jurisdiction/taxability tables;
- `IEmailProvider`;
- `IObjectStorage`;
- `ISecretStore`;
- optional future `IBankTransferProvider` only if a genuine provider capability requires it.

Adapters return normalized outcomes with typed `ProviderObjectReference` evidence; owner domains decide business transition. A ProviderObjectReference carries at minimum provider instance/account context, `provider_object_type`, `provider_object_id`, optional `provider_event_id`, observed timestamps and safe evidence metadata.

Tax-provider contracts include versioned `TaxEngineBinding`, adapter capability/readiness metadata and provider tax-calculation evidence. `StripeTaxAdapter` is the initial `ITaxEngine` implementation. Additional approved managed tax services can be added as adapters without changing Product-facing contracts or the canonical Checkout/Invoice/Payment/Finance ownership model. Tax-engine selection is configurable independently from payment-provider route selection, even when policy prefers using the same provider for both capabilities.

Provider-native terms such as transfer, balance transaction, disbursement or payout remain adapter/evidence terminology. They do not create a generic Tunner `Transfer` domain resource.

Product-facing contracts are provider-neutral: provider name/account/object identifiers are not required fields in Connected Product APIs/events. Product compliance resolves `ProductKycCase` eligibility where required; Payment/Provider modules resolve Tunner provider routing independently and expose only Tunner IDs, normalized requirements/capabilities and operation results.

Product compliance canonical contracts include `ProductComplianceProfile`, `ProductKycCase`, `KycRequirement`, `KycEvidenceRef` and `ProductComplianceDecision`. Provider infrastructure contracts separately include `ProviderInstance`, `ProviderAccountBinding`, `ProviderRouteResolution`, `ProviderCapability` and `ProviderObjectReference`.

Finance evidence contracts include `TunnerTreasuryAccount`, `FxConversion`, `ProductPayable`, derived `ProductPayableBalance`, `Settlement`, `ProductPayoutPreference`, `ProductPayoutDestinationVersion`, `Payout`, `PayoutReceipt` and `PayoutProcessingCase`; provider execution/finality evidence references `ProviderRouteResolution`/`ProviderObjectReference` without becoming Product ownership.

Notification provider contracts remain Tunner-internal. Connected Products request Tunner-managed notification intents through Tunner SDK/API and do not provide provider credentials or depend on provider-native message identity.

## 16. Object storage contract

`IObjectStorage` supports the subset required by Tunner:

- put/get/head/delete according to lifecycle policy;
- multipart if required for large exports/artifacts;
- presigned/temporary authorized access where appropriate;
- metadata/checksum handling;
- bucket/object key supplied by storage policy.

Code must not assume unsupported AWS S3 features merely because R2 is S3-compatible. Retention/hold authority remains PostgreSQL/policy even when provider bucket locks/lifecycle are used as defense in depth.

## 17. Policy registries

Versioned policy families include at minimum:

- Authentication/Assurance/Recovery/Age Assurance;
- Billing Authority and Billing Account lifecycle;
- Subscription lifecycle/proration/dunning/cadence;
- Commercial/Agreement/Fee/Discount;
- Market Eligibility / Product Compliance / payment-service capability;
- Tax Responsibility/Engine/Remittance;
- Currency Support / Provider Settlement / Product Payout / FX Economic Treatment;
- Payment execution/retry/refund/dispute;
- Provider routing/capability/incident;
- Notification class/template/route/consent;
- Support SLA/Risk/Approval/SoD/Internal Access;
- Retention/Privacy/Processor/Residency;
- ReliabilityBudget/DataProtection/Change;
- API protection/integration compatibility.

Every policy version has stable ID, status, effective interval, supersedes/version lineage and approval evidence.

### FxEconomicTreatmentPolicy contract

`FxEconomicTreatmentPolicy` is a versioned/effective commercial policy, not application code. It resolves by scope including Product/environment/agreement as applicable, FX context, source/target currency or corridor where needed, and effective time. Canonical FX contexts include `CUSTOMER_ISSUER`, `TUNNER_TREASURY`, `PRODUCT_PAYOUT` and `STATUTORY_ACCOUNTING`.

Canonical treatment modes are:

- `EXTERNAL_TO_TUNNER`;
- `TUNNER_BORNE`;
- `PRODUCT_BORNE`;
- `SHARED`;
- `REPORTING_ONLY`.

`SHARED` requires an explicit allocation percentage totaling 100% and may include approved cost/subsidy caps. Product-affecting treatment records the allocatable cost-component set, exact `ProductCommercialAgreement`/policy version and actual `FxConversion` evidence. Provider settlement/treasury FX is `TUNNER_BORNE` by platform default; Product-requested payout FX is `PRODUCT_BORNE` by platform default; customer/issuer FX is `EXTERNAL_TO_TUNNER`; statutory/accounting translation is `REPORTING_ONLY`. Narrower approved agreement policy may override Product-affecting defaults. No policy may allocate an invented/unevidenced spread or duplicate an already-posted external cost.

Configuration APIs/UI expose a guided candidate-version workflow: resolve current policy -> choose template/mode -> validate scope/currency/corridor -> configure split/caps/components where applicable -> preview financial effect -> submit for approval -> activate effective version. Historical policy/event references are immutable.

## 18. Policy resolution rule

Runtime resolves policy by **business effective time + explicit scope**. Current UI/operator state cannot substitute for exact event-time policy. If resolution is missing/ambiguous for a required control, the operation blocks rather than chooses a default in application code.

## 19. Data ownership

- each aggregate/resource has one owning module;
- other modules reference stable IDs/read projections rather than shared writable tables;
- no generic cross-domain repository permits direct mutation;
- reporting projections may denormalize but are rebuildable/read-only;
- immutable evidence preserves exact owner version references.

## 20. API versioning / compatibility

OpenAPI contract changes are classified as compatible/additive, conditionally compatible or breaking. Breaking public Product contracts require a new supported version and deprecation/retirement process.

SDK generated transport layer uses the normative OpenAPI contract; handwritten ergonomic SDK layer cannot invent fields/endpoints absent from the contract.

## 21. Data types

- money: decimal/minor-unit-safe domain type + ISO currency; never binary floating point;
- timestamps: UTC instants; local timezone/calendar only where business rule explicitly needs it;
- effective intervals: explicit start/end semantics;
- IDs: opaque strings/UUID/ULID implementation detail hidden behind domain ID type;
- enums: unknown/forward-compatibility handling defined at API boundary;
- secrets: reference type only;
- external provider IDs: typed provider reference, never unqualified string in domain core.

## 22. Acceptance cases

- one domain cannot directly update another domain's authoritative table;
- event redrive preserves event_id and does not recreate originating business action;
- stale expected_version fails deterministically;
- missing required policy resolution blocks rather than uses hidden constant;
- provider ID collision across providers cannot alias because provider reference is typed;
- SDK output stays compatible with OpenAPI schema;
- Product webhook retry is dedupable by event/subscription identity;
- read model deletion/rebuild cannot lose business truth.


## Global Presentation Content contracts

The global governed presentation-content capability defined by documents 10/14/31 is a cross-cutting Tunner contract and must not be implemented as screen-local hard-coded copy. Canonical resources/contracts include `ContentDefinition`, `ContentKey`, `ContentVariant`, immutable `ContentVersion`, `ContentPublication` and `ContentProposal`.

Minimum API capabilities: resolve effective content by semantic key + authorized safe context; query content definitions/versions for authorized administration; create/update draft proposal; preview candidate resolution; validate variables/slots/content class; submit/approve where policy requires; publish prospectively; inspect history/diff; and rollback by governed publication of an approved version.

Resolution dimensions are allowlisted and versioned. Missing content follows deterministic fallback; mandatory sensitive content may block. Content APIs never mutate domain state, eligibility, authorization, price, financial calculation, compliance outcome or workflow behavior. AI/model adapters can create `ContentProposal` only. Publication emits auditable content-publication/version events where required and historical consent/notification/disclosure evidence retains effective content-version references.
