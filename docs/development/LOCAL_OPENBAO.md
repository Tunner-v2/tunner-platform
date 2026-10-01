# Local OpenBao secrets foundation

## Scope and boundary

P0-007 provides a local, credential-free foundation only. Application code must depend on a secret-store abstraction that resolves a `SecretReference` through an `ISecretStore` port; the local OpenBao adapter is the reference implementation. The port returns a secret only to the caller that is authorized to resolve that reference. It must never serialize secret values into configuration, persistence, logs, evidence, PR text, or source code.

This document does not authorize Product/business secret handling, production Vault deployment, cloud-provider credentials, an application token, or application secret injection. Those scopes require later governed work.

## Non-negotiable handling rules

- Never commit a root token, unseal key, application token, password, API key, or private key.
- Never add secret values to `infra/docker/.env.local`, screenshots, evidence, issues, or pull requests.
- Use the loopback-only local OpenBao endpoint supplied by P0-006; it is not a production service.
- Treat OpenBao initialization output as sensitive. Save it only in an approved secure local store, then clear terminal history or other transient capture according to the operator's local policy.

## Bootstrap workflow

Prerequisite: P0-006 has been started by an operator. Codex does not start containers or receive secret values.

1. Confirm the OpenBao state without supplying credentials:

   ```powershell
   ./tools/dev/openbao-bootstrap.ps1 status
   ```

2. If the local volume is uninitialized, an authorized local operator runs the OpenBao initialization command interactively inside the container. The command prints unseal material and an initial root token; do not redirect or copy that output into this repository:

   ```powershell
   docker compose --project-directory infra/docker --env-file infra/docker/.env.local --file infra/docker/compose.yaml exec openbao bao operator init
   ```

   When `.env.local` does not exist, replace it with `infra/docker/.env.example`. This initializes only the named local Docker volume. It is not a production procedure.

3. An authorized local operator unseals OpenBao interactively with the local unseal material. Do not pass an unseal key on a command line or write it to an environment file:

   ```powershell
   docker compose --project-directory infra/docker --env-file infra/docker/.env.local --file infra/docker/compose.yaml exec openbao bao operator unseal
   ```

4. Apply the repository policy files. The helper prompts for the root token as a secure PowerShell value, sends it only to the short-lived local Compose command, and does not echo or persist it:

   ```powershell
   ./tools/dev/openbao-bootstrap.ps1 apply-policies
   ```

The bootstrap policy may provision the `tunner` KV-v2 mount and update only the `tunner-local-runtime` policy. The runtime policy can read and list only the `tunner/local/*` namespace. P0 does not generate, store, or distribute a runtime token.

## Repository secret scan

Run the local scan before a commit or push:

```powershell
./tools/security/Invoke-TunnerSecretScan.ps1
```

The scanner inspects tracked and unignored working-tree files for high-confidence private-key, GitHub-token, Slack-token, and generic secret-assignment patterns. It is a local defense-in-depth control, not a replacement for GitHub secret scanning, push protection, or the P0-013 supply-chain pipeline. A match blocks the command without printing the matched value. Rotate exposed credentials outside the repository before resuming work.

## Validation

```powershell
./tools/validation/Validate-TunnerSecretsSource.ps1
```

Validation is static and includes a generated negative fixture. It does not initialize, unseal, or start OpenBao and it does not require any credential.