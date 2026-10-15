# Local R2 configuration

Current automation: P1-024-RV001. The installer reuses non-secret settings and stores R2 credential values only in OpenBao.

From any PowerShell working directory, invoke this script by its repository location. From the repository root:

```powershell
.\tools\dev\setup-r2.ps1
```

The first run copies `profile.example.json` to the ignored `profile.json` if no profile exists. The example contains the non-secret values the operator supplied, including the confirmed dedicated non-production `tunner` bucket and selected vault field names. Review/edit the local profile when those settings change. Existing profiles are preserved.

Each run displays its target, requires a fresh `DEDICATED NON-PRODUCTION` confirmation, and asks privately for the local OpenBao bootstrap/root token used previously for policy setup. The local bootstrap is restricted to `LOCAL_DEVELOPMENT` and a loopback OpenBao endpoint. It issues an exact-resource, non-root, nonrenewable token with a maximum 30-minute lifetime and passes it in memory to the secret-store adapter.

If the R2 credential record is absent, the installer privately asks for the bucket-scoped R2 Access Key ID and Secret Access Key once and uses create-only KV-v2 CAS=0. Later runs automatically resolve that record. They do not repeat the configuration or R2 key prompts. A record with missing fields, denied access, a soft-deleted/versioned record, or a concurrent creator fails safely rather than being overwritten.

The same invocation runs the existing unique-object R2 put/read/delete test. It independently attempts to revoke the child token and remove its uniquely named temporary policy after success, failure or cancellation. Success requires verified object content, object cleanup and bootstrap cleanup. A bootstrap-cleanup failure returns a failing result while preserving the object key and original probe failure for recovery. Forced process termination can interrupt cleanup; the child token still has its hard expiry, and an operator must inspect any reported object or remaining temporary policy.

Tokens are entered privately and remain in process memory; they are not written to profiles, arguments, environment variables or the clipboard. Normal managed-memory collection does not guarantee immediate zeroization. One private bootstrap login is still required per invocation; permanent root-token storage is not used.

Local profiles and temporary files are ignored by Git. This README, the ignore rules and `profile.example.json` are tracked. Protect this directory against unauthorized modification; it controls credential destinations.

To use another existing profile:

```powershell
.\tools\dev\setup-r2.ps1 -ProfilePath "./local-config/r2/another-profile.json"
```

The original manual `setup-r2` and `test-r2` commands remain available; their setup mode accepts mixed case. See [manual installer behavior](../../docs/development/R2_INSTALLER.md) and [automatic setup evidence/recovery](../../docs/development/R2_AUTOMATIC_LOCAL_SETUP.md).

This directory is the operator-selected repository-local location for non-secret configuration. Live R2 acceptance remains pending until an actual authorized run reports success; disposable tests establish implementation behavior only.
