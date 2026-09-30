# Tests and evidence

- `dotnet build Tunner.Governance.sln --no-restore` — required test
- `dotnet run --project tests/Tunner.Governance.FunctionalTests --no-restore` — required test
- `positive and negative CLI fixture tests for validate, status, next, and gate check` — required test
- `static source and fixture validation` — required test
- `AMD-0002 local-readiness, merged-main integration-action, and configured chain-limit fixture validation` — required test

## Required evidence

- `governance/context/CTX-TUN-P0-004-001.yaml` — required evidence
- `governance/evidence/TUN-P0-004-RD-PACKAGES.json` — required evidence
- `governance/evidence/TUN-P0-004-VALIDATION.json` — required evidence
- `governance/evidence/TUN-P0-004-ROLE-REVIEWS.json` — required evidence
- `governance/policies/pr-integration-policy.yaml` — required evidence
- `governance/dependencies/DEP-TUN-P0-004-TUN-P0-003.yaml` — required evidence
- `src/Tunner.Governance/GovernanceApplication.cs` — required evidence
- `tests/Tunner.Governance.FunctionalTests/Program.cs` — required evidence
- `governance/schemas/v1/governance-record.schema.json` — required evidence
- `tools/validation/Validate-TunnerGovernanceSource.ps1` — required evidence
- `src/Tunner.Governance/README.md` — required evidence
