# Local Platform Host Composition

`TUN-P1-002` establishes the initial P1 structure only:

- `Tunner.Platform.slnx` is the application-only solution.
- `Tunner.Governance.sln` remains tooling-only and is not referenced by application projects.
- `Tunner.Api` is the HTTP/application composition root and exposes only the technical `/health` endpoint in this slice.
- `Tunner.Worker` is the background-process composition root. It does not dispatch messages, schedule durable work, or execute Product behavior yet.
- `Tunner.Platform` supplies the explicit module-registration convention. No domain module has been created.

## Deliberate exclusions

This is not a Product API, OAuth/Identity implementation, persistence/migration change, RabbitMQ/Redis/R2/SignalR/gRPC adapter, BFF/UI, deployment, or production-readiness claim. The hosts are not started by the validation commands below.

## Local validation

```powershell
dotnet restore .\Tunner.Platform.slnx
dotnet build .\Tunner.Platform.slnx --no-restore
dotnet test .\tests\Tunner.Platform.ArchitectureTests\Tunner.Platform.ArchitectureTests.csproj --no-restore
powershell.exe -NoProfile -File .\tools\validation\Validate-TunnerPlatformHostSource.ps1 -RepositoryRoot (Get-Location).Path
```

To run a host interactively only when needed, use `dotnet run --project .\src\Tunner.Api` or `dotnet run --project .\src\Tunner.Worker`. These are development commands, not deployment approval or evidence of dependency health.