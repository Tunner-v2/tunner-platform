> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 03 — Governed Development Lifecycle: Scrum + Git

## 1. Objective

Make every change traceable from Product intent to released artifact and make defects/reopens/amendments part of normal development rather than exceptional manual cleanup.

## 2. Scrum model

Use Scrum according to the current official Scrum Guide while adding Tunner governance gates around engineering evidence.

Hierarchy:

`Product Goal → Product Backlog → Milestone/Epic → Sprint Goal → Work Item → Increment → Acceptance → Release`

The Product Backlog remains ordered and emergent. Governance does not turn Scrum into a waterfall approval chain.

## 3. Work-item lifecycle

```text
DRAFT
  ↓
BACKLOG
  ↓
REFINEMENT
  ↓
READY
  ↓
IN_PROGRESS
  ↓
CODE_REVIEW
  ↓
VALIDATION
  ↓
PRODUCT_ACCEPTANCE  (when required)
  ↓
DONE
```

Side states:

- `BLOCKED`
- `NEEDS_PRODUCT_DECISION`
- `UI_REQUIRED`
- `CONTRACT_REQUIRED`
- `SECURITY_REVIEW_REQUIRED`
- `REOPENED`
- `DEFERRED`
- `CANCELED`
- `SUPERSEDED`
- `DUPLICATE`
- `NOT_APPLICABLE`

No record is deleted merely to clean the board.

## 4. Milestone lifecycle

`PROPOSED → APPROVED → ACTIVE → VALIDATION → PRODUCT_ACCEPTANCE → RELEASED → CLOSED`

Exceptional:

- `BLOCKED`
- `AMENDED`
- `REOPENED`
- `SUPERSEDED`

Closure requires evidence, not task count.

## 5. Sprint change management

A sprint change must be classified:

- `SPRINT_SCOPE_ADDITION`
- `SPRINT_SCOPE_REMOVAL`
- `PRIORITY_CHANGE`
- `BLOCKER`
- `DISCOVERED_REQUIREMENT`
- `DEFECT`
- `PRODUCT_AMENDMENT`
- `ARCHITECTURE_DECISION`

The system preserves original commitment and changed scope for review.

## 6. Git strategy

Use protected trunk-based development.

- `main` is the integration/release line.
- short-lived branches.
- normal direct pushes to `main` prohibited.
- environments are not branches.
- release artifacts progress through environments.

Branch patterns:

```text
feat/TUN-184-account-activation
fix/TUN-209-session-expiry
change/TUN-233-sdk-compatibility
hotfix/TUN-301-payment-idempotency
docs/TUN-155-subscription-contract
```

## 7. Commit convention

Recommended:

```text
feat(identity): add verified-contact activation

Work-Item: TUN-184
Milestone: M02
Flow: TUN-FLOW-ID-01
ADR: ADR-0017
```

Commits should be cohesive and buildable when practical.

Do not use commit messages as the sole requirement documentation.

## 8. Pull request gate

Required as applicable:

- linked work item;
- acceptance criteria;
- architecture/flow references;
- documentation impact;
- ADR/decision impact;
- build;
- unit tests;
- integration tests;
- contract validation;
- architecture tests;
- static analysis;
- secret scan;
- dependency/SCA scan;
- migration validation;
- UI screenshots if UI changed;
- test evidence manifest;
- human/AI review evidence;
- required CODEOWNERS review.

Sensitive areas require strengthened review policy.

## 9. Defect model

Types:

- `BUG`
- `REGRESSION`
- `SECURITY_DEFECT`
- `DATA_DEFECT`
- `CONTRACT_DEFECT`
- `UI_DEFECT`
- `PERFORMANCE_DEFECT`
- `INTEGRATION_DEFECT`
- `DOCUMENTATION_DEFECT`
- `TEST_DEFECT`

Severity is policy-versioned, not free text.

Initial classification semantics:

- `SEV-1`: critical security/data integrity/platform outage.
- `SEV-2`: major business capability materially broken/unsafe.
- `SEV-3`: functional defect with bounded workaround.
- `SEV-4`: minor/non-blocking defect.

Response times are configuration/policy, never hardcoded here.

## 10. Reopen model

Reopening preserves prior “done” evidence.

Required:

```yaml
reopen_id: REOPEN-0042
work_item: TUN-184
previous_release: 0.4.0-rc.2
reason: ""
detected_environment: STAGING
classification: REGRESSION
severity: SEV-2
new_acceptance_criteria: []
affected_versions: []
evidence: []
```

A reopened item enters active work with causation linked to the prior completion.

## 11. Change request vs defect

Use `CHG-*` for intentional business/technical change.

Use `DEF-*` for deviation from approved expected behavior.

A discovered missing requirement may require both `DEC-*` and `CHG-*`.

## 12. Hotfix

Hotfixes still require:

- work/defect record;
- root-cause statement;
- targeted tests;
- security/contract check;
- release evidence;
- post-merge synchronization with main.

“Emergency” does not mean untraceable.

## 13. Release Git tags

Formal release tags:

- `v0.1.0`
- `v0.4.0-rc.1`
- `v1.0.0`

A tag maps to:

- commit SHA;
- container digest;
- migration set;
- API contract;
- SDK versions;
- SBOM;
- provenance/attestation;
- security report;
- test evidence;
- documentation baseline.

## 14. Example — staging regression

1. `TUN-184` was DONE in `0.4.0-rc.1`.
2. Staging discovers duplicate verification callback.
3. Create `DEF-0098` + `REOPEN-0042`.
4. Work item becomes REOPENED.
5. branch `fix/TUN-184-verification-idempotency`.
6. new regression test proves duplicate callback is safe.
7. PR gates pass.
8. `0.4.0-rc.2` evidence references both original and reopened history.

## 15. Acceptance tests

| ID | Scenario | Expected |
|---|---|---|
| LIFE-001 | PR has no work item | merge blocked |
| LIFE-002 | completed item reopened | previous evidence retained and new reopen record created |
| LIFE-003 | sprint scope changes | original vs changed scope visible |
| LIFE-004 | migration PR | migration rehearsal gate executes |
| LIFE-005 | release tag | evidence manifest resolves exact source/build/contracts |

## GitHub hosting and CI/CD baseline

GitHub is the selected source host and GitHub Actions is the selected CI/CD automation system.

Required controls:
- repository/organization Rulesets for protected branches and release tags;
- pull request required for normal changes;
- required status checks;
- CODEOWNERS on sensitive paths;
- signed commits/tags where governance policy requires;
- no force-push/delete on protected release history;
- reusable workflows for build/test/security/release;
- GitHub OIDC federation for cloud deployment credentials when supported;
- artifact attestations and SBOM linkage for released artifacts;
- environment protection/approval for production deployments;
- `tunner-governance` remains the work-state authority, while GitHub issues/PRs are synchronized execution surfaces rather than a replacement for Tunner governance records.


## 16. Governance-controlled orchestration rule

Automated/AI orchestration is an execution mechanism inside the governed lifecycle, not an alternate lifecycle.

Before orchestration starts a work item, governance must confirm:

- valid work item and milestone;
- prerequisites and blockers;
- applicable authority/decisions/contracts/flows;
- required role/skill reviews;
- context policy and authorized access;
- required tests/evidence.

During execution, orchestration may parallelize independent checks or specialist reviews when dependencies permit. It must preserve causation/evidence and cannot skip lifecycle states or required gates.

If any required role, test, decision, authority or context is missing, only the affected work is blocked. The orchestrator must return the blocker to governance and continue unrelated authorized work where safe.

## 17. Pre-repository bootstrap interpretation

Before TUN-P0-001 creates the repository, Git branch/PR/Ruleset/Actions requirements are prospective controls. Their URLs cannot exist yet and must not be fabricated.

P0 creates the repository execution surface, records its canonical URL, configures the applicable controls, and then begins normal governed Git/PR execution.

The first repository/bootstrap commits are part of P0 evidence and must establish the governance files required to prevent subsequent ungoverned development.

## 18. Pull-request approval and continued local execution

Human pull-request approval is an **integration/merge gate for protected `main`**, not a general execution gate for all development.

A work item may progress through local/branch execution states independently of the pull request's merge state:

```text
IN_PROGRESS → CODE_REVIEW → VALIDATION → READY_FOR_MERGE
```

The related pull request has a separate integration state such as:

```text
DRAFT → OPEN → CHECKS_RUNNING → READY_FOR_REVIEW → APPROVAL_PENDING → APPROVED → MERGED
```

`APPROVAL_PENDING` does not by itself place the work item or milestone in `BLOCKED`.

While a PR waits for required human approval, governance/orchestration must continue any work that is:

- independent of the pending merge;
- safely executable against the current local branch/worktree state;
- dependent only on a prerequisite that is already `LOCAL_VALIDATED`/`READY_FOR_MERGE`; or
- review/test/evidence/context/documentation work that does not require merged-main state.

A downstream item is blocked only when its prerequisite explicitly requires `MERGED_TO_MAIN` or another genuine human-authority gate.

Dependency readiness therefore distinguishes at minimum:

- `LOCAL_VALIDATED` — prerequisite behavior/evidence is available in the current governed branch/worktree lineage;
- `MERGED_TO_MAIN` — prerequisite must be integrated into protected `main` before the dependent work is safe/valid.

Governance must record which readiness level a dependency requires. It must not infer `MERGED_TO_MAIN` merely because a PR exists.

Human interaction remains mandatory where the applicable policy requires it, including protected-main merge approval, explicit Product decisions/acceptance, production/release authorization, and security/compliance/legal approval that cannot be delegated.

To avoid both human bottlenecks and unreviewable branch accumulation, policy may limit dependent unmerged chains/concurrency. Such limits are configurable governance policy rather than hard-coded Product behavior.

### Additional lifecycle acceptance tests

| ID | Scenario | Expected |
|---|---|---|
| LIFE-006 | PR is `APPROVAL_PENDING` and independent eligible work exists | governance exposes that work; project does not globally block |
| LIFE-007 | downstream work requires only `LOCAL_VALIDATED` prerequisite | work may continue on governed local/branch lineage before merge |
| LIFE-008 | downstream work explicitly requires `MERGED_TO_MAIN` | only that dependent scope blocks until merge |
| LIFE-009 | agent continues commits/tests/evidence while PR awaits approval | allowed when governance eligibility remains satisfied |
| LIFE-010 | unmerged dependency/concurrency policy limit reached | governance stops additional dependent chaining and exposes an integration action |
