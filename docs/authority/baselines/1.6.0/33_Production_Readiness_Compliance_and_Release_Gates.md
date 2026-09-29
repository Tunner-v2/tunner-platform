> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Closure revision date:** 2026-09-29  

# 33 — Production Readiness, Compliance & Release Gates

## 1. Purpose

This document defines objective gates for promoting Tunner from development to Sandbox, Staging and Production. “Feature complete” or “tests pass” alone is not release authority.

## 2. Gate model

Every release has a `ReleaseReadinessRecord` with gate status:

```text
NOT_EVALUATED
PASS
PASS_WITH_ACCEPTED_EXCEPTION
FAIL
NOT_APPLICABLE_WITH_EVIDENCE
```

Exceptions are time-bound, owned and approved; they cannot waive constitutional Product Decisions or critical financial/security invariants.

## 3. Documentation/traceability gate

PASS requires:

- implemented work maps to active FRD/workflow/ADR/PDR;
- API/event/schema documentation updated;
- migrations/operational runbooks updated;
- UI requirements/Figma approved for user-facing changes;
- acceptance criteria/tests/evidence linked;
- no unresolved `TBD/TODO/FIXME` in normative authority for released behavior;
- no implemented behavior contradicts FigJam authority boundary without approved architecture change.

## 4. Code quality gate

- build clean with warnings policy;
- formatter/static analysis passes;
- unit tests pass;
- architecture/dependency tests enforce module boundaries;
- no duplicate infrastructure/domain implementation where a shared primitive is required;
- no hardcoded secrets/config/legal deadlines/SLOs/provider credentials;
- package licenses and known vulnerabilities reviewed.

## 5. Dependency/supply-chain gate

- dependencies pinned/locked and centrally governed;
- stable supported packages only unless approved ADR;
- Dependabot/security scanning enabled;
- SBOM generated (SPDX-compatible target);
- artifact provenance/attestation generated for release artifact;
- container image scan passes policy;
- GitHub Rulesets/required checks protect `main` and release tags;
- deployment credentials use short-lived/OIDC mechanisms where target platform supports them.

## 6. Security gate

- threat model updated for materially changed capability;
- OWASP ASVS 5 target controls applicable to release evidenced;
- API security controls tested;
- auth/session/recovery/step-up tests pass;
- privileged/internal access and SoD tests pass;
- secrets never present in repo/build logs/database fields designated secret references;
- SAST/dependency/secret scanning passes;
- DAST/penetration/security test scope appropriate to milestone passes before Production;
- abuse/rate-limit/lockout tests cover critical public endpoints;
- security logging/audit is sufficient and privacy-safe.

## 7. Identity/privacy gate

- NIST SP 800-63 Revision 4-informed identity policy reviewed for implemented assurance/recovery behavior;
- PDR-0007 DOB-based 18+ eligibility implemented with calendar-safe server calculation, under-18 denial, privacy inventory and retention behavior;
- privacy inventory/processing contract covers new data fields/providers;
- retention class exists for every new durable resource/data class;
- privacy request discovery/export/erasure behavior implemented for new personal-data resource;
- processor/subprocessor record/DPA evidence exists where applicable;
- transfer/residency policy does not promise unsupported residency;
- required notices/consent/communication policy versions are active.

## 8. Payment/PCI, Product compliance and provider-account gate

- PDR-0006 provider-neutral merchant/payment orchestration is implemented; Connected Product contracts contain no provider dependency;
- Product KYC/KYB/compliance collection, R2 object access, retention, redaction, step-up and audit controls pass security/privacy tests;
- Product/provider/payment configuration matches PDR-0006: Products depend only on Tunner contracts; provider-native account/routing details remain internal;
- Financial Regulatory Classification Policy is approved for each enabled market/provider model and no route classified as requiring disallowed Tunner financial-services registration is enabled;
- PAN/CVV does not enter Tunner application logs/database/backend where provider-hosted/tokenized architecture is required;
- Stripe configuration uses least-privileged keys/secret store;
- webhook signatures verified and replay/dedup tests pass;
- payment idempotency/unknown-outcome/reconciliation tests pass;
- Tunner CheckoutOperation uses an approved provider-secure embedded or provider-hosted presentation mode; raw PAN/CVV does not traverse Tunner application servers/logs/databases;
- refund/dispute duplicate-safety tests pass;
- current PCI DSS scope/responsibility reviewed and evidence package maintained;
- Tunner provider merchant/account capability gates tested independently from Product economics.

## 9. Market/tax/commercial gate

For each enabled market/Product/payment-service profile:

- `MarketEligibilityPolicy` ACTIVE;
- Product payment/compliance eligibility passes (`ProductKycCase` is `VERIFIED` where required) and Tunner provider route/account capabilities/readiness are independently satisfied;
- provider supports route/presentment currency/payment method and is currently charge-capable/not held or pending review;
- tax responsibility/registration readiness resolved and an approved compatible `TaxEngineBinding` is ACTIVE for every tax-required released scope;
- tax calculation uses a managed `ITaxEngine` adapter (initially Stripe Tax where supported); no Tunner-maintained tax-rate/jurisdiction/taxability calculator or silent zero-tax fallback exists;
- tax-engine adapter extensibility tests prove that an additional approved managed tax provider can be introduced without changing Product-facing or canonical Checkout/Invoice/Payment/Finance contracts;
- Product tax classification and commercial catalog valid;
- an explicit Product subscription price exists in an enabled `CurrencySupportPolicy` presentment currency; no implicit FX-derived customer price is generated;
- required consumer/privacy/marketing controls active;
- sanctions/restricted-business/provider policy passes.

Missing market compliance policy means **market blocked**, not release-wide fabricated defaults.

## 10. Finance/accounting gate

- Chart of Accounts and posting rules active for every released financial event;
- journal balance/idempotency tests pass;
- Invoice numbering/legal field policy active for enabled seller/jurisdiction;
- payment/invoice/tax/fee/Product-economics lineage reconciles;
- open critical posting/reconciliation defects = zero;
- ProductPayable/ProductPayableBalance/Settlement respect ProductCommercialAgreement and Product+payable-currency attribution; payable currency follows Product commercial transaction currency and is not re-denominated by provider settlement; presentment→provider-settlement treasury conversions and Settlement→Product-payout conversions have immutable FxConversion evidence; Payout resolves ProductPayoutPreference/ProductPayoutDestinationVersion and eligible payout route;
- settlement/payout finality, receipt, notification and reconciliation tests pass;
- active TunnerTreasuryAccount/ProviderSettlementProfile mappings reconcile for every enabled settlement currency;
- conversion-required payout resolves an unambiguous active `FxEconomicTreatmentPolicy` under PDR-0010; platform Standard Cause-Based defaults and any ProductCommercialAgreement override are versioned/effective-dated;
- FX policy configuration UI/API validates treatment mode, cost-component scope, currency/corridor support, percentage totals/caps, preview, approval and audit lineage; raw/manual financial mutation is not a substitute;
- Product-affecting FX allocation uses actual external evidence, prevents duplicate cost allocation and preserves ProductPayable from provider-settlement re-denomination;
- period close rehearsal passes before first financial production close.

## 11. Provider gate

Stripe/Resend/R2 and any enabled provider require:

- production account created and approved;
- environment-specific credential binding in vault;
- webhook/callback/signing tested;
- required capabilities ACTIVE;
- provider incident/disable runbook tested;
- sandbox/staging/prod resources isolated;
- rate limits/quotas/cost protections configured;
- provider ToS/restricted-use/business eligibility reviewed;
- any production claim/configuration of provider continuity has at least two independently ACTIVE/charge-capable eligible routes and tested pre-submission routing; no continuity claim relies on an account still in verification/review/hold.

## 12. Data/storage gate

- PostgreSQL migration rehearsal on production-like copy/test dataset;
- backup + restore rehearsal successful;
- R2 bucket/environment policy, access token scope and lifecycle configured;
- object checksum/reference integrity tests pass;
- retention/hold/purge behavior tested;
- Redis/RabbitMQ loss/restart tests prove business truth is recoverable/not solely stored there.

## 13. Reliability/DR gate

Before Production, each critical service has approved:

- SLO/SLI/ReliabilityBudgetPolicy;
- RPO/RTO/DataProtectionContract;
- alert/escalation ownership;
- dependency/circuit/preflight behavior;
- backup/restore evidence;
- incident/rollback/fix-forward runbook.

Load/stress/capacity test establishes adequate headroom for planned release. No fixed universal target is invented; the approved release profile stores the measured/required values.

## 14. Observability gate

- trace correlation across BFF/API/worker/DB/provider operations;
- domain operation IDs visible in safe logs/traces;
- critical business finality/reconciliation metrics exist;
- queue/outbox/inbox/DLQ/timer health monitored;
- dashboards have freshness indicators;
- PII/secrets redacted;
- alerts tested through a synthetic signal before Production.

## 15. Accessibility/UI gate

- approved Figma screen coverage exists for released user/operator workflow;
- responsive supported viewports pass visual regression/screenshots;
- no overlap/clipping/alignment defects at release breakpoints;
- keyboard/focus/error/status semantics meet WCAG 2.2 AA engineering target;
- backend-denied actions are not shown as always-enabled;
- nonterminal/unknown state has correct customer/operator copy;
- screenshots/evidence attached to milestone acceptance.

## 16. Performance and concurrency gate

Critical scenarios include:

- registration/login/session validation;
- subscription checkout;
- payment webhook burst;
- outbox dispatch/consumer dedupe;
- renewal timer batch;
- invoice generation;
- Admin search/read-model load;
- Product webhook fan-out;
- privacy export/archive where relevant.

Concurrency tests specifically target duplicate creation, version conflict, idempotency and balance/allocation invariants.

## 17. Environment promotion

Immutable artifact promotion:

```text
DEV -> SANDBOX -> STAGING -> PRODUCTION
```

Artifact is built once per release candidate and promoted with environment configuration/secret bindings. Environment-specific rebuild is prohibited except when a new artifact/version is intentionally produced and revalidated.

Database migration is separately gated and must record exact migration set/hash.

## 18. Release approval

Release approval records:

- release version/tag/artifact digest;
- included work items/FRDs;
- gate evidence refs;
- outstanding accepted exceptions;
- migration/config versions;
- provider/schema/API versions;
- approvers;
- deployment window;
- rollback/fix-forward plan;
- post-deploy verification result.

Git tag/release artifact becomes immutable closure evidence.

## 19. Post-deployment verification

Verify:

- health/SLO/freshness;
- DB migration/schema;
- authentication;
- representative safe API/BFF paths;
- outbox/broker/worker;
- provider webhooks and safe synthetic/nonfinancial checks;
- object storage;
- no error-rate/regression anomaly;
- read models/projections current.

Financial live-charge smoke tests require explicit safe test/production policy and are never improvised.

## 20. Rollback/fix-forward

Rollback is used only when safe for code/data/provider contract. If irreversible migration or external side effect occurred, use fix-forward/reconciliation/compensating operation. Deployment rollback must never rollback a successful real-world Payment/Email/Bank side effect in database history.

## 21. Production blocker examples

- unresolved Product Decision affecting released workflow;
- missing market/tax policy for enabled market;
- unknown payment outcome logic not tested;
- no restore evidence;
- missing financial posting rule;
- secrets in config files;
- UI workflow missing/contradictory;
- critical provider capability not active;
- stale/unsupported dependency with known unacceptable vulnerability;
- no SLO/RPO/RTO policy for critical production service;
- open Sev-1/critical security/financial reconciliation defect.


## Governed presentation-content release gate

Production promotion requires evidence that all applicable user/operator presentation surfaces use the governed content architecture rather than unmanaged screen-local mutable copy; ordinary UI does not expose raw internal implementation terminology as primary content; deterministic fallback is tested; sensitive/legal/security/payment/tax/privacy/consent content has required version/approval evidence; published-version cache invalidation is tested; historical content references remain reconstructable where required; and any model-assisted content path is proposal-only with no direct production publication authority.
