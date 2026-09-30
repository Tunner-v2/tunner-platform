# CURRENT_STATE

## Baseline

- Documentation: Baseline 1.6.0 (ACTIVE / LOCKED).
- Active amendments: AMD-0001 and AMD-0002.
- Authority verification: PASS for 125/125 bootstrap-bundle entries and 54/54 Pre-P0 entries; canonical Drive folders and controlling documents are readable. Full source-to-mirror diff remains TUN-P0-033 scope.

## Current milestone

- ID: P0
- Status: ACTIVE
- Goal: establish the self-governing development control plane before P1–P8.

## Active work items

| ID | State | Blocker | Next action |
|---|---|---|---|
| TUN-P0-001 | DONE | none | Bootstrap complete |
| TUN-P0-002 | VALIDATION | none | Retain AMD-0002 contribution-policy evidence for final aggregate P0 PR |
| TUN-P0-003 | VALIDATION | none | Retain schema-validation evidence for final aggregate P0 PR |
| TUN-P0-004 | VALIDATION | none | Retain AMD-0002 dependency-readiness evidence for final aggregate P0 PR |
| TUN-P0-005 | VALIDATION | none | Retain bounded context-pack evidence for final aggregate P0 PR |
| TUN-P0-006 | VALIDATION | none | Runtime health is PASS; retain Compose evidence for final aggregate P0 PR |
| TUN-P0-019 | VALIDATION | none | Retain GitHub governance evidence for final aggregate P0 PR |

## Build/test state

- .NET SDK: 10.0.401 installed and used for local P0 validation.
- Tunner.Governance build: PASS (0 warnings, 0 errors).
- Functional harness: PASS, including fresh and stale generated-context scenarios and active-authority-summary inclusion.
- Governance live validation: PASS for 16 records with no diagnostics.
- Context pack: PASS; `docs/context/current/` is manifest-first, hash-verifiable, and CURRENT for TUN-P0-005. It includes the active-authority summary and AMD-0002 source.
- P0-006 static Compose source/configuration validation: PASS; `tunner-dev doctor` confirmed Docker Desktop 4.87.0 / Engine 29.7.2 and Compose v5.4.0 without pulling images or starting containers.
- P0-006 runtime dependency health: PASS. User-run output confirmed PostgreSQL, RabbitMQ, Redis, OpenBao, Mailpit, and LGTM healthy after the corrected readiness probes and OpenBao configuration path fix.
- NuGet vulnerability metadata: no vulnerable packages reported by current source metadata.
- Bundle integrity: PASS (125/125). Pre-P0 package integrity: PASS (54/54). Repository mirror integrity: PASS (125/125).

## Delivery model and controls

- DEC-0001 permits the temporary P0-only local integration branch: change/TUN-P0-control-plane.
- Each P0 work item is separately committed, validated, evidenced, and governed locally.
- A locally validated P0 prerequisite may unblock dependent P0 work, but no P0 item becomes DONE until the final protected P0 PR merges.
- One final protected P0 pull request requires an independent ai-dev code-owner approval; GitHub may auto-merge only after protected requirements pass.

## Exact next authorized action

- TUN-P0-005 is locally validated. `governance next` reports only existing validation-stage records and no protected-main human action. Select the next governed P0 work item only after its repository work-item record is created from the canonical backlog; do not invent an unrecorded scope.