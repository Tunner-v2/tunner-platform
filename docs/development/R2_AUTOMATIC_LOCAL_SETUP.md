# Automatic local R2 setup

Revision: P1-024-RV001. Original TUN-P1-024 remains DONE with its historical closure/evidence unchanged.

## Operator command

```powershell
.\tools\dev\setup-r2.ps1
```

This uses `local-config/r2/profile.json` and creates it from the tracked operator-supplied non-secret example only when absent. It resolves the repository dynamically and preserves existing configuration. Supply `-ProfilePath` to select another local profile.

One invocation performs configuration reuse, restricted OpenBao authentication, credential resolution/create-only setup and the dedicated non-production R2 probe. It asks for fresh non-production confirmation and one private local bootstrap/root token. Bucket-scoped R2 values are requested only for an absent record. It does not require obtaining/copying a scoped token or retyping endpoint/reference fields.

The bootstrap accepts LOCAL_DEVELOPMENT with loopback OpenBao only. Root authority is used only for identity/policy metadata and cleanup; the existing secret-store adapter uses the minted exact-path non-root token to read/create R2 credentials. That token has no default/root policy, is nonrenewable, and has a hard maximum lifetime of 30 minutes. Temporary token revocation and unique-policy removal have independent bounded cleanup attempts.

The result retains the exact test object key, content verification, object cleanup, original probe failure, bootstrap cleanup and combined Passed flag. Any cleanup failure prevents combined success. The original unique-object probe retains its own independent cleanup deadline. Authentication/provider failures are sanitized and never treated as an absent credential record.

## Recovery

- Retry the same command after an interrupted/failed attempt. A successfully created vault record is retained and automatically reused.
- Missing required fields or existing soft-deleted/versioned records require operator correction/restoration; CAS=0 prevents silent overwrite.
- A failed object deletion requires authorized cleanup of the exact reported key and retained failed evidence.
- A token revocation or policy-removal failure requires administrator inspection. Tokens retain their hard expiry. A forced process/host stop can interrupt cleanup; no unconditional crash-cleanup claim is made.
- The bootstrap root token is supplied privately each invocation. No permanent root credential, cloud account/bucket provisioning or production authentication flow is introduced.

## Validation and lifecycle

Implementation validation is in progress. Focused tests and disposable OpenBao integration have passed; final evidence must bind the committed candidate before revision closure. Live Cloudflare R2 acceptance is NOT_EXECUTED by the agent because private operator authentication is required. TUN-P1-023 and its live R2 defect remain pending actual provider receipts.

Primary sources:
- [OpenBao token creation and lifetime controls](https://openbao.org/docs/commands/token/create/)
- [OpenBao exact-path policy semantics](https://openbao.org/docs/concepts/policies/)
- [OpenBao 2.7.0 token API implementation](https://raw.githubusercontent.com/openbao/openbao/v2.7.0/api/auth_token.go)
