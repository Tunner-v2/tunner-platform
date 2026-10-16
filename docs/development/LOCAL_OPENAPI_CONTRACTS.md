# Local OpenAPI 3.1 contract foundation

## Scope

TUN-P1-004 adds only the technical OpenAPI 3.1 generation and build-time publication pipeline for the existing API host. It uses the .NET 10 first-party Microsoft.AspNetCore.OpenApi and Microsoft.Extensions.ApiDescription.Server packages, as required by ADR-0011.

The generated JSON is written below the API project's ignored obj/ directory using repository-relative MSBuild configuration. It is a local build artifact, not a committed public API, SDK input, release artifact, or external publication.

## Operator validation

From <repo-root> after restoring packages:

`powershell
dotnet restore .\Tunner.Platform.slnx
dotnet build .\Tunner.Platform.slnx --no-restore
powershell.exe -NoProfile -File .\tools\validation\Validate-TunnerOpenApiContractSource.ps1 -RepositoryRoot (Get-Location).Path
dotnet test .\tests\Tunner.Platform.ArchitectureTests\Tunner.Platform.ArchitectureTests.csproj --no-restore
`

The build generates 	unner-openapi.json under src/Tunner.Api/obj/ for the active build configuration. The architecture test verifies the document advertises OpenAPI 3.1 and contains no route other than the existing technical /health route.

## Boundaries

- No Product/domain endpoint, operation identifier, schema, contract family, error taxonomy, or authorization behavior is introduced.
- No runtime OpenAPI endpoint, Swagger UI, Scalar UI, external documentation site, SDK generation, package publication, API-versioning policy, webhook, or client compatibility promise is introduced.
- The .NET 10 baseline remains OpenAPI 3.1. OpenAPI 3.2 requires the explicit governed upgrade trigger in ADR-0011.
- This local contract artifact is not evidence of production deployment, public API availability, Product acceptance, or protected-main integration.