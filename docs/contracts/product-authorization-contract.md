# Product authorization and revocation contract

Status: P2-014 implementation contract; native Product and browser qualifications recorded; final affected-consumer, UI and acceptance gates pending.
Authority: current FRD17 §9, baseline FRD19 §§2–4, ADR-0005/0006, contract catalog32 and read-only FigJam39 (`88:6723`) /41 (`88:6729`). This contract selects no production policy values or provider activation.

## Admission and ownership

Identity owns Tunner authentication sessions and Product authorization artifacts. Product registry/environment/client and Account–Product Relationship decisions must come from explicit, current, versioned owner contracts. Missing, stale, mismatched or unavailable authority denies issuance. Synthetic owner implementations qualify local tests only.

Validate the registered Product, environment and client; exact redirect; client authentication and permitted flow/response mode; PKCE; allowed scopes/audiences; current Tunner session, Account restrictions and requested assurance. An invalid registration or redirect must never redirect to an untrusted target. Preserve the approved Product-origin request and protocol correlation without accepting an arbitrary return URL.

Flow39 makes a qualified explicit Product-origin request Relationship activation intent. Create/activate that Relationship only through its owner; never create Product membership, roles or entitlements. Each later Product is authorized independently. A Tunner session cookie is never shared with a Product.

The explicit review profile requires qualified published Product context on Login and account-bound review at `/product-authorization`. Anonymous presentation contains no Account/session facts. Allow, Deny and Use another account bind the saved native request, actual actor, registry, policy and publication. Native processing revalidates Allow before issuance; Deny returns the original native error with private decline evidence and no grant. Unmet assurance never enables Allow.

The separately enabled operation-StepUp profile reuses the existing password, passkey, TOTP and federation proof paths. Bind qualified proof to the exact native request, continuation generation, actor, registry, owner facts, policy and publication. The browser receives only `PROOF_READY` with its exact operation reference: it neither grants Product authorization nor upgrades the Tunner session. Re-read current proof and authority, require explicit Allow, and consume the proof once in the native authorization transaction. Reject owner conflicts, stale factors, changed authority, replay and expired proof; rollback restores uncommitted consumption. Missing or unqualified mappings deny activation.

Persist review decisions in the existing native request-token row, serialized against competing decisions and consumption. Replaying a browser pointer cannot replace a terminal decision. Switching accounts increments its durable generation, invalidating prior pointers/references while retaining original request/freshness context; it does not itself sign out Tunner. Commit before publishing decisions and keep references outside URLs/storage.

## Issuance and redemption

Reuse pinned OpenIddict protocol processing, application/authorization/token stores, permissions and PKCE protection. Do not implement a parallel OAuth protocol or custom token cryptography. Keep HTTPS and token storage enabled.

Codes bind the original Account, Tunner session, Product/environment/client, exact redirect, PKCE and request context. Issued artifacts carry only policy-permitted audience/scope/claims. Subject mapping, issuer, keys, client capabilities, allowed claims/scopes/audiences and all code/token/refresh lifetimes are explicit qualified inputs; no production fallback is permitted.

Redemption must revalidate current session/Account/Relationship/client and applicable policy evidence. Reject changed or revoked authority and code replay, including concurrent redemption. Native redemption/token writes, durable owner lineage and required outbox effects must share the intended database transaction; commit before making the success response visible. Preserve original bindings rather than resnapshotting caller-supplied context.

Keep access/refresh tokens outside Tunner browser JavaScript and browser storage. Protocol response handling follows the registered client profile and approved URL/transport boundary. A standard protocol token endpoint must not acquire a blanket exemption for ordinary BFF operations. Reuse only an exact bounded terminal protocol transport where its native-handler compatibility is proven.

## Continued use, revocation and propagation

Refresh, introspection and revocation must enforce the current registered client and applicable ownership, session/grant status and qualified lifecycle policy. Cryptographic validity alone does not prove an artifact remains active. A revoked Tunner session cannot continue Tunner authorization.

Lifecycle qualification binds the original grant, authenticated requesting client, native artifact and current owner facts to an explicit versioned decision. Token traffic does not renew browser activity. The original code deadline does not limit later refresh validity; browser-session termination and issuance-policy expiry are facts for qualified artifact policy, not an implicit universal token lifetime or cascade. Client-authorized cleanup remains possible after browser eligibility ends. Refresh rotation, reuse leeway, expiry bounds, introspection disclosure and presented-token versus authorization-chain effects require explicit qualification; final owner checks and durable effects precede response publication.

An operation proof clips code issuance/redemption to its expiry. Later refresh follows separately qualified lifecycle policy and preserves the original authentication claims; it neither reconsumes the operation proof nor renews browser activity. Proof expiry is not an implicit refresh lifetime.

Apply grant/token effects according to explicit active policy, with durable causal evidence. Propagate signed/versioned security/revocation events only for enabled Product contracts. Track delivery and Product acknowledgement separately. Product-local sessions remain Product-owned; neither local grant revocation nor event dispatch proves global Product sign-out.

## Required qualification

Execute an actual HTTPS authorization-code/PKCE roundtrip and one-time redemption using the existing qualified PostgreSQL and authentication test facilities. Cover invalid Product/environment/client/redirect/PKCE/audience/scope/assurance, unavailable or changed authority, revoked/compromised sessions, code replay/concurrency, rollback and response loss. Complete applicable refresh, introspection, revocation, isolation, propagation and regression cases before feature acceptance. Scaffolding, startup suitability, local synthetic providers or a happy-path token alone do not establish P2-014 completion.
