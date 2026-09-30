# Tests and evidence

- `powershell -ExecutionPolicy Bypass -File tools/validation/Validate-TunnerDevSource.ps1` — required test
- `docker compose --env-file infra/docker/.env.example --file infra/docker/compose.yaml config --quiet` — required test
- `powershell -ExecutionPolicy Bypass -File tools/dev/tunner-dev.ps1 doctor` — required test
- `user-run tunner-dev start and tunner-dev health dependency acceptance after review of the generated instructions` — required test

## Required evidence

- `governance/context/CTX-TUN-P0-006-001.yaml` — required evidence
- `governance/evidence/TUN-P0-006-RD-SERVICES.json` — required evidence
- `governance/evidence/TUN-P0-006-VALIDATION.json` — required evidence
- `governance/evidence/TUN-P0-006-ROLE-REVIEWS.json` — required evidence
