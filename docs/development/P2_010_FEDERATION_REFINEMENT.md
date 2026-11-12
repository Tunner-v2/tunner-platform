# P2-010 federation refinement

Status: bounded engineering contract proposal in REFINEMENT. This document changes no authority, production policy, runtime capability or public Product API. Native work-start has not been claimed. Google and Microsoft are the only initial providers; GitHub remains future scope.

## Authority and existing capabilities

FRD17 sections 2–8, 11, 13 and 16, catalog32, ADR-0006 and PDR-0011 resolve the domain semantics. The recorded AMD-0005 pass found no new Product decision gap. The six frozen preparation inputs and their original identities are retained under governance/evidence/p2-010-federation-20261112/preparation/. Startup role PASS means suitability for refinement, never feature acceptance.

Identity owns Account/contact/link/session/proof state. Reuse the existing User BFF, RegistrationApplication and Personal Information/consent machinery, AuthenticationGrant/ticket store, TOTP required-assurance continuation, shared StepUpApplication, effective-policy resolver, Account locks, expected-version guards and transaction/outbox. OpenIddict EF registration is not an external OIDC client or the P2-014 authorization server.

## Closed provider identity

A trusted versioned provider-registration profile identifies Google or Microsoft, its stable Tunner namespace ID, exact configured client registration, validated issuer set/explicit issuer equivalences, audience, origin, callback, secret reference, current effective interval and evidence. Namespace identity survives secret rotation and unrelated policy revisions. Changing client identity or identity-bearing issuer mapping requires an explicit migration/link operation; it cannot rewrite existing immutable keys.

The normalized key is the stable registered namespace ID plus the validated immutable subject. The existing ExternalIdentityLinkRow.Provider stores the registered namespace ID; Subject stores the exact validated subject. AccountId, Provider and Subject remain immutable. Do not trim, case-fold, parse as an email/number, truncate or normalize a subject into another value. Bounds and allowed syntax come from the selected supported provider profile and must reject before storage. Display provider names, mutable email/preferred_username, domain hints, tenant display names and client secrets are not keys.

The profile must prove that the validated issuer/audience/client context belongs to that namespace. Microsoft pairwise subject is not silently replaced by oid. Google issuer aliases are admitted only by explicit validated profile mapping. No wildcard/common-tenant issuer inference or unregistered client fallback is supported. Existing ambiguous legacy namespace records require explicit migration evidence; they are never rekeyed by email.

## Attempt, callback and evidence

One Identity-owned attempt has stable request/operation/correlation identity, purpose (SignIn, Registration, Link or OperationProof), exact profile/namespace, opaque browser binding, created/expiry bounds and an allowlisted local continuation reference. Link/proof additionally bind the real Account/session, exact owner operation/context, source link and expected versions. Browser input chooses only an enabled fixed provider selector and an original safe request identity; it cannot supply policy, issuer, subject, Account, assurance, freshness or arbitrary return URL.

The existing official ASP.NET Core confidential OIDC client is the preferred protocol mechanism under Doc01 section16, subject to exact supported package/profile qualification. It owns state, nonce, PKCE, authorization-code exchange and signature/issuer/audience/time validation. Do not copy JWT/protocol verification or build a generic authentication framework. No downstream API/offline scopes or persisted tokens are needed by this authentication slice.

Start is same-origin/HTTPS/CSRF protected. The callback is an exact isolated protocol endpoint, not a general exception to ordinary mutation controls. Its selected profile must qualify form_post interoperability and short-lived framework correlation/nonce transport; ordinary User/Admin authentication cookies remain protected by their existing rules. No middleware-wide origin/CSRF bypass, unbounded callback body or application query-carried token is allowed. Exact endpoint paths and cookie settings are engineering inputs to the reviewed protocol adapter, not values supplied by the browser.

Callback processing resolves and consumes only the exact original unexpired attempt and purpose. Protocol success produces one closed validated-evidence value: namespace, issuer, subject, audience/client binding, attempt identity, verified contact facts if actually established, authentication time/method evidence if actually established, profile/version/effective interval and verification provenance. Raw tokens, authorization codes, provider responses and claims bags never become domain DTOs, cookies, events, logs or test receipts. The external handler cannot directly issue an ordinary Tunner authentication cookie.

Provider network work finishes before the local owner transaction. The final locked decision rechecks attempt ownership/purpose, profile and source currentness, Account/contact/session restrictions, exact expected versions and policy at the last decision instant. Missing/ambiguous/stale evidence fails closed. A duplicate callback reads the exact safe original result; UNKNOWN never automatically replays a link, registration or session mutation. Return to the original allowlisted clean canonical screen; P2-014 retains ownership of Product return/grant semantics.

## Registration and collision

An existing active exact link resolves its one Tunner Account; a mutable provider email cannot redirect it. Unlinked provider evidence enters a narrow typed federation intake in RegistrationApplication. It must share the existing registration/contact/Personal Information/eligibility/consent completion mechanics, operation lineage and restrictions. Do not invoke the password-only RegisterAsync with a fabricated password or make ProtectedCredential a bypass flag.

A provider email becomes verified-contact evidence only when the supported adapter proves the relevant verified-email semantics. Microsoft email/preferred_username alone is insufficient. Otherwise use Tunner contact proof. Account activation/session issuance remains subject to all applicable registration and current authority gates.

A verified-email collision produces a safe original continuation requiring authentication/recovery of the existing Account and explicit confirmed link. It creates neither a duplicate Account nor an automatic link. Public response behavior cannot enumerate arbitrary existing Accounts. Provider authentication, existing-Account authentication and final link consent are separate evidence.

## Link and unlink finality

Link binds the real authenticated Account, exact validated external key and reviewed impact. The existing shared fresh proof engine authorizes the operation; explicit link confirmation remains required. Under the same ordered Account/link locks, recheck unique external ownership, expected versions, attempt/profile/evidence and independent authority. Link, exact safe operation result, proof consumption and safe outbox commit together. Contention produces one owner and safe original-result reconciliation.

Unlink targets the exact current link/version and uses the same current operation-bound proof. A typed trusted alternatives decision binds the current Account/version, target/version, complete relevant authentication/recovery facts digest, selected policy/evidence and effective interval. Re-resolve the same decision from immutable original facts at the final instant after awaited work. Active policy selects the required remaining posture; no fixed count, last-password-only rule or assumed recovery capability is introduced.

Add a minimal versioned terminal unlink lifecycle and original-result lineage to the existing link resource. Preserve the original immutable namespace/subject/Account and historical evidence. Do not physically delete/reassign a link to hide unlink history. Re-link semantics and identity ownership after unlink must be made explicit in the reviewed lifecycle before that behavior is implemented; this proposal does not select reassignment/recycling.

Session effects remain explicit supported owner-policy inputs. Unlink cannot claim to end all Tunner/Product sessions by default. Security events carry stable references and safe metadata only.

## Fresh proof for passwordless Accounts

TODO-TUN-P2-010-STEPUP001 remains required feature scope. Reuse StepUpApplication with one closed federation proof-source/currentness extension, not a password/passkey label or second proof engine. Evidence binds the original current Account/session/link, exact operation/context/purpose, verified provider authentication time/method and profile/assurance policy. No caller-provided timestamps or reusable proof key enter the browser.

A new token, iat, redirect, consent or account selection is not fresh authentication. Optional auth_time/amr/acr and challenge support must be qualified for the actual Google/Microsoft client/account profile. Microsoft prompt=login or OIDC max_age semantics cannot be assumed to transfer to Google. Missing/stale/unknown evidence yields no federated proof; another actually eligible existing native method can serve the Account without forcing a password. Final proof-source, local link, policy and Account/session authority checks remain independent.

## Concrete next slice and proof

After exact namespace/lifecycle/protocol and package review, current context and native READY/work-start eligibility, the first independent backend slice should enforce registered namespace/subject identity and history-preserving exact link mutation with public synthetic evidence. Include the real persistence boundary, unique ownership, source/version/currentness and safe original results; do not present a DTO-only placeholder as completed federation.

Required vectors: same subject under wrong issuer/client/namespace; email change and same-email different subject; collision without authentication or consent; missing/stale proof; callback state/nonce/PKCE/purpose/replay/mix-up; concurrent ownership/link/unlink; final alternatives/source drift; rollback/cancel/UNKNOWN; last policy-required path; no raw token/PII diagnostic or outbox disclosure. Existing password/passkey/TOTP and registration consumers require affected-source regression. Live provider/private tests need their separately qualified finite boundary.

Before affected frontend edits, resolve the exact inventory items, existing approved Login/Sign-in Methods/modal/proof references and any material scoped adaptations. Preserve closed-scope RV lineage. Existing provider-placeholder or P2-009 consent does not approve new P2-010 states. URL/VAL/UX-FID evidence remains per applicable item.

## Remaining engineering qualifications

Exact stable GA OIDC package/version, dependencies/license/security/support and supported callback/fresh-evidence profiles are not yet selected. No packages are installed. The official guidance was re-read on 2026-11-12; an exact NuGet10.0.13 page was inaccessible through the browsing tool, so package availability/security was not confirmed and no version claim follows. Existing2026-11-11 provider research remains historical preparation.

Re-link ownership after a terminal unlink and the complete bounded federation-registration intake need the next closed contract review. These are engineering refinements within existing authority, not a new Product decision or permission to invent behavior. Production issuer/client/account-type scope, secrets, assurance/lifetimes/effects, processing/retention and published content remain explicit owner inputs; absence leaves the capability disabled.

Source references: docs/authority/current/development/01_Engineering_Technology_Foundation.md section16; docs/contracts/authentication-session-contract.md; docs/contracts/operation-step-up-contract.md; src/Modules/Identity/IdentityRows.cs; IdentityModelConfiguration.cs; IdentityMigrationSql.cs; RegistrationApplication.cs; AuthenticationApplication.cs; StepUpApplication.cs; TotpPrimaryMfa.cs; src/Tunner.User.Bff/TunnerAuthenticatedUserBffHost.cs.

Primary native-client guidance: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0 . No provider discovery, JWKS, credential, authentication, package install or external write was executed.
