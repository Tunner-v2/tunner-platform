# `tunner` governance and context MVP

P0-004 and P0-005 supply a repository-native .NET 10 control-plane tool. Governance commands are read-only; context commands write only the documented generated context pack. The tool does not read secrets or access Product data.

## Commands

```powershell
dotnet run --project src/Tunner.Governance -- authority verify
dotnet run --project src/Tunner.Governance -- governance validate
dotnet run --project src/Tunner.Governance -- governance status
dotnet run --project src/Tunner.Governance -- governance next
dotnet run --project src/Tunner.Governance -- governance gate check TUN-P0-004
dotnet run --project src/Tunner.Governance -- governance roles calculate TUN-P0-027
dotnet run --project src/Tunner.Governance -- governance transition check TUN-P0-004 VALIDATION
```

Pass `--repository <path>` before `governance` to point at another checkout. The tool reports JSON and returns a nonzero exit code for invalid records, blocked gates, or rejected transitions.

## Authority verification

```powershell
dotnet run --project src/Tunner.Governance -- authority verify
```

`authority verify` reads the immutable bootstrap integrity evidence and checks every tracked `docs/authority/` artifact against its recorded SHA-256 value. It is read-only: no Drive access, import, or file mutation occurs. A mismatch blocks `governance validate`, `governance next`, `governance gate check`, and both context commands, preventing implementation or generated context from proceeding on an altered authority mirror.
## Approval-pending execution

`governance next` returns `Items` for local/branch work and `HumanIntegrationActions` separately. A dependency explicitly declared `LOCAL_VALIDATED` may proceed after local validation; `MERGED_TO_MAIN` remains blocked until its prerequisite is `DONE` and the dependency’s merge-evidence references exist. This never approves, merges, or bypasses protected `main` controls.

The PR-integration policy may set positive dependent-chain or concurrent-unmerged limits. The repository default leaves both values `null`, so no limit is inferred without an approved policy value.


## Context commands

```powershell
dotnet run --project src/Tunner.Governance -- context build --work-item TUN-P0-005
dotnet run --project src/Tunner.Governance -- context verify
```

`context build` writes `docs/context/current/` from the active-authority summary, explicit work-item references, activated role skills, required evidence, durable project state, and bounded Git history. It records source hashes and exclusions. `context verify` returns `STALE` if an included source is missing or has changed; generated context is never authority. Hash verification and build refusal for a corrupt authority mirror remain TUN-P0-033 scope.

## Evidence manifests

```powershell
dotnet run --project src/Tunner.Governance -- evidence generate --scope-id TUN-P0-011 --output artifacts/evidence/TUN-P0-011.local.manifest.json --artifact src/Tunner.Governance/EvidenceApplication.cs --build-reference governance/evidence/TUN-P0-010-VALIDATION.json --test-reference governance/evidence/TUN-P0-010-VALIDATION.json --security-scan-reference governance/evidence/TUN-P0-010-VALIDATION.json
```

`evidence generate` produces a write-once, repository-local JSON manifest for an existing work item. It verifies the authority mirror, records the checked-out Git commit and pre-generation working-tree state, hashes only selected repository-relative files, and requires build, test, and security-scan references. Output is restricted to a new direct `.json` child of `artifacts/evidence/`; source contents, credentials, releases, Product acceptance, protected-main approvals, deployment claims, and external publication are outside its boundary. See `docs/development/LOCAL_EVIDENCE_MANIFEST.md` for the local workflow.

## Source registry

`powershell
dotnet run --project src/Tunner.Governance -- sources check --as-of 2026-09-30 --max-age-days 14
`

sources check deterministically validates local primary-source evidence hashes, scope linkage, review dates, and a caller-supplied freshness window. It does not browse externally or make release/Product claims. See docs/development/LOCAL_SOURCE_REGISTRY.md.

## Validation boundary

The MVP parses YAML with file/line/column diagnostics, builds the versioned JSON Schema catalog, and validates record identifiers, versioning, required fields, and undeclared top-level fields from that catalog. It deliberately does not mutate records. Rich nested type/format checks and Git-history immutability are follow-on governance work and must not be claimed as implemented until their tests exist.

## Functional harness

After the .NET 10 SDK is installed, run:

```powershell
dotnet restore Tunner.Governance.sln
dotnet build Tunner.Governance.sln --no-restore
dotnet run --project tests/Tunner.Governance.FunctionalTests --no-restore
```

The harness creates an isolated temporary repository fixture, covers valid validation/status/next behavior, required-evidence gate failure, invalid lifecycle transition rejection, AMD-0002 local/merged readiness behavior, configured chain-limit blocking, and malformed-YAML rejection, then removes the fixture.