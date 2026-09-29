# PDR-0006 — Tunner-Managed Provider Accounts, Product KYC and Payment Orchestration

**Status:** APPROVED — CONSTITUTIONAL BUSINESS DECISION  
**Effective:** 2026-09-23

## Decision

Connected Products integrate only with Tunner payment, billing and compliance contracts. Tunner centrally owns and administers the payment-provider/gateway relationships used by the platform, including provider instances, credentials, provider merchant/account relationships, routing policy, payment execution, provider event intake and external payout execution.

The Connected Product is the commercial/economic attribution boundary inside Tunner. Product revenue attribution, `ProductPayable`, payable balance, Settlement and Payout are Product-scoped Tunner financial records and do not depend on which Tunner provider merchant/account processed the underlying payment.

A Connected Product must not select a payment gateway, hold provider credentials, depend on provider-native identifiers/statuses, or become part of Tunner's provider merchant-account ownership model. Tunner may use one or many provider merchant/accounts for the same Product and may reuse the same provider infrastructure across many Products without changing Product economics or the Product-facing SDK/API contract.

## Connected Product KYC / compliance boundary

Tunner maintains a canonical `ProductKycCase` for the Connected Product when KYC/KYB/compliance evidence is required to provide Tunner services. This record exists for Tunner compliance, risk, payment-service and payout-eligibility decisions. It is separate from the payment provider's verification/KYC of Tunner's own merchant/account relationship.

Canonical `ProductKycCase` states are:

```text
DRAFT
SUBMITTED
UNDER_REVIEW
VERIFIED
RESTRICTED
REJECTED
EXPIRED
REVERIFICATION_REQUIRED
CLOSED
```

Only `VERIFIED` is new-payment and payout eligible by default. Other states block the affected new operation unless an approved policy defines a narrower exception. Existing earned `ProductPayable` is never deleted or rewritten because Product compliance status changes. Settlement may continue to calculate preserved obligations; approval and/or payout may remain blocked until eligibility is restored according to policy.

Product KYC/KYB evidence is purpose-limited to Tunner's documented service/compliance needs. Every collected field/evidence item requires explicit purpose, classification, access and retention metadata. Raw restricted evidence uses private object storage, checksum/evidence lineage, privileged access controls and immutable access audit.

## Tunner provider merchant/account boundary

The payment-provider merchant/account relationship is strictly **Tunner ↔ Provider**. Tunner owns the configured external provider merchant/accounts and customer payments are received through Tunner's provider account path.

`ProviderAccountBinding` represents a versioned Tunner relationship to an external provider merchant/account/configuration. Multiple ProviderAccountBindings may be eligible for the same Product operation, and the same ProviderAccountBinding may serve transactions for many Products. This relationship is execution infrastructure, not Product financial ownership.

`ProviderRouteResolution` records the exact provider route selected for an operation. Historical transactions retain their exact route/binding/evidence versions for reconciliation, but those provider references never partition or own the Product's payable balance.

`ProviderObjectReference` stores typed provider-native evidence such as provider instance/account context, provider object type, provider object ID and provider event ID. Provider evidence supports reconciliation/finality but does not replace Tunner business authority.

## Payment routing

Tunner resolves provider routing from the Product/environment operation context, `ProductKycCase` eligibility where applicable, market/currency/payment method, provider capabilities/readiness, available `ProviderAccountBinding` records, routing policy and operational health.

The Product receives Tunner-native Product/payment/settlement/payout IDs and normalized results. Provider names, credentials, account IDs, routing decisions and provider-native object IDs remain internal unless an explicit support/compliance disclosure policy requires them.

### Checkout presentation and provider continuity

Tunner owns the provider-neutral `CheckoutOperation`, customer-entry URL, commercial/tax review, operation state and final return/result. The selected provider may present payment collection through either:

- `EMBEDDED_PROVIDER_COMPONENT` — preferred when the provider offers a PCI-safe component that can be mounted inside Tunner's checkout surface; or
- `PROVIDER_HOSTED_REDIRECT` — permitted when it materially reduces implementation/PCI complexity or the selected payment method/provider requires a hosted flow.

The presentation mode is an adapter capability, not Product-facing business logic. A Connected Product starts one Tunner checkout and receives one Tunner result regardless of provider or presentation mode.

Provider routing is resolved **before** provider-specific payment collection/tokenization begins. A provider/account in onboarding, verification review, hold, disabled, restricted or otherwise non-charge-capable state is not an eligible new-payment route.

Business continuity requires at least one separately approved, active and charge-capable alternate provider/account. Tunner may route a new checkout to that alternate before an external side effect occurs. No software design can bypass a provider's verification/hold or guarantee collection when no eligible route exists.

If an external payment may already have been submitted, provider failover is prohibited until authoritative evidence establishes that another attempt is safe.

Tunner does not proactively force 3-D Secure as an initial business feature. Payment nevertheless supports normalized `REQUIRES_ACTION` continuation because a provider, network or issuer may require customer authentication. Required authentication is handled through the selected provider flow and converges back to the same Tunner CheckoutOperation.

## Financial attribution and Product payable balance

Every Tunner-managed transaction is attributed to exactly one Connected Product and one effective `ProductCommercialAgreement`/`FeePolicy`.

`ProductPayable` is the Tunner obligation attributable to that Product from Tunner-managed activity after the approved transaction, tax, refund/credit/dispute, provider-cost, reserve and Tunner-fee rules are applied.

Product payable ownership is determined by **Product + payable currency**, not by ProviderAccountBinding, provider merchant/account, provider route or provider-native object. In the initial model, payable currency is the Product commercial transaction/presentment currency from the immutable CommercialSnapshot. Provider settlement currency and immutable `FxConversion` evidence remain separate Tunner treasury/clearing facts and never re-denominate the Product obligation.

Transactions processed through multiple Tunner provider accounts contribute to the same Product payable position when they belong to the same Product and payable currency. Different settled currencies remain separate payable positions.

`ProductPayableBalance` is a derived authoritative financial-control view of the Product's payable liability by Product + payable currency. It is calculated from immutable ProductPayable/ledger evidence and may expose accrued, settled, payout-pending, paid, held/restricted and adjustment components. It is never an independently editable amount.

Example: Product A may be charged `8% + USD 0.25` per eligible transaction while Product B may be charged `12%`. Provider routing may differ transaction by transaction, but each Product's payable calculation and balance remain independent and auditable.

## Settlement and payout lifecycle

The canonical lifecycle is:

`ProductPayable → ProductPayableBalance → Settlement → Payout → Provider Route/Adapter → Provider/Bank Finality → PayoutReceipt → Reconciliation`

`Settlement` is Product + payable currency + settlement-period/cycle scoped. Settlement approval confirms calculation/readiness and does not mean money moved. An approved Settlement is immutable; corrections create governed superseding/correcting Settlement operations plus compensating financial entries.

`ProductPayoutPreference` is the versioned Product choice of preferred payout currency and applicable destination/policy. `ProductPayoutDestinationVersion` is the immutable Product-owned payout destination snapshot selected for a Payout. Provider account selection for executing the Payout remains a separate Tunner routing decision.

If the approved Settlement currency differs from the selected Product payout currency, the Payout must reference the actual `FxConversion` evidence used to determine the payout amount. An unsupported country/currency/destination/provider corridor blocks the Payout rather than silently changing the Product's preference. The active `FxEconomicTreatmentPolicy` determines whether conversion fees/spread are borne by Tunner or the Product.

Each Payout represents one externally meaningful payout attempt. Once provider submission may have created an external side effect, any retry/replacement capable of another money movement receives a **new `payout_id` and new idempotency context** and preserves predecessor lineage such as `replaces_payout_id`.

`PayoutReceipt` is immutable final provider/bank evidence for one Payout attempt/finality outcome. A Settlement may therefore have multiple Payout/PayoutReceipt lineages when a failed or returned payout is replaced.

`PayoutProcessingCase` is **exception/reconciliation-only**. Normal successful finality does not create a processing case. A case is opened/linked for failure, return, unknown external outcome, destination/provider mismatch, reconciliation mismatch or another policy-defined exception requiring intervention or evidence processing.

Successful path:

`Payout → provider/bank finality → PayoutReceipt → automatic reconciliation → CLOSED`

Exception path:

`Payout → FAILED / RETURNED / UNKNOWN / MISMATCH → PayoutProcessingCase → evidence/reconciliation/correction or replacement payout`

The Finance scope governed by this decision is limited to Tunner-managed payment, Product payable, payable balance, settlement, payout, reconciliation and financial-control evidence required to deliver Tunner services.
