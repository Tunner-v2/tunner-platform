# R2 installer and contract validation

Authority: approved Document 06 (configuration and secrets), Document 08 (security) and Document 15 (R2 and reference OpenBao). Work item: TUN-P1-024. Original component work TUN-P1-009 and TUN-P1-016 remains closed; TUN-P1-023 owns live provider acceptance.

This engineering installer collects configuration at setup time. It defines no canonical environment-variable names, vault resource paths, credentials, residency policies or deployed defaults. Supply the existing approved references. No Cloudflare account, bucket, vault mount, policy or token is provisioned by these commands.

From the repository root, use an interactive local terminal:

```powershell
dotnet run --project src/Tunner.Installer -- setup-r2 "<operator-selected-profile-path>"
dotnet run --project src/Tunner.Installer -- test-r2 "<same-profile-path>"
```

Replace the profile placeholder with an operator-selected file outside the checkout. The parent directory must already exist. Setup writes an atomic new file and refuses to overwrite an existing profile. To change configuration, rerun setup to a new operator-selected path. Protect the profile and its directory from unauthorized modification: it controls credential destinations even though it contains no secret values.

Run `./tools/validation/Test-TunnerR2Installer.ps1` for the installer build, all installer tests with disposable OpenBao, existing secret scan and SAST. It resolves the checkout dynamically and keeps TRX receipts locally under `artifacts/r2-installer/`. Use `-NoRestore` when dependencies are already restored. The installer is built directly from its project; the existing platform solution retains its historical composition. No workflow or external artifact upload is added.

Setup asks for:

- Existing engineering environment class (`LOCAL_DEVELOPMENT` or `EPHEMERAL_TEST`) and environment reference, validated through the existing environment configuration model.
- R2 S3 account endpoint base URL and bucket as separate values. Enter the endpoint host without a bucket path. Jurisdiction-qualified R2 account hosts are accepted. No production configuration or deployment is inferred by this engineering slice.
- Existing logical secret-store reference, OpenBao base URL, exact KV-v2 API resource reference (`<mount>/data/<resource>`), and the exact access-key-ID and secret-key field names.
- Whether the bucket is confirmed dedicated to non-production integration tests. An unconfirmed answer permits profile setup but prevents live testing.
- `EXISTING` to resolve a pre-provisioned record, or `CREATE` to enter bucket-scoped credentials privately and create an absent record with OpenBao KV-v2 CAS=0. This never overwrites an existing version, including soft-deleted/versioned records. If creation succeeds but later reading or profile saving fails, retain the vault record and retry with `EXISTING`; the installer does not delete vault records.
- A short-lived, scoped, non-root OpenBao token entered without echo. The token and R2 credential values are never command arguments, environment-variable requirements, profile fields, logs or evidence. Redirected input is rejected.

The operator must obtain the approved short-lived token through the existing authentication/bootstrap process. It must allow lookup-self and read of the exact credential resource; CREATE additionally requires the provider's create/update capability on that resource. Existing repository runtime policies are read-only and are not widened by installation. The adapter rejects root policies and tokens with no remaining TTL; operators still own least-privilege policy verification. Neither token metadata nor the S3 API proves a deployed R2 key is bucket-scoped: verify that scope in the provider configuration. Do not use the local bootstrap root token here.

OpenBao transport permits HTTPS, or HTTP only for loopback development. Redirect following is disabled; request time and response size are bounded. Errors are sanitized without remote bodies, credential values or exception stacks. In-memory .NET strings are subject to garbage collection; clearing containers does not claim guaranteed memory zeroization. Separate provider-neutral `ISecretStore` and OpenBao adapter implementations allow later approved workload identity/provider adapters without making OpenBao a Product contract.

`ConfiguredR2Storage.CreateAsync` resolves the profile's opaque reference through `ISecretStore` before constructing the existing AWS R2 adapter. It owns SDK lifetime, uses the R2 region `auto`, explicit endpoint and path-style addressing, and retains the existing R2 upload flags. Application hosts are not automatically activated or given bootstrap tokens. An application owner must use its approved workload identity and supplied profile when storage becomes eligible in that host.

The live test requires the profile confirmation and a fresh typed `DEDICATED NON-PRODUCTION` confirmation before connecting to R2. It writes one cryptographically random payload under a unique test key, reads exact bytes, compares SHA256, and always attempts deletion after a PUT attempt. Cleanup uses an independent 45-second deadline even when the operation is canceled or PUT times out. HEAD must return 404 to verify absence; 403, unavailable storage or a failed deletion never counts as success. The operation itself is bounded to two minutes. No bucket enumeration, existing-object modification, bucket creation or policy change occurs.

The command returns JSON containing the test object key, content/cleanup verification and a stable failure code. Exit zero requires both verifications. Preserve this non-secret result and the tested commit in governed evidence. If cleanup fails, use the exact returned key for authorized cleanup and retain the failed result; never record acceptance PASS until cleanup is verified. A process/host crash can interrupt finally cleanup, so a crashed run requires operator inspection of the dedicated test prefix.

Current live prerequisite: the supplied bucket's environment is explicitly unconfirmed. No deployed profile, exact vault references or credentials have been supplied. Do not contact that R2 bucket until its dedicated non-production status is confirmed and setup succeeds. Local disposable tests are implementation evidence and do not certify live Cloudflare compatibility.

Primary sources checked for this implementation:

- [Cloudflare .NET SDK setup and R2 upload flags](https://developers.cloudflare.com/r2/examples/aws/aws-sdk-net/)
- [Cloudflare S3 API region and object operations](https://developers.cloudflare.com/r2/api/s3/api/)
- [Cloudflare bucket-scoped API credentials](https://developers.cloudflare.com/r2/api/tokens/)
- [OpenBao KV-v2 CAS behavior](https://openbao.org/docs/secrets/kv/kv-v2/)
- [OpenBao primary token lookup implementation](https://github.com/openbao/openbao/blob/main/api/auth_token.go)
