# Governance self-hosting cutover

TUN-P0-034 replaces the limited pre-tool bootstrap exception with repository-governed replay. Bootstrap records document only control-plane setup; they cannot authorize Product feature work.

## Replay input

A bootstrap record is a YAML document beneath `governance/bootstrap/` with the template fields: identifier, title, status, authority references, acceptance criteria, activated roles, changes, tests, evidence, exact commit SHA, and timestamps. A replay accepts a record only when all required fields are present, the status is `COMPLETE`, every evidence path remains inside the repository and exists, and the recorded commit resolves to a Git commit.

## Exceptions

Bootstrap replay emits one result per record. Any missing evidence, unresolvable commit, malformed record, non-complete status, or out-of-repository reference is a blocking exception. Exceptions are never silently waived: they must be corrected in the record or represented as a blocking finding in cutover evidence.

## Permanent boundary

`bootstrap_scope` is limited to `CONTROL_PLANE_ONLY`. A record declaring Product, feature, runtime-domain, API, UI, billing, identity, or provider work is refused. After a passing cutover, new work must be represented by a governed work-item and is evaluated through `tunner governance` and `tunner work`; there is no unrestricted bootstrap command.

## Required P0-034 verification

1. Replay the checked-in `BOOT-P0-001` metadata and verify its commit/evidence references.
2. Verify that a deliberately malformed or Product-scoped fixture is rejected as a blocking exception.
3. Verify normal governance validation still passes.
4. Record machine-readable cutover evidence and each mandatory role review before the final gate.

This is governance tooling only. It makes no Product feature, deployment, release, or production-readiness claim.