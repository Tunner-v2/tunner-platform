# P2-006 — assurance and operation-bound step-up

State: governed refinement; backend-first vertical slice. MilestoneP2 is ACTIVE. Monitoring-image remediation is separate release work under P1-RV001, as directly instructed by the user. This scope does not depend on a monitoring image or protected-main merge.

Authority resolution: FRD17§7 defines versioned method/context assurance and fresh operation-bound step-up;§8 retains server-owned sessions. Catalog32§1 permits executable private field schemas;§§17–18 require immutable approved versions selected by business effective time plus explicit scope, with missing/ambiguous resolution blocked. Reuse the completed P1-013 EffectivePolicyVersionResolver and P1-012 technical conflict contracts. PDR-0011/AMD-0007 Account age/consent boundaries remain unchanged. AMD-0008/0009 apply before any new UI is accepted.

First implementation slice: immutable private Identity assurance policy contracts and exact operation/context/effective-time selection. Keep assurance identifiers opaque, method mappings explicit and proof lifetimes owner-supplied. Snapshot caller-owned collections. Reject absent/draft/ambiguous/stale versions, mismatched expected policy versions and unconfigured methods. No invented assurance ordering, numeric policy defaults or automatic session upgrade.

This first slice adds no HTTP route, UI, authenticator implementation, schema migration, public Product API/SDK or production configuration. It is not proof issuance/consumption or complete P2-006 acceptance. Its policy objects are not registered as production runtime providers; tests use labelled synthetic policy evidence.

Next slices: bind successful fresh authentication to Account/session/BFF/origin, exact operation/resource/version/context and policy lifetime; persist one-use evidence with transactional replay/concurrency protection and current restriction/session checks. Then add the sensitive-operation BFF/UI flow after reviewing actual Figma patterns, governed copy and URL/VAL/UX evidence. Missing designs/material deviations require the existing explicit Product Owner approval process; no visual approval is inferred from the backend review.

Required QA: exact scope, case/context separation, effective intervals, draft/missing/ambiguity, expected-version conflicts, immutable mapping snapshot and safe logging; subsequent slices add wrong-operation/context/session, expiry, concurrent reuse/replay and real browser regressions. Preserve current onboarding/login behavior.

Startup specialist assessments are executing-agent contract reviews, not independent human review, Product acceptance or release approval. They authorize only this backend scope; final per-role and UI acceptance remain pending. Exact next action: native governance next must report eligibility, implement this first slice, commit it, run focused and existing policy tests and retain actual source-bound results.
