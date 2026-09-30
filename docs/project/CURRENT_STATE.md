# CURRENT_STATE

## Baseline

- Documentation: Baseline 1.6.0 (ACTIVE / LOCKED).
- Active amendments: AMD-0001.
- Authority verification: PASS for 125/125 bootstrap-bundle entries and 54/54 Pre-P0 entries; canonical Drive folders and controlling documents are readable. Full source-to-mirror diff remains TUN-P0-033 scope.

## Current milestone

- ID: P0
- Status: ACTIVE
- Goal: establish the self-governing development control plane before P1–P8.

## Active sprint

- ID: none
- Goal: local P0 integration of Git policy and B1 governance schemas.
- Dates: not scheduled.

## Active work items

| ID | State | Owner/roles | Blocker | Next action |
|---|---|---|---|---|
| TUN-P0-001 | DONE | governance-engineer, tester-qa-engineer, auditor, cyber-security-engineer, devops-engineer | none | Bootstrap complete |
| TUN-P0-019 | DONE | governance-engineer, devops-engineer, cyber-security-engineer, auditor | none | Ruleset, code-owner review, auto-merge, and automatic PR creation validated through PR #4 |
| TUN-P0-002 | VALIDATION | governance-engineer, cyber-security-engineer, devops-engineer, tester-qa-engineer, auditor | none | Local policy/evidence validation passed; final DONE remains contingent on final protected P0 PR |
| TUN-P0-003 | VALIDATION | governance-engineer, tester-qa-engineer, auditor | none | Schema validation passed; P0-004 is in progress locally |
| TUN-P0-004 | IN_PROGRESS | governance-engineer, full-stack-engineer, tester-qa-engineer, auditor, rd-engineer, cyber-security-engineer | ENV-DOTNET-SDK-001 | Read-only MVP source/static evidence is recorded; executable validation awaits .NET 10 SDK |

## Build/test state

- Last verified main commit: `b87f4c39d8bd4a11c53a713673aaf36a1ec4697c` (PR #4 automatic PR-creation workflow).
- TUN-P0-002 static policy/content validation: PASS.
- TUN-P0-003 structural schema validation: PASS for 16 entry schemas and 17 parsed JSON documents.
- TUN-P0-003 negative unexpected-entry validation: PASS.`n- TUN-P0-004 package research: PASS; System.CommandLine 2.0.12, YamlDotNet 18.1.0, and JsonSchema.Net 9.4.0 are current stable baseline pins.`n- .NET SDK: unavailable locally; no restore, build, or executable test has been run.
- Build: not run; dependency restore/build is a user-run step.
- Bundle integrity: PASS (125/125).
- Pre-P0 package integrity: PASS (54/54).
- Repository mirror integrity: PASS (125/125).

## Delivery model and controls

- DEC-0001 permits a temporary P0-only local integration branch: `change/TUN-P0-control-plane`.
- Each P0 work item remains separately committed, validated, evidenced, and governed locally.
- A locally validated P0 prerequisite may unblock a dependent local P0 work item; it cannot become DONE until the final P0 protected PR merges.
- One final protected P0 pull request will require an independent `ai-dev` code-owner approval; GitHub may auto-merge only after protected requirements pass.
- The temporary exception expires when that final P0 pull request is merged or closed. P1 onward uses the strict per-work-item procedure.

## Exact next authorized action

- Install the .NET 10 SDK and run the documented restore/build/functional harness for TUN-P0-004; then record the resulting executable evidence before continuing its completion gate.