# P2-004 registration and contact activation

This slice implements Account registration, private operation recovery, email proof and resend in Identity and the User BFF. The existing 18+ evaluator, effective-policy resolver, EF Identity store, shared outbox, BFF HTTPS/origin/antiforgery boundary and @tunner/ui are reused. Product access and authenticated sessions are separate owning work.

## Server composition and activation

The User BFF remains health-only without IRegistrationPolicySource. An owner must explicitly register IdentityPersistenceDbContext, IRegistrationPolicySource, IRegistrationPasswordBlocklist, IRegistrationNotificationDispatcher, IRegistrationPresentationSource and RegistrationNetworkPartitionKey. Incomplete opt-in composition refuses startup. The application also requires the existing configured Bff:BrowserOrigin and production Data Protection/key custody. A policy catalog resolves exactly one ACTIVE, approved effective REGISTRATION version using the P1 resolver. Required processing, retention, notice, age, hashing, proof, abuse and browser-context values have no production defaults. Published content must contain all required semantic keys with matching locale and publication provenance; legal/support URLs must use credential-free HTTPS. No browser can supply these providers or approve a policy.

Build the pinned web workspace before building/publishing the User BFF. Its packaged registration distribution is copied under wwwroot/registration. Exact static routes use a restrictive self-only CSP, no-referrer policy, no-store and nosniff. Published configuration makes /registration available; the form refuses processing when policy/content is missing or ambiguous. No live database/container or production provider was activated by this change.

## Browser REST contracts

All requests stay on the configured User BFF origin over HTTPS. POSTs require the framework X-Tunner-CSRF header/cookie and a separate protected __Host-Tunner.Registration browser-context cookie (Secure, HttpOnly, SameSite=Strict, root path, explicit policy lifetime). This cookie grants no authenticated identity. Actual network peers are HMAC-partitioned with an explicitly supplied server secret; forwarded caller/network claims are not trusted.

| Route | Input and result |
| --- | --- |
| GET /registration/options | Current public policy lengths/notice versions, server time, published copy/links and antiforgery token; establishes an expiring anonymous context. No contact query. |
| POST /registration/register | idempotencyKey GUID, email, password, dateOfBirth ISO date, termsVersion, privacyVersion, acknowledgedNotices. Stable browser/key identity returns the same private operation. |
| GET /registration/status/{key} | Recover only the current protected browser's original registration key after an uncertain result, without reposting DOB/password. Independently rate limited. |
| POST /registration/verify | operationId GUID, fresh idempotencyKey GUID, expectedVersion integer and code. Proof and uniqueness are checked in the canonical transaction. |
| POST /registration/resend | operationId, fresh idempotencyKey and expectedVersion. Replays the same mutation result; policy cooldown/budget, expiry and optimistic version remain server authority. |

Operation results carry operationId, server correlationId, status, version, expiresAt and resendAfter. AccountId is disclosed only on successful proof/activation. Pending/new/existing paths return the same REQUIRES_ACTION response contract; no contact-existence endpoint, password reset or implicit link is introduced. Framework password hashing runs before existing/new lookup. This reduces timing differences; it is not a claim of mathematically constant end-to-end timing. Problems use safe error_code/correlation_id/operation_id/current_version projections, never request details. Malformed bodies and content-length/chunked overflows are bounded before application processing. Existing/new paths use the same policy abuse controls.

## Transaction and credential protection

Age failure stores derived eligibility failure/provenance only, with no Account, contact, framework credential, outbox event or notification. Raw DOB has no persistence column. Pending accounts have no active password. Each browser operation protects its own proposed IdentityV3 password hash and numeric proof under separate Data Protection purposes. Competing pending registrations share the canonical Account but cannot activate each other's proposed credential. Verified-email binding, Account ACTIVE state/version/history, winning credential, contact evidence, completed operation and AccountActivated outbox record commit together. No login/session/Product state is created.

Database advisory locks serialize network buckets, private operation mutations and normalized-email ownership across processes; EF concurrency tokens and the existing unique verified-contact constraint remain final guards. Ambiguous legacy pending contacts never cause silent ownership selection. Historical/unverified contacts on active accounts do not reserve arbitrary registration ownership. Mutation-result rows retain safe idempotency evidence. Forward migration creates three Identity tables; rollback refuses a populated registration operation table with permanent integrity SQLSTATE 23514.

## Notifications, retention and owning follow-on work

The canonical transaction writes safe reference-only AccountRegistered / RegistrationNotificationRequested / AccountActivated outbox envelopes. The trusted dispatcher receives protected-scope address/proof only after commit, never through HTTP/events/diagnostics. Its stable DeliveryId must be deduplicated. Queue acceptance does not assert provider delivery. Typed queue unavailability leaves the committed outbox available for recovery. DispatchNotificationAsync is a trusted consumer entry, not a public route, and refuses retired/expired/completed material.

PurgeExpiredMaterialAsync takes an explicit bounded batch size, clears expired pending address/normalized-email/proof/credential material and expires abuse buckets while preserving canonical Account and safe evidence. Purge timing and retry scheduling must use the approved operational retention/security policy. The existing TUN-P2-016 owns production notification/audit consumer and scheduler integration; TUN-P2-005 owns authenticated sessions/sign-in. These are explicit planned dependencies, not claims of production delivery or complete P2 activation.

## UX, content and limits

The existing editable Figma registration/verification/result frames were aligned with FRD17/31 before implementation: DOB replaces full name, unsupported social/implicit Product-access actions are removed, proof entry is a single accessible field, no policy duration/composition is inferred from screenshots, and the Tunner name has no icon. Shared controls add finite spacious/verification/outline presentations while existing standard metrics remain intact. Existing pinned local Geist/Inter assets are reused; supplied Figma glow/dot/success-circle SVGs retain intrinsic geometry. Programmatic heading focus has no decorative outline; interactive keyboard focus remains visible.

content.proposal.json is DRAFT only. Tests explicitly wrap its values in synthetic fixture publications; no runtime fallback publishes it. Approved publication/legal/policy/provider composition remains a deployment precondition. Successful proof renders Account readiness; no unavailable sign-in button or automatic Product grant is exposed. Expiry/cooldown display advances from server time using a monotonic browser clock; the server rechecks all limits. Password/DOB are cleared after submit, operation recovery uses the original private key, and browser storage/query strings hold no credentials, tokens or raw DOB.

Exact committed validation, failures, role reviews, source hashes and receipts are recorded in governance/evidence/TUN-P2-004-VALIDATION.json. Product acceptance, protected-main approval and existing P1-RV001 container/security gates remain separate.
