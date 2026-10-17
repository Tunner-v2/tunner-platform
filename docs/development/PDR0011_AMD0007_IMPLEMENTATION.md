# PDR-0011 / AMD-0007 implementation

Approved current authority supersedes the global adult-only Account gate prospectively. Work uses active P2-002/003/004; original P2-004 was VALIDATION at f64f9976a150e3c4a08378c9b87c980b91359a81, with no historical closure.

The current slice preserves registration/proof/credential/outbox behavior, moves DOB to verified-contact-bound normal Personal Information, centralizes age/consent/commercial policy, and emits no raw DOB. Billing, payment-method, payer and Product authorization decisions remain with their owning future-plane contracts. Current module inventory has no Billing or Subscription implementation. TUN-P2-005 and TUN-P2-014 remain the separate auth/session and Product authorization implementation owners.

Security design: registration browser context plus successful contact proof authorizes only one bounded onboarding Personal Information workflow for that same Account; it grants no login session or general Account editing. Account-owned profile storage is reusable by later authenticated adapters. Client data cannot mark contact verified, choose authoritative jurisdiction, assert consent evidence or grant BillingAuthority. Policies, processing purpose, retention and published content are explicit trusted providers with no production defaults.

Consent is a trusted, subject/policy/time-bound evidence relationship. Guardian/authorized consent is the normal assurance; school evidence and stronger proof are accepted only by the active policy. No consent grant endpoint accepts browser assertions. Later Privacy/Consent integration supplies the verified evidence adapter; absence produces only policy-required consent handling, not a global age rejection.

Current commercial eligibility is recomputed from protected profile material and effective policy at action time. Unknown/expired/missing evidence cannot independently authorize a payer; the existing PAYER_CONTEXT_REQUIRED contract and BillingAuthority own the resolution. No minor payment resources are introduced.

Status: preparation in progress. Final committed-source tests, reviews and receipts must replace this preparation status before readiness.
