# Local adaptive context tooling

TUN-P0-029 adds a governed adaptive layer to the manifest-first context pack.

- `tunner context build --mode CORE|TASK|EXPANDED|FULL_AUDIT --token-budget <n> --access-scope <scope>` records a bounded selection.
- `tunner context index build` writes a deterministic disposable discovery index under `artifacts/context-index/`.
- `tunner context escalate <work-item> --from TASK --reason "..."` returns an explicit `INSUFFICIENT_CONTEXT` escalation without implicitly adding sources.
- Repository access scope is recorded separately from prompt selection.
- A budget that cannot include mandatory authority returns `CONTEXT_BUDGET_INSUFFICIENT`; optional artifacts are recorded as exclusions.
- `FULL_AUDIT` indexes discoverable repository files (excluding generated, build, and secret-shaped paths); it does not inject the entire repository into a prompt.
- `tunner context verify` verifies selected source hashes and invalidates FULL_AUDIT output when the indexed source set changes.

The tool is read-only with respect to Product behavior and governance lifecycle state. Generated indexes and packs remain derived artifacts, never authority.