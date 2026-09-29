> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 08 — Security, Privacy, Compliance & Global Market Governance

## 1. Important scope statement

This document is an engineering compliance baseline, not legal advice.

Tunner must maintain a versioned jurisdiction/policy register and obtain qualified legal/compliance review for exact applicability, merchant obligations, tax, money-transmission/payment-service classification, breach deadlines, retention and contract wording.

Do not hardcode legal rules simply because they appear in this document.

## 2. Secure SDLC baseline

Use:

- NIST SSDF v1.1 as current final secure-development framework.
- Monitor NIST SSDF v1.2 draft; adopt useful practices through approved policy without labeling the draft as final.
- OWASP ASVS 5.0.0 as web/application verification baseline.
- OWASP API Security Top 10 2023.
- threat modeling for sensitive flows;
- code review;
- SAST/SCA/secret scanning;
- container scanning;
- dependency inventory;
- SBOM;
- release provenance;
- signed artifacts where practical.

## 3. Software supply chain

Target SLSA 1.2-aligned source/build controls progressively.

Release artifacts should include:

- SBOM (SPDX and/or compatible accepted format);
- source commit;
- build identity;
- dependency manifest;
- container digest;
- provenance/attestation;
- artifact signature/verification metadata.

Sigstore/Cosign is a suitable cloud-neutral artifact signing option subject to ADR.

## 4. Payment security / PCI

PCI DSS v4.0.1 is the active PCI DSS baseline; its future-dated requirements became effective March 31, 2025.

Engineering objective: minimize PCI scope.

Tunner must not store PAN/CVV.

Use provider-hosted or provider-secure embedded payment collection selected by Tunner checkout orchestration so Tunner stores:

- provider token/payment-method reference;
- provider/customer/merchant references;
- non-sensitive display metadata where permitted;
- immutable financial evidence.

Exact SAQ/ROC/TPSP scope depends on the implemented Tunner payment architecture/provider configuration and must be formally assessed.

Tunner does not proactively force 3-D Secure as a universal business rule, but it must not disable or bypass provider/network/issuer-required authentication. Such flows are normalized as payment `REQUIRES_ACTION` and handled through the selected provider.

## 5. U.S. privacy

U.S. privacy is not one national general private-sector law.

Engineering baseline must support state-level applicability and rights.

California CCPA/CPRA requirements and the regulations effective January 1, 2026 are a key launch baseline.

Other U.S. state privacy obligations must be maintained in the jurisdiction registry rather than encoded as California-only logic.

System capabilities must support, where applicable:

- disclosure/notice versioning;
- access/export;
- correction;
- deletion/erasure subject to retention/exception;
- opt-out/preference;
- sensitive-data handling;
- request verification;
- processor/service-provider contract evidence;
- risk assessments where required;
- cybersecurity/audit evidence where applicable.

## 6. Canada privacy

Baseline includes:

- PIPEDA federal private-sector framework where applicable;
- Alberta PIPA;
- British Columbia PIPA;
- Quebec private-sector privacy law / Law 25 obligations.

Because applicability can depend on province, organization and commercial activity, resolve jurisdiction through policy rather than one global Boolean.

Capabilities:

- accountable privacy program;
- purpose/collection/use/disclosure governance;
- meaningful consent where consent is applicable;
- access/correction;
- safeguards;
- retention/destruction;
- privacy incident evidence;
- cross-border/processor governance;
- privacy impact/risk evaluation where policy requires.

## 7. Electronic communications

### Canada

CASL requires controls for commercial electronic messages, including consent basis, sender identification and unsubscribe behavior.

Transactional/security/service messages must be classified separately from marketing; do not apply marketing suppression blindly to security-critical communication where the law/contract permits or requires service messaging.

### United States

CAN-SPAM requirements apply to commercial email and include accurate sender/header information, non-deceptive subject, identification/address obligations and opt-out processing.

Notification policy must resolve jurisdiction/message class.

## 8. Consent & disclosure evidence

Consent is not assumed to be the legal basis for every processing operation.

Where consent/disclosure is required, preserve:

- purpose;
- notice/document version;
- locale;
- actor;
- timestamp;
- environment/channel;
- affirmative action evidence;
- withdrawal/revocation;
- jurisdiction/policy reference.

UI can be compact; evidence must be precise.

## 9. Accessibility

Adopt WCAG 2.2 AA as the Tunner web accessibility engineering target.

Accessibility is part of component acceptance, not post-launch polish.

## 10. Identity security

- WebAuthn Level 3/passkeys supported by design.
- MFA and step-up based on risk/operation policy.
- password storage using modern adaptive password hashing via platform-approved implementation;
- credential stuffing/brute force defenses;
- verified contact lifecycle;
- session/device management;
- operation-bound step-up evidence for sensitive action;
- OAuth BCP rules.

## 11. Data classification

At minimum:

- `PUBLIC`
- `INTERNAL`
- `CONFIDENTIAL`
- `RESTRICTED`

Payment credentials, authentication secrets, highly sensitive identity/security evidence are `RESTRICTED`.

Classification controls:

- logging;
- export;
- retention;
- encryption;
- access;
- test-data use;
- AI/model use.

## 12. Encryption

- TLS for all network communication outside explicitly isolated dev-only cases.
- encryption at rest through database/storage/platform controls.
- sensitive application-level cryptography only through approved key-management contract.
- no custom crypto.

## 13. Privacy-by-owner-domain

Do not build a central PII lake for convenience.

Privacy coordinator sends durable tasks to owner domains and records results/evidence.

Owner domain remains authoritative.

## 14. Example — marketing email to Canadian user

Product asks Tunner to send a promotional message.

Tunner Notification:

1. classifies message as commercial/marketing;
2. resolves recipient jurisdiction/policy;
3. validates consent/legal sending basis;
4. applies suppression/unsubscribe rules;
5. renders required identification content;
6. records template/policy/consent evidence;
7. sends via provider;
8. stores delivery outcome separately from consent state.

## 15. Compliance acceptance cases

| ID | Scenario | Expected |
|---|---|---|
| SEC-001 | repository scan | no secret committed |
| SEC-002 | restricted field logged | automated test/redaction policy prevents exposure |
| PCI-001 | card checkout | Tunner receives provider token/reference, not PAN/CVV |
| PRIV-001 | deletion request conflicts with ledger retention | retained record classes documented; no false “fully erased” result |
| CASL-001 | Canadian marketing recipient lacks valid basis | send blocked with policy reason |
| PRIV-002 | jurisdiction policy changes | new effective policy version applies prospectively; historical evidence retained |
| A11Y-001 | guided wizard | keyboard/focus/error semantics satisfy WCAG 2.2 AA target |
| PAY-REG-001 | Product/market/provider route requires an unapproved registration/license or violates active provider/compliance policy | route remains `BLOCKED`/`REVIEW_REQUIRED`; no production activation |
| PAY-SCOPE-001 | Product onboarding submits fields outside the active Tunner service, provider or compliance contract | unsupported fields are rejected; only contract-required, purpose-defined data may be collected |
| FIN-SET-001 | provider payout finality arrives | Tunner creates the immutable `PayoutReceipt`, performs automatic Product payable/settlement/payout reconciliation, and opens/links `PayoutProcessingCase` only when failure, return, unknown outcome, mismatch or another exception requires review/action |
| AGE-001 | server-side DOB calculation establishes applicant is under 18 | Tunner Account activation is denied and the user receives the approved age-ineligibility notice |

## Business-model compliance scope

Tunner's current corporate/home compliance baseline is Toronto, Ontario, Canada. Customer commerce is not limited to Canada. The compliance program covers Canadian/Ontario home-jurisdiction obligations plus destination-market obligations that become applicable when Tunner deliberately offers/sells into other eligible countries:

- user/customer privacy and personal-information governance;
- security and breach-management obligations applicable to handled data;
- marketing/commercial electronic-message consent, preference, suppression and evidence;
- payment-card/security obligations where Tunner systems are in PCI scope;
- merchant, billing, invoicing, tax, settlement and accounting obligations applicable to Tunner-provided services;
- provider/processor contractual and data-protection controls;
- accessibility obligations/targets for Tunner interfaces.

Tunner does not automatically claim healthcare, education, government, gambling or other sector-specific certification/compliance merely because a Connected Product operates in such a sector. A Connected Product requiring a regulated profile must activate an approved compliance profile before Tunner represents support for it.

### Locked Product Decisions

1. **Global commercial reach/currency — PDR-0005.** Tunner's current legal/home jurisdiction is Toronto, Ontario, Canada. Tunner currently has a CAD-denominated Canadian bank account. Eligible customer commerce may be global; presentment, provider settlement, Product payable and Product payout currencies are separately configured/evidenced.
2. **Tunner-managed Product compliance/payment orchestration — PDR-0006.** Connected Products integrate only with Tunner. Product compliance/KYC is represented by canonical `ProductKycCase`; ProductPayable and payable balance are Product + payable-currency scoped. Tunner separately owns provider/gateway merchant-account relationships, provider-account bindings, routing and provider evidence. Provider infrastructure does not partition Product economics.
3. **DOB-based 18+ eligibility — PDR-0007.** Registration collects DOB, performs a calendar-safe server-side age calculation and denies activation when the applicant is under 18. Raw DOB retention is purpose/policy controlled.
4. **Connected Product service relationship/platform scope — PDR-0008.** Tunner records the service capabilities, commercial terms, merchant/payment configuration, financial settlement evidence, support/security context and audit lineage required for each Product.

### Object-storage residency with Cloudflare R2

Cloudflare R2 is the selected primary object store. Storage policy must distinguish:

- **Location Hint** — best-effort placement/performance signal; never treated as a residency guarantee.
- **Jurisdictional Restriction** — enforceable R2 jurisdiction boundary where Cloudflare offers that jurisdiction.
- **Tunner `data_region` / residency policy** — Tunner's business/compliance requirement, which may be stricter than R2's available jurisdictions.

If a Product/environment requires a guaranteed jurisdiction not offered by R2, the Product/environment cannot be declared compliant using a location hint. The storage capability is blocked until an approved `IObjectStorage` provider can satisfy the policy.

R2's S3 compatibility is also not complete AWS S3 parity. Retention/hold controls must use supported R2 bucket controls and Tunner governance; code must not assume AWS S3 Object Lock or AWS KMS semantics.

### Global commercial reach and market eligibility

Tunner may accept eligible customers globally through approved payment providers. Market availability is not equivalent to provider card acceptance alone. Each country/territory must resolve through a versioned `MarketEligibilityPolicy` (or equivalent) that can express:

- `ENABLED`, `RESTRICTED`, `BLOCKED`, or `REVIEW_REQUIRED`;
- permitted Products/payment-service profiles;
- allowed presentment currencies;
- provider/payment-method availability;
- sanctions/restricted-person and provider restrictions;
- privacy/marketing policy profile;
- indirect-tax obligation state and registration status where applicable;
- required disclosures/consumer terms;
- data-region/residency constraints when applicable.

Product presentment currency is explicit Product commercial configuration. Customer-card/account currency, Product presentment currency, provider settlement currency, Product payable currency, Product payout currency and statutory/accounting reporting currency are separate concepts. Every actual Tunner/provider currency conversion must preserve immutable FX evidence.

A successful provider authorization does not by itself prove that Tunner is legally/commercially permitted to sell into the customer's jurisdiction. Eligibility must be evaluated before payment execution.

Global commerce can create destination-country obligations even when Tunner has no local entity. For example, GDPR territorial scope can apply to a non-EU controller/processor when offering goods or services to people in the EU, and indirect-tax/VAT/GST obligations can arise from cross-border sales. These obligations are policy/registration inputs, not hardcoded assumptions.

### Cross-border processing

No strict residency promise is made before cloud/deployment region selection. Cross-border processing may be used only when applicable contracts/privacy obligations permit it. Product/environment `data_region` and residency-policy metadata are designed now so stricter regional enforcement can be enabled later.
