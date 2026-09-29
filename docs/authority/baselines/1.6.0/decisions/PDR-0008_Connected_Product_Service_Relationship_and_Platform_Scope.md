# PDR-0008 — Connected Product Service Relationship and Platform Scope

**Status:** APPROVED — CONSTITUTIONAL BUSINESS DECISION  
**Effective:** 2026-09-22

## Decision

Tunner treats every Connected Product uniformly as a consumer/integrator of Tunner platform services. The Product integrates through Tunner SDK/API contracts and receives only the capabilities activated for that Product/environment.

Tunner maintains the records required to provide, charge, secure, support, reconcile and audit those services.

## Tunner service records

The platform may maintain:

- Product registry and environments;
- SDK/API clients and integration contracts;
- Product capability assignments;
- Product commercial agreements and fee policies;
- ProductKycCase and Product compliance records required for Tunner services; Tunner provider merchant/accounts and routing remain separate provider infrastructure;
- Billing Accounts and Tunner Billing Subscriptions;
- invoices, payments, refunds, disputes and credits managed by Tunner;
- Tunner fee calculations;
- Product-specific payable, ProductPayableBalance, settlement and payout records derived from Tunner-managed transactions and scoped by Product + currency;
- notification, support, risk, security, audit and integration evidence.

Any field collected under provider, KYC, tax or compliance policy must have a documented purpose, classification, access rule, retention rule and authority source.

## Commercial relationship

Each Product has a versioned `ProductCommercialAgreement` defining what Tunner provides and how Tunner charges the Product. Fee policies may differ by Product and may be percentage, fixed, combined, tiered or another approved deterministic model.

Historical transactions retain the exact commercial agreement and fee-policy versions used. Future agreement changes never rewrite prior financial evidence.

## Transparency

Tunner must provide traceable views appropriate to each audience:

- Tunner operators: full authorized Tunner service/financial/provider/audit lineage;
- Connected Product: its Tunner commercial, transaction, fee, payable, settlement/payout and integration information;
- end user/subscriber: user-relevant Product purchase/subscription/charge/tax/invoice/refund status without exposing Tunner internal provider routing or Product/Tunner economics unless required by policy/law.
