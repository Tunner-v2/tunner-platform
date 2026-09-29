> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Closure revision date:** 2026-09-29  

# 26 — Finance Ledger, Settlement & Payout FRD

## 1. Authority and traceability

Fulfills FigJam **19, 23–27** under PDR-0006 and PDR-0008. Finance owns Tunner-managed financial event normalization, immutable ledger evidence, Product fee/payable calculations, settlement, payout lifecycle, reconciliation, period control and governed corrections.

Finance is **not** the general accounting system for a Connected Product. It records only financial facts required to provide, charge, settle, reconcile and audit Tunner services.

## 2. Platform financial boundary

Tunner Finance may record:

- customer charges processed through Tunner;
- Tunner fees under the Product's commercial agreement;
- provider costs attributable to Tunner-managed transactions;
- tax evidence;
- invoices, credits and receivables managed by Tunner;
- refunds and disputes;
- Product payable;
- settlement and payout evidence;
- reserves/holds when explicitly required by the Tunner commercial/payment contract;
- reconciliation and financial-control evidence.

Tunner Finance is the system of record for the financial facts created by Tunner-managed services and their reconciliation/evidence lifecycle.

## 3. Canonical resources

- `FinancialEvent` — normalized owner-domain financial fact.
- `ChartOfAccountsVersion` and `LedgerAccount` — Tunner financial-control ledger accounts only.
- `PostingRuleVersion`.
- `Journal` / `JournalLine`.
- `AccountingPeriod`.
- `PostingException` / `LedgerReconciliationCase`.
- `FinancialAdjustment` / `JournalReversal`.
- `ProductSettlementSnapshot` — immutable calculation evidence for a Product's Tunner-managed activity.
- `ProductPayable`.
- `ProductPayableBalance` — derived Product + payable-currency liability view from authoritative ledger/payable evidence; never independently editable.
- `ReserveHold` where the active agreement requires one.
- `TunnerTreasuryAccount` — Tunner-owned bank/treasury destination metadata and currency/readiness; secret/full banking data remains protected.
- `FxConversion` — immutable evidence of an actual currency conversion affecting Tunner financial state.
- `ProductPayoutPreference` — versioned Product preference for payout currency/destination policy.
- `Settlement`.
- `ProductPayoutDestinationVersion` — immutable Product-owned payout-destination version/snapshot.
- `Payout` — one external payout attempt/lineage.
- `PayoutReceipt` — immutable final provider/bank evidence for one Payout attempt.
- `PayoutProcessingCase` — exception/reconciliation-only case.
- `ProviderObjectReference` — typed provider-native evidence reference used during payout/reconciliation.

## 4. Financial event contract

Only authoritative owner-domain facts become financial events, for example:

- Invoice issued/voided;
- Payment succeeded/refunded/disputed;
- Credit Note issued;
- Tunner fee calculated;
- provider cost evidenced;
- Tax Determination/correction/remittance;
- Product payable/settlement finalized;
- provider payout submitted/confirmed/returned/reconciled.

A provider webhook alone is not posted until the owning domain commits the mapped authoritative financial event.

## 5. Tunner fee and Product payable

Every fee-bearing transaction resolves the exact effective `ProductCommercialAgreement` and `FeePolicy`.

Example:

```text
Product A fee = 8% + USD 0.25 per eligible transaction
Product B fee = 12% per eligible transaction
```

The agreement defines the fee basis, fixed/percentage components, currency, rounding, tax interaction, provider-cost treatment, refund/dispute treatment and effective interval.

`ProductPayable` is the amount Tunner determines is attributable/payable for a Product from Tunner-managed activity after applying only the approved transaction, tax, refund/credit/dispute, provider-cost, reserve and Tunner-fee rules.

Every ProductPayable retains `product_id`, `payable_currency`, exact commercial/version references and source financial-event lineage. In the initial model, `payable_currency` is the source Product transaction/presentment currency from the immutable CommercialSnapshot. Provider settlement currency and provider FX remain separate clearing/treasury evidence and never re-denominate or partition ProductPayable. Provider merchant/account, ProviderAccountBinding and provider route are not ProductPayable ownership keys.

A Product KYC/compliance restriction never deletes or rewrites already-earned ProductPayable. It affects eligibility for new payment, Settlement approval and/or Payout according to Product compliance policy.

`ProductPayable` is Tunner's payable obligation derived from exact Tunner-managed Product transactions, commercial agreement, fee policy and financial evidence.

## 5A. Product payable balance

`ProductPayableBalance` is the authoritative derived financial-control view of what Tunner owes a Product by **Product + payable currency**. It is calculated from immutable ProductPayable and ledger evidence rather than stored as a freely editable amount.

The view may expose, as derived components:

```text
accrued_payable
settled_amount
payout_pending_amount
paid_amount
held_or_restricted_amount
adjustment_amount
current_payable
```

Transactions for the same Product/payable currency may be processed through different Tunner provider merchant/accounts. Those provider routes remain separate clearing/reconciliation dimensions and all contribute to the same Product payable position when the underlying economics belong to that Product.

A provider-account change, failover or routing-policy change cannot transfer, split or rewrite ProductPayable ownership.

## 6. Product settlement snapshot

For each settlement period/cycle, Tunner creates immutable Product-attributed evidence including applicable:

```text
customer_presentment_gross
presentment_currency
provider_settlement_gross
provider_settlement_currency
fx_conversion_ref
discounts
refunds_credits
disputes_reversals
tax
provider_cost
tunner_fee
reserve_hold
product_payable
payable_currency
agreement_version
fee_policy_version
provider_evidence_refs
```

The calculation is traceable to source payments/invoices/refunds/disputes and exact commercial versions. Later policy changes never rewrite historical snapshots.

## 7. Treasury and FX evidence

Tunner currently has one active CAD-denominated Canadian Treasury/Bank account. It is represented by `TunnerTreasuryAccount` and may be referenced by ProviderSettlementProfile. A future USD account is configuration-only until created, verified and activated. Provider settlement into CAD may therefore create a provider FX conversion when the customer/Product transaction is denominated in another currency. That conversion belongs to Tunner treasury/clearing and does not change the ProductPayable currency or amount except where an explicitly approved Product economic-treatment policy permits a specific pass-through cost.

`FxConversion` exists only for an actual conversion or an accounting/statutory translation that policy explicitly requires. It records at minimum:

```text
fx_conversion_id
purpose
source_amount
source_currency
target_amount
target_currency
effective_rate
rate_source / provider
provider_or_bank_reference
explicit_fee_amount/currency when available
spread/economic_cost evidence when available
quoted_at / executed_at / observed_at
related Payment / Settlement / Payout / Tax / Journal refs
policy/version
```

A quote/indicative rate is never posted as an actual conversion. Historical FX evidence is immutable.

Provider settlement FX is created from provider evidence such as balance/settlement transactions. Product payout FX is created from the actual payout conversion/quote execution. Statutory/accounting translation is separate from both.

No cross-currency netting occurs inside one Settlement in the initial model. Each Settlement consumes ProductPayable in one payable currency. If the Product payout preference requests another currency, conversion occurs as part of the Payout path and is evidenced separately.

`FxEconomicTreatmentPolicy`, referenced by `ProductCommercialAgreement` or resolved from the approved platform default, determines who bears actual externally evidenced conversion fee/spread/variance where Product economics are affected. PDR-0010 locks the Standard Cause-Based defaults:

- customer/issuer FX outside Tunner -> `EXTERNAL_TO_TUNNER`;
- Tunner provider-settlement/treasury FX caused by Tunner routing -> `TUNNER_BORNE`;
- Product-requested payout FX -> `PRODUCT_BORNE`;
- statutory/accounting translation -> `REPORTING_ONLY`.

A Product-specific agreement may override an allocatable context with `PRODUCT_BORNE`, `TUNNER_BORNE` or `SHARED`. `SHARED` records an explicit percentage split and may record approved caps/subsidy limits. Allocation applies only to cost components supported by external evidence; no inferred spread or duplicate fee may be posted.

Policy resolution precedence is: explicit effective ProductCommercialAgreement override -> narrower active Product/environment FX policy -> approved platform Standard Cause-Based policy. Missing or ambiguous required resolution blocks the conversion. Historical records retain the exact policy version. Guided configuration must provide template selection, context/currency/corridor validation, cost-component selection, split/cap controls when applicable, financial preview, effective-date/version review and approval before activation.

## 8. Ledger scope and posting rules

The Tunner financial-control ledger uses double-entry journals for Tunner-managed financial facts. Posted journals are immutable and balanced in transaction currency.

Logical roles may include:

- cash/provider clearing;
- accounts receivable where Tunner invoices;
- tax payable where applicable;
- Tunner fee income;
- Product payable liability;
- refunds/credits/contra amounts;
- provider processing cost;
- dispute/chargeback clearing;
- reserves/holds where applicable.

These are platform financial-control roles. They do not create a full general ledger for the Product.

`PostingRuleVersion` resolves by financial event type, Product, currency, applicable commercial/tax/provider context and effective time. Arbitrary runtime accounting script execution is prohibited.

## 9. Journal immutability and corrections

Posted journal is append-only. Corrections use:

- `JournalReversal` referencing original journal;
- `FinancialAdjustment` with reason/approval and corrected source evidence; or
- a corrected owner-domain event mapped by posting rule.

There is no production operation that edits a posted journal line in place.

## 10. Accounting periods

Canonical states:

```text
OPEN
CLOSE_IN_PROGRESS
CLOSED
REOPENED_BY_EXCEPTION
```

Period close checks blocking posting exceptions, critical reconciliations, trial-balance integrity, required tax/provider clearing evidence and configured approvals/SoD.

Late events for a closed period follow approved subsequent-period adjustment policy. Reopen requires a governed exception.

## 11. Settlement lifecycle

`Settlement` represents the Tunner-calculated settlement position for one Product + payable currency + settlement period/cycle. It does not cross-net different currencies in the initial model.

Canonical states:

```text
DRAFT
CALCULATING
READY
BLOCKED
APPROVED
PAYOUT_PENDING
PAYOUT_PROCESSING
RECONCILIATION_REQUIRED
RECONCILED
CLOSED
```

Settlement approval confirms calculation/readiness. It does not mean money has moved.

Once a Settlement reaches `APPROVED`, its calculation/version is immutable. A later correction must create a governed superseding/correcting Settlement operation and compensating ledger evidence; no operator or background process may rewrite the approved historical calculation.

ProductKycCase/payout-eligibility restrictions may leave a valid obligation in `BLOCKED`/`PAYOUT_PENDING`; they do not erase ProductPayable or rewrite the approved Settlement.

## 12. Payout lifecycle

Tunner Finance owns the platform payout lifecycle and evidence. The provider/bank remains authoritative for external payout finality.

Before Payout creation/submission Tunner must resolve:

- approved Settlement version;
- Product identity, Settlement/payable currency and `ProductKycCase` payout eligibility where applicable;
- active `ProductPayoutPreference` and immutable ProductPayoutDestinationVersion;
- requested payout currency/corridor support;
- actual or executable FX conversion evidence when payout currency differs from Settlement currency;
- active FxEconomicTreatmentPolicy when conversion affects Product economics;
- current provider routing/capability/readiness through `ProviderRoutingPolicy`/`ProviderRouteResolution`;
- required approvals/SoD;
- duplicate/idempotency and unresolved prior-payout checks.

Canonical payout states:

```text
READY
REQUESTED
SUBMITTED
PROCESSING
SUCCEEDED
FAILED
RETURNED
UNKNOWN_EXTERNAL_STATE
RECONCILIATION_REQUIRED
RECONCILED
CLOSED
```

The Product payout currency is independent from Product country, customer presentment currency and provider settlement currency, but it must be supported by the selected payout route/destination. If payout currency differs from Settlement currency, `Payout` references the exact `FxConversion` used to calculate the externally submitted payout amount. Unsupported corridors block; no silent fallback currency is chosen.

Each `Payout` represents one externally meaningful payout attempt. If another provider/bank submission could create another money movement, the replacement must use a **new `payout_id` and new idempotency context** and preserve predecessor lineage (`replaces_payout_id` or equivalent). An uncertain prior outcome blocks replacement until authoritative evidence establishes that another payout is safe.

A Payout stores the immutable ProductPayoutDestinationVersion used at creation/submission. Later Product destination changes never mutate an existing Payout.

Provider-native money-movement, balance, payout, disbursement or equivalent objects are recorded only through typed `ProviderObjectReference` evidence. Tunner Finance remains authoritative through `ProductPayable`, `Settlement`, `Payout`, `PayoutReceipt` and reconciliation resources.

On terminal provider/bank finality (`SUCCEEDED`, `FAILED` or `RETURNED`):

1. persist the terminal `Payout` result and ProviderObjectReference evidence;
2. create exactly one immutable `PayoutReceipt` for that Payout attempt/finality outcome;
3. run the applicable reconciliation automatically;
4. on clean `SUCCEEDED` reconciliation, close without creating `PayoutProcessingCase`;
5. on failure, return, unknown outcome, mismatch or another exception, create/link `PayoutProcessingCase` and route governed correction/replacement work.

Provider acceptance/submission is not bank finality. `UNKNOWN_EXTERNAL_STATE` blocks blind duplicate payout or reroute.

## 13. Reconciliation

Reconciliation is separated into three authoritative layers; a generic single reconciliation flag must not collapse them:

1. **Transaction/Ledger reconciliation** — owner-domain FinancialEvents, journals/subledgers and provider transaction/clearing evidence.
2. **ProductPayable/Settlement reconciliation** — commercial calculation, fees/costs/reserves, payable liability and approved Settlement evidence.
3. **Payout/Provider-Bank finality reconciliation** — Product-scoped Payout/PayoutReceipt, ProductPayoutDestinationVersion, ProviderRouteResolution and ProviderObjectReference/bank evidence.

Cases include:

- provider balance/transaction mismatch;
- ProductPayable/Settlement mismatch;
- payout amount/currency/destination mismatch;
- failed/returned payout;
- unknown payout finality;
- cash/provider-clearing mismatch;
- tax remittance mismatch;
- invoice/receivable allocation mismatch;
- journal/subledger mismatch.

Unresolved differences remain open with owner/reason/evidence. Operators cannot type a balancing adjustment without a governed correction operation.

A normal successful Payout that reconciles automatically does **not** create `PayoutProcessingCase`. The case exists only when operational/reconciliation work is required.

## 14. Approval and segregation of duties

Sensitive actions resolve `FinancialActionPolicy`. Examples that may require step-up and independent approval:

- large/manual refund;
- write-off;
- reserve release/change;
- settlement approval;
- payout override/retry after confirmed safe failure;
- journal adjustment/reversal;
- period close/reopen;
- tax remittance.

Approval authorizes execution but does not imply success. Expected resource versions are revalidated before mutation/external side effect.

## 15. Reporting and transparency

Tunner operator views must expose authorized drill-down from:

```text
Product -> agreement/fee/FX policy -> customer presentment -> provider settlement/FX -> fee/tax/provider cost -> ProductPayable -> ProductPayableBalance -> Settlement -> Payout -> PayoutReceipt -> reconciliation -> ledger/audit evidence
```

Connected Product views expose only that Product's Tunner-managed commercial/transaction/fee/payable/settlement/payout information.

End-user views expose customer-relevant purchase/subscription/charge/tax/invoice/refund information and do not expose Tunner internal provider routing or Product/Tunner economics unless policy/law requires it.

## 16. Events

Events include:

- `ProductPayableCalculated`;
- `ProductPayableBalanceChanged` (derived-view notification/read-model event; ledger/payable records remain authority);
- `SettlementCalculated/Approved/Blocked/Reconciled/Closed`;
- `PayoutRequested/Submitted/Processing/Succeeded/Returned/Failed/OutcomeUnknown`;
- `PayoutReceiptCreated`;
- `PayoutProcessingCaseOpened/Reconciled/Closed` (exception/reconciliation cases only);
- `JournalPosted`;
- `PostingExceptionOpened`;
- `PeriodClosed`.

## 17. Acceptance cases

- Product A and Product B can have different fee policies and historical transactions retain exact versions;
- ProductPayable includes only amounts derived from Tunner-managed commercial and financial events;
- ProductPayable ownership is Product + payable currency; provider merchant/account/binding does not partition the payable;
- customer presentment and provider settlement currencies may differ only with immutable provider/FX evidence;
- one Settlement never cross-nets multiple payable currencies in the initial model;
- Product payout currency may differ from Settlement currency only through an evidenced FX conversion and active FxEconomicTreatmentPolicy;
- transactions for one Product routed through multiple ProviderAccountBindings aggregate into the same ProductPayableBalance for that currency;
- every posted journal balances and duplicate source event cannot post twice;
- correction creates reversal/adjustment rather than editing prior journal;
- settlement approval cannot be interpreted as payout finality;
- successful provider/bank payout finality creates immutable PayoutReceipt and automatic reconciliation without opening PayoutProcessingCase when no exception exists;
- unknown payout outcome blocks duplicate movement;
- failed/returned payout replacement uses a new payout_id/new idempotency context with predecessor lineage;
- Payout retains immutable ProductPayoutDestinationVersion even after Product destination changes;
- provider-native money-movement and payout objects remain typed `ProviderObjectReference` evidence;
- ProductKycCase restriction preserves existing ProductPayable while blocking ineligible Settlement approval/Payout;
- Connected Product never depends on provider-native payout IDs or provider choice;
- Tunner Finance scope is limited to the Tunner-managed commercial, payment, payable, settlement, payout, reconciliation and accounting evidence defined by this FRD.
