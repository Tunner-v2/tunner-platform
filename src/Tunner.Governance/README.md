# `tunner` governance and context MVP

P0-004 and P0-005 supply a repository-native .NET 10 control-plane tool. Governance commands are read-only; context commands write only the documented generated context pack. The tool does not read secrets or access Product data.

## Commands

```powershell
dotnet run --project src/Tunner.Governance -- governance validate
dotnet run --project src/Tunner.Governance -- governance status
dotnet run --project src/Tunner.Governance -- governance next
dotnet run --project src/Tunner.Governance -- governance gate check TUN-P0-004
dotnet run --project src/Tunner.Governance -- governance transition check TUN-P0-004 VALIDATION
```

Pass `--repository <path>` before `governance` to point at another checkout. The tool reports JSON and returns a nonzero exit code for invalid records, blocked gates, or rejected transitions.
## Approval-pending execution

`governance next` returns `Items` for local/branch work and `HumanIntegrationActions` separately. A dependency explicitly declared `LOCAL_VALIDATED` may proceed after local validation; `MERGED_TO_MAIN` remains blocked until its prerequisite is `DONE` and the dependency’s merge-evidence references exist. This never approves, merges, or bypasses protected `main` controls.

The PR-integration policy may set positive dependent-chain or concurrent-unmerged limits. The repository default leaves both values `null`, so no limit is inferred without an approved policy value.


## Context commands

```powershell
dotnet run --project src/Tunner.Governance -- context build --work-item TUN-P0-005
dotnet run --project src/Tunner.Governance -- context verify
```

`context build` writes `docs/context/current/` from the active-authority summary, explicit work-item references, activated role skills, required evidence, durable project state, and bounded Git history. It records source hashes and exclusions. `context verify` returns `STALE` if an included source is missing or has changed; generated context is never authority. Hash verification and build refusal for a corrupt authority mirror remain TUN-P0-033 scope.
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