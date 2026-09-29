> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Closure revision date:** 2026-09-29  

# 24 — Payments, Refunds, Disputes & Reconciliation FRD

## 1. Authority and traceability

Fulfills FigJam **13, 19, 21** and payment portions of Subscription/Provider/Finance flows. Payment owns Tunner payment transaction and attempt finality. Stripe is the initial execution provider, but Stripe objects are supporting evidence rather than Tunner business authority.

## 2. Canonical resources

- `PaymentTransaction` — logical financial collection/charge identity.
- `PaymentAttempt` — one distinct external submission attempt under a transaction.
- `PaymentMethodReference` — Billing-owned PCI-safe provider reference.
- `PaymentProviderEvidence` — external IDs/events/balance transaction refs, including provider settlement amount/currency and exchange-rate evidence when applicable.
- `Refund` and `RefundAttempt`.
- `PaymentVoid` where provider/payment rail supports cancellation before settlement/capture.
- `Dispute` and `DisputeEvidenceSubmission`.
- `PaymentReconciliationCase`.
- `PaymentAllocation` — allocation to Invoice/receivable; Finance/Invoice contract.

## 3. PaymentTransaction state model

Canonical states:

```text
CREATED
REQUIRES_ACTION
PROCESSING
SUCCEEDED
FAILED
UNKNOWN_EXTERNAL_STATE
CANCELED
```

Rules:

- `CREATED` means authoritative Tunner identity exists; no success implied;
- `REQUIRES_ACTION` means customer/provider continuation is required;
- `PROCESSING` means known external processing with no final Tunner outcome;
- `SUCCEEDED` requires contract-defined financial success evidence;
- `FAILED` requires definitive failure evidence;
- `UNKNOWN_EXTERNAL_STATE` blocks retry/reroute/refund assumptions until reconciled;
- `CANCELED` is allowed only before a succeeded financial outcome and according to provider/operation contract.

Provider status strings are mapped explicitly and are never persisted as the Tunner state enum without normalization.

## 4. PaymentAttempt state model

```text
PREPARED
SUBMITTED
REQUIRES_ACTION
PROCESSING
SUCCEEDED
FAILED
UNKNOWN_EXTERNAL_STATE
CANCELED
```

Each actual external submission has a distinct `attempt_id` and immutable provider route/config/idempotency lineage.

A retry creates a **new PaymentAttempt under the same PaymentTransaction**, never overwrites the previous failed attempt.

## 5. Creation and preflight

Before creating/submitting an external attempt, Payment validates:

- Billing Account and applicable authority;
- immutable Commercial Snapshot and amount/currency;
- Invoice/receivable relationship when applicable;
- Payment Method Reference readiness;
- active Product payment/compliance eligibility;
- Provider Account/Instance capability and environment;
- market/Product-compliance/tax gate;
- no existing processing/unknown/successful attempt that would make a duplicate unsafe;
- stable operation and idempotency identity.

If provider readiness is unavailable **before** submission, persist a blocked operation/result without pretending an external attempt exists.

## 6. Provider execution and checkout presentation

Stripe is the initial provider adapter through official `Stripe.net`, but Payment execution is provider-neutral.

A Tunner CheckoutOperation may use an approved provider `EMBEDDED_PROVIDER_COMPONENT` or `PROVIDER_HOSTED_REDIRECT` presentation mode. Raw PAN/CVV never traverses Tunner application servers/logs/databases. Provider-hosted or provider-secure client components collect/tokenize sensitive payment data and return only provider-safe references/results.

Provider route resolution occurs before provider-specific collection begins. A provider/account that is not currently charge-capable is excluded. If an eligible route fails before any external side effect, a new PaymentAttempt may use another already-active route under policy. If submission may have occurred, `UNKNOWN_EXTERNAL_STATE`/processing semantics prohibit blind failover.

Tunner does not force 3-D Secure by default. When a provider/network/issuer requires authentication, Payment enters `REQUIRES_ACTION`, preserves the continuation evidence and resumes the same PaymentTransaction after the required customer action.

`PaymentIntent`, `Charge`, provider Checkout/session objects and equivalent provider-native resources are execution artifacts. Tunner maps them to stable `PaymentTransaction` and `PaymentAttempt`.

When provider settlement currency differs from customer presentment currency, Payment/Provider evidence preserves both amounts/currencies and the provider balance/settlement reference so Finance can create immutable `FxConversion` evidence.

Per PDR-0006, Product-facing payment contracts never require or reveal the selected provider. The Connected Product submits one Tunner payment/billing operation and receives Tunner IDs/status/results.

## 7. Payment success boundary

The exact success boundary is part of the Payment Execution Policy:

- for automatic card capture, `SUCCEEDED` requires captured/succeeded financial evidence;
- an authorization-only state is **not** collected revenue and remains nonterminal for capture-required workflows;
- asynchronous payment methods remain processing until their mapped finality event/reconciliation;
- provider acceptance of an API request is not Payment success.

Subscription activation/renewal reads the normalized Payment result and does not inspect raw Stripe statuses.

## 8. Webhook/event ingestion

Stripe events are:

1. signature-verified against the active/rotation signing configuration;
2. deduplicated by provider event identity;
3. persisted/correlated;
4. mapped to supported Tunner provider outcomes;
5. applied by Payment transaction rules with expected/current state checks;
6. committed before post-commit events are dispatched.

Out-of-order or repeated events must converge without rolling back a later authoritative state improperly.

## 9. Unknown outcome and reconciliation

Timeout/connection break after possible submission becomes `UNKNOWN_EXTERNAL_STATE` when Tunner cannot establish whether Stripe accepted/performed the operation.

Required behavior:

- freeze blind retry and route failover;
- use original provider object/idempotency/external refs to query/reconcile;
- create/update `PaymentReconciliationCase`;
- preserve every reconciliation observation;
- transition only after authoritative evidence establishes finality.

Reconciliation may use provider object status, authenticated webhook evidence, balance transaction or settlement evidence appropriate to the operation.

## 10. Retry policy

Retries are failure-class specific and versioned. Retry is permitted only after a **definitive failed** attempt and when all current preconditions still pass.

No generic automatic retry of unsafe payment POSTs is allowed by HTTP resilience middleware. Transport resilience for payment creation must respect domain idempotency and unknown-outcome handling.

Dunning/renewal determines **when another attempt is allowed**; Payment determines **how that attempt is safely executed**.

## 11. Refunds

`Refund` states:

```text
REQUESTED
APPROVED
SUBMITTED
PROCESSING
SUCCEEDED
FAILED
UNKNOWN_EXTERNAL_STATE
CANCELED_BEFORE_SUBMISSION
```

Refund review validates:

- original succeeded/captured amount;
- remaining refundable balance;
- currency/provider relationship;
- invoice/allocation/credit consequences;
- dispute/conflict state;
- reason, authority, step-up/approval when policy requires;
- duplicate/idempotency protection.

A successful refund creates compensating financial/ledger evidence; it never edits the original Payment journal/history.

Unknown refund outcome blocks another refund for the same amount/basis until reconciled.

## 12. Voids/cancellations

Authorization void/cancel is separate from refund. It is available only when the original provider/payment state supports it and no capture/settlement already requires refund semantics.

Void result is immutable provider/financial evidence. A void request approval is not void success.

## 13. Disputes/chargebacks

`Dispute` states are normalized separately from Payment:

```text
OPEN
EVIDENCE_DUE
EVIDENCE_SUBMITTED
WON
LOST
CLOSED_OTHER
```

Provider dispute events create/update a Dispute linked to original Payment. Dispute financial effects are compensating events/journals; original Payment evidence remains immutable.

Evidence submission:

- is allowed only for permitted provider dispute phase;
- uses approved evidence fields/doc refs;
- does not expose unrelated customer/Product data;
- records provider acknowledgement and submission version;
- remains distinct from final dispute outcome.

## 14. Fraud/risk boundary

Risk/Security may gate payment creation/refund/dispute evidence according to active policy, but Payment remains owner of transaction finality. A Risk approval or denial is evidence/input, not a direct database state rewrite.

## 15. API surface

At minimum:

- create/resume Payment operation;
- get Payment/attempt status;
- continue required customer action;
- request/approve/execute refund;
- request void when eligible;
- get/open reconciliation case;
- query disputes and submit governed dispute evidence;
- internal provider-event ingestion endpoint;
- internal reconciliation operation.

All mutating commands accept stable operation/idempotency identity and expected versions where applicable.

## 16. Events

Post-commit examples:

- `PaymentCreated`, `PaymentRequiresAction`, `PaymentProcessing`, `PaymentSucceeded`, `PaymentFailed`, `PaymentOutcomeUnknown`;
- `RefundRequested`, `RefundSucceeded`, `RefundFailed`, `RefundOutcomeUnknown`;
- `DisputeOpened`, `DisputeEvidenceSubmitted`, `DisputeWon`, `DisputeLost`;
- `PaymentReconciled`.

## 17. UI/operator requirements

Admin Finance views expose:

- immutable transaction/attempt timeline;
- provider route/config version;
- Invoice/Subscription/Commercial refs;
- current finality and reason;
- action eligibility calculated by backend;
- reconciliation evidence;
- refund/dispute guided actions.

No generic “set payment status” control exists.

## 18. Acceptance cases

- duplicate checkout/API submission reuses original logical operation and does not create duplicate Payment;
- provider timeout after possible charge creates UNKNOWN and no blind retry;
- verified duplicate Stripe webhook causes no duplicate journal/subscription action;
- failed attempt can retry only as a new attempt under same transaction;
- authorization-only evidence is not treated as captured success when capture is required;
- partial refund cannot exceed remaining refundable amount under concurrent requests;
- refund success creates compensating evidence without editing original payment;
- dispute loss produces separate financial adjustment while original Payment stays historically succeeded;
- HTTP resilience middleware cannot silently repeat an unsafe external side effect.
