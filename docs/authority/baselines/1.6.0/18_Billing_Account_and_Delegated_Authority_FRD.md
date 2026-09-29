> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 18 — Billing Account & Delegated Billing Authority FRD

## 1. Authority and traceability

This FRD fulfills FigJam sections **46–56**. `BillingAccount` is the Tunner-owned payer/commercial context used for billing profile, delegated billing authority, payment-method references, Product billing relationships and lifecycle governance.

## 2. Canonical resources

- `BillingAccount` (`billing_account_id`)
- `BillingAccountMembership`
- `BillingAuthority`
- `BillingProfile`
- `PaymentMethodReference`
- `ProductBillingRelationship`
- `BillingAuthorityException`
- `BillingMigrationOperation`

All relationships are versioned/effective-dated where historical authority matters.

## 3. Billing Account types and state

Types:

- `PERSONAL` — payer context principally associated with an individual Tunner Account;
- `BUSINESS` — payer context with delegated members/authority.

States:

```text
ACTIVE
RESTRICTED
CLOSING
CLOSED
```

A `PERSONAL` Billing Account may be automatically created on the first eligible billing action when the active Product/Billing policy permits automatic provisioning. Automatic provisioning is idempotent and cannot create multiple default personal Billing Accounts for the same Account and policy scope.

If zero eligible Billing Accounts exist and automatic provisioning is not permitted, the operation returns `PAYER_CONTEXT_REQUIRED`. If more than one is eligible, Tunner requires explicit selection; it never guesses.

## 4. Membership roles

Canonical presentation roles:

- `BILLING_OWNER`
- `BILLING_ADMIN`
- `BILLING_VIEWER`

Roles are not the final authorization decision. Backend capabilities are resolved from membership + Billing Authority + Product policy + restrictions.

A normal user-managed Business Billing Account must retain at least one effective owner unless a governed transfer/closure operation is completing simultaneously.

Membership invitation is a first-class resource with `PENDING | ACCEPTED | EXPIRED | REVOKED`. Acceptance requires the intended Tunner Account and current invitation proof; invitation acceptance does not itself grant payment/subscription authority beyond the resulting membership role/capabilities.

## 5. Billing authority modes

Per Product Billing Relationship:

```text
TUNNER_DELEGATED
PRODUCT_MANAGED
HYBRID
```

Resolution precedence:

1. Account/Billing Account restrictions and explicit deny policies;
2. exact scoped privileged exception, if valid;
3. relationship authority mode;
4. current membership visibility requirements;
5. action-specific Billing Authority capability;
6. Product-managed policy/intent where applicable;
7. expected-version/effective-time checks.

A role label alone never grants a sensitive capability.

Representative capabilities include:

- `billing.profile.read|change`;
- `billing.payment_method.read|add|replace|set_default|disable`;
- `billing.subscription.create|change|pause|resume|cancel`;
- `billing.invoice.read`;
- `billing.membership.manage`;
- `billing.authority.manage`.

## 6. Authority change and transfer

Authority mutations create a new immutable version with effective time. `SCHEDULED` authority is not effective until activation-time revalidation passes.

In-flight operations bind the authority version validated immediately before the authoritative owner-domain mutation/external submission. A later authority revocation does not rewrite a committed operation, but queued work that has not crossed its execution preflight must revalidate current authority.

A transfer cannot strand the Billing Account without required owner/authority coverage.

## 7. Product-managed exception

A `BillingAuthorityException` is narrow, one action/class, one Product relationship, one principal and time-bounded. It requires active authorization, reason, step-up and independent approval when policy requires.

It never changes the relationship authority mode and cannot be reused after consumption/expiry.

## 8. Billing Profile

Billing Profile owns invoice-facing payer data only:

- display/legal billing name;
- billing address;
- verified contact references;
- tax-profile reference;
- invoice delivery context;
- optional business registration identifiers as permitted by policy.

Identity email/phone verification remains Accounts & Identity authority. Tax determination remains Tax authority. Provider customer objects are execution artifacts.

Changes are versioned/future-effective when required; finalized invoices retain the exact Billing Profile version used.

## 9. Payment Method Reference

Tunner stores PCI-safe provider references only.

States:

```text
PENDING_VERIFICATION
ACTIVE
DISABLED
FAILED
```

Rules:

- raw PAN/CVV never enters Tunner application logs/database/Admin UI;
- setup/verification may be synchronous or HYBRID and can remain nonterminal;
- default payment method is a Billing Account relationship, not provider authority;
- fallback precedence is explicit: subscription-specific method, Billing Account default, then other eligible backup only when the active payment policy permits it;
- disabling a reference is blocked if a policy requires a replacement for an imminent/in-flight collection and no safe alternative exists;
- an in-flight Payment Attempt keeps the exact payment-method/provider reference bound at submission time;
- provider-token migration is never assumed portable; a new provider normally requires a new setup/verification flow.

## 10. Product Billing Relationship

States:

```text
PENDING
ACTIVE
SUSPENDED
ENDED
```

It binds Product, Billing Account, authority mode/source, scope and effective versions. `ACTIVE` only means future billing actions may evaluate it; it is not subscription activation or Product entitlement.

Suspension/ending is prospective. Historical financial records retain the relationship version used.

## 11. Closure blockers

Billing Account closure is blocked while any of these require the Billing Account to remain operational:

- active Billing Subscription requiring cancellation/migration decision;
- open receivable/invoice not covered by closure policy;
- Payment/Refund/Dispute with unresolved external finality;
- active Product Billing Relationship that has no approved end/migration;
- pending payer migration;
- legal/privacy/financial hold that prohibits closure state change.

Closure never deletes invoices, payments, ledger or audit history.

## 12. Merge/split/payer migration

Self-service arbitrary merge/split is **not supported**.

The supported governed operation is **prospective payer migration**:

1. create/select target Billing Account;
2. create explicit mapping plan for eligible Product Billing Relationships and future Billing Subscription responsibility;
3. validate payment-method portability (normally requires new method setup unless provider explicitly supports it);
4. choose an effective boundary that does not rewrite finalized invoices/payments;
5. revalidate authority and open financial operations;
6. activate target relationships prospectively;
7. retain source IDs and immutable migration lineage forever.

Historical invoice/payment/ledger `billing_account_id` values are never rewritten.

A requested topology that cannot satisfy these rules returns `UNSUPPORTED_MIGRATION_TOPOLOGY` rather than inventing semantics.

## 13. APIs/events/UI

APIs cover provisioning/selection, membership/invitations, authority query/change, profile versioning, payment-method setup/default/disable, Product Billing Relationship lifecycle, closure and payer migration.

Events include versioned Membership/Authority/Profile/PaymentMethod/ProductRelationship/BillingAccount state changes after owner-domain commit.

Account Center/Admin UI must always render the backend-resolved allowed action set. Product-managed actions show “Managed by Product” plus the configured resolution path rather than an enabled button that will always fail.

## 14. Acceptance cases

- one idempotent personal Billing Account is created when policy permits;
- ambiguous payer candidates require explicit selection;
- membership alone cannot change payment method/subscription without capability;
- scheduled authority does not authorize early execution;
- revoked authority blocks queued-but-not-submitted work after revalidation;
- provider payment reference cannot be treated as portable across providers;
- Billing Account closure preserves financial history;
- payer migration never rewrites historical invoice/payment/ledger ownership;
- Product membership/entitlement remains unaffected by Billing Account membership changes.
