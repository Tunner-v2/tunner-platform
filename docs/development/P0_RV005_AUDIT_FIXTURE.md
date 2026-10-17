# P0-RV005 audit fixture correction

The P2-004 GitHub security job passes all platform/installer/observability tests but fails the audit harness assertion that the post-validation recovery names its remaining acceptance/release authority. A current local run reproduces the same failure. The test copies the live governance backlog and deletes six fixed revision files to imply no eligible work. Later backlog evolution invalidates that assumption.

Only the disposable copied fixture is adjusted. Existing terminal history is preserved. Nonterminal work/revision records are marked CANCELED within that fixture to model the intended no-eligible-work branch; an explicit native-next precondition verifies there is no actionable work. All existing source-integrity, supplemental-registration, linked-defect/evidence, dirty-project preservation, acceptance/release and stale-context assertions remain. No production gate or domain rule is weakened.

P0-RV005 is append-only corrective lineage after closed P0-RV004, with technical-only acceptance. Registration behavior remains anchored to its existing validated commit and is unchanged. The current P1 image findings remain genuine release blockers. Exact committed validation and role reviews precede closure.
