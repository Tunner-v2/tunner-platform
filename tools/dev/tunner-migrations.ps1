[CmdletBinding()]
param([Parameter(Mandatory = $true, Position = 0)][ValidateSet("verify", "rehearse")][string]$Command)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
$migrationsProject = Join-Path $repositoryRoot "src/Tunner.Database.Migrations/Tunner.Database.Migrations.csproj"

switch ($Command) {
    "verify" {
        & dotnet build $migrationsProject --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Migration project build failed with exit code $LASTEXITCODE." }
        Write-Host "Migration source verified. No database was contacted."
    }
    "rehearse" {
        if ([string]::IsNullOrWhiteSpace($env:TUNNER_MIGRATION_CONNECTION_STRING)) { throw "Set TUNNER_MIGRATION_CONNECTION_STRING only in the current operator process before a local rehearsal." }
        & dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed with exit code $LASTEXITCODE." }
        & dotnet tool run dotnet-ef database update --project $migrationsProject --startup-project $migrationsProject
        if ($LASTEXITCODE -ne 0) { throw "Migration rehearsal failed with exit code $LASTEXITCODE." }
        Write-Host "Migration rehearsal completed. Do not reuse this command for production deployment."
    }
}