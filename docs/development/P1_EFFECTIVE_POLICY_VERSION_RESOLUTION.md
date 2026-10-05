# Effective policy version selection

Work item: TUN-P1-013. This technical contract implements existing approved authority; it does not introduce policy values or a new authority record.

## Authority and ownership

Document 32 sections 1, 17, 18 and 19 govern executable contract shapes, policy-version metadata, resolution by business effective time plus explicit scope, and owner-domain boundaries. PDR-0010 and Document 26 provide an approved owner-specific example of precedence and immutable historical evidence. AMD-0004 requires cohesive reuse of deterministic mechanics; AMD-0005 requires using that existing authority.

The owning module supplies immutable scope context, candidate version records, scope matching, and eligibility evaluation. It decides which statuses/approvals qualify and applies its approved precedence before passing the selected candidate cohort to this primitive. PDR-0010 precedence remains a Finance/commercial responsibility. The shared resolver does not interpret policy payloads, select commercial defaults, approve policy, authorize callers, or resolve financial calculations.

## Executable contract

The capability belongs in `src/Tunner.Platform/Policies/EffectivePolicyVersionResolver.cs`, alongside the existing cohesive Platform capabilities. Repository search found no compatible effective-version resolver to extend. No project, package, process, database table, API endpoint or generic helper layer is needed.

`EffectivePolicyInterval` contains an explicit start instant, optional end instant, and explicit inclusion flags for both bounds. The owning contract supplies the flags: this primitive imposes no universal inclusive/exclusive interval convention. Comparisons use absolute instants; no machine clock or operator/UI time is consulted. An inverted or empty interval is invalid. An explicitly inclusive single-instant interval is valid.

`PolicyVersionCandidate<TScope>` contains immutable version ID, owner status, owner scope, effective interval, optional superseded-version reference, and copied approval-evidence references. Scope must be an owner-defined immutable value. Status and lineage are evidence, never an implicit ordering/default. Approval references identify owner evidence; their presence does not itself verify an approval or authorize use. Owner eligibility evaluation is mandatory. Each candidate has a distinct nonempty version ID and nonempty approval evidence references.

`EffectivePolicyVersionResolver.Resolve` receives business effective time, explicit requested scope, candidate records, an owner scope matcher, and an owner eligibility predicate. It snapshots the candidate collection, rejects duplicate IDs, and selects only records that match the supplied scope, contain the supplied instant and pass owner eligibility. Missing owner callbacks or malformed metadata fail as argument errors; owner callback failures propagate without choosing a fallback.

The result distinguishes `Resolved`, `Missing` and `Ambiguous`. Exactly one matching version yields the original immutable version reference and metadata. Zero matches yields no version. Multiple matches yield no version and deterministically sorted matching IDs. Input order, lexical ID order, current time, status names and supersession lineage never break a tie. `RequireResolvedVersion` rejects either unresolved outcome so an owner can enforce the required-control rule. Optional controls remain an explicit owner concern; no permissive default is supplied.

## Reviews and verification

Preimplementation reviews assess this contract and the test plan only. They do not assert an implemented capability, passing tests or a validated commit. Final role reviews must reference the actual committed implementation and executed checks. Codex performs the required Full-stack Engineer, Solution Architect and Tester/QA reviews; these are engineering reviews, not independent human or Product acceptance.

Deterministic tests cover historical event-time selection, equivalent UTC instants, explicit scope isolation, owner eligibility/precedence, all explicit boundary combinations, open-ended and single-instant intervals, missing/ambiguous resolution, input-order independence, duplicate identities, malformed metadata, defensive collection copying and owner-evaluator failures. Existing Platform architecture tests and the governed zero-warning solution build run against the committed candidate. Authority, governance and context verification accompany the implementation evidence.

This work is technical-only. Completion does not approve financial rules, provider behavior, UI or a public Product contract. The validated/tested implementation SHA and later metadata closure SHA are recorded separately. P0 historical closure and its closed Revision lineage remain unchanged.
