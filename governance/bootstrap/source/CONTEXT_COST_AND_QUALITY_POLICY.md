# Context Cost & Quality Policy

Optimize for minimum sufficient authoritative context, not minimum tokens at any cost and not maximum context by default.

Modes: CORE, TASK, EXPANDED, FULL_AUDIT.

Retrieval priority: explicit links; dependency/symbol graph; changed files/Git history; lexical/path search; semantic retrieval; governed repository-wide scan.

On budget pressure never silently omit authority/blocking rules; prefer locators/excerpts; use only fresh cached summaries; record exclusions; return `INSUFFICIENT_CONTEXT` and escalate if correctness is affected. Record mode, inclusion/exclusion, approximate token/input usage when available, cache hit/miss, stale regeneration, escalation reason, role activation and gate outcome. Cost optimization cannot weaken authority/evidence coverage.
