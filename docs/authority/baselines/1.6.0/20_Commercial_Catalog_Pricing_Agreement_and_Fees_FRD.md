> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Closure revision date:** 2026-09-29  

# 20 — Commercial Catalog, Pricing, Agreement & Fee FRD

## 1. Authority and traceability

Fulfills FigJam **73–80, 84–86** and commercial inputs to 57–72. Connected Product owns package/feature/entitlement business meaning. Tunner owns the billing-safe projection, event-time commercial resolution, immutable snapshots, Tunner fee policy and economic attribution required for Tunner-owned billing/finance duties.

## 2. Commercial source contract

Every Product/environment publishes a signed/versioned source payload containing only billing-safe fields:

- stable plan/item source keys;
- price model and explicit monetary amounts;
- explicit ISO-4217 presentment currency and optional market scope;
- billing cadence/quantity/tier contract;
- intro/trial/discount references;
- tax classification reference;
- effective interval/source version.

Product features, workspace/member roles, Product entitlement rules and raw operational usage do not enter the Tunner catalog.

Accepted source versions are immutable. Invalid/overlapping/conflicting versions create `CatalogSyncException`; the last accepted catalog remains authoritative.

## 3. Initial pricing models

Supported contract models:

- `RECURRING_FLAT`;
- `RECURRING_QUANTITY`;
- `ONE_TIME`;
- `TIERED_QUANTITY`;
- `EXTERNAL_BILLABLE_QUANTITY` where the Product supplies an already-authorized normalized quantity under contract.

Tunner does not collect Product feature/session/consumption telemetry to derive billable usage. No arbitrary runtime formula/code execution is allowed.

## 4. Currency

Product prices are explicit by currency. A price is eligible only when its currency is enabled by the active `CurrencySupportPolicy` and an eligible payment route supports that currency/market/payment method.

Tunner does not silently derive one customer price from another currency. Cardholder/account currency may differ from Product presentment currency; issuer/cardholder conversion is external unless Tunner/provider actually performs a documented conversion.

Provider settlement currency and Product payout currency may differ from presentment currency. In the initial model Product payable currency remains the source Product presentment/commercial transaction currency; provider settlement FX does not re-denominate Product economics. Every actual conversion that affects Tunner financial state is represented by immutable `FxConversion` evidence.

A Product may publish one or more explicit currency prices. Unsupported currency requests block deterministically; no implicit FX-priced substitute is generated.

## 5. Cadence

Recurring cadence is represented as `interval_unit + interval_count`, calendar-safe. Initial offered cadence may use monthly, quarterly (`3 MONTH`), semiannual (`6 MONTH`) and annual (`1 YEAR`) patterns when the Product publishes them.

Month/year are calendar intervals, never fixed-day approximations. Anchor/timezone/end-of-month behavior is defined by the Billing Subscription contract.

## 6. Commercial Agreement

A versioned Agreement binds Product/environment/market/currency scope to:

- allowed catalog versions;
- Tunner fee assignment;
- provider-cost economic treatment;
- refund/dispute treatment;
- merchant/provider-routing policy references;
- tax responsibility/liability profile;
- settlement policy refs;
- CurrencySupportPolicy and payout-currency policy refs;
- FxEconomicTreatmentPolicy reference/override where conversion can affect Product economics; the approved platform Standard Cause-Based policy applies when no narrower agreement override exists;
- effective interval/termination rules.

Agreements are `DRAFT | SCHEDULED | ACTIVE | ENDED | SUPERSEDED` and cannot overlap ambiguously for the same resolution scope.

### FX commercial configuration

FX economic treatment is policy-driven, not hardcoded into fee logic. `ProductCommercialAgreement` may accept the platform Standard Cause-Based policy or reference a narrower Product-specific `FxEconomicTreatmentPolicy`. Policy resolution is effective-dated and deterministic.

Configurable Product-affecting allocation modes are:

- `PRODUCT_BORNE`;
- `TUNNER_BORNE`;
- `SHARED` with explicit Product/Tunner percentage split;
- optional approved cost/subsidy caps where the commercial agreement requires them.

Context-only modes `EXTERNAL_TO_TUNNER` and `REPORTING_ONLY` are used when no Tunner/Product economic allocation exists. Actual allocatable cost components are explicitly selected from externally evidenced fee/spread/bank/rounding components; the agreement cannot invent a provider cost that evidence does not support.

Configuration UX must be guided: choose the scope/context, select a standard template or custom override, configure any split/caps, preview sample financial impact and supported currency/corridor behavior, review the effective date/version, and activate only after required approval. Historical financial events keep the exact policy/agreement version used.

## 7. Tunner fee

Fee models:

- percentage;
- fixed;
- fixed + percentage;
- explicit tier table.

Fee basis is versioned (`gross captured`, `excluding tax`, or other approved basis) and never inferred from provider processing fees. Provider cost and Tunner fee are separate evidence.

Every Product fee is a Tunner service charge under the Product's effective `ProductCommercialAgreement`. Product A may use `8% + USD 0.25` while Product B uses `12%`; historical events retain the exact fee-policy version.

## 8. Trials and introductory phases

Capability is supported only when an active Product commercial policy defines it. Absence of an intro/trial policy means no trial/intro behavior applies.

Supported phase types:

- free trial before first recurring charge;
- one-time activation/intro fee then recurring charge at phase end;
- introductory recurring price for a defined number of cycles/period;
- prepaid first recurring term with a contract-defined bonus/trial extension.

Phase records are versioned and immutable once financially used. Provider-native trial objects are execution artifacts, not Tunner authority.

## 9. Discounts/promotions

Capabilities include percentage, fixed amount and introductory replacement price.

Every promotion has explicit scope, effective dates, optional redemption bounds and priority/exclusive group. **There is no implicit stacking rule.** If more than one eligible adjustment remains ambiguous after policy resolution, price resolution blocks rather than guessing.

Resolved price may never become negative. Redemption reservation/consumption/refund behavior is explicitly stored with the subscription/invoice operation lineage.

## 10. Commercial Snapshot

Every financially relevant activation/change/renewal/invoice/payment creates or references immutable `CommercialSnapshot` containing exact Product source/catalog/price/item/agreement/fee/tax-context/provider-route evidence where applicable, quantities, cadence, presentment currency and calculation evidence.

Historical financial records never reconstruct price from today’s catalog.

## 11. Change governance

Commercial changes use candidate version -> validation -> impact simulation -> approval where required -> active/future-effective publication -> activation-time revalidation.

Rollback after financial use is a new prospective corrective version. No retroactive bulk rewrite.

## 12. Acceptance cases

- conflicting source versions do not replace accepted catalog;
- unsupported currency never silently converts;
- ambiguous discount stacking blocks resolution;
- fee and provider cost remain separate;
- future price change never modifies prior snapshot/invoice/payment;
- no Product entitlement data enters the commercial catalog;
- no raw Product usage is turned into Tunner usage metering.
