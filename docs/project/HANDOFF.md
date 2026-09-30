# HANDOFF

## Work item

- ID: TUN-P0-004
- Milestone/Sprint: P0 / no sprint
- Status: IN_PROGRESS (local P0 integration under DEC-0001)
- Branch: `change/TUN-P0-control-plane`
- Prerequisite: TUN-P0-003 (locally validated; final DONE awaits the final protected P0 PR)

## Completed

- P0-002 contribution policy and P0-003 governance schemas are locally integrated with evidence and validation.
- Recorded DEC-0001: P0 may be locally integrated until one final protected P0 pull request is ready.
- Created P0-004 work record, bounded context manifest, and package-research evidence for the read-only `tunner governance` MVP.
- Current package pins were verified: System.CommandLine 2.0.12, YamlDotNet 18.1.0, and JsonSchema.Net 9.4.0.

## Tests/evidence

- TUN-P0-002 static policy/content validation: PASS.
- TUN-P0-003 schema contract, JSON parse, negative-entry, and whitespace validation: PASS.
- TUN-P0-004 context/work-record/research static validation: PASS.
- .NET SDK is absent locally. No package restore, build, or executable .NET test has run; this limitation must remain in P0-004 evidence until a user performs the documented SDK setup.

## Decisions/blockers

- P1–P8 Product behavior remains out of scope until P0 closes.
- DEC-0001 allows local integration of P0 only. A locally validated P0 prerequisite may unblock a dependent local P0 item, but no P0 item becomes DONE until the final P0 PR merges.
- GitHub Actions creates pull requests and enables auto-merge, but does not approve or bypass required CODEOWNERS review. The final P0 PR retains that human gate.
- P0-004 is source-authorable, but executable validation is blocked until .NET 10 SDK installation is completed by the user.

## Context state

- Authority: Baseline 1.6.0 documents 03, 04, and 12; AMD-0001 document 36; `governance/bootstrap/source/GITHUB_BOOTSTRAP_POLICY.md`; and DEC-0001.
- Context: `governance/context/CTX-TUN-P0-004-001.yaml` (TASK; Product FRDs and UX references explicitly excluded as unrelated).
- Activated roles: governance-engineer, full-stack-engineer, tester-qa-engineer, auditor, rd-engineer, cyber-security-engineer.

## Exact next action

- Implement `tunner governance` read-only commands (`validate`, `status`, `next`, and `gate check`) plus fixture tests and static source validation; do not run dependency installation or .NET restore/build in this environment.