# Local Governance Orchestrator

TUN-P0-028 provides `tunner work start`, `context`, `run`, `validate`, and `handoff`. It is a coordinator only: it cannot mutate work state, execute Product actions, merge, or release. Each command consults governance; refusals are scoped to the requested work item.
