> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Closure revision date:** 2026-09-29  

# 23 — Provider Account & Routing Operations FRD

## 1. Authority and traceability

Fulfills FigJam **87–92** and provider portions of **11–15** under PDR-0006 and PDR-0008. Provider Operations owns Tunner provider instances, provider merchant/account bindings, provider capabilities/readiness evidence, credentials references, provider webhooks, routing policy, route-resolution evidence and provider health/incidents. It does **not** own Product economic attribution, ProductPayable, Settlement, Product KYC authority, Payment finality, Subscription, Invoice, Tax, Notification or Ledger finality.

Initial provider decisions:

- payment provider: **Stripe** through official `Stripe.net`;
- transactional email provider: **Resend** through official .NET SDK;
- local/test email capture: **Mailpit**;
- object storage: **Cloudflare R2** through S3-compatible `IObjectStorage` and AWS SDK for .NET;
- secrets: OpenBao abstraction/reference implementation.

Provider-specific objects remain execution artifacts. Tunner domain records remain authoritative.

## 2. Canonical resources

- `ProviderInstance` — configured Tunner provider adapter instance such as Stripe production.
- `ProviderAccountBinding` — versioned Tunner relationship to an external provider merchant/account/configuration.
- `ProviderObjectReference` — typed provider-native object/event evidence reference; never a Tunner business-state substitute.
- `ProviderCapability` — normalized provider/account capability and readiness state.
- `ProviderCredentialBinding` — vault reference only; no secret value in database.
- `ProviderWebhookBinding` — provider endpoint/signing/version relationship.
- `ProviderRoutingPolicy` — deterministic selection policy.
- `ProviderRouteResolution` — immutable event-time resolution of the exact Tunner provider account/route used for an operation.
- `ProviderSettlementProfile` — versioned mapping of provider/account + currency context to settlement currency, TunnerTreasuryAccount destination and provider conversion behavior.
- `ProviderHealthEvidence` — normalized health/degradation evidence.
- `ProviderIncident` — provider-specific operational incident linked to Platform/Support when required.

`ProductKycCase` is consumed as a Tunner Product eligibility input where applicable but is owned by the Product compliance/risk authority, not Provider Operations. Provider-side verification/KYC of Tunner's own account is retained only as provider/account evidence and capability state.

## 3. Product/provider relationship

All Connected Products use the same Tunner-facing integration model and provider abstraction.

The provider merchant/account relationship is strictly Tunner ↔ Provider. Products do not own, select or receive a persistent assignment to provider merchant/accounts as an economic ownership model.

One Product may have transactions executed through multiple ProviderAccountBindings over time or concurrently when routing policy permits. One ProviderAccountBinding may serve transactions for many Products. Every transaction remains economically attributed to its `product_id`; provider route/binding identity exists only for execution, finality and reconciliation evidence.

Historical operations persist the exact `ProviderRouteResolution`, ProviderAccountBinding version and ProviderObjectReference evidence used. Changing provider routing never rewrites ProductPayable, Settlement or historical Product attribution.

The provider's own topology may use a master account, merchant IDs, sub-accounts, connected accounts or another construct. That topology is internal to the adapter and must not become a Product API contract.

## 4. Provider Instance lifecycle

Canonical states:

```text
DRAFT
VALIDATING
ACTIVE
DEGRADED
DISABLED
RETIRED
```

Activation requires:

- adapter version supported;
- environment match;
- vault credential binding active;
- required endpoint/signing configuration active;
- mandatory capabilities verified;
- provider account/merchant readiness evidence current;
- provider health/readiness tests pass;
- no blocking Security/Risk/Platform incident;
- routing policy resolves without ambiguity.

`DEGRADED` can remain route-eligible only when active routing/incident policy explicitly permits the affected operation type. `DISABLED` and `RETIRED` are never route-eligible for new operations.

## 5. Provider account verification and Product compliance separation

Provider Operations records the external provider verification/requirement/capability evidence needed for Tunner's own provider merchant/accounts. Provider evidence can include requirements, capability status, account restrictions and remediation evidence.

Connected Product KYC/KYB is represented separately by Tunner `ProductKycCase`. Provider Operations may consume the Product's resulting eligibility state during payment/payout preflight but does not merge Product compliance evidence into ProviderAccountBinding ownership.

A provider-side account ID, verification flag or capability is evidence only. It never proves Product compliance eligibility, Product payable ownership or business finality.

## 6. Capability model

Capabilities are explicit and operation-specific, for example:

- `CARD_PAYMENT_CREATE`;
- `CARD_PAYMENT_CAPTURE`;
- `REFUND_CREATE`;
- `DISPUTE_EVIDENCE_SUBMIT`;
- `PROVIDER_PAYOUT_OBSERVE`;
- `PROVIDER_PAYOUT_REQUEST` where the provider contract supports Tunner initiation;
- `EMAIL_SEND`;
- `EMAIL_WEBHOOK_RECEIVE`;
- `TAX_CALCULATE` where an adapter is enabled.

A provider route is eligible only when **all required capabilities** for that operation are active.

## 7. Routing policy

`ProviderRoutingPolicy` is versioned by capability, Product/environment operation context, Product eligibility/compliance result where applicable, presentment currency/market, payment method, checkout presentation capability, settlement capability, available ProviderAccountBinding records and operational health.

Resolution returns exactly one of:

```text
ELIGIBLE_ROUTE
NO_ELIGIBLE_ROUTE
AMBIGUOUS_ROUTE
POLICY_BLOCKED
DEPENDENCY_UNAVAILABLE
```

An `ELIGIBLE_ROUTE` produces immutable `ProviderRouteResolution` evidence identifying the exact ProviderInstance/ProviderAccountBinding/policy versions selected for that operation.

Provider confidentiality/abstraction rule: Connected Products receive Tunner operation/payment IDs and normalized capabilities/results. Provider names, account IDs, routing scores and provider-native objects are internal unless an explicit support/compliance disclosure policy requires otherwise.

A successful route resolution does not authorize or execute the external side effect. The owning domain must still validate authority, expected versions, idempotency and operation-specific preconditions immediately before submission.

For interactive checkout, route resolution occurs before provider-specific payment collection begins. An eligible route may use an approved embedded provider component or provider-hosted checkout mode. The Connected Product still starts one Tunner CheckoutOperation and never selects the provider.

A provider/account in onboarding, pending review, verification hold, disabled/restricted state or without the required charge capability is not eligible for a new payment. Provider switching does not imply token portability; a route change before submission may require secure recollection through the new provider flow.

## 8. Provider settlement configuration

`ProviderSettlementProfile` resolves the provider/account settlement behavior for a supported transaction currency. It references:

- ProviderInstance / ProviderAccountBinding;
- applicable presentment/payment currencies;
- settlement currency;
- active TunnerTreasuryAccount destination;
- provider FX/conversion behavior when presentment differs from settlement;
- effective interval/version;
- reconciliation evidence requirements.

Tunner currently has an active CAD-denominated Canadian bank/treasury account. A planned USD account is not considered available until its TunnerTreasuryAccount and provider settlement binding are verified/ACTIVE.

Provider settlement conversion evidence is normalized into Finance `FxConversion`/ProviderObjectReference records. Provider settlement configuration never changes Product ownership.

## 9. Failover safety

Failover is allowed only **before** an external side effect has been attempted, or after authoritative evidence establishes that the prior attempt definitively did not create the side effect and policy allows alternate routing.

If outcome is:

```text
WAITING_EXTERNAL
PROCESSING
UNKNOWN_EXTERNAL_STATE
```

blind provider failover is prohibited.

This applies to payments, refunds, payouts, email delivery and other externally visible effects. Provider timeout is never sufficient proof that an operation did not occur.

## 10. Credential and signing management

Provider secrets are referenced through `ProviderCredentialBinding`:

- secret values live in vault/provider secret store only;
- database stores secret reference, provider, environment, purpose, version and activation metadata;
- rotation creates a new version and supports overlap only when provider protocol requires it;
- secret reveal is prohibited in normal Admin UI;
- webhook signing secrets support dual-key verification during configured rotation window where provider supports it;
- every credential access/rotation operation is auditable.

## 11. Webhook/callback configuration

Every provider webhook endpoint is bound to provider instance, environment, event families, signing configuration version, endpoint version, source/provider contract and deduplication/event identity rules.

Inbound provider events use the Tunner event-intake contract: authenticate/verify, persist safe evidence/reference, deduplicate, correlate, normalize, then hand to the owning domain.

A provider event never mutates authoritative business state through a generic reflection/mapping layer. Owning domain code maps only supported provider event semantics.

## 12. Health and incident behavior

Provider health evidence may come from official provider status/API, synthetic safe checks, authenticated request error rates, webhook lag/failures, credential/capability failures and reconciliation backlog.

Health affects **future routing eligibility**, not historical finality or Product economics.

Provider Operations may disable or constrain a route prospectively; it cannot rewrite Payment/Invoice/Notification/ProductPayable/Settlement state merely because a provider is degraded.

## 13. Admin requirements

Provider Operations Control Center must provide guided workflows for:

- provider instance setup/validation/activation;
- Tunner provider merchant/account binding and readiness;
- provider account requirement/remediation evidence;
- capability/readiness status;
- credential/signing rotation without secret reveal;
- webhook validation/test evidence;
- routing simulation and future-effective publication;
- provider incident disable/re-enable;
- retirement impact review.

Connected Product KYC/compliance review belongs to the applicable Product Compliance/Risk workflow and is shown here only as read-only eligibility context where required for provider preflight.

No generic key/value provider-config editor is allowed for production-sensitive settings.

## 14. Events

Post-commit events include:

- `ProviderInstanceActivated/Degraded/Disabled/Retired`;
- `ProviderAccountBindingChanged`;
- `ProviderAccountRequirementChanged`;
- `ProviderRoutingPolicyPublished`;
- `ProviderRouteResolved`;
- `ProviderCredentialRotated`;
- `ProviderIncidentOpened/Resolved`.

These are operational facts; they do not imply a payment/notification/ProductPayable business result.

## 15. Acceptance cases

- one Product has eligible transactions routed through Provider Account A and Provider Account B => all transactions remain attributed to the same Product while provider route evidence remains separate;
- one ProviderAccountBinding serves multiple Products => each transaction remains Product-attributed and independently auditable;
- provider binding exists but required charge capability is inactive/pending review/on hold => payment preflight excludes the route before provider collection;
- primary route is unavailable before payment submission and a separately approved active alternate exists => the checkout can use the alternate according to routing policy;
- no active alternate route exists => checkout blocks; Tunner does not bypass provider verification/hold;
- ProductKycCase is not eligible => Product operation blocks even when provider account is technically capable;
- provider timeout after request submission => no alternate provider attempt until reconciliation establishes safe finality;
- provider credentials rotate without exposing secret values to Admin/operator logs;
- routing-policy change affects future operations only and historical records retain prior route resolution evidence;
- Connected Product can complete payment integration without learning provider name/account/native IDs;
- provider-native money-movement/account object is observed => it is stored as ProviderObjectReference evidence and does not become Product financial ownership.
