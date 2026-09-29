> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Closure revision date:** 2026-09-29  

# 21 — Billing Subscription, Checkout, Renewal & Dunning FRD

## 1. Authority and traceability

Fulfills FigJam **57–72** and all explicit contract markers in 60–72. Tunner Billing Subscription is commercial/billing lifecycle only. Connected Product owns entitlement/access/onboarding consequences.

## 2. Canonical resources

- `BillingSubscription` and immutable versions;
- `SubscriptionOperation`;
- `SubscriptionItem` version set;
- `ScheduledSubscriptionChange`;
- `BillingPhase` (trial/intro/standard);
- `RenewalRun`;
- `DunningCase`;
- `ProrationPreview`;
- immutable `CommercialSnapshot` reference.

## 3. Canonical subscription states

```text
PENDING_ACTIVATION
TRIALING
ACTIVE
PAST_DUE
PAUSED
ENDING
CANCELED
```

Rules:

- `PENDING_ACTIVATION` exists while activation preconditions/first payment are incomplete;
- `TRIALING` only when an active intro/trial policy defines a no-standard-recurring-charge phase;
- `ACTIVE` means Tunner billing lifecycle is active, not Product entitlement;
- `PAST_DUE` means a financial obligation is overdue/failed under Dunning Policy;
- `PAUSED` suppresses future billing only according to lifecycle policy;
- `ENDING` is a future-effective cancellation already committed;
- `CANCELED` is terminal for future renewals, but historical invoices/payments remain.

Operation/process states are separate and include `CREATED`, `REQUIRES_ACTION`, `WAITING_EXTERNAL`, `UNKNOWN_EXTERNAL_STATE`, `SUCCEEDED`, `FAILED`, `BLOCKED`, `PARTIAL` as appropriate.

## 4. Creation and idempotency

A Product-origin request includes stable Product/client/environment, authenticated Account, Product Relationship, Billing Account, `product_subscription_ref`, commercial action, item refs, currency/cadence and idempotency/correlation.

Duplicate logical request resumes/returns the same Subscription Operation. Arbitrary return URLs are rejected.

## 5. Free activation ordering

For amount due now = 0:

1. revalidate Product/Billing authority and commercial snapshot;
2. create/commit the Subscription version;
3. create required future renewal schedule only if recurrence exists;
4. create zero invoice only when active Invoice Policy requires it (default is no invoice for a permanent zero-due activation);
5. emit Product-facing lifecycle result post-commit.

No Stripe payment object is created solely because a Billing Subscription exists.

## Tunner checkout orchestration

Tunner owns the `CheckoutOperation`, customer-entry URL, Product/commercial summary, explicit price/currency, tax review, Billing Account/customer context, consent/disclosure, processing state and final result. Connected Products start Tunner checkout and never integrate directly with a payment provider.

The selected provider adapter declares one approved presentation mode:

- `EMBEDDED_PROVIDER_COMPONENT` — provider-secure/tokenized fields/components rendered inside the Tunner checkout surface; preferred when practical; or
- `PROVIDER_HOSTED_REDIRECT` — permitted when it materially reduces implementation/PCI complexity or is required by the provider/payment method.

In either mode, the Tunner CheckoutOperation remains the business authority. Provider-native checkout/session IDs are execution evidence only, and the provider flow must return/converge on Tunner's operation/result.

Provider routing is resolved before provider-specific payment collection/tokenization begins. A route is eligible only when the provider/account is active, verified/readiness-approved, charge-capable for the Product/market/currency/payment method and not held/restricted/disabled.

If an eligible primary route becomes unavailable **before** any external payment side effect, Tunner may select another already-active eligible provider/account. This continuity behavior does not bypass provider onboarding, verification or account holds. If no eligible route exists, checkout is temporarily blocked with a Tunner-owned reason/result rather than pretending payment can proceed.

Provider-specific payment tokens are not assumed portable. A route change before submission may require secure payment-detail recollection through the newly selected provider flow. After possible external submission or `UNKNOWN_EXTERNAL_STATE`, automatic provider failover is prohibited until finality is reconciled.

Tunner does not proactively force 3-D Secure as an initial business rule. The payment flow nevertheless supports provider/issuer-required authentication through normalized `REQUIRES_ACTION`. Authentication can be embedded, modal or provider-hosted as required and must return/converge on the same Tunner CheckoutOperation.

## 6. Paid first-activation ordering

Normative order:

1. create stable Subscription Operation;
2. resolve/verify payment method if needed;
3. resolve immutable commercial/tax snapshot;
4. create `PENDING_ACTIVATION` Billing Subscription version;
5. create/finalize the first Invoice/receivable according to Invoice Policy;
6. create Tunner Payment transaction/attempt before provider submission;
7. execute the selected provider route under stable Tunner/provider idempotency;
8. wait for authoritative provider finality when challenge/processing applies;
9. on successful capture/settlement state required by Payment Policy, allocate Payment to Invoice;
10. transition Subscription to `ACTIVE` or `TRIALING` only when its activation contract prerequisites are satisfied;
11. publish post-commit events/results.

For initial card payments, authorization alone does not activate a charge-dependent Subscription when capture is still outstanding. Manual-capture activation requires an explicit Product/Payment policy; no implicit authorization-based activation exists.

## 7. Plan/item change and proration

Commercial action enum must explicitly state one of:

- `CREATE_SUBSCRIPTION`;
- `CHANGE_SUBSCRIPTION`;
- `ADD_ITEM`;
- `REMOVE_ITEM`;
- `CHANGE_QUANTITY`;
- `CHANGE_CADENCE`;
- `CANCEL_SCHEDULED_CHANGE`.

A change with monetary mid-term effect must resolve an active `ProrationPolicy`. If none is assigned, the change is blocked; Tunner does not choose a default silently.

Policy modes include:

- `NONE`;
- `CREDIT_NEXT_INVOICE`;
- `INVOICE_IMMEDIATELY`.

Preview is non-posting and must be recomputed/revalidated at commit. Scheduled changes are separate resources and overlapping changes require explicit replace/cancel/precedence action.

## 8. Renewal

`RenewalRun` has stable identity and revalidates Subscription, Billing Account, authority, Product relationship, payment method, scheduled changes and policy.

Version selection occurs at the renewal boundary after due scheduled commercial changes are resolved. The resulting Renewal Commercial Snapshot is immutable.

Normative renewal order for automatic card collection:

1. readiness + version selection;
2. Tax Determination;
3. Invoice finalization;
4. Payment transaction/attempt;
5. authoritative payment finality/reconciliation;
6. allocation;
7. advance Subscription renewed-through/next-billing boundary only when contract prerequisites pass;
8. Product-facing event.

Partial failure remains in the same RenewalRun and never creates a duplicate invoice/payment by starting a new renewal identity.

## 9. Dunning

Every paid recurring Product contract must reference a versioned `DunningPolicy`; absence blocks activation of automatic recurring collection.

Policy defines retry schedule/count/max-age, customer notification schedule, payment-method update behavior and terminal billing transition. These values are configuration, never developer constants.

Each actual retry creates a **new Payment Attempt** under the same obligation. A provider outcome in `UNKNOWN_EXTERNAL_STATE` blocks retries/failover until reconciled.

Updating a payment method never itself charges unless the Dunning Policy explicitly schedules an immediate recovery attempt and current authority is revalidated.

Product grace/access behavior remains Product-owned.

## 10. Pause/resume/cancel

Allowed effective modes are policy-controlled:

- pause now or future-effective;
- resume now/future-effective;
- cancel `IMMEDIATE` or `END_OF_TERM` only when Product lifecycle policy permits.

Account Center must display only permitted options and must use Product-contract wording for access consequences. It may say “billing ends on X” but must not promise Product access-through-date unless the Product contract explicitly supplies that guarantee.

Immediate cancellation never erases already-issued Invoice/Payment evidence and may require a separate Refund/Credit operation.

## 11. Trials/intro conversion

Trial/intro phase is optional and absent by default. Phase transition is a distinct operation, not ordinary renewal unless policy explicitly maps it.

At phase end, missing payment method behavior is explicit policy: `REQUIRE_ACTION`, `PAUSE`, `CANCEL`, or invoice terms where supported. No hidden provider default becomes Tunner business policy.

## 12. Cadence and anchors

Fields are distinct: `phase_end_at`, `next_billing_at`, `renewal_at`, `cancel_effective_at`.

Calendar math uses policy timezone and end-of-month/leap-year rules. Default billing calendar uses UTC for technical timestamps and the commercial policy’s named timezone for calendar anchor calculation. If an anchor day does not exist in a month, use the last valid day of that month while preserving the configured anchor intent for later months.

Changing cadence is a versioned commercial/subscription change.

## 13. Product handoff

Authoritative Product handoff is server-side response/query or signed versioned event/webhook. Browser redirect is UX only.

Delivery acknowledgement is not Product application success. Product Application Result can report local failure without rolling back Tunner billing success.

## 14. Acceptance cases

- duplicate checkout resumes same operation and does not duplicate payment;
- paid subscription never becomes ACTIVE before required first-payment finality;
- unknown Payment blocks duplicate first-charge retry;
- missing proration policy blocks monetary mid-term change;
- scheduled change conflict is explicit;
- renewal partial failure resumes same RenewalRun;
- dunning retry is a new attempt, not a new invoice/renewal identity;
- cancellation wording never promises Product entitlement behavior without contract;
- Product event redelivery cannot recreate billing work.
