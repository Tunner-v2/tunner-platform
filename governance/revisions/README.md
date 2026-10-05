# Post-closure Revisions

This directory stores canonical Revision YAML records. No real Revision is created by TUN-P1-022.

Use `tunner governance revision create`, `show`, `close-check`, and `transition check`. Child work items stay in `governance/work-items/` and link their owner through `revision_ref`. Status projections derive active/closed Revision references without rewriting historical originals. Existing milestone reference arrays may also declare and validate this linkage.

IDs are monotonically allocated per original scope using unbounded integers and a local allocation lock. The prefix `TUN-` on original work-item records is resolved to the canonical `P0-004-RV001` display identity. Original IDs remain unchanged. Git history reserves previously committed identities and prevents deletion, identity mutation, and changes to closed Revision records.
