# P0-RV002 committed readiness correction

This revision preserves the original CLOSED P0 and P0-RV001 records. It corrects current own-item and dependency readiness consistently, requires committed ownership envelopes for repository changes in all active milestones, rejects empty ownership, binds milestone reviews to a scope fingerprint and ancestor commit, labels findings with their actual milestone, and generates a concrete recovery command when no ordinary item is actionable.

Validation covers missing state, absent ownership evidence, post-validation source edits, future-milestone readiness and stale milestone reviews. Original closed milestones retain historical compatibility; new revision work requires its own committed evidence. No Product or external resource semantics are introduced.

Candidate validation is recorded in governance/evidence/P0-RV002-VALIDATION.json. The twelve pre-existing generated context/project paths remain excluded from commits. P1 reconciliation is separately owned by TUN-P1-023.
