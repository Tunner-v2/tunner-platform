> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  


# 12 — AI Development Protocol, Context & Project Memory


## 1. Principle


No critical Tunner knowledge may exist only in an AI chat session.


The repository is the transferable project memory.


## 2. Mandatory startup sequence for AI/human contributor


1. Read `README.md`.
2. Run/confirm `tunner authority verify` for the repository authority mirror.
3. Read current generated context manifest.
4. Read relevant ACTIVE development specs.
5. Read relevant Product Decisions/amendments.
6. Read relevant `TUN-FLOW-*` references.
7. Read current work item and acceptance criteria.
8. Inspect latest Git history touching the area.
9. Run governance validation.
10. Perform required current Internet R&D.
11. Only then propose/implement.


## 3. Context pack structure


```text
docs/context/current/
  manifest.json
  summary.md
  authority.md
  business-rules.md
  relevant-flows.md
  contracts.md
  decisions.md
  open-blockers.md
  git-changes.md
  tests.md
  exclusions.md
```


Generated; do not hand-edit generated pack.


## 4. Memory quality rules


Context must distinguish:


- approved fact;
- open question;
- recommendation;
- historical/superseded decision;
- implementation detail;
- external research.


Never summarize an unresolved question as a decision.


## 5. Assumption handling


If ambiguity could affect:


- business behavior;
- domain ownership;
- security;
- privacy/compliance;
- money;
- external side effects;
- API/event compatibility;
- user-visible workflow;


the work item enters a blocking state.


The AI creates a compact Product Decision request containing:


- missing information;
- why it matters;
- architecture/business constraints;
- 1-3 viable options;
- recommended option if evidence supports one;
- impact of each;
- exact items blocked.


Unrelated work continues.


## 6. No silent “cleanup”


AI must not:


- rename public contract fields casually;
- delete “duplicated” flows;
- merge domain concepts because names are similar;
- refactor across module boundaries without an ADR/impact check;
- update dependency versions without compatibility/research;
- change tests to make a defect disappear.


## 7. Handoff


At work stop/transfer:


- working tree state recorded;
- current branch/commit;
- work item status;
- completed changes;
- pending tests;
- known failures;
- decisions needed;
- exact next action;
- generated context refreshed.


## 8. AI-generated code expectations


- compile;
- test;
- follow existing patterns;
- no placeholder production behavior;
- no TODO without governed work item;
- no fabricated data contract;
- no secret;
- no disabled security control merely to make tests pass.


## 9. Internet research protocol


Search before implementing:


- current .NET/library features;
- current protocol RFCs;
- OAuth/security requirements;
- provider API behavior;
- compliance rules;
- package support/EOL;
- breaking changes.


Record primary sources in `13_RD_Source_Register.md` or ADR-specific evidence.


## 10. Context minimization


AI should receive the smallest sufficient context.


`tunner-context` resolves dependency graph from:


- module;
- work item;
- flows;
- contracts;
- decisions.


Avoid loading complete Finance docs into a simple Identity UI task unless referenced.


## 11. Acceptance tests


| ID | Scenario | Expected |
|---|---|---|
| AI-001 | new AI starts on TUN-184 | context pack identifies applicable authority and open blockers |
| AI-002 | requirement missing | AI creates decision/block, not guessed implementation |
| AI-003 | handoff occurs | next developer resumes from repository without prior chat |
| AI-004 | outdated external source | R&D gate flags/requires refresh |
| AI-005 | AI tries to complete item with TODO placeholder | governance gate fails |


## Decision records


Every AI/human handoff must include applicable files from `decisions/`. Product Decision Records are mandatory business authority and ADRs are mandatory technical authority. A model may recommend an amendment but must not silently override an accepted record.


## 12. Multidisciplinary AI role and skill registry


AI development uses one shared authority/context/memory system. Roles are review lenses and capability contracts, not independent sources of truth and not separate project memories.


Repository location:


```text
.agent/
  AGENTS.md
  skills/
    full-stack-engineer/SKILL.md
    solution-architect/SKILL.md
    project-manager/SKILL.md
    ui-ux-engineer/SKILL.md
    frontend-engineer/SKILL.md
    tester-qa-engineer/SKILL.md
    financial-specialist/SKILL.md
    compliance-specialist/SKILL.md
    auditor/SKILL.md
    founder-product-owner/SKILL.md
    admin-operator/SKILL.md
    support-engineer/SKILL.md
    end-user-ux-reviewer/SKILL.md
    content-writer/SKILL.md
    business-analyst/SKILL.md
    sdk-engineer/SKILL.md
    governance-engineer/SKILL.md
    rd-engineer/SKILL.md
    cyber-security-engineer/SKILL.md
    devops-engineer/SKILL.md
```


`AGENTS.md` is the concise agent entry contract and repository map. It points to authoritative documentation, context tooling and available skills; it does not duplicate the FRD or become a large project encyclopedia. Skill manifests are loaded only when the task activates them. Supporting references/scripts/templates may live beside each `SKILL.md`.


Every skill file defines at minimum:


- mission and scope;
- authority it must read;
- questions/checks it owns;
- prohibited assumptions and authority boundaries;
- required evidence/output;
- blocking conditions;
- handoff requirements.


Required roles:


| Role | Mandatory responsibility |
|---|---|
| Full-stack engineer | implement vertical slices across approved backend/BFF/frontend boundaries without inventing contracts |
| Solution architect | module boundaries, integration patterns, consistency, scalability, resilience, ADR triggers |
| Project manager | milestone/sprint/work-item dependencies, blockers, evidence, completion and next-work calculation |
| UI/UX engineer | journeys, states, accessibility, responsive behavior and Figma-reference alignment subordinate to FRD |
| Frontend engineer | reusable components, presentation mapping/content resolver, BFF/API integration, accessibility and frontend tests |
| Tester & QA engineer | acceptance, negative, concurrency, retry, failure, contract, integration, UI and regression evidence |
| Financial specialist | ledger invariants, money precision, fee/tax/FX/settlement/reconciliation/accounting treatment; cannot invent commercial policy |
| Compliance specialist | privacy, payment, market, tax/compliance and evidence obligations; jurisdiction/provider uncertainty blocks affected activation |
| Auditor | traceability, immutable evidence, approvals/SoD, history, controls, reconciliation and release evidence |
| Founder/Product Owner | validates explicit business/product decisions and acceptance where Product authority is required; AI cannot impersonate approval |
| Admin/operator | verifies operational controls, permissions, diagnostics, recoverability, safe actions and auditability |
| Support engineer | verifies case handling, user-safe diagnostics, escalation, correlation/evidence and recovery workflows |
| End-user UX reviewer | reviews the experience from the intended user's task perspective; cannot override business/security/compliance requirements |
| Content writer | owns user-oriented labels, explanations, errors, guidance and content proposals under governed presentation-content rules |
| Business analyst | decomposes approved business requirements into actors, rules, scenarios, acceptance criteria, flow/contract traceability and gap findings; cannot invent Product decisions |
| SDK engineer | owns Product-facing SDK ergonomics, generated/manual layers, authentication, idempotency/retry semantics, errors, events/webhooks, compatibility, examples and SDK contract tests |
| Governance engineer | owns governance schemas, policies, role activation, orchestration gates, evidence/context integrity and enforcement tooling; cannot redefine Product requirements |
| R&D engineer | verifies current primary sources for material technology/provider/protocol/security/compliance decisions, records freshness and feeds evidence into ADR/source governance |
| Cyber security engineer | owns threat modeling, authentication/authorization review, secrets, cryptography, abuse cases, secure coding, dependency/supply-chain controls and security evidence |
| DevOps engineer | owns Docker/runtime automation, CI/CD, environment promotion, observability infrastructure, deployment controls, secrets integration, provenance, rollback and IaC when authorized |


A single work item may activate several roles. Role activation is risk-based and declared in the work item/context manifest. Detailed normative skill contracts, blockers and structured review outputs are defined in `35_AI_Agent_Skill_Catalog_and_Review_Contracts.md`.


## 13. Persistent project state


The repository must maintain durable state independently of chat history:


```text
docs/project/
  PROJECT_CONTEXT.md
  CURRENT_STATE.md
  DECISION_INDEX.md
  RISK_REGISTER.md
  HANDOFF.md


governance/
  milestones/
  sprints/
  work-items/
  todos/
  defects/
  evidence/
```


Rules:


1. `PROJECT_CONTEXT.md` summarizes stable project purpose, architecture and authority links; generated/verified from authority where practical.
2. `CURRENT_STATE.md` records current milestone, active sprint, active work items, blockers, latest verified build/test state and next governed actions.
3. TODOs exist only as governed records linked to a work item, owner/role, priority, prerequisite and acceptance condition.
4. Milestones and sprint state are repository records, not chat memory.
5. Every material decision links to PDR/ADR/amendment authority.
6. Every session that changes code/docs/tests refreshes affected project state before handoff.
7. Generated summaries never become higher authority than their source documents.


## 14. Mandatory AI development loop


For every work item:


```text
CONTEXT SYNC
  → GOVERNANCE CHECK
  → ROLE ACTIVATION
  → R&D / AUTHORITY CHECK
  → IMPLEMENTATION PLAN
  → IMPLEMENT SMALL VERTICAL SLICE
  → ITERATIVE BUILD / TEST
  → COMMIT VALIDATION CANDIDATE
  → FINAL BUILD / TEST / REVIEWS AGAINST COMMITTED SHA
  → TRACEABILITY / EVIDENCE
  → GOVERNANCE GATE
  → CLOSURE/HANDOFF COMMIT WHEN NEEDED
  → HANDOFF / NEXT
```


The agent must not batch multiple milestones into an unreviewable implementation.


Before implementation it records:


- work-item ID and milestone;
- authoritative FRD/contract/flow references;
- applicable ADR/PDR records;
- activated roles;
- acceptance criteria;
- dependencies/blockers;
- required tests/evidence.


Before DONE it records:


- changed files/contracts/migrations;
- test/evidence results;
- security/compliance/financial reviews when activated;
- UI/UX and content review when user-facing;
- audit/traceability result;
- remaining defects/TODOs;
- documentation/context changes;
- validated commit SHA;
- work-item commit SHA list and tested commit identity;
- closure/handoff commit SHA when distinct;
- classification of any remaining unrelated/pre-existing uncommitted paths;
- commit/release evidence.


## 14A. Commit/readiness discipline — AMD-0002 clarification


For every repository-changing work item, the AI/human contributor must create a cohesive validation-candidate commit before claiming local readiness or completion.


Final validation must correspond to that commit identity.


Do not:


- mark repository-changing work `DONE` while current work-item changes remain only in the working tree;
- treat a passing build/test on an uncommitted state as durable `LOCAL_VALIDATED` evidence;
- silently include unrelated/pre-existing user changes in the work-item commit;
- modify behavior-affecting files after final validation without invalidating and rerunning the applicable gates.


A later commit containing only governance/evidence/handoff closure metadata may follow the validated candidate. Record both SHAs when distinct.


If a work item changes no repository artifact, record that fact explicitly; do not create a meaningless commit merely for ceremony.


### Commit/readiness acceptance cases


| ID | Scenario | Expected |
|---|---|---|
| AI-029 | repository-changing work reaches completion with uncommitted owned changes | governance blocks completion |
| AI-030 | validated SHA differs from tested/evidenced source state | validation evidence is rejected/stale |
| AI-031 | unrelated user changes exist in worktree | agent preserves/classifies them and does not absorb them into its commit |


## 15. Mandatory review matrix


Role review is triggered by impact, not by convenience.


- Any architecture/module/integration change → Solution Architect.
- Any user/admin screen or user-facing state → UI/UX + Frontend + End-user UX + Content Writer.
- Any financial value, ledger, payment, invoice, fee, tax, FX, settlement or payout → Financial Specialist + Tester/QA + Auditor; Compliance where jurisdiction/provider obligations apply.
- Any identity, permission, privacy, market, payment-provider, legal/regulated or sensitive-data change → Compliance + Auditor + Tester/QA.
- Any admin/support workflow → Admin/operator + Support Engineer + UI/UX + Tester/QA.
- Every implementation → Full-stack/owning engineer + Tester/QA.
- Every milestone/release closure → Project Manager + Auditor + applicable specialists.
- Any new/changed business requirement decomposition, acceptance model, cross-domain scenario or traceability gap → Business Analyst.
- Any Product API/SDK behavior, SDK package, generated client, webhook/event consumption, compatibility or examples → SDK Engineer + Tester/QA; Solution Architect when contract boundaries change.
- Any governance schema, gate, context/orchestration policy, evidence rule or agent-skill change → Governance Engineer + Auditor + Tester/QA.
- Any material library/provider/protocol/standard selection or freshness-sensitive technical/compliance assertion → R&D Engineer; applicable specialist still owns the resulting decision.
- Any authentication, authorization, secrets, cryptography, sensitive-data, dependency/supply-chain, abuse-resistance or security-control change → Cyber Security Engineer + Tester/QA + Auditor.
- Any CI/CD, container/runtime, environment, observability infrastructure, deployment, secrets delivery, artifact provenance, rollback or IaC change → DevOps Engineer + Cyber Security Engineer where security-sensitive.
- Founder/Product Owner activation occurs only for explicit acceptance or a decision that existing authority does not resolve.


A role finding blocks only the affected scope unless its risk is systemic.


## 16. Agent entry contract


`.agent/AGENTS.md` must require an AI developer to:


1. never use chat history as the sole project memory;
2. run `tunner context build` and `tunner governance status/next` at startup;
3. read only the relevant authority pack plus the authority index;
4. never treat Figma as business authority;
5. never alter FRD/PDR/ADR semantics silently;
6. create a blocker/decision request rather than assume;
7. maintain work-item, TODO, milestone, evidence and handoff state;
8. activate required specialist skills from the review matrix;
9. use current primary-source R&D where document 13 requires refresh;
10. finish with tests, evidence, context refresh and an exact next action.




## 17. Governance-controlled orchestration


The execution orchestrator is subordinate to governance. It may sequence work and specialist reviews but cannot start, skip, close or release work contrary to governance state.


Required control flow:


```text
AUTHORITY / WORK ITEM
  → GOVERNANCE ELIGIBILITY CHECK
  → IMPACT CLASSIFICATION
  → REQUIRED ROLE/SKILL CALCULATION
  → CONTEXT POLICY / BUDGET
  → EXECUTION ORCHESTRATION
  → TESTS / SPECIALIST REVIEWS
  → EVIDENCE COLLECTION
  → GOVERNANCE GATE
  → COMMIT / HANDOFF / NEXT
```


Governance decides whether work is eligible, which mandatory reviews apply, which blockers exist and what evidence is required. The orchestrator decides only the efficient execution sequence inside those constraints.


## 17A. Mandatory Figma-first visual fidelity and owner-consent gate — AMD-0008


**Active authority:** https://drive.google.com/file/d/1DErZZpc3rIwzfpADnDzD9a361draTuhW/view. Applicable to all AI/human agents implementing, drafting, reviewing or testing Tunner User/Admin/Connected Product-facing UI.


Before editing a visual screen, agents MUST resolve the approved Figma frame/node/version, applicable approved components/tokens/assets, screen ID and FRD/flow; retrieve actual design geometry/states rather than infer a replacement style. Existing designs (especially Login, Registration, Verify Email and Account Ready) require exact matching of approved background gradients/glows, full-page composition, fields, cards, typography, icons, spacing and controls. TailAdmin code scaffolding does not license its default visuals. Do not omit aesthetically significant layers as optional.


When a component/state/screen lacks a matching design, AI may propose on-theme variants and state a recommendation, but may not approve or implement a final unapproved UX. Any material change to existing Figma requires **explicit Product Owner visual consent** and recorded before/after scope. Business/identity contract conflicts are resolved under applicable FRD/PDR and governed Figma revision, not by silently changing the design or ignoring functional authority.


Activation: `UI/UX Engineer + Frontend Engineer + End-user UX Reviewer + Tester/QA` must check `UX-FID-001..008`, Figma screenshot/diff at matched viewport, responsive/accessibility states, `UI_FIDELITY_DEFECT` remediation, and evidence of Product Owner approval for deviations. Governance blocks `UI_VISUAL_APPROVAL` and production UI acceptance when visual proof/approval is missing. This is a required specialist review contract; it cannot be waived by passing unit tests or AI self-review.


## 18. Adaptive context and token-cost governance


Repository access and prompt/context inclusion are separate controls.


A specialist may have full repository **access** while receiving only bounded, relevant prompt context. Repository-wide visibility must not imply repository-wide prompt injection.


Context modes:


| Mode | Use | Default contents |
|---|---|---|
| `CORE` | every session | authority order, current milestone/work item, blocking rules, exact next action |
| `TASK` | normal implementation | relevant FRD/contract/decision/flow, activated skills, affected source/tests, bounded Git history |
| `EXPANDED` | investigation/cross-domain review | dependency neighborhood, related modules/FRDs/evidence/history |
| `FULL_AUDIT` | release/audit/security/architecture/finance-wide investigation | repository-wide index/access with partitioned retrieval and synthesis, not blind one-shot prompt loading |


Context policy is versioned and may define configurable token/input budgets, dependency depth, history depth, evidence scope and maximum escalation mode. Exact token budgets are operational configuration, not hard-coded business rules.


The default strategy is:


```text
MAP → RETRIEVE MINIMUM SUFFICIENT CONTEXT → EXECUTE/REVIEW
  → DECLARE INSUFFICIENT_CONTEXT WHEN NEEDED
  → EXPAND DEPENDENCY SCOPE
  → EXPAND DOMAIN/CROSS-DOMAIN SCOPE
  → FULL_AUDIT ONLY WHEN JUSTIFIED
```


Auditor, Tester/QA, Cyber Security Engineer, Solution Architect and other authorized roles may request repository-wide access. The context engine still supplies information incrementally unless a full snapshot is specifically required.


## 19. Context index, freshness and integrity


`tunner-context` maintains a repository/authority knowledge index. Indexed metadata should include, as applicable:


```text
artifact_id
artifact_type
path
module
domain
authority_type
authority_version
work_items
flows
contracts
decisions
skills
dependencies
dependents
security_impact
financial_impact
compliance_impact
ui_impact
sdk_impact
last_changed_commit
content_hash
summary_hash
```


Generated summaries/context fragments must record their source hash, authority version, generator/tool version and generation time. If the underlying source hash changes, the derived context is `STALE` until regenerated or revalidated.


A generated summary never outranks its source authority.


## 20. Context escalation contract


An AI/agent must be allowed to return `INSUFFICIENT_CONTEXT` instead of guessing.


Escalation is controlled:


1. task-local dependency expansion;
2. module/domain expansion;
3. cross-domain expansion;
4. repository-wide investigation.


Governance records the reason for escalation, the additional artifacts loaded and whether the context budget changed. Sensitive data and secrets remain subject to authorization even during `FULL_AUDIT`.




## 21. Additional acceptance tests


| ID | Scenario | Expected |
|---|---|---|
| AI-006 | new chat/agent starts with no prior conversation | repository context reconstructs current project/work state |
| AI-007 | payment work item starts | financial, QA and audit skills activate automatically; compliance activates when applicable |
| AI-008 | user-facing UI changes | UI/UX, frontend, end-user and content skills review before DONE |
| AI-009 | agent creates free-floating TODO | governance validation fails |
| AI-010 | role recommendation conflicts with FRD | FRD remains authoritative; amendment/decision gate is created if change is needed |
| AI-011 | milestone closure attempted with incomplete evidence | governance gate refuses closure |
| AI-012 | handoff after partial implementation | current state, tests, blockers and exact next action allow another agent to resume without chat history |
| AI-013 | ordinary work item starts | context defaults to bounded `TASK` scope rather than full repository dump |
| AI-014 | auditor needs repository-wide visibility | full access is allowed while prompt context remains partitioned/retrieved incrementally |
| AI-015 | source authority hash changes | stale generated context is rejected/regenerated before use |
| AI-016 | agent lacks necessary evidence | agent returns `INSUFFICIENT_CONTEXT` and governed escalation occurs instead of guessing |
| AI-017 | orchestrator attempts to skip required specialist review | governance gate blocks progression |
| AI-018 | SDK contract changes | SDK Engineer review and compatibility/contract evidence are required |


## 22. Pre-repository startup state


Before P0 repository bootstrap, Drive-hosted locked authority + active amendments are the source package.


Once P0 creates the repository, the repository becomes the transferable working project-memory surface by importing the immutable baseline and active amendment lineage. The GitHub URL and Actions links are then recorded in document 13 and `PROJECT_CONTEXT.md`.


An AI must never infer or fabricate those links before creation.


## 23. Authority mirror rule


After P0 repository bootstrap, normal AI/human development reads the hash-verified repository mirror for speed, availability and reproducibility.


The mirror must preserve:


- immutable baseline version;
- source Drive file ID/URL;
- baseline/archive hash;
- active amendment IDs/source IDs/hashes;
- import timestamp/tool version;
- verification result.


A mirror mismatch is not resolved by AI judgment. The affected work is blocked pending `tunner authority diff` and governed reconciliation.


## 22. Pending PR approval does not imply agent idling


When a pull request is awaiting required human approval, an AI/human contributor must not treat the entire project as blocked solely because the merge gate is pending.


The contributor must run governance status/next and continue all eligible work that does not require the pending change to be merged into protected `main`.


Allowed while approval is pending, when governance permits:


- additional local/branch implementation on the governed lineage;
- build/test/QA/security/audit work;
- evidence generation;
- documentation/context/project-state refresh;
- independent work items;
- dependent work whose declared prerequisite readiness is `LOCAL_VALIDATED`;
- preparation of another reviewable PR when concurrency policy allows it.


The contributor stops only the affected scope when a declared dependency requires `MERGED_TO_MAIN` or a genuine human-authority gate is pending.


The exact next action must therefore distinguish **human integration action** from **agent-executable work**. A pending human action may remain visible while the agent continues other authorized work.


Do not create an arbitrarily long chain of dependent unmerged changes. Respect the configured PR/dependency concurrency policy and prefer cohesive, reviewable increments.


### Additional AI acceptance tests


| ID | Scenario | Expected |
|---|---|---|
| AI-019 | PR awaits approval | agent runs governance next and continues eligible work instead of idling |
| AI-020 | independent work exists during pending PR | agent continues independent work and preserves pending merge action |
| AI-021 | local-validated dependency is sufficient | dependent local work may proceed before protected-main merge |
| AI-022 | merged-main dependency is required | only dependent scope pauses and human merge action is requested |
| AI-023 | no agent-executable work remains | agent reports the single genuine human action required rather than inventing work |




## Location-independent execution rule


AI/human contributors must treat repository location as runtime context, not project authority.


- Never write a developer-specific drive letter, home directory, checkout directory, CI workspace or container mount path into committed Tunner source/config/governance/evidence merely because that is the current execution location.
- Use repository-relative paths in work items, context manifests, evidence, handoffs and generated summaries.
- Resolve repository root through the governed path resolver; do not assume the current working directory is the root.
- If a tool emits an absolute runtime path for diagnostics, normalize persisted references back to repository-relative form when the artifact is inside the repository.
- External canonical URLs/IDs are not converted into filesystem paths.
- A fresh agent working from a relocated checkout must reconstruct the same logical project state without path edits.


Additional acceptance test:


| ID | Scenario | Expected |
|---|---|---|
| AI-021 | fresh agent receives repository in a different checkout directory/drive | startup, authority verification, context build and governance-next use relative logical paths and succeed without editing committed files |




## 24. Authority-first decision protocol — AMD-0005


The mandatory startup sequence is not satisfied merely by reading a generated summary. Before declaring a requirement/contract/architecture gap, the contributor must resolve the question against verified source authority.


### Required sequence


`QUESTION → AUTHORITY VERIFY → APPLICABLE AUTHORITY RESOLUTION → CONFLICT/GAP CLASSIFICATION → R&D IF MATERIAL → DECISION/BLOCKER ONLY IF STILL UNRESOLVED`


The Authority Resolution Pass reviews, as applicable:


- Current Authority Index / verified authority mirror;
- relevant active Development Documentation / FRD;
- accepted ADRs/PDRs/amendments;
- canonical contracts;
- applicable flows/FigJam architecture;
- current governed work/decision history;
- Figma for an actual UX implementation question;
- primary-source research for freshness-sensitive external facts.


Only after this pass may the agent create a decision or missing-authority blocker.


If active authority answers the question, use it.


If implementation contradicts active authority, classify the problem as implementation nonconformance/defect unless a deliberate authority change is proposed.


If current external best practice differs, report the evidence and use ADR/PDR/amendment governance; do not replace Tunner authority in place.


If retrieval is inadequate, return `INSUFFICIENT_CONTEXT` and expand context before deciding that authority is absent.


### Conditional capabilities


An optional/selective capability without its activation trigger is not an ambiguity. Do not ask the Product Owner to invent a use case and do not create `CONTRACT_REQUIRED` merely because no trigger exists.


### Decision evidence


Every proposed material decision/missing-authority blocker records its Authority Resolution Pass. Duplicate decisions overlapping applicable active authority are rejected unless explicit correction/supersession/conflict/revisit lineage is present.


### Additional acceptance cases


| ID | Scenario | Expected |
|---|---|---|
| AI-024 | agent believes architecture decision is missing | agent proves authority-resolution pass before decision/blocker creation |
| AI-025 | accepted ADR already resolves issue | agent references ADR and proceeds; no duplicate decision |
| AI-026 | selective technology has no trigger | no Product Owner question and no missing-contract blocker |
| AI-027 | external best practice conflicts with active authority | conflict is surfaced through governed change process; authority is not silently replaced |
| AI-028 | generated context omits relevant authority | context expands/returns `INSUFFICIENT_CONTEXT` before declaring a gap |




## 25. Revision-aware AI and project-memory protocol


AI/human contributors must distinguish historical closure from later Revision work.


For a historically closed milestone/work item:


- do not overwrite the original closure/evidence;
- do not repeatedly reopen it merely because later maintenance/improvement is required;
- resolve/create the applicable `RV` lineage through governance;
- load the original scope/evidence plus the smallest sufficient Revision delta context;
- preserve prior Revision lineage and current effective state;
- link any required ADR/PDR/amendment rather than treating the Revision as authority.


Generated `CURRENT_STATE`, `PROJECT_CONTEXT` and `HANDOFF` must represent both:


1. the historical original milestone/work-item status; and
2. active/latest Revision state and its dependency impact.


A fresh agent must be able to answer: which original scope is closed, which Revision is active, why it exists, what it blocks, what prior evidence remains valid, and the exact next Revision action.


Revision-aware context selection remains bounded. The existence of a Revision does not justify rereading every historical artifact; retrieve the original/evidence/delta neighborhood relevant to the Revision and expand only when required.


Acceptance:


| ID | Scenario | Expected |
|---|---|---|
| AI-029 | closed item gets later correction | agent uses Revision lineage and leaves original completion intact |
| AI-030 | active Revision exists during startup | generated project state identifies original closure + active Revision + exact next action |
| AI-031 | Revision requires changed business authority | agent blocks on applicable authority process; Revision does not invent the rule |
| AI-032 | Revision has bounded technical scope | context pack retrieves original + relevant delta only, not full-history dump |