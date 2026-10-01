# Local evidence manifests

P0-011 provides a deterministic, local-only evidence-manifest generator. It captures repository-relative evidence paths and SHA-256 hashes, the checked-out Git commit, the pre-generation working-tree state, the generator version, and the verified authority-summary hash. It deliberately does not copy source contents into the manifest.

It is not a release artifact, Product acceptance, protected-main approval, deployment record, or production-readiness claim.

## Generate a local manifest

From the repository root, after the referenced evidence files exist, run:

```powershell
dotnet run --project src/Tunner.Governance --no-build -- evidence generate --scope-id TUN-P0-011 --output artifacts/evidence/TUN-P0-011.local.manifest.json --artifact src/Tunner.Governance/EvidenceApplication.cs --build-reference governance/evidence/TUN-P0-010-VALIDATION.json --test-reference governance/evidence/TUN-P0-010-VALIDATION.json --security-scan-reference governance/evidence/TUN-P0-010-VALIDATION.json
```

Expected result: JSON with `Outcome` set to `GENERATED`, and a new ignored file at `artifacts/evidence/TUN-P0-011.local.manifest.json`.

The output file must be a previously nonexistent direct child of `artifacts/evidence` with a `.json` extension. The command refuses absolute or escaping input paths, missing artifacts, reparse-point artifacts, an invalid authority mirror, inability to determine the current Git commit, existing output files, and secret-shaped output values. It does not contact Drive, GitHub, Docker, a database, or any Product service.

To obtain a second manifest, choose a distinct output file name. Manifest content is byte-identical for the same repository state and inputs; the output path itself is not embedded.

## Local verification

```powershell
powershell.exe -NoProfile -File .\tools\validation\Validate-TunnerEvidenceSource.ps1 -RepositoryRoot (Get-Location).Path
dotnet run --project .\tests\Tunner.Governance.FunctionalTests --no-build
```

The functional harness uses an isolated temporary repository. It verifies successful generation, byte-for-byte determinism, output-path refusal, and secret-shaped output refusal, then removes its fixture.