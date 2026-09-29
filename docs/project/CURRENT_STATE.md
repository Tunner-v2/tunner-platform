# CURRENT_STATE

## Baseline

- Documentation: Baseline 1.6.0 (ACTIVE / LOCKED).
- Active amendments: AMD-0001.
- Authority verification: PASS for 125/125 bootstrap-bundle entries and 54/54 Pre-P0 entries; external Drive comparison pending.

## Current milestone

- ID: P0
- Status: ACTIVE
- Goal: establish the self-governing development control plane before P1–P8.

## Active sprint

- ID: none
- Goal: B2 Git/GitHub bootstrap.
- Dates: not scheduled.

## Active work items

| ID | State | Owner/roles | Blocker | Next action |
|---|---|---|---|---|
| TUN-P0-001 | DONE | governance-engineer, tester-qa-engineer, auditor, cyber-security-engineer, devops-engineer | none | Resume B2 |
| TUN-P0-019 | BLOCKED | governance-engineer, devops-engineer, cyber-security-engineer, auditor | Current SSH identity is unauthorized for the registered private repository | Add an authorized SSH key or grant existing key write access |

## Build/test state

- Last verified commit: `3fe58ef79909451405e434c8d06d651885a5dce6`.
- Build: not run; dependency restore/build is a user-run step.
- Bundle integrity: PASS (125/125).
- Pre-P0 package integrity: PASS (54/54).
- Repository mirror integrity: PASS (125/125).

## Open blockers

- `EXT-DRIVE-001`: canonical Drive inventory was not readable through the connected session. This blocks only external-source comparison, not use of the immutable verified bootstrap bundle.
- `GITHUB-BOOT-002`: remote `git@github.com:Tunner-v2/tunner-platform.git` is registered, GitHub host identity was verified, but SSH authentication returned `Permission denied (publickey)`.

## Exact next authorized action

- Authorize an SSH key with write access to `Tunner-v2/tunner-platform`; then push `main` and register the canonical repository URL.
