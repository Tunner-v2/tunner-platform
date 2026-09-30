# `tunner governance` MVP

P0-004 supplies a repository-native, read-only .NET 10 control-plane tool. It does not write governance records, invoke shells, read secrets, or access Product data.

## Commands

```powershell
dotnet run --project src/Tunner.Governance -- governance validate
dotnet run --project src/Tunner.Governance -- governance status
dotnet run --project src/Tunner.Governance -- governance next
dotnet run --project src/Tunner.Governance -- governance gate check TUN-P0-004
dotnet run --project src/Tunner.Governance -- governance transition check TUN-P0-004 VALIDATION
```

Pass `--repository <path>` before `governance` to point at another checkout. The tool reports JSON and returns a nonzero exit code for invalid records, blocked gates, or rejected transitions.

## Validation boundary

The MVP parses YAML with file/line/column diagnostics, builds the versioned JSON Schema catalog, and validates record identifiers, versioning, required fields, and undeclared top-level fields from that catalog. It deliberately does not mutate records. Rich nested type/format checks and Git-history immutability are follow-on governance work and must not be claimed as implemented until their tests exist.

## Functional harness

After the .NET 10 SDK is installed, run:

```powershell
dotnet restore Tunner.Governance.sln
dotnet build Tunner.Governance.sln --no-restore
dotnet run --project tests/Tunner.Governance.FunctionalTests --no-restore
```

The harness creates an isolated temporary repository fixture, covers valid validation/status/next behavior, required-evidence gate failure, invalid lifecycle transition rejection, and malformed-YAML rejection, then removes the fixture.