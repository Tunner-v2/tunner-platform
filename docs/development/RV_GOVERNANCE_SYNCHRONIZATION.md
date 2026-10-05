# TUNNER RV REPOSITORY SYNCHRONIZATION

Work item: TUN-P1-022. Scope: execution synchronization of already approved RV authority. Current branch: change/TUN-P1-001-platform-foundation-refinement. Initial HEAD: 4c7e83977115232717323da995ea6c8ed9a831ac. No clone or repository move occurred.

## Execution and ownership

Shell and root patch-editor capability probes passed; the temporary probe was deleted immediately and is not committed. Nested patch-editor writes returned a runtime reparse/write error despite normal physical directory metadata. Root patch-created scripts applied the authorized exact edits inside the same repository.

The initial 162 dirty file paths and their raw hashes, remote and local unpushed commits are captured in governance/evidence/RV-SYNC-INITIAL-STATE.json. Every initial path except the authorized AGENTS.md overlap remains byte-identical. Exact task ownership is in governance/evidence/RV-SYNC-OWNED-PATHS.json; staging uses individual listed paths. Previous P1 implementation, P0 reopen records, defects, evidence and context remain excluded. No reset, clean, stash, discard, broad staging or local-work absorption occurred.

## Canonical authority synchronization

The current approved Documents 00, 03, 04, 11, 12, 36, 37 and Development README were fetched from canonical Drive. The entire Pre-P0 1.0.7 package is mirrored: 58 manifest-listed files plus the manifest itself. Every listed package SHA-256 matches the canonical manifest. Provenance/source IDs/timestamps/hashes are in docs/authority/current/mirror-manifest.json and governance/evidence/RV-SYNC-AUTHORITY.json.

Root AGENTS.md, lifecycle/work-item/milestone/gate/evidence/Revision templates and durable project-memory templates are materialized from the current package. The existing integrity format pins the new readback artifacts. The prior integrity bytes are preserved in RV-SYNC-PRIOR-AUTHORITY-INTEGRITY.json. AuthorityApplication.cs is unchanged, and byte-exact verification remains required. No authority record or new AMD was created.

## Approved model and executable behavior

Revision storage is governance/revisions; the executable wrapper is governance/schemas/v1/revision.schema.json with nested validation in governance-record.schema.json. All canonical fields, ten Revision types and four impact classifications are represented. Existing schemas carry optional revision_ref and milestone active/closed lineage links.

IDs follow <milestone>-RV<sequence>, <work-item>-RV<sequence> and <revision>-<child-sequence>. Original TUN-prefixed work items resolve to the canonical stem. Allocation reserves current and historical Git filenames, uses an exclusive mutex and BigInteger, rejects duplicates and never caps sequence count. Committed identities and closed Revision records cannot be rewritten or deleted.

PROPOSED → APPROVED → ACTIVE → VALIDATION → PRODUCT_ACCEPTANCE when required → CLOSED is supported by the existing CLI conventions. BLOCKED, CANCELED and SUPERSEDED are valid side states; no unapproved side-state transition graph is invented. Close-check requires new evidence, committed-state lineage, completed children, declared gates/reviews and resolved linked defects. Explicit technical-only acceptance retains the existing product_acceptance_required rule.

Historical originals remain CLOSED/DONE; Revision create never mutates them or their evidence. Earlier valid REOPENED records remain intact. The initial working P0 record is REOPENED while its committed historical record is CLOSED; status/context show these facts separately, without treating the dirty tree as a global stop. This synchronization does not create or convert any reopen record.

NON_BLOCKING work cannot declare blocking scope and leaves unrelated eligible work available. Blocking impact requires declared affected scope and propagates through explicit/transitive dependency records. Immediate prior Revision linkage records history; it does not invent a prerequisite.

## CLI and bounded recovery

The existing governance namespace supplies revision create, show, child-create, transition check and close-check. Status/next expose Revision state separately from historical milestones. Accepted mirrored ADR/PDR references resolve without duplicate Product decisions.

Context follows CORE → TASK → EXPANDED → FULL_AUDIT only when justified. The selected original/evidence, current RV, immediate predecessor/parent, linked findings/dependencies, applicable current authority and relevant tests/evidence/Git history are bounded. Source and Revision-set hashes detect staleness. Generated CURRENT_STATE, PROJECT_CONTEXT and HANDOFF expose original state, active/latest lineage, reason/type/impact, SHA linkage and exact next action. Durable recovery documents were refreshed without requiring chat history.

Canonical startup is tunner context build. Active source/guidance contains no stale context build --current requirement. Locked historical authority artifacts remain unchanged.

## Baseline ZIP and portability

The locked ZIP was covered by text eol=lf despite being binary. Before the override, filtered hash-object differed from the raw/HEAD blob; no ZIP diff existed. System core.autocrlf is true, core.eol is unset and registered LFS filters do not cover the archive.

The narrow docs/authority/**/*.zip binary override makes filtered/raw/HEAD Git object hashes equal: 88b8f7f7ac91ff58167bbb794e328fbcc089ed49. The unchanged ZIP SHA-256 is c020ddf78065eae94cfb307d68df513774f0df53337d897a30d2641a0b0c22c1. Current mirror text uses explicit LF checkout. Test-TunnerAuthorityCheckout.ps1 checks every authority expectation against committed bytes after the declared checkout filters, without cloning or replacing archives.

## Validation and handoff

The pre-commit governance harness passes with 56 RV assertions; all legacy checks pass. Both governance and application builds pass with zero warnings/errors. Architecture tests pass 18/18; 17 schema entries, source guards, current source registry, documentation, bounded context and fresh recovery pass. Exact committed-candidate validation and SHA linkage are recorded in governance/evidence/TUN-P1-022-VALIDATION.json after candidate commit creation.

The five role reviews are scoped reviews by the executing agent, not independent human approvals. Their structured records and skill hashes are in governance/reviews/REV-TUN-P1-022-*.yaml and TUN-P1-022-ROLE-REVIEWS.json.

Candidate/validated/tested/closure SHA values and final readiness are recorded in TUN-P1-022's git_state and validation evidence. Later metadata-only closure records do not change tested behavior.

P0-RV001 created: NO.
Any actual P0 work-item Revision created: NO.
New AMD created: NO.

Exact next action: run the mandatory startup commands and governance next, then obtain separate authorization for an actual P0 Revision scope. Existing dirty reopen/P1 work remains separately owned. This synchronization stops at RV governance readiness.
