# Tunner governance and context tooling

The standalone .NET 10 control plane uses repository-native governance records. Status, validation, gate, transition and close-check commands are read-only. Revision create and child-create allocate new governed records under the existing governance layout. Context commands write generated retrieval packs.

## Startup

Run from the governed repository root:

~~~powershell
dotnet run --no-restore --project src/Tunner.Governance -- authority verify
dotnet run --no-restore --project src/Tunner.Governance -- governance status
dotnet run --no-restore --project src/Tunner.Governance -- governance next
dotnet run --no-restore --project src/Tunner.Governance -- context build
dotnet run --no-restore --project src/Tunner.Governance -- context verify
~~~

Use --repository before the command for explicit root resolution. Startup uses context build with no --current option. It selects eligible Revision work first, otherwise eligible ordinary work or a read-only current-state pack.

## Revisions

RV is approved post-closure work lineage. Historical milestones stay CLOSED and original work items stay DONE. REOPENED remains available for corrections within the active acceptance/release cycle. Existing reopen history is preserved.

The following commands are available; creation against P0 is outside this synchronization task and requires separate authorization:

~~~text
tunner governance revision create --scope <original-id> --type <approved-type> --title <title> --reason <reason> --impact NON_BLOCKING --authority-ref <verified-authority-path> --acceptance <criterion>
tunner governance revision show <revision-id>
tunner governance revision child-create <revision-id> --title <title>
tunner governance revision transition check <revision-id> <destination>
tunner governance revision close-check <revision-id>
~~~

Original TUN-prefixed work-item records resolve to the canonical identity stem. Allocation reserves current and Git-historical identifiers, uses an exclusive local mutex and arbitrary-precision sequence values, and formats at least three digits. Existing IDs are never recycled. Creation writes PROPOSED state; lifecycle checks do not approve or mutate progression.

The approved lifecycle is PROPOSED → APPROVED → ACTIVE → VALIDATION → PRODUCT_ACCEPTANCE when required → CLOSED, with BLOCKED, CANCELED and SUPERSEDED side states. Side-state transitions require their governed record; no transition graph is invented. An explicit product_acceptance_required: false declaration retains the existing technical-only acceptance rule.

Records declare authority, decisions, impact, affected scope, acceptance criteria, prerequisites, children, gates, roles, evidence and Git lineage. Accepted ADR/PDR mirror references resolve directly; no duplicate Product decision is needed. NON_BLOCKING work leaves unrelated eligible work available. Blocking classifications affect only the declared scope and its dependent work.

Closure requires committed-state evidence, linked completed children, declared passing gates/reviews and resolved defects. Committed identity fields and closed Revision history are immutable. Revision IDs, storage, schemas and examples are documented in governance/revisions/README.md and docs/development/RV_GOVERNANCE_SYNCHRONIZATION.md.

## Authority and context

Authority verification remains a byte-exact, read-only SHA-256 check using the existing integrity manifest. Current Drive readback provenance is recorded in docs/authority/current/mirror-manifest.json and governance/evidence/RV-SYNC-AUTHORITY.json. The previous integrity manifest and locked baseline are preserved.

Context retrieval follows CORE → TASK → EXPANDED → FULL_AUDIT only when justified. Selected Revision context contains the original scope/evidence, current Revision, immediate previous and parent lineage, applicable current authority, linked findings, declared sources and bounded Git history. Generated CURRENT_STATE, PROJECT_CONTEXT and HANDOFF expose historical state separately, latest relevant lineage, SHA linkage and exact next action. Source and lineage hashes detect stale packs.

## Validation and evidence

Governance validate checks the existing schema catalog and record conventions. Revision records additionally execute nested JSON Schema validation and lineage checks. Existing evidence generate accepts Revision scopes and records revision_ref for Revision-owned work.

~~~powershell
dotnet build Tunner.Governance.sln --no-restore
dotnet run --no-build --project tests/Tunner.Governance.FunctionalTests
~~~

The harness exercises ordinary governance regression behavior and isolated RV identity/lifecycle/schema/context/portability fixtures. No fixture Revision is created in the actual repository.
