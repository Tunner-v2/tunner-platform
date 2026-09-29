# PDR-0005 — Canada/Ontario Home Jurisdiction, Configurable Currency and Treasury Model

**Status:** APPROVED — CONSTITUTIONAL BUSINESS DECISION  
**Effective:** 2026-09-23

## Decision

Tunner is currently treated as based in **Toronto, Ontario, Canada** for the active business/development model. Future entities or establishments in other countries are not part of the active authority until separately approved and activated.

Tunner may serve eligible customers globally when market, provider, sanctions, tax, privacy/consumer and other applicable policy gates permit the transaction. Customer location does not change Tunner's current home jurisdiction.

Tunner currently has an active **CAD-denominated Canadian bank account** for provider settlement. A USD bank account is planned but is not active authority until it is created, verified and bound through Tunner configuration.

Currency is modeled explicitly and independently across these dimensions:

- **cardholder/account currency** — the customer's card or bank-account currency;
- **presentment currency** — the currency of the Product price and customer charge;
- **provider settlement currency** — the currency in which the payment provider credits Tunner;
- **Product payable currency** — the Product commercial transaction currency in which the Product obligation is recorded; in the initial model this is the source presentment currency from the immutable CommercialSnapshot;
- **Product payout currency** — the currency configured by the Product for an eligible payout route/destination;
- **statutory/accounting reporting currency** — any currency required for Tunner reporting, filing or accounting translation.

Connected Products publish explicit prices in currencies enabled by the active `CurrencySupportPolicy`. Tunner does not silently derive a customer price from another currency. A customer's card/account may be denominated in another currency; issuer/cardholder FX remains external unless Tunner or its provider actually performs a documented conversion.

A `ProviderSettlementProfile` resolves, by provider account and operation context, the supported presentment currencies, settlement currency, active Tunner treasury/bank destination and any provider conversion behavior. When a provider converts funds, Tunner records immutable `FxConversion` evidence containing source amount/currency, target amount/currency, actual exchange rate, provider/bank evidence, fees/spread when available and timestamps. Provider-settlement FX is a Tunner treasury/clearing event; it does not reduce or re-denominate ProductPayable unless an explicitly approved ProductCommercialAgreement/FxEconomicTreatmentPolicy authorizes a specific Product economic treatment.

`ProductPayable` is created in the **Product commercial transaction/presentment currency** from the immutable CommercialSnapshot. Provider settlement currency and provider FX are separate Tunner treasury/clearing facts and never change or partition the Product obligation. This preserves Product economics even when Tunner changes provider route or settlement bank. A future capability to normalize ProductPayable into a different Product economic currency requires an explicitly approved commercial policy; it is not implicit.

A Product may configure a versioned `ProductPayoutPreference` with preferred payout currency and verified payout destination. If the payout currency differs from the approved Settlement currency, Tunner must execute or obtain an explicit FX conversion and record immutable `FxConversion` evidence before or as part of Payout execution. Unsupported payout country/currency/provider corridors are blocked rather than silently substituted.

The initial model does **not** cross-net multiple source currencies into one Settlement. Each Settlement remains Product + payable currency + period/cycle scoped. Multiple Product currency balances may therefore produce separate settlements/payouts even when they ultimately use the same preferred payout currency.

## Tax interpretation

Tax applicability is resolved from Tunner's active legal/tax registrations, customer/place-of-supply evidence, Product tax classification and applicable market policy. Incorporation/home location alone does not determine every customer's tax rate. Tax calculation is performed through the approved `ITaxEngine` adapter and normalized into Tunner `TaxDetermination` evidence.

Tax calculation currency, tax remittance currency and statutory reporting currency may differ when policy requires it. Any actual currency conversion used for remittance or accounting is separately evidenced and never rewrites the original customer transaction.

## FX economic responsibility

The system must never assume who economically bears FX fees, spread or conversion variance. Every conversion affecting Product economics must resolve an active `FxEconomicTreatmentPolicy` referenced by the `ProductCommercialAgreement`.

Until that policy is approved for a conversion-required Product payout, the payout is blocked. This prevents implementation from silently charging either Tunner or the Product.

## Acceptance invariants

- current legal/home jurisdiction is Toronto, Ontario, Canada;
- current active Tunner treasury/bank account is CAD-denominated;
- a future USD bank account changes behavior only after explicit activation/configuration;
- Product/customer presentment currency is configuration-driven and must be supported by the selected provider route;
- cardholder/account currency does not need to equal presentment currency;
- ProductPayable is recorded in the Product commercial transaction/presentment currency and is never re-denominated merely because a provider settles Tunner in another currency;
- every actual provider, payout, tax-remittance or accounting FX conversion is separately recorded with exact conversion evidence;
- Product payout currency is configurable independently from Product country and customer charge currency, subject to an eligible payout corridor/destination;
- no unsupported currency/corridor is silently substituted;
- no cross-currency netting occurs inside one Settlement in the initial model;
- future U.S., Pakistan or other Tunner entities require an explicit Product Decision before becoming active home-jurisdiction/Seller-of-Record authority.
