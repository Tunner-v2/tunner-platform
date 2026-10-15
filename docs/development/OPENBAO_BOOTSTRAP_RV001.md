# OpenBao bootstrap correction

Scope: P0-007-RV001, original TUN-P0-007 remains DONE.

A first-run empty mount list currently throws under PowerShell StrictMode before the existing helper can create its approved tunner KV-v2 mount. Fix only property-existence detection. Test complete helper execution with synthetic private input and isolated command doubles for absent/existing mounts and provider failures. Validate Windows PowerShell 5.1 and the current PowerShell runtime. Real operator bootstrap is retried privately; no real root token is collected by validation.

Next operator action after committed validation: rerun `./tools/dev/openbao-bootstrap.ps1 apply-policies` from the repository root and provide the root token privately at its secure prompt. No reinitialization or additional unseal is required if the existing vault remains unsealed. R2 record provisioning and non-production confirmation remain separate open prerequisites.
