[CmdletBinding()]
param([string]$RepositoryRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) { $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path }
$required = @('Directory.Packages.props','src/Tunner.Database/Tunner.Database.csproj','src/Tunner.Database/TunnerMigrationsDbContext.cs','src/Tunner.Database.Migrations/Tunner.Database.Migrations.csproj','src/Tunner.Database.Migrations/TunnerMigrationsDbContextFactory.cs','src/Tunner.Database.Migrations/Migrations/20260930173000_P0MigrationFoundation.cs','tools/dev/tunner-migrations.ps1','.config/dotnet-tools.json','docs/development/LOCAL_MIGRATIONS.md')
foreach ($path in $required) { if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $path))) { throw "Missing P0-008 artifact: $path" } }
$databaseContext = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Database/TunnerMigrationsDbContext.cs')
$packages = Get-Content -Raw (Join-Path $RepositoryRoot 'Directory.Packages.props')
$factory = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Database.Migrations/TunnerMigrationsDbContextFactory.cs')
$migration = Get-Content -Raw (Join-Path $RepositoryRoot 'src/Tunner.Database.Migrations/Migrations/20260930173000_P0MigrationFoundation.cs')
$guide = Get-Content -Raw (Join-Path $RepositoryRoot 'docs/development/LOCAL_MIGRATIONS.md')
$tool = Get-Content -Raw (Join-Path $RepositoryRoot 'tools/dev/tunner-migrations.ps1')
if ($databaseContext -match 'DbSet<' -or $databaseContext -match 'Entity\s*<') { throw 'P0-008 database context must not define Product/domain entities.' }
if ($migration -notmatch 'protected override void Up\(MigrationBuilder migrationBuilder\)\s*\{\s*\}') { throw 'P0 rehearsal migration Up method must remain empty.' }
if ($migration -match '(?i)CreateTable|AlterTable|DropTable|AddColumn|DropColumn|CreateIndex|DropIndex|InsertData|DeleteData|UpdateData') { throw 'P0 rehearsal migration must not contain Product/domain schema operations.' }
if ($packages -notmatch 'Microsoft.EntityFrameworkCore" Version="10\.0\.12' -or $packages -notmatch 'Microsoft.EntityFrameworkCore.Design" Version="10\.0\.12' -or $packages -notmatch 'Npgsql.EntityFrameworkCore.PostgreSQL" Version="10\.0\.3') { throw 'P0-008 must retain the reviewed EF Core 10.0.12 and Npgsql 10.0.3 package pins.' }
if ($factory -notmatch 'TUNNER_MIGRATION_CONNECTION_STRING' -or $factory -match '(?i)(password|secret|token)\s*=\s*"') { throw 'Migration factory must require an operator-supplied connection without embedded credentials.' }
if ($factory -notmatch 'UseNpgsql' -or $factory -notmatch 'MigrationsAssembly') { throw 'Migration factory must configure Npgsql and the separate migrations assembly.' }
if ($migration -notmatch 'throw new NotSupportedException' -or $migration -notmatch 'P0MigrationFoundation') { throw 'P0 rehearsal migration must be explicit and forward-only.' }
if ($tool -notmatch 'dotnet tool run dotnet-ef database update' -or $tool -notmatch 'TUNNER_MIGRATION_CONNECTION_STRING') { throw 'Migration rehearsal tool must require an operator connection and use the pinned EF tool.' }
if ($guide -notmatch 'forward-only' -or $guide -notmatch 'must not execute production migrations') { throw 'Migration guide must record forward-only and production-startup boundaries.' }
Write-Host 'TUN-P0-008 static migration source validation passed.'