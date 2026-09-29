> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 30 — Platform Reliability, Change, Disaster Recovery & Automation FRD

## 1. Authority and traceability

Fulfills FigJam **28–35, 123–127A, 136–140**. Platform Operations owns technical health/change/recovery evidence and execution infrastructure. Owner domains retain business finality. Automation invokes typed owner-domain Operations; it never edits business tables directly.

## 2. Platform health model

`ServiceComponent` and `Dependency` are registered with environment/owner/criticality.

Health states:

```text
HEALTHY
DEGRADED
UNAVAILABLE
UNKNOWN_OR_STALE
MAINTENANCE
```

Health is calculated from versioned `ReliabilityBudgetPolicy` and evidence freshness. Health status can block **new external side effects** through owner-domain preflight but cannot rewrite already committed Payment/Notification/Provider state.

## 3. Reliability budget / SLO contract

Every production-critical service/dependency must have an approved `ReliabilityBudgetPolicy` before production activation containing:

- SLI definition/query;
- objective/window;
- error budget;
- burn-rate alert rules;
- freshness threshold;
- owner/escalation;
- customer/internal criticality;
- change-freeze/remediation behavior.

Exact SLO values are deployment/product policy inputs and are not invented in source code. Missing production SLO policy is a **release blocker**, not a developer choice.

## 4. Incident lifecycle

`PlatformIncident` states:

```text
OPEN
TRIAGED
MITIGATING
MONITORING_RECOVERY
RECOVERED
POST_INCIDENT_REVIEW
CLOSED
```

It records affected services/dependencies/capabilities, customer/Product impact, evidence timeline, mitigation operations, external unknowns, recovery verification and follow-up actions.

Technical incident resolution never claims affected business operations succeeded/failed unless owner domains establish finality.

## 5. Diagnostics

Diagnostics are read-only by default and correlation-driven through logs/metrics/traces, queue/job state, provider health, database/replication evidence and owner-domain operation state.

Admin UI cannot expose arbitrary shell/SQL/command execution.

Any remediation is an allowlisted typed operation with authorization, expected state and audit evidence.

## 6. Change / maintenance lifecycle

`PlatformChange` states:

```text
DRAFT
READINESS_REVIEW
APPROVED
SCHEDULED
EXECUTING
VERIFYING
COMPLETED
ROLLED_BACK
FIX_FORWARD_REQUIRED
FAILED
CANCELED
```

Readiness includes:

- deployment artifact/hash/version;
- dependency/migration compatibility;
- backup/restore readiness where data affected;
- rollout/rollback or fix-forward plan;
- observability/health checks;
- customer/internal communication if needed;
- approval/SoD/maintenance window policy.

A rollback is never promised when a migration/change is explicitly forward-only; in that case the plan must define fix-forward/data restoration behavior.

## 7. Async execution foundation

Execution resources include:

- `OutboxMessage`;
- `EventSubscription`;
- `EventDelivery` / consumer inbox;
- `BackgroundWorkItem`;
- `WorkerLease`;
- `DeadLetterItem`;
- `DurableTimer`;
- `Operation` / state machine.

Rules:

- owner commit and outbox persist atomically;
- publisher confirm / broker transfer is retriable without redoing owner mutation;
- consumer inbox dedupes event/subscription/handler generation;
- worker lease prevents uncontrolled concurrent execution;
- dead-letter/redrive preserves original business/event identity;
- replay never creates a new original business side effect.

## 8. Retry taxonomy

Failures are classified, for example:

```text
TRANSIENT_SAFE_RETRY
TRANSIENT_REQUIRES_IDEMPOTENCY
DEPENDENCY_UNAVAILABLE
BUSINESS_RULE_BLOCKED
AUTHORIZATION_OR_POLICY_BLOCKED
VERSION_CONFLICT
UNKNOWN_EXTERNAL_STATE
PERMANENT_INVALID
BUG_OR_INVARIANT_VIOLATION
```

Retry middleware cannot override domain classification. `UNKNOWN_EXTERNAL_STATE` routes to reconciliation, not blind retry.

## 9. Durable timers/scheduler

Timer record contains due time, timezone where relevant, policy/version, operation/resource reference, concurrency key, status and attempt lineage.

Timer states:

```text
SCHEDULED
CLAIMED
EXECUTING
COMPLETED
CANCELED
FAILED_RETRYABLE
DEAD_LETTERED
```

Timer firing means “evaluate due work now,” not “assume the business transition should happen.” The handler revalidates current versions/policy before operation.

## 10. Disaster recovery contract

`DataProtectionContract` per resource/service/environment defines:

- authoritative data stores;
- backup method/frequency policy;
- encryption/access;
- RPO target;
- RTO target;
- restore order/dependencies;
- external-provider reconstruction limitations;
- restore validation tests;
- failover/failback behavior;
- evidence owner.

Exact production RPO/RTO must be approved before production release. Missing values block P8; code must not embed guessed values.

## 11. Backup/restore

PostgreSQL backup/restore is tested against actual supported engine/version. R2 object references/content recovery is tested separately. Redis/RabbitMQ are not treated as sole business truth and must be reconstructable from authoritative persistence/outbox/inbox policies where appropriate.

Restore test verifies:

- schema/migrations;
- referential/integrity checks;
- ledger balance/invoice/payment identity;
- outbox/inbox consistency;
- object checksum/reference;
- secret bindings re-established without copying secrets into backups improperly;
- read models can rebuild.

A backup existing is not proof of recoverability.

## 12. Workflow orchestration

Default implementation is typed/compiled application orchestration. Generic JSON DSL is introduced only for repeated configurable patterns that justify it.

A workflow definition, if used, contains:

- schema/version;
- allowed typed operation IDs;
- step ordering/dependencies;
- timeout/retry/compensation policy refs;
- HumanRequired conditions;
- evidence/finality rules.

It cannot contain arbitrary code/SQL/shell.

## 13. Governor

Governor continuously evaluates conformance:

- missed required step;
- stale policy/config/version;
- expired pending case;
- workflow/deadline drift;
- reconciliation backlog;
- integration delivery health;
- required evidence missing.

Auto-remediation is allowed only when a versioned policy declares a deterministic typed operation safe. Governor starts a **new governed operation**; it never edits source rows.

## 14. Non-human principals and AI

Automation/service/AI identities are first-class principals with explicit scope, environment, credential/attestation and audit identity.

AI:

- may assist classification, summarization, diagnosis and draft recommendations;
- cannot be sole authority for Payment/Identity/Finance/Access/Tax/legal finality;
- can invoke only allowlisted tools/actions under policy;
- tool execution records model/prompt/tool/policy/version evidence where required;
- deterministic fallback exists for critical workflows.

## 15. Observability

Applications emit OpenTelemetry logs/metrics/traces with correlation IDs. Sensitive data is minimized/redacted by classification policy.

Local/test reference stack: Grafana OTEL-LGTM. Production backend is replaceable; application code depends on OTLP/OpenTelemetry contracts rather than vendor APIs.

## 16. Release/rollback operational behavior

Deployments use immutable artifact promoted across environments. Health verification gates promotion. Database migration must declare backward/forward compatibility window and rollback/fix-forward strategy.

No environment-specific long-lived branch is the deployment authority.

## 17. UI requirements

Platform Operations Control Center includes:

- overall health dashboard;
- dependencies and freshness/SLO evidence;
- incidents;
- maintenance/change operations;
- queues/workers/timers/DLQ;
- event subscription/delivery health;
- backup/restore/DR evidence;
- reliability-budget status;
- governed remediation actions.

No generic infrastructure command console.

## 18. Acceptance cases

- subscriber failure does not rollback committed owner-domain transaction;
- broker outage after DB commit leaves outbox pending and later publishes without repeating mutation;
- duplicate delivery is inbox-deduped;
- timer fires after resource changed => handler revalidates and does not blindly apply stale action;
- UNKNOWN external operation never auto-redrives as new side effect;
- restore rehearsal proves authoritative state plus object integrity and rebuildable projections;
- Redis outage does not corrupt business truth;
- Governor cannot directly update Payment/Invoice rows;
- AI tool invocation outside allowed scope is denied/audited;
- production release is blocked when required SLO/RPO/RTO policy is absent.
