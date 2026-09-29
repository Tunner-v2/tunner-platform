> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Closure revision date:** 2026-09-29  

# 22 — Global Market, Tax, Product Compliance & Remittance FRD

## 1. Authority and traceability

Fulfills FigJam **81–83, 141** plus PDR-0005, PDR-0006 and PDR-0008. Tunner's current legal/home jurisdiction is Toronto, Ontario, Canada; commercial reach may include global eligible markets under policy. Currency support is explicit/configuration-driven and provider/corridor gated.

This document governs market eligibility, Product compliance/KYC, tax and remittance behavior required to deliver Tunner services.

## 2. Market eligibility

`MarketEligibilityPolicy` is versioned by Product/environment/country-or-territory and resolves:

```text
ENABLED
REVIEW_REQUIRED
BLOCKED
```

Inputs may include provider/network eligibility, sanctions/restricted-business rules, tax readiness, privacy/marketing/consumer-policy readiness and Product capability configuration. Provider technical acceptance never overrides a `BLOCKED` market.

## 3. Tunner-managed merchant model

The payment-provider merchant/account relationship is Tunner ↔ Provider. Tunner owns provider merchant/accounts and may route a Product's transactions through one or many eligible Tunner provider accounts without changing the Product's economic ownership or payable balance.

The Connected Product:

- integrates only with Tunner SDK/API contracts;
- is the Product-level commercial/economic attribution boundary inside Tunner;
- does not select or own a provider merchant/account;
- does not store Tunner provider credentials;
- does not depend on Stripe/provider-native IDs or statuses;
- receives Tunner-native Product/payment/settlement/payout IDs and normalized results.

Provider-specific account/sub-account/merchant topology remains internal through `ProviderAccountBinding`, `ProviderRouteResolution` and typed `ProviderObjectReference` evidence. Provider account selection is execution/reconciliation context, not Product financial ownership.

## 4. KYC scope

Tunner owns the canonical `ProductKycCase` for each Connected Product when KYC/KYB/compliance evidence is required for Tunner services. The ProductKycCase is a Tunner compliance/risk record and is separate from provider verification of Tunner's own merchant/account relationship.

Canonical states:

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

Only `VERIFIED` is new-payment and payout eligible by default. A non-verified state blocks the affected new operation; already-earned ProductPayable remains authoritative. Settlement may calculate preserved obligations while approval and/or Payout is held according to policy.

Product KYC/KYB is purpose-limited. Every field/evidence item must be required by active Tunner service/compliance policy and have explicit purpose, classification, access and retention rules.

Raw restricted evidence requires private object storage, checksum/evidence lineage, privileged access controls, step-up where required and immutable access audit.

## 5. Tax responsibility profile

Each Product/market resolves a versioned `TaxResponsibilityProfile`, including as applicable:

- responsible seller/merchant tax context needed for the transaction;
- registration IDs/status/effective dates;
- Product tax-classification mapping;
- Billing Account/customer location evidence rules;
- exemption/tax-ID validation rules;
- inclusive/exclusive offered-price treatment;
- tax-engine binding;
- filing/remittance owner/channel;
- correction/credit rules;
- market activation gate.

The profile records only information required for Tunner tax execution and compliance.

## 6. Managed tax engine and extensible tax-provider foundation

**PD-CLOSE-03 is RESOLVED by PDR-0009.** Tunner does not implement or maintain proprietary tax-rate, jurisdiction, threshold or taxability calculation logic. Tax calculation is delegated to approved managed tax services through the stable provider-neutral `ITaxEngine` contract.

The initial production implementation is `StripeTaxAdapter` using **Stripe Tax** where the active market, Product tax classification, currency and transaction type are supported. Tunner may prefer the selected payment provider's managed tax service when that provider exposes an approved compatible tax capability, but a payment-provider route and a tax-engine binding are separate capabilities. Changing or failing over the payment provider does not by itself change or recalculate an already-authoritative `TaxDetermination`.

`TaxEngineBinding` is versioned/effective configuration that selects an approved managed tax-service adapter by environment/market/legal-tax context and records capability/readiness, supported scope, priority and activation state. A future managed tax provider is added by implementing another `ITaxEngine` adapter plus its capability mapping, provider configuration, evidence normalization and contract/acceptance tests. Adding such an adapter must not require a new Product-facing API/SDK contract or a redesign of Checkout, Invoice, Payment, Finance or the canonical `TaxDetermination` model.

Tunner remains authoritative for:

- `TaxResponsibilityProfile` and registration/readiness policy;
- `TaxEngineBinding`;
- normalized immutable `TaxDetermination`;
- Product tax-classification mapping and customer/location evidence references;
- tax accounting, corrections, reconciliation and `TaxRemittanceObligation`;
- market activation/blocking decisions.

The managed tax service remains responsible for its maintained tax calculation rules, rates, jurisdiction resolution and provider-native calculation evidence. Provider-native tax objects never become Tunner domain authority.

Tax calculation flow:

1. resolve Product/tax responsibility profile;
2. resolve jurisdiction/location evidence;
3. resolve Product tax classification and Billing Tax Profile/exemption;
4. build immutable normalized taxable-basis lines;
5. resolve an ACTIVE compatible `TaxEngineBinding`;
6. execute the managed tax-service calculation idempotently;
7. normalize/validate the provider result;
8. create immutable `TaxDetermination` with exact engine/binding/provider evidence and versions.

If no approved compatible tax engine is ACTIVE for a required determination, the affected checkout/financial operation is blocked. Tunner must not fall back to local rate tables, guessed rates, stale cached rules or silent zero tax. An unknown external tax-engine outcome blocks blind alternate-engine calculation whenever duplicate or inconsistent determination risk exists.

## 7. Market/provider regulatory readiness

Tunner does not assume that technical provider acceptance means a market is legally or operationally ready.

Before enabling a Product/market/provider route, `FinancialRegulatoryClassificationPolicy` and the applicable market policy record whether the configured Tunner activity/provider arrangement is approved for that market. A route that requires an unapproved registration/license or violates provider/compliance constraints remains `BLOCKED`/`REVIEW_REQUIRED` until remediated or explicitly approved.

This is a market/provider activation gate.

## 8. Tax remittance

`TaxRemittanceObligation` is created from reconciled tax-payable evidence by applicable registration/jurisdiction/period/currency.

States:

```text
OPEN
READY
BLOCKED
SUBMITTED
WAITING_EXTERNAL
UNKNOWN_EXTERNAL_STATE
RECONCILIATION_REQUIRED
RECONCILED
CLOSED
```

Due dates, filing/payment ordering, destination/account mapping and documentary requirements come from approved jurisdiction policy; there is no hardcoded universal tax deadline.

Initial implementation may use a guided operator/provider/government-portal payment/filing path. Tunner creates the obligation/case, exact amount/evidence package, reminders/escalations, requires execution evidence and reconciles ledger/external proof before closure. A future approved provider may automate submission behind the same contract.

No operator may type an arbitrary replacement tax liability amount merely to unblock remittance. Corrections use the tax-correction workflow and compensating financial evidence.

## 9. Currency/global cards

Product presentment currency is explicit Product commercial configuration and must be supported by the active `CurrencySupportPolicy` plus the selected payment route. The customer's card/account currency may differ; issuer/cardholder FX is external unless Tunner/provider performs a documented conversion.

Tunner currently has a CAD-denominated Canadian treasury/bank account for provider settlement. Provider settlement currency is resolved by `ProviderSettlementProfile` and may differ from presentment currency. Any provider FX used to settle Tunner is recorded as immutable `FxConversion` evidence.

TaxDetermination preserves the customer taxable basis/tax in the applicable transaction currency. Tax-remittance or statutory/accounting currency conversions are separate immutable evidence and do not rewrite the original customer charge.

## 10. Acceptance cases

- provider accepts card technically but market policy is `BLOCKED` => checkout denied before charge;
- missing/ambiguous tax jurisdiction => Tax Exception, no fabricated rate;
- old Tax Determination survives policy/rate/provider change;
- tax correction creates linked compensating evidence;
- tax remittance provider acceptance is not reconciled statutory finality;
- ProductKycCase records contain only Tunner-service/compliance-required evidence and remain separate from Tunner provider merchant/account verification;
- Connected Product receives no provider-native routing dependency;
- Product compliance/tax records contain only data required by active Tunner service and compliance contracts.
