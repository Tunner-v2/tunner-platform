# Local security and supply-chain baseline

TUN-P0-013 adds control-plane security checks only. It does not build a Product image, access a credential, deploy anything, publish an attestation, or authorize a release.

After dependencies are restored, run the complete local baseline with a fixed evidence date:

```powershell
./tools/security/Invoke-TunnerSecurityBaseline.ps1 -AsOf 2026-09-30
```

The command runs four fail-closed local controls:

1. a narrow SAST-style source-policy scan for unsafe serialization, unsafe JSON polymorphism, insecure legacy hash implementations, and unsafe XML resolver assignment;
2. the repository secret scan;
3. a static Compose policy scan that requires exact image version defaults, loopback port bindings, and no privileged, host-network, or local image-build directives; and
4. a NuGet vulnerability query, SPDX 2.3-compatible dependency inventory, and checksum/provenance evidence generation.

The generated `artifacts/supply-chain/tunner.spdx.json` and `artifacts/supply-chain/tunner.provenance.json` files are ignored disposable evidence. The provenance document records only hashes, the checked-out commit, and whether the tree was clean before evidence generation. It contains no source contents or credentials.

The GitHub workflow at `.github/workflows/security-supply-chain.yml` runs the same local baseline using a read-only token, then performs a separate high/critical public-image vulnerability scan for the exact Compose image defaults. All third-party workflow actions are pinned to immutable commit IDs. CI artifacts are evidence hooks, not signed release attestations.

## Boundaries

- This source-policy SAST is intentionally narrow. A language-aware code scanner, threat model, DAST, penetration test, SBOM attestation, and release provenance each require their own governed scope.
- The container image scan runs in GitHub Actions. Do not pull or scan images locally merely to satisfy this document; local Docker lifecycle remains operator-owned.
- A reported NuGet vulnerability or a high/critical fixable container vulnerability blocks its check. Resolve it or record a governed exception before release work.
- GitHub artifact attestation and SBOM publication happen only in later approved release scope; P0-013 does not invoke them.