# P2-009 existing-proof first-delivery seam — review candidate v2

This is an artifact-only technical design. It authorizes no implementation, runtime profile or test execution. The original contract packet stays immutable. The actual L01–L03 consent now permits one volatile reveal view with one optional local download request; it selects no count, code format, replacement timing or session effect.

Keep one proof engine and the existing `/authentication/step-up/*` endpoints. Extend the actual applied outcome with a **closed recovery-code value**, not a generic payload, response delegate, cache key or later disclosure route.

## Concrete type and ownership changes proposed

1. Add an internal sealed `RecoveryCodeFirstDelivery : IDisposable`, with no public code getter, auto-record printing, general serialization or deconstruction. It owns bounded plaintext and the exact recovery owner/request/set/profile/context binding. Its one-shot transfer is local to the winning request. Disposal is idempotent and clears buffers it owns; native immutable strings and already-sent network bytes cannot be promised erased.
2. Add internal `RecoveryCodeExecutionValidity?` and the first-delivery carrier to `UserStepUpOperationApplyOutcome`. Only an internal recovery-owned factory can construct that combination for an applied recovery Generate/Replace operation. An existing non-recovery outcome has neither field. A final closed-shape guard rejects a recovery delivery without its matching validity, a foreign scope or an unsupported owner-validity/terminal combination. The initial proposed profile admits no session mutation; an owner requiring other effects has an unsupported profile, not an inferred preserve/revoke default.
3. Replace the internal auto-record `StepUpExecutionResult` with a sealed secret-safe owned result. Its applied success factory receives the carrier only after confirmed commit **and successful transaction disposal**. Safe failure/reconciliation factories cannot accept a carrier. `ToString` and generic JSON serialization cannot expose it.
4. `ForPasskey`/`ForTotp` must preserve every unrelated existing/new validity field and move the same one-shot carrier; replacing one owner-validity field cannot silently erase another field. A separate final closed-shape guard rejects unsupported combinations without dropping evidence or converting recovery validity into the existing causal-source exemption. Typed fallback forwarding returns the complete outcome unchanged. No wrapper stores a payload in an instance field.
5. The shared `ExecuteProofAsync` creates a dedicated `RecoveryCodeProofCompletedResult` only from its actual committed-applied result. Existing `ProofCompleted` remains a **safe-only** factory for every completed/read/reconcile path. Do not add an optional secret argument to a factory also used by reads.

The public owner interface can keep its current return type. New recovery payload types/factories stay internal to Identity; no new generic completion interface, arbitrary owner JSON or Product API is needed. Existing `IUserStepUpBrowserCompletionSource` continues to perform only safe cookie/completion work and carries no plaintext.

## One ownership chain through commit and HTTP serialization

```text
recovery owner local prepared value
  -> applied outcome (same transaction; exact recovery validity)
  -> StepUpApplication pending local guard
  -> SaveChanges + CommitAsync returns successfully
  -> transaction DisposeAsync returns successfully
  -> owned StepUpExecutionResult
  -> existing safe browser-completion hook succeeds
  -> dedicated bound recovery result
  -> IResult.ExecuteAsync writes once, finally disposes
```

Before the final transfer, an outer `finally` owns the carrier across every return, awaited validation, rollback, save/commit exception and transaction-disposal exception. In particular, do not detach it inside a `return` expression while `await using` still has disposal to perform. UNKNOWN commit throws away the carrier even if a later status read discovers that the database committed.

The BFF owns the result while awaiting `CompleteBrowserAsync`. If that hook, sign-out processing, response construction or a pre-write abort fails, dispose the carrier. Never call the safe completed-result fallback with it. In the proposed recovery profile `SignInRequired` must be false; unrelated passkey/TOTP terminal outcomes keep their current behavior and no recovery field.

ASP.NET executes `IResult` after the route handler returns. Therefore `Results.Json` with a payload whose `using` scope has already ended is incorrect. The dedicated result must own it until its own `ExecuteAsync` `finally`; a request-completion event alone is insufficient cleanup. Before writing, check exact frame/owner/request/context/set/profile bindings, unstarted response and cancellation, set no-store/no-referrer, and use a fixed explicit JSON writer. A mid-write disconnect may have sent some bytes: the owner result remains committed and later reads say codes unavailable. No repeat or shown/saved claim follows.

## All method paths use the same winning branch

Password, passkey completion/continuation and TOTP completion/continuation already converge on `ExecuteProofAsync`. A continuation with a still-ACTIVE original proof after an owner rollback can be the first successful application and may carry its own new first-delivery value. A continuation after the owner committed is a read and cannot.

Every `operation.Completed` early return, options-completed reply, password issuance-error race, `CompletedAsync` fallback after a losing race, `ReadNativeCompletedAsync`, `CompletedTotpAsync` and owner original-result read stays safe-only. Native request/attempt/ceremony metadata remains intact, but never acts as a delivery grant.

`IUserStepUpOperationSource.ApplyAsync` and `StepUpApplication.ExecuteAsync(Func<...bool>)` are legacy boolean compatibility paths. They cannot represent this operation. Reject the exact recovery Generate/Replace scopes **before calling the mutation**; do not apply first and silently drop a carrier. The existing Passkey/TOTP boolean wrappers need the same guarded delegation rule, while their typed `ApplyWithOutcomeAsync` fallback passes the complete recovery outcome. Preserve ordinary legacy owners. This is a narrow closed-scope guard, not a new capability registry or bypass flag.

## Existing fallback selection

The opt-in composition currently wraps a prior owner with `PasskeyStepUpOwner`, then `TotpStepUpOwner`, before registering shared step-up. Propose `RecoveryCodeStepUpOwner` as the last explicit opt-in wrapper, using the same scoped owner-factory pattern and immutable protected selection metadata.

All three mutation selectors must clear the other two **pending owner selection** cookies when selecting a new action. Otherwise a stale recovery selection would mask a later passkey/TOTP operation. Retain safe historical result cookies separately. An invalid present selection fails closed; it does not fall through and silently authorize a different owner. Resolver/apply/completion forwarding must agree on the exact operation selected, including when recovery is absent or disabled. Selection cookies never contain codes, verifiers or a delivery lease.

## Final recovery validity does not weaken native proof checks

Existing proof/session/contact/source/expiry/assurance/causal-termination checks remain. A recovery-owned validity record binds actor, operation/context, current recovery profile, issued set, exact expected prior-set plan and the resulting tracked rows/effective intervals. After all awaited owner/native/source checks, validate it at the same final `decisionAt` used by shared proof consumption. An optional recovery policy source in `StepUpApplication` can synchronously re-resolve the exact effective profile and check the closed receipt against transaction-pinned rows. Do not inject `RecoveryCodeApplication` back into `StepUpApplication` and create a dependency cycle; no new generic callback is needed.

The proposed recovery profile does not revoke the proof source. Therefore it gains **no** `allowCausal` exception. Keep the existing passkey/TOTP owner-validity logic and legitimate composite-source behavior unchanged. Bind and revalidate any replacement-review impact facts that depend on code use, such as remaining count, through a separate usage version/facts digest; ordinary code use must not masquerade as a set-lifecycle change. Missing, wrong-owner, stale profile/set/plan/impact facts, expired authority or an unexpected session effect rolls back the recovery mutation, proof consumption and outbox, then disposes the carrier.

## Fixed wire and volatile frontend consumer

Keep `StepUpCompletion` safe. Only the winning response may add a typed `recoveryCodeFirstDelivery` field with a fixed discriminator, original **ownerRequestId**, shared **stepUpRequestId**, operation reference, issued set reference, context/profile versions and bounded code array. The parent recovery view already holds its expected owner binding from StartChange; the shared bootstrap holds the proof request ID. Compare both against the response, not returned fields against each other.

Password, passkey and TOTP parsers must call one recovery-only, same-stack consumer **before** parent completion/unmount/redirect can discard the payload. Immediately before capture, require the original live view/controller/current operation and expected bindings; a canceled or stale receiver discards it. No generic secret field is added to `StepUpCompletion`, and no state/cache registry retains the response. The optional consumer is absent for existing owners. An unexpected payload with no eligible consumer is rejected/discarded safely, not logged or silently accepted as an ordinary completion. Current parsers strip unknown fields, so merely extending server JSON would lose the codes and is not sufficient.

Safe completed/options/reconcile schemas reject a first-delivery field. After body loss the UI shows neutral completed/codes-unavailable status. It does not say shown, downloaded, saved or received. The consented optional download is generated from the same current volatile collection, with at most one local request; it is never another server fetch. Clear the collection on close/unmount/departure and keep the existing TOTP no-download boundary unchanged.

## Supported profile candidate, not a production selection

Propose one closed crypto shape: native RNG16 bytes -> built-in uppercase hex32; admit at most39 raw input characters, accepting either exactly32 ASCII hex characters or exactly8 groups of4 separated by ASCII `-`, then canonical uppercase hex32. This represents128 random bits after canonicalization. Reject other alphabets/Unicode/whitespace/ambiguous separators and do not copy the pinned Identity default47-bit format. Count canonical uniqueness in transient generation with a bounded retry budget; salted verifier uniqueness is not code uniqueness.

Use native IdentityV3 PBKDF2-HMAC-SHA512, native16-byte salt/32-byte subkey, purpose/Account/set/entry/format-bound input and explicitly supplied hash iterations. Before native Verify, bound the encoded size and allow only the exact supported marker/PRF/salt/subkey/iteration profile. Configured IterationCount is not a verifier-cost cap; synchronous PBKDF2 is not canceled by a surrounding stopwatch. Count/iteration/scan/retry/concurrency/time limits must be explicit measured owner inputs, never arbitrary production defaults. This proposal claims no test execution, FIPS status or deployed NIST compliance.

Propose one replacement *mechanism*: atomically retire an exact server-derived expected list of prior owned sets at the same proof/issuance commit. Generate has an empty retire list; Replace requires the current policy's exact nonempty list, lifecycle versions and admission/cardinality. Deferred/first-use retirement and session mutation are unsupported in this first adapter unless separately refined. This does not select the owner's replacement timing, prior-set population or session policy: an absent/unselected/unmatched profile leaves that capability disabled. Current policy must explicitly admit the supported mechanism before use.

## Consumption finality

Refine the v1 low-level consumption signature into one Identity-owned `ExecuteConsumptionOutcomeAsync` transaction boundary, analogous in finality to existing owner executors but not another step-up engine. It requires clean entry: reject an existing transaction, foreign context or uncommitted tracked changes, then create the single owner transaction itself. A narrow internal `IRecoveryCodeConsumptionOwner` supplies trusted admission, applies its local effect and supplies exact final-currentness evidence using that same context/transaction. No generic response/authority delegate or public code-validity endpoint is introduced.

The owner admission binds Account, owner operation/correlation/context, selected set/lifecycle and policy/effective intervals. Admission acquires the complete existing common owner/Account/(when relevant)session lock set **before** recovery set/entry locks. Apply cannot subsequently acquire an earlier-order lock or perform network/provider I/O. The owner re-reads its **actual existing state**, then returns a closed receipt binding the same DbContext/current transaction identity, operation and the versions/intervals/tracked mutations/results it owns. After all awaited owner work, its pure final-currentness check and the recovery policy/set/entry checks run at the last decision instant. The executor cannot infer owner workflow truth from a supplied ID or elapsed time. A claim is provisional until code use, the dependent owner mutation/result and outbox commit in this same transaction. Wrong/inactive transaction identity, owner rejection or final drift rolls back everything.

P2-009 creates **no RecoveryCase table or production fake-case workflow**. P2-011 will own RecoveryCase and implement this trusted owner seam when its workflow exists. Tests can bind/lock the real existing Account state and use actual owner/outbox mutations to prove success, rollback, currentness and duplicate lineage; no speculative case persistence is needed. Different-code concurrency follows explicit owner admission; lifecycle version and per-entry usage remain separate. Same-operation duplicate reconciliation returns safe original outcome only; a consumed code is not a fresh proof. Do not require an already-held strong-factor session, mint a session, or silently replenish codes. Full011/016 effects remain owner orchestration.

Backend/domain/profile and public boundary tests can be prepared independently of UI consent after actual native implementation eligibility. Actual visual/download work also needs the recorded consent, committed source review, approved runtime inputs and the recovery-only private QA boundary. No source work or resources are authorized by this design.
