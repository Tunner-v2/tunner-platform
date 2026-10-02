[CmdletBinding()]
param([Parameter(Mandatory)][string]$RepositoryRoot)
$ErrorActionPreference = "Stop"

$resolvedRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
function Require-Path([string]$relativePath) {
    $path = Join-Path $resolvedRoot $relativePath
    if (-not (Test-Path -LiteralPath $path)) { throw "Required P1 host-composition artifact is missing: $relativePath" }
    return $path
}
function Require-Text([string]$relativePath, [string]$expected) {
    $content = [IO.File]::ReadAllText((Require-Path $relativePath))
    if ($content.IndexOf($expected, [StringComparison]::Ordinal) -lt 0) { throw "Expected '$expected' in $relativePath." }
    return $content
}

$platformSolution = Require-Path 'Tunner.Platform.slnx'
$governanceSolution = Require-Path 'Tunner.Governance.sln'
$platformSolutionText = [IO.File]::ReadAllText($platformSolution)
$governanceSolutionText = [IO.File]::ReadAllText($governanceSolution)

foreach ($project in @('Tunner.Api', 'Tunner.Worker', 'Tunner.Platform', 'Tunner.Database', 'Tunner.Database.Migrations', 'Tunner.Observability', 'Tunner.Testing')) {
    if ($platformSolutionText.IndexOf($project, [StringComparison]::Ordinal) -lt 0) { throw "Tunner.Platform.slnx must include $project." }
    if ($governanceSolutionText.IndexOf($project, [StringComparison]::Ordinal) -ge 0) { throw "Tunner.Governance.sln must not include application project $project." }
}
if ($platformSolutionText.IndexOf('Tunner.Governance', [StringComparison]::Ordinal) -ge 0) { throw 'Tunner.Platform.slnx must not include governance tooling.' }

$apiProject = Require-Text 'src/Tunner.Api/Tunner.Api.csproj' '../Tunner.Platform/Tunner.Platform.csproj'
if ($apiProject.IndexOf('../Tunner.Observability/Tunner.Observability.csproj', [StringComparison]::Ordinal) -lt 0) { throw 'Tunner.Api must reference Tunner.Observability.' }
$workerProject = Require-Text 'src/Tunner.Worker/Tunner.Worker.csproj' '../Tunner.Platform/Tunner.Platform.csproj'
if ($workerProject.IndexOf('../Tunner.Observability/Tunner.Observability.csproj', [StringComparison]::Ordinal) -lt 0) { throw 'Tunner.Worker must reference Tunner.Observability.' }

foreach ($projectPath in @('src/Tunner.Api/Tunner.Api.csproj', 'src/Tunner.Worker/Tunner.Worker.csproj', 'src/Tunner.Platform/Tunner.Platform.csproj')) {
    $content = [IO.File]::ReadAllText((Require-Path $projectPath))
    if ($content.IndexOf('Tunner.Governance', [StringComparison]::Ordinal) -ge 0) { throw "$projectPath must not reference governance tooling." }
}

Require-Text 'src/Tunner.Api/Program.cs' 'builder.Services.AddTunnerModules();' | Out-Null
Require-Text 'src/Tunner.Api/Program.cs' 'application.MapHealthChecks("/health");' | Out-Null
Require-Text 'src/Tunner.Worker/Program.cs' 'builder.Services.AddTunnerModules();' | Out-Null
Require-Text 'src/Tunner.Platform/TunnerModuleRegistration.cs' 'public interface ITunnerModule' | Out-Null

Write-Output 'TUN-P1-002 static platform host-composition validation passed.'