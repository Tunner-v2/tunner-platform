# PDR-0011 / AMD-0007 implementation

Approved current authority supersedes the global adult-only Account gate prospectively. Work uses active P2-002/003/004; original P2-004 was VALIDATION at f64f9976a150e3c4a08378c9b87c980b91359a81, with no historical closure.

The current slice preserves registration/proof/credential/outbox behavior, moves DOB to verified-contact-bound normal Personal Information, centralizes age/consent/commercial policy, and emits no raw DOB. Billing, payment-method, payer and Product authorization decisions remain with their owning future-plane contracts. Current module inventory has no Billing or Subscription implementation. TUN-P2-005 and TUN-P2-014 remain the separate auth/session and Product authorization implementation owners.

Security design: registration browser context plus successful contact proof authorizes only one bounded onboarding Personal Information workflow for that same Account; it grants no login session or general Account editing. Account-owned profile storage is reusable by later authenticated adapters. Client data cannot mark contact verified, choose authoritative jurisdiction, assert consent evidence or grant BillingAuthority. Policies, processing purpose, retention and published content are explicit trusted providers with no production defaults.

Consent is a trusted, subject/policy/time-bound evidence relationship. Guardian/authorized consent is the normal assurance; school evidence and stronger proof are accepted only by the active policy. No consent grant endpoint accepts browser assertions. Later Privacy/Consent integration supplies the verified evidence adapter; absence produces only policy-required consent handling, not a global age rejection.

Current commercial eligibility is recomputed from protected profile material and effective policy at action time. Unknown/expired/missing evidence cannot independently authorize a payer; the existing PAYER_CONTEXT_REQUIRED contract and BillingAuthority own the resolution. No minor payment resources are introduced.

Implementation: the active P2-002 evaluator and P2-003 persistence corrections are committed and validated. P2-004 now implements verification-bound normal Personal Information and policy-driven completion. The current result is exclusively the committed TUN-P2-004-VALIDATION.json receipt; a PENDING candidate is never readiness.

Contracts: docs/contracts/registration-onboarding.openapi.json records the strict private BFF schema. No public SDK or Product API is activated. Forward migrations preserve historical PDR-0007 rows and refuse populated downgrade. The new protected profile payload is the only DOB store; immutable history, replay receipts and outbox hold safe references/evaluations.

Figma: existing initial70:270, normal profile35:107 and onboarding derivative198:2 align before code. Historical DOB-only187:2 is hidden/labeled superseded. Published copy remains an explicit production dependency.

Handoff: use the work-item validation/role receipts and the task-specific PDR0011_AMD0007_HANDOFF.md. Generated context lives under the ignored task artifacts to preserve the pre-existing dirty context/project files. No Product/release acceptance or closure is asserted.
