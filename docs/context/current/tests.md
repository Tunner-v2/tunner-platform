# Tests and evidence

- `dotnet build Tunner.Governance.sln --no-restore` — required test
- `dotnet run --project tests/Tunner.Governance.FunctionalTests --no-restore` — required test
- `context build/verify positive fixture test` — required test
- `context verify stale-source negative fixture test` — required test
- `generated P0-005 context pack acceptance readback` — required test

## Required evidence

- `governance/context/CTX-TUN-P0-005-001.yaml` — required evidence
- `governance/evidence/TUN-P0-005-VALIDATION.json` — required evidence
- `governance/evidence/TUN-P0-005-ROLE-REVIEWS.json` — required evidence
