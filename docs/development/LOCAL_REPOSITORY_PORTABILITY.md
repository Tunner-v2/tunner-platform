# Local repository portability

TUN-P0-035 implements AMD-0003. Repository tooling must resolve the checkout dynamically and must not persist a developer, CI, worktree, container-host, or drive-specific absolute path.

## Resolver contract

The shared resolver uses this precedence, stopping at the first valid directory containing `.tunner-root`:

1. An explicit `--repo-root` (governance CLI) or `-RepositoryRoot` (PowerShell tool) argument.
2. The `TUNNER_REPO_ROOT` process environment variable.
3. Git's `rev-parse --show-toplevel` result.
4. An upward search from the current directory for `.tunner-root`.
5. A clear error explaining how to supply the root.

Persisted repository paths use `/` separators relative to `<repo-root>`. Runtime paths are resolved only in process memory.

## Everyday nested-directory use

The following commands work from a nested directory because they use the shared resolver:

```powershell
Push-Location .\src\Tunner.Governance
dotnet run --no-build -- authority verify
Pop-Location

Push-Location .\tools\dev
.\tunner-test.ps1 unit
Pop-Location
```

## Required relocation acceptance proof

Run this from the repository root when P0-035 is ready for final validation. It copies the current checkout to a fresh temporary location without `.git`, so the run proves the marker fallback as well as nested-directory execution. It does not modify the source checkout. `robocopy` exit codes 0–7 are successful outcomes.

```powershell
$source = (Resolve-Path .).Path
$target = Join-Path ([IO.Path]::GetTempPath()) ('tunner-portability-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $target -Force | Out-Null
robocopy $source $target /E /XD .git bin obj artifacts .vs
if ($LASTEXITCODE -gt 7) { throw "Relocation copy failed with robocopy exit code $LASTEXITCODE." }

Push-Location $target
try {
    dotnet restore Tunner.Governance.sln
    dotnet build Tunner.Governance.sln --no-restore
    Push-Location .\src\Tunner.Governance
    dotnet run --no-build -- authority verify
    Pop-Location
    powershell.exe -NoProfile -File .\tools\validation\Test-TunnerRepositoryRoot.ps1 -RepositoryRoot $target
    powershell.exe -NoProfile -File .\tools\validation\Validate-TunnerLocationIndependentPaths.ps1 -RepositoryRoot $target
}
finally {
    Pop-Location
}
```

Expected result: the build succeeds, `authority verify` reports `VALID`, and both P0-035 validation scripts pass. If any command fails, preserve the temporary copy for diagnosis, report the command output, and do not mark P0-035 locally validated. Delete the temporary folder manually only after the result has been recorded.