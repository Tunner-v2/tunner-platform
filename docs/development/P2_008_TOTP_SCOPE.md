# P2-008 — TOTP lifecycle, MFA and operation proof

Status: implementation and executed local tests are recorded; final governed acceptance requires the committed evidence and native gates. Refinement started from the validated P2-007 handoff `ea85e3c1c4bd3430214f99bd2c9cd7fc250e6f99`, with verified authority, eligible parent003/005/006 readiness, startup role reviews and bounded current context. No production profile or release approval is selected by this scope.

## Authority and ownership

FRD17 and FigJam38 require fresh current-session proof before one-time seed/QR disclosure, an inactive candidate until its first valid code, then versioned active metadata and safe audit/outbox. Exact nodes are recorded in [authority resolution](../../governance/evidence/TUN-P2-008-AUTHORITY-RESOLUTION.json). Disable/reset is a separate governed action with explicit effects. Recovery flow42 permits only the affected-method changes and explicit result; lost-factor restoration belongs P2-011.

P2-008 owns the actual TOTP verifier for three uses: first-code activation, policy-required sign-in MFA through P2-005, and exact sensitive-operation proof through P2-006. A setup-only implementation is insufficient. Required second factor completes before a qualifying final session exists; TOTP is not standalone primary login and enrollment does not impose MFA on every login. Product OAuth requested-assurance orchestration belongs014; recovery codes009 and lost-factor recovery011 remain separate.

No reviewed authority selects a universal single-active-TOTP limit or forced disable-before-new-code replacement. Cardinality, replacement ordering, session/related-factor effects and assurance/context values remain explicit approved profile inputs. Do not derive them from framework `TwoFactorEnabled` or Figma sample copy. Unselected production values are not a missing-contract blocker; unsupported or absent profiles fail closed.

## Reuse and technical boundary

Use native ASP.NET Identity verification, current shared Account/source locks, policy resolution, result recovery, proof issuer/consumer, owner transaction, outbox and existing BFF session authority. Do not build a second identity/session/assurance store or custom OTP cryptography. Native helpers that change security stamps must not silently select Tunner session effects.

Doc06 requires secret-store resolution and references/fingerprints/metadata in application storage. Reuse the approved OpenBao transport, short-lived scoped identity and create-only CAS behind a narrow per-credential immutable-version reference adapter. Engineering deploy-time references are not reused as unvalidated user input. Provider preparation/reconciliation stays outside the Identity owner callback; lifecycle metadata denies a disabled factor immediately, with provider retirement/purge following explicit retention policy. Existing Data Protection purposes do not establish a long-lived seed encryption contract. No new KMS or permanent root-token bootstrap is selected.

Use a bounded set of short-lived used-code verifiers per authenticator and immutable seed generation, shared across activation/MFA/operation proof. Reuse the existing framework hasher with explicit supported cost; no new hashing key or OTP math. Check every live entry under the shared lock: a single last-code value permits A→B→A replay. Retention covers the native full acceptance horizon and declared clock bounds; do not claim the server counter is the matched counter. Reject capacity exhaustion rather than evict an unexpired replay claim.

For operation proof, acceptance/replay claim/proof issuance commit together; existing proof consumption/owner mutation/outbox commit together afterward. On owner failure, continue only the same active proof and exact original operation. For primary MFA, code acceptance/final session/result/outbox commit together using a protected pending-authentication record, not a provisional live session or a StepUpProofRow requiring a session FK. Refresh persisted source/version/policy/actor eligibility at the established final boundaries and preserve narrow causal self-disable behavior.

Provision exactly one seed for the original pending operation. Unknown outcomes reconcile its existing immutable reference/status; they never silently regenerate or re-reveal it. Activation is separate from one-time disclosure. No seed/private OTP appears in Identity tables, Admin/Support views, source/config, URLs, logs, events, screenshots or evidence.

## Visual and form boundary

Reuse actual approved Figma83:2 management,83:117 setup and83:298 ready at1440×1100, the shared Account shell, code field83:287 and validation70:1257. Prototype person/provider/date/key/internal notes are not runtime facts or copy. The generic46:150 request-new-code wording is not correct for offline TOTP.

[Read-only reference coverage](../../governance/evidence/TUN-P2-008-UI-AUTHORITY.json) records proposed canonical management/setup/ready paths plus the independent login-MFA challenge. Paths remain proposals until registered and implemented. Same-screen sensitive-action code proof stays in the shared dialog. Before frontend code, resolve owning inventory entries and guards; preserve existing callbacks and Product-origin contracts.

The original material gaps tracked by `DEF-TUN-P2-008-UI-001` are covered by the actual [008-V01–V05 consent](../../governance/evidence/TUN-P2-008-VISUAL-CONSENT.json) and [scoped factor-picker consent](../../governance/evidence/TUN-P2-008-FACTOR-PICKER-CONSENT.json). Final per-item evidence and reviews still govern implementation acceptance. Owner-held Figma backfill remains separately tracked by `TODO-TUN-P2-008-DESIGN001`; no007 approval is borrowed. Figma/Drive/FigJam stay read-only.

All forms use shared typed schemas/field and form summaries with independent backend validation, canonical code handling, first-invalid focus, safe sensitive-value handling, pending/disabled guards and no duplicate submission. Reuse cohesive formatters and primitives; retain domain-owned rules and server authority. Use a local QR renderer under package governance if an approved compatible one is absent; no external QR service receives a seed.

## Execution slices and acceptance

1. Refine supported profiles, owner-bound versioned seed adapter and replay/clock/resource contracts; preserve existing secret-store consumers.
2. Implement fresh-proof enrollment, one-time disclosure, inactive pending lifecycle and native first-code activation with idempotent reconciliation.
3. Integrate the same verifier/replay primitive into policy-required primary MFA and exact operation proof; preserve existing password/passkey consumers.
4. Implement governed disable/reset with explicit selected effects, source freshness, concurrency and exact terminal result recovery.
5. Implement faithful approved layouts and genuinely approved state adaptations; all independent screens have guarded canonical routes and refresh/history behavior.
6. Commit the complete work-owned behavior array before meaningful candidate validation; execute current native/provider/browser/visual/security regressions and final role reviews before local readiness.

Required evidence includes invalid/expired/wrong-scope/replayed codes, A→B→A and concurrent acceptance, source disable between issuer and executor, provider timeout/create-only reconciliation, no repeated secret, no early active state/session, both configured effects, rollback, immutable migrations and old password/passkey regressions. Run URL001–006, VAL001–008 and UX-FID001–008 for every applicable new item. Keep exact test SHAs and distinguish fixtures, real native flows and Product/release approval.

Visual proposals use nonfunctional public examples. Real browser tests prevent live seed/QR/OTP from runner snapshots, traces, Markdown contexts or assertion diffs through the bounded private child and native oracle; all original checks remain required. [Primary-source R&D](../../governance/evidence/TUN-P2-008-RD-SOURCES.json) documents the native and vault constraints.

At tested commit `947f8b090b4f74a474a71e47badcb595424bd734`, all three finite HTTPS/native-provider journeys pass: 70 browser predicates, 63 coordinator predicates and 15 persistence observations, including verified owned-resource cleanup. Public TOTP tests pass43/43, contract tests29/29 and TypeScript passes. UI005 has an eight-case expected-red baseline and an unchanged green rerun; the correction refreshes authoritative factor metadata after same-path cancellation while retaining disabled actions and logical focus. Four fresh public composition/keyboard cases supplement the retained188 historical renders and194 resolved accessibility observations; no approved JSX, CSS or copy was redesigned.

Native Identity260, Architecture237 and Installer113 results retain their actual earlier execution SHAs and explicit current source-equivalence witnesses. They are not relabelled as947 executions. Final role, UI and work-item gates are recorded in the governed validation evidence. Production policy/content activation, physical data-retirement work, Edge/WebKit qualification, manual source-design backfill, protected-main integration and release remain separate governed scopes. An unattributed Windows cache observation remains unread and preserved; local evidence makes no exhaustive global-host filesystem claim.
