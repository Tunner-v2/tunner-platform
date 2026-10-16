[CmdletBinding()]
param([Parameter(Mandatory)][string]$RepositoryRoot)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$packagePath = Join-Path $root 'Directory.Packages.props'
$apiProjectPath = Join-Path $root 'src/Tunner.Api/Tunner.Api.csproj'
$programPath = Join-Path $root 'src/Tunner.Api/Program.cs'
$testPath = Join-Path $root 'tests/Tunner.Platform.ArchitectureTests/OpenApiContractFoundationTests.cs'
$guidePath = Join-Path $root 'docs/development/LOCAL_OPENAPI_CONTRACTS.md'

function Require-Text([string]$Path, [string]$Expected) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Required file is missing: $Path" }
    if (-not ([IO.File]::ReadAllText($Path).Contains($Expected))) { throw "Expected text is missing from ${Path}: $Expected" }
}

Require-Text $packagePath '<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.13" />'
Require-Text $packagePath '<PackageVersion Include="Microsoft.Extensions.ApiDescription.Server" Version="10.0.13" />'
Require-Text $apiProjectPath '<PackageReference Include="Microsoft.AspNetCore.OpenApi" />'
Require-Text $apiProjectPath '<PackageReference Include="Microsoft.Extensions.ApiDescription.Server" PrivateAssets="all" />'
Require-Text $apiProjectPath '<OpenApiDocumentsDirectory>$(BaseIntermediateOutputPath)openapi</OpenApiDocumentsDirectory>'
Require-Text $apiProjectPath '<OpenApiGenerateDocumentsOptions>--file-name tunner-openapi --openapi-version OpenApi3_1</OpenApiGenerateDocumentsOptions>'
Require-Text $programPath 'builder.Services.AddOpenApi(options =>'
Require-Text $programPath 'options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_1;'
if ([IO.File]::ReadAllText($programPath).Contains('MapOpenApi(')) { throw 'TUN-P1-004 must not add a runtime OpenAPI endpoint or interactive API surface.' }
Require-Text $testPath 'BuildPublishesAnOpenApi31ContractWithoutProductOperations'
Require-Text $testPath 'Assert.All(paths.EnumerateObject(), path => Assert.Equal("/health", path.Name));'
Require-Text $guidePath '# Local OpenAPI 3.1 contract foundation'
Write-Output 'TUN-P1-004 static OpenAPI contract source validation passed.'
