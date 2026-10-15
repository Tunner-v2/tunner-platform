# Local R2 configuration

Work item: TUN-P1-023. This directory holds non-secret installer profiles for local engineering. The actual object-storage contract test uses the operator-confirmed dedicated non-production R2 bucket.

The profile contains endpoint and bucket names, environment references, OpenBao endpoint, and exact vault resource/field references. Enter R2 key values and short-lived scoped OpenBao tokens only in the installer's private prompts. Keep the R2 credential values in OpenBao.

Local profiles and installer temporary files are ignored by Git. Only this README and this directory's ignore rules are tracked. Protect local files and this directory from unauthorized modification; their references control credential destinations. Git ignore rules do not encrypt files or grant access control.

## Save the profile here

Run these commands from the repository root in an interactive PowerShell terminal:

```powershell
dotnet run --project src/Tunner.Installer -- setup-r2 "./local-config/r2/profile.json"
dotnet run --project src/Tunner.Installer -- test-r2 "./local-config/r2/profile.json"
```

The directory is ready for use. The installer creates `profile.json` only after configuration validates and credential resolution succeeds. Setup refuses to overwrite an existing profile. If that file already exists, use it with `test-r2`, or choose a new filename in this directory for another setup.

If an existing successfully saved profile is elsewhere, copy only that non-secret profile here after checking its fields. Do not copy a credential export, token file, or OpenBao initialization/unseal output.

## Known setup values

| Prompt | Existing value |
|---|---|
| Engineering environment class | `LOCAL_DEVELOPMENT` |
| R2 S3 endpoint base URL | `https://c38886fbac5e924b1e9f58c3185aae78.r2.cloudflarestorage.com` |
| Bucket name | `tunner` |
| OpenBao base URL | `http://127.0.0.1:28200` |
| Dedicated non-production confirmation | `DEDICATED NON-PRODUCTION` |

Use the existing approved environment reference, logical secret-store reference, exact KV-v2 API resource reference and both credential field names. These values are not inferred from the bucket name. Use `EXISTING` for an already provisioned credential record. Use `CREATE` only for an approved absent record with a token permitted to create and read that exact resource. The bootstrap root token is rejected by this installer.

The R2 test uses authenticated S3 access. Public bucket access is unnecessary. It writes and deletes one unique test object and requires both content and cleanup verification to pass. The created directory alone does not establish live R2 acceptance.

See [installer behavior and failure recovery](../../docs/development/R2_INSTALLER.md) for credential boundaries and cleanup behavior. This directory is the explicitly selected repository-local profile location for this setup; the earlier general outside-checkout recommendation does not apply to this location.
