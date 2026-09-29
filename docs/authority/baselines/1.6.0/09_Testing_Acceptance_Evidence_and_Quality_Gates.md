> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Closure revision date:** 2026-09-29  

# 09 — Testing, Acceptance, Evidence & Quality Gates

## 1. Principle

A milestone is not complete until its behavior is independently reproducible from scripts and evidence.

## 2. Test layers

### Unit

- domain invariants;
- value objects;
- policy decision functions;
- calculations;
- state transitions.

### Integration

Run real dependencies in Docker/Testcontainers where practical:

- PostgreSQL;
- RabbitMQ;
- Redis where cache/realtime behavior is under test;
- Cloudflare R2 contract/integration storage where provider object behavior is under test; deterministic fake storage is allowed only for unit/component tests;
- vault test instance;
- local provider stubs;
- HTTP integrations.

Do not mock the database for behavior that depends on transaction/concurrency semantics.

### Contract

- OpenAPI compatibility;
- AsyncAPI/event schemas;
- SDK generated contract tests;
- webhook signature vectors;
- backward-compatibility cases.

### End-to-end

Critical guided user/operator journeys.

### Security

- authn/authz;
- object/property-level authorization;
- rate/resource protection;
- CSRF/CORS/cookie policy;
- OAuth redirect validation;
- secrets;
- injection;
- dependency scanning;
- container/security configuration.

### Performance/reliability

Run when the milestone introduces behavior needing capacity/latency/concurrency validation.

No invented SLO; tests use approved target policy.

## 3. Browser testing

Use Playwright for web acceptance and screenshot evidence.

Capture:

- happy path;
- material error state;
- review/confirmation state for sensitive actions;
- responsive/wide-screen requirement where applicable.

Screenshots are evidence, not pixel-perfect authority over the Figma source.

## 4. Environment test packs

### Development

May reset/reseed local data.

### Sandbox

Uses Product test clients/providers.

### Staging

Production-like, non-destructive where possible, migration rehearsal and integration verification.

### Production-safe

Only:

- health;
- read-only diagnostics;
- synthetic operations explicitly designed for production safety;
- non-destructive contract probes.

## 5. Release evidence pack

```text
release-evidence/
  <release>/
    manifest.json
    governance/
    test-results/
    coverage/
    contract-validation/
    security/
    migrations/
    screenshots/
    sbom/
    provenance/
    logs/
    known-issues.md
    acceptance-checklist.md
```

## 6. Evidence manifest

Must record:

- release;
- Git SHA/tag;
- build identity;
- test tool version;
- environment;
- database migration set;
- API/event contract versions;
- SDK versions;
- container digests;
- test result hashes/paths;
- Product acceptance status;
- unresolved accepted risks/issues.

## 7. Milestone closure gates

Minimum:

- requirements/acceptance criteria complete;
- code complete;
- tests pass;
- security checks pass;
- docs current;
- install/update/test scripts complete;
- migration rehearsal complete where applicable;
- evidence generated;
- no unresolved blocking defect;
- Product Acceptance complete where required.

## 8. Test case structure

Every business acceptance test should include:

```yaml
id:
flow_ref:
requirement_ref:
given:
when:
then:
evidence:
environment_profiles:
```

## 9. Mandatory generic edge cases

For applicable flows:

- unauthorized actor;
- invalid state;
- stale expected version;
- duplicate request/idempotency;
- dependency unavailable;
- timeout;
- retryable technical failure;
- non-retryable failure;
- unknown external outcome;
- duplicate event;
- stale/out-of-order event;
- policy/version change;
- approval/step-up;
- audit evidence;
- subscriber failure isolation.

## 10. Real example — payment unknown

Given provider accepted the TCP/TLS request but Tunner timed out before receiving a conclusive result.

Then:

- payment becomes unknown/waiting state;
- retry/failover blocked;
- reconciliation scheduled;
- no duplicate charge;
- UI shows accurate unresolved state;
- evidence includes provider request reference/correlation.

## 11. Acceptance cases for test system

| ID | Scenario | Expected |
|---|---|---|
| TEST-001 | milestone with failing integration test | cannot close |
| TEST-002 | UI PR | Playwright result + required screenshots included |
| TEST-003 | production-safe suite | no create/update/delete customer operation executed |
| TEST-004 | release evidence regenerated | same Git/build identifiers resolve consistently |
| TEST-005 | flaky test threshold breached | governance marks quality gate failed rather than auto-ignoring |

## 12. Runtime infrastructure acceptance tests

| ID | Scenario | Expected |
|---|---|---|
| MSG-001 | domain transaction rolls back | no RabbitMQ message is emitted |
| MSG-002 | outbox committed, RabbitMQ temporarily unavailable | dispatcher retries; business transaction remains committed and message is not lost |
| MSG-003 | consumer receives duplicate delivery | inbox/idempotency prevents duplicate business mutation |
| MSG-004 | RabbitMQ consumer fails | publisher and unrelated subscribers remain committed |
| CACHE-001 | Redis unavailable | authoritative operation still uses PostgreSQL/domain truth; degraded/cache-miss behavior is explicit |
| RT-001 | SignalR connection drops | client reconnects/resyncs authoritative state through API/read model |
| RT-002 | duplicate/reordered realtime hint | UI does not create authoritative state from SignalR payload alone |
| OBJ-001 | object upload checksum mismatch | object is rejected/quarantined and metadata is not marked usable |
| OBJ-002 | storage unavailable | domain preserves pending/failed file operation state without corrupting authoritative metadata |
| RPC-001 | gRPC-Web feature disabled | equivalent required business capability remains available through canonical REST path unless ADR explicitly makes gRPC-Web required for that internal UI |

## Constitutional business-rule tests

| ID | Given | When | Then |
|---|---|---|---|
| PAY-SCOPE-001 | Product/provider onboarding is reviewed | only fields required by active Tunner service, provider or compliance contracts are present in domain contracts |
| PAY-ROUTE-001 | one Product has eligible transactions routed through multiple Tunner ProviderAccountBindings | every transaction remains Product-attributed; provider route evidence is preserved separately and the same-currency amounts contribute to one ProductPayableBalance |
| KYC-ELIG-001 | ProductKycCase is not `VERIFIED` | a new payment or payout is attempted | operation is blocked; already-earned ProductPayable remains authoritative and unchanged |
| FIN-PAY-001 | Product fee policy is 8% + USD 0.25 and a transaction is eligible | immutable fee calculation uses the exact effective agreement/version and ProductPayable is derived accordingly |
| FIN-PAY-002 | payout reaches successful provider/bank finality | immutable `PayoutReceipt` is created and automatic reconciliation runs; no `PayoutProcessingCase` is opened unless an exception/mismatch exists |
| FIN-PAY-003 | payout outcome is unknown | duplicate payout/reroute is blocked until authoritative reconciliation establishes safe finality |
| FIN-PAY-004 | prior externally submitted payout failed/returned and replacement is permitted | replacement is created | replacement has a new `payout_id`, new idempotency context and predecessor lineage; prior Payout/PayoutReceipt evidence remains immutable |
| FIN-PAY-005 | payout is created | destination is resolved | Payout stores immutable `ProductPayoutDestinationVersion` snapshot/reference and later destination edits cannot mutate it |
| FIN-PAY-006 | Product X receives transactions through Provider Account A and B | payable balance is calculated | one Product+payable-currency ProductPayableBalance is produced; no provider-account-specific Product balance is created |
| TREAS-005 | operator clicks complete without authoritative bank evidence | completion attempted | completion is rejected |
| TREAS-006 | bank transfer is confirmed and matched | reconciliation runs | case transitions to reconciled/closed exactly once and preserves bank reference/evidence |

## Global commerce, checkout and FX acceptance cases

| ID | Scenario | Expected evidence |
|---|---|---|
| GCOM-001 | eligible foreign-issued card, configured Product presentment currency | market/currency/provider route passes; card-country or card-account currency alone does not cause rejection |
| GCOM-002 | cardholder currency differs from Product presentment currency | issuer/cardholder FX remains external unless provider performs a documented conversion; Tunner preserves presentment amount/currency |
| GCOM-003 | blocked/restricted jurisdiction | payment initiation is blocked before external side effect with machine-readable policy reason and audit evidence |
| GCOM-004 | market requires tax registration/profile not ACTIVE | checkout is blocked or routed according to approved tax policy; developer cannot silently collect without authority |
| TAX-ENG-001 | required taxable checkout has an ACTIVE compatible Stripe Tax binding | calculation is executed through `StripeTaxAdapter`, normalized into immutable `TaxDetermination`, and no Tunner-maintained tax-rate table is consulted |
| TAX-ENG-002 | a second approved managed tax service adapter is activated for a supported scope | the same canonical tax/Checkout/Invoice/Payment/Finance contracts continue to work; provider-specific objects remain adapter evidence only |
| TAX-ENG-003 | no approved compatible managed tax engine is ACTIVE for a required determination | checkout/financial operation blocks with a stable tax-engine-readiness reason; Tunner does not guess a rate or silently apply zero tax |
| TAX-ENG-004 | payment routing changes after an authoritative TaxDetermination and no tax input/policy requires recalculation | payment may use the eligible route while the existing TaxDetermination remains authoritative; PSP change alone does not force a different tax result |
| GCOM-005 | provider/card issuer declines otherwise eligible card | Tunner records provider result; market eligibility does not convert provider decline into Tunner success |
| GCOM-006 | requested presentment currency has no active route | checkout blocks with unsupported-currency/route reason; no implicit converted price is generated |
| CHK-001 | primary provider/account is pending review/on hold before payment collection | primary is ineligible; an already-active alternate route may be selected |
| CHK-002 | no active alternate provider/account exists | checkout blocks; verification/hold is never bypassed |
| CHK-003 | provider returns required customer authentication | payment becomes REQUIRES_ACTION and resumes same PaymentTransaction/CheckoutOperation; Tunner does not force or bypass 3DS |
| CHK-004 | provider outcome becomes UNKNOWN after possible submission | alternate-provider charge is blocked until reconciliation proves another attempt safe |
| FX-001 | customer charge converts from presentment currency to provider settlement currency | immutable FxConversion records source/target amounts/currencies, actual rate, provider reference, timestamps and fees when available |
| FX-001A | provider settles a Product transaction in a different currency | ProductPayable remains in the Product commercial transaction/presentment currency; provider settlement FX is separate treasury/clearing evidence |
| FX-002 | Product payout preference differs from Settlement currency | payout requires supported corridor, resolved PDR-0010 FxEconomicTreatmentPolicy and immutable payout FxConversion; Standard Cause-Based default allocates actual Product-requested payout FX cost to Product unless effective agreement override applies |
| FX-003 | Product payout currency/corridor unsupported | payout remains blocked; no silent fallback currency/destination |
| FX-004 | multiple payable currencies exist for one Product | each ProductPayableBalance/Settlement remains currency-specific; initial model does not cross-net currencies |
| FX-005 | provider settlement converts Product transaction currency because Tunner treasury route settles another currency | provider/treasury FxConversion is recorded; default economic treatment is TUNNER_BORNE; ProductPayable is not reduced or re-denominated |
| FX-006 | ProductCommercialAgreement selects TUNNER_BORNE for Product payout FX | actual payout conversion executes | Tunner absorbs only the externally evidenced allocatable cost; policy/agreement version and journal lineage are preserved |
| FX-007 | ProductCommercialAgreement selects SHARED 40/60 | payout FX occurs | Product/Tunner allocations total exactly 100%, respect configured caps, and reconcile to one external cost without duplication |
| FX-008 | no narrower Product override exists | FX policy resolves | approved Standard Cause-Based policy resolves deterministically without manual configuration |
| FX-009 | guided operator config has unsupported currency/corridor, overlapping scope, invalid split or missing approval | activation attempted | activation is blocked with actionable validation; no ambiguous policy becomes ACTIVE |
| FX-010 | operator previews payout conversion | provider quote/indicative rate is available | UI labels preview as estimate; actual ledger allocation uses executed/observed FxConversion evidence only |

## Provider integration acceptance cases

| ID | Given | When | Then |
|---|---|---|---|
| PROV-001 | same Tunner email delivery retried inside Resend's idempotency window | provider call repeats | no duplicate email is created; Tunner delivery evidence remains one logical delivery |
| PROV-002 | Resend idempotency window has expired | old delivery is redriven | Tunner's durable delivery/outbox state prevents an unintended duplicate external send; provider idempotency is not the only safeguard |
| PROV-003 | forged Resend webhook | ingestion executes | signature verification fails and no delivery state changes |
| PROV-004 | R2 object upload succeeds | post-upload verification executes | stored Tunner SHA-256/size/media metadata matches the object before state becomes usable |
| PROV-005 | R2 location hint is configured | residency evaluation runs | hint is never accepted as a residency guarantee |
| PROV-006 | approved residency policy requires unsupported R2 jurisdiction | environment activation runs | activation is blocked with a machine-readable storage-residency capability reason |
| PROV-007 | Stripe mutating request outcome is unknown after timeout | retry path runs | no new business side effect is created until the original outcome is reconciled/idempotently retried |
| PROV-008 | forged/duplicate Stripe webhook | ingestion executes | invalid signature is rejected; valid duplicate is deduplicated by provider-event/inbox identity |


## Tunner checkout acceptance cases

| ID | Given | When | Then |
|---|---|---|---|
| CHK-OWN-001 | customer enters a Product checkout | checkout renders | CheckoutOperation, entry/result state and commercial/tax review remain Tunner-owned; payment collection may use approved embedded or provider-hosted presentation mode |
| CHK-PCI-001 | provider route is resolved | card fields mount | sensitive card fields are provider PCI-safe embedded components; raw PAN/CVV never enters Tunner application servers, logs or databases |
| CHK-ROUTE-001 | provider route changes before submission and token is not portable | new provider component is selected | same CheckoutOperation continues but secure payment details are recollected; no cross-provider token assumption |
| CHK-UNK-001 | external submission may have occurred | outcome becomes unknown | provider failover/retry is blocked until reconciliation establishes safe finality |


## Governed presentation-content quality gate

Automated/acceptance coverage must include: semantic key resolution; locale/audience/surface/context precedence; deterministic fallback; missing mandatory sensitive content blocking; immutable version/history behavior; concurrent edit/version conflict; authorization and approval/SoD for sensitive content; prospective publication and rollback; cache invalidation; unsafe rich-content/link handling where applicable; reusable-component rendering without raw technical constants; exact historical content-version evidence for consent/notification/disclosure where required; and model-generated content remaining unpublished until the normal validation/authorization/publication path completes.
