[CmdletBinding()]
param([string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path }
$required = @('src/Tunner.Database/Tunner.Database.csproj','src/Tunner.Database/TunnerMigrationsDbContext.cs','src/Tunner.Database.Migrations/Tunner.Database.Migrations.csproj','src/Tunner.Database.Migrations/TunnerMigrationsDbContextFactory.cs','docs/development/LOCAL_MIGRATIONS.md')
foreach ($path in $required) { if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $path))) { throw "Missing P0-008 artifact: $path" } }
$factory = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Database.Migrations/TunnerMigrationsDbContextFactory.cs')
$guide = Get-Content -Raw (Join-Path $RepositoryRoot 'docs/development/LOCAL_MIGRATIONS.md')
if ($factory -notmatch 'TUNNER_MIGRATION_CONNECTION_STRING' -or $factory -match '(?i)(password|secret|token)\s*=\s*"') { throw 'Migration factory must require an operator-supplied connection without embedded credentials.' }
if ($factory -notmatch 'UseNpgsql' -or $factory -notmatch 'MigrationsAssembly') { throw 'Migration factory must configure Npgsql and the separate migrations assembly.' }
if ($guide -notmatch 'forward-only' -or $guide -notmatch 'must not execute production migrations') { throw 'Migration guide must record forward-only and production-startup boundaries.' }
Write-Host 'TUN-P0-008 static migration source validation passed.'