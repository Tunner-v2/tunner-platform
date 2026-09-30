# Tests and evidence

- `powershell -ExecutionPolicy Bypass -File tools/validation/Validate-GovernanceSchemas.ps1` — required test
- `JSON parse check for all governance/schemas/v1/*.json documents` — required test
- `negative validation check for unexpected entry schema` — required test

## Required evidence

- `governance/schemas/v1/README.md` — required evidence
- `governance/evidence/TUN-P0-003-SCHEMA-VALIDATION.json` — required evidence
- `governance/evidence/TUN-P0-003-ROLE-REVIEWS.json` — required evidence
