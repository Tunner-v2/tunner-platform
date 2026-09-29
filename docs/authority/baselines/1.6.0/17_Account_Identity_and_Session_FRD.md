> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 17 — Account, Identity, Authentication & Session FRD

## 1. Authority and traceability

This FRD fulfills FigJam sections **36–45**, including the explicit contract markers in 41–44. Accounts & Identity owns Tunner Account identity, verified contacts, authenticators, Tunner sessions, recovery, Account state, Product authorization grants and identity-security mutations. Connected Products own Product membership, roles, entitlements and Product-local sessions.

Normative implementation baseline: ASP.NET Core Identity + OpenIddict, separate User/Admin BFFs, Google + Microsoft external identity adapters, passkeys/WebAuthn, TOTP and recovery codes. Identity guidance follows the current NIST SP 800-63 Revision 4 family and OWASP authentication/session/recovery guidance.

## 2. Canonical resources

- `TunnerAccount` — stable `account_id`; never reused.
- `VerifiedContact` — email initially; phone is not an MVP authentication factor.
- `ExternalIdentityLink` — provider + immutable provider subject; never keyed only by email.
- `Authenticator` — password credential metadata, passkey public-key metadata, TOTP enrollment metadata, recovery-code set metadata.
- `TunnerSession` — server-side session with assurance, authentication time, expiry, revocation state and BFF/client context.
- `DeviceRecord` and `DeviceTrust` — safe device evidence and trust relation; trust is not authentication by itself.
- `ProductAuthorizationGrant` — Product/client/audience/scope-bound authorization evidence.
- `RecoveryCase` — stable recovery operation and proof/evidence lineage.
- `AccountRestriction` — versioned restriction with reason/scope/effective interval.
- `IdentityVerificationRecord` — normalized result/evidence reference; raw identity documents do not live in the Account aggregate.
- `SecurityInvestigation` — separate Security-owned evidence linked by stable IDs.

## 3. Account state model

Canonical states:

```text
VERIFICATION_PENDING
ACTIVE
RESTRICTED
CLOSING
CLOSED
```

Rules:

- new email/password registrations begin `VERIFICATION_PENDING`;
- federated registrations can become `ACTIVE` only when the provider supplies a verified email and all registration/eligibility gates pass;
- `RESTRICTED` retains the Account and history while a versioned restriction controls allowed operations;
- `CLOSING` means closure was accepted but cross-domain blockers/effective-time obligations remain;
- `CLOSED` disables new Tunner authentication and is not a deletion of financial/audit evidence;
- no Product entitlement state is inferred from Account state.

`CLOSED` is not self-service reversible. A demonstrably erroneous/compromised closure may be corrected only through a governed Security/Recovery operation before identity anonymization/purge; the correction is a new Account-state version, never deletion of the closure evidence.

## 4. Contact lifecycle and uniqueness

Email is the initial required verified contact.

```text
UNVERIFIED -> VERIFIED -> SUPERSEDED | REVOKED
```

Rules:

- normalized verified email is unique across non-anonymized Accounts;
- registration and recovery responses must not disclose arbitrary Account existence;
- email change creates an `UNVERIFIED` candidate; the existing verified email remains effective until candidate proof succeeds and uniqueness is rechecked;
- ordinary profile editing cannot mark a contact verified;
- Product-provided email is input only and does not create verified Tunner identity evidence.

## 5. External identity linking

Initial providers: Google and Microsoft.

**No automatic linking solely because provider emails match.**

Allowed link paths:

1. authenticated Tunner Account initiates `LinkExternalIdentity`, completes provider authentication and confirms the link; or
2. login/registration discovers a verified-email collision and requires the user to authenticate/recover the existing Tunner Account before linking.

Provider `sub`/immutable subject is the external identity key. Email changes at the provider never rewrite the link identity.

Unlinking is blocked if it would leave the Account without an authentication/recovery path required by active security policy.

## 6. Authentication methods

Supported initial methods:

- password;
- passkey/WebAuthn;
- Google federation;
- Microsoft federation;
- TOTP authenticator app as MFA/step-up;
- one-time recovery codes.

SMS/voice OTP is not an initial authentication factor.

Passwords, passkeys, TOTP seeds and recovery codes use framework/standards-compliant storage and lifecycle rules. Passkey private keys never reach Tunner. TOTP enrollment is not active until first code verification. Recovery codes are generated once, displayed/downloaded once, stored only as verifier-safe values and individually consumed.

## 7. Assurance and step-up

`AuthenticationAssurancePolicy` is versioned and maps method/context to an assurance level. Sensitive operations resolve required assurance at execution time.

Baseline sensitive actions that require fresh reauthentication/step-up include:

- adding/removing/resetting authenticators;
- changing verified contact;
- generating a new recovery-code set;
- Account closure;
- payment-method add/replace/default/disable when policy requires;
- high-impact Billing/Finance actions;
- privileged workforce elevation (separate workforce domain).

Step-up evidence is operation-bound, short-lived by policy, and cannot be reused as a general session upgrade after its authorized context expires.

## 8. Session contract

Tunner browser clients use server-managed BFF cookie sessions. OAuth access/refresh tokens are not stored in browser JavaScript.

`TunnerSession` contains:

- `session_id`, `account_id`, BFF/client/origin;
- authentication methods and assurance;
- `authenticated_at`, `last_seen_at`, `expires_at`;
- optional `device_id` and trust-version ref;
- current state `ACTIVE | REVOKED | EXPIRED`;
- restriction/security evidence refs.

Session renewal/absolute/idle limits are versioned security policy values. Session identifiers rotate on authentication and material privilege/assurance changes.

Revocation is immediate and idempotent in Tunner. Product-local sessions are not directly mutated by Tunner.

## 9. Product authorization and revocation propagation

OpenIddict issues only client/audience/scope-bound artifacts after validating Product/environment/client/redirect/PKCE, Tunner session and Product Relationship.

Revocation semantics:

- revoked Tunner session is denied immediately by Tunner;
- refresh/grant artifacts are revoked according to token/grant policy;
- affected Product integrations receive signed/versioned revocation/security events when their integration contract enables them;
- Product acknowledgement is tracked separately;
- Product-local session termination remains Product authority;
- Tunner UI must never claim “signed out of all Products” unless every relevant Product contract provides and acknowledges that guarantee.

## 10. Recovery contract

Recovery never exposes existing credentials and Support cannot bypass the proof hierarchy.

Recovery evidence precedence:

1. active passkey or other strong authenticator;
2. valid recovery code;
3. verified contact challenge combined with risk/context checks;
4. governed Identity Verification / Security review when strong authenticators and safe contact recovery are unavailable.

Password reset alone cannot be used to mint a different factor without the recovery operation satisfying the active policy.

A successful recovery may, according to active policy, revoke existing sessions, invalidate compromised authenticators, require MFA re-enrollment and temporarily restrict sensitive operations. Every side effect is explicit in the `RecoveryResult`.

No security questions are used.

## 11. 18+ eligibility

**PDR-0007** is constitutional: Tunner Accounts are 18+ only. Registration uses DOB-based server-side eligibility.

 registration collects a `date_of_birth` value and the authoritative server calculates eligibility under the active `AgeAssurancePolicy`. Eligibility is true only when `date_of_birth + 18 calendar years <= evaluation_date`. Calendar arithmetic is date-based rather than an approximation such as `18 * 365` days.

If the result is under 18, registration terminates with stable `AGE_INELIGIBLE`; no active Tunner Account/session/Product Relationship is created, and the UI tells the user that Tunner is available only to users aged 18 or older. There is no separate age-attestation checkbox.

The Account stores `age_eligibility_status`, `age_evaluated_at`, policy/version and evidence reference. Full DOB is classified `RESTRICTED_PERSONAL` and is retained only where an approved Processing Governance Contract/Retention Policy requires it; otherwise the raw DOB is discarded after the derived eligibility result is committed. Stronger age/identity proof may be invoked only when the active policy requires it.

## 12. Account closure

Closure review checks:

- current authentication/step-up;
- Billing Account ownership/authority that must be transferred or ended;
- active Billing Subscriptions and closure/cancellation requirements;
- open invoices/receivables, payment unknowns, disputes and refunds;
- Security/Risk/Support/Privacy cases requiring preservation;
- Product Relationships requiring prospective deactivation notification;
- retention/hold obligations.

Closure does not delete immutable financial/audit/security evidence. Product data deletion is Product-owned unless a separate integration contract assigns a Tunner task.

## 13. API/operation surface

At minimum:

- Register Account / Verify Contact / Resend Verification;
- Authenticate / Sign Out / Sign Out Other Sessions;
- List/Revoke Sessions and Devices;
- Register/Revoke Passkey;
- Enroll/Disable TOTP;
- Generate/Replace Recovery Codes;
- Link/Unlink External Identity;
- Request/Complete Recovery;
- Request/Complete Verified Contact Change;
- Request Account Closure / Get Closure Blockers;
- OAuth/OIDC authorize/token/introspection/revocation endpoints as required by selected OpenIddict profile.

All mutating operations have stable operation/idempotency identity where duplicate submission is possible and expected-version checks for versioned resources.

## 14. Events

Post-commit events include versioned forms of:

- `AccountRegistered`, `AccountActivated`, `AccountRestricted`, `AccountClosing`, `AccountClosed`;
- `VerifiedContactChanged`;
- `AuthenticatorAdded`, `AuthenticatorRevoked`;
- `SessionCreated`, `SessionRevoked`;
- `RecoveryStarted`, `RecoveryCompleted`, `RecoveryDenied`;
- `ProductAuthorizationGranted`, `ProductAuthorizationRevoked`.

Security-sensitive payloads contain stable refs and safe metadata only.

## 15. UI requirements

Account Center must provide:

- registration/login/verification;
- passkey/TOTP/recovery-code setup;
- linked Google/Microsoft identities;
- session/device listing and revocation;
- verified-email change;
- recovery;
- Account closure review showing exact blockers and owner-domain resolution paths;
- security/activity history with permission-safe evidence.

Admin/Support must never expose passwords, TOTP seeds, recovery-code values, passkey private material or raw identity documents.

## 16. Acceptance cases

- duplicate registration does not disclose arbitrary Account existence;
- verified-email collision never auto-links Google/Microsoft identity;
- candidate email cannot replace current verified email before proof;
- passkey/TOTP enrollment is inactive until ceremony/first code succeeds;
- recovery cannot use Support override to bypass proof policy;
- session revocation blocks Tunner immediately while Product-local propagation remains separately evidenced;
- Account closure is blocked by defined owner-domain blockers;
- closed Account cannot authenticate;
- replayed registration/recovery/verification request is idempotent;
- concurrent email/security changes detect version conflict;
- no Product membership/entitlement is created by Tunner Account activation.
