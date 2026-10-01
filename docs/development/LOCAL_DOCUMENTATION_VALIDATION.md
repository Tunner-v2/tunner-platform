# Local documentation validation

P0-015 provides a read-only documentation gate for repository-local Markdown links, canonical governance identifiers, duplicate canonical identifiers within a record type, authority headers, governance schema checks, and recorded source-review freshness.

Run it after a local build:

    dotnet build Tunner.Governance.sln --no-restore
    powershell.exe -NoProfile -File .\tools\validation\Validate-TunnerDocumentation.ps1 -RepositoryRoot (Get-Location).Path -AsOf 2026-10-01 -MaxSourceAgeDays 14

The validator does not fetch external links. It only resolves repository-local link targets. The freshness result is the local P0-012 source registry evaluated at the caller-supplied date and window; it is not a live source refresh, Product decision, release approval, or deployment check.

Run the isolated regression cases without reading or modifying the real repository documentation:

    powershell.exe -NoProfile -File .\tools\validation\Test-TunnerDocumentationValidation.ps1 -RepositoryRoot (Get-Location).Path

The test uses a temporary fixture and proves positive validation plus rejection of a missing local link, duplicate canonical identifier, and missing authority header.