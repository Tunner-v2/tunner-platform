# Local R&D source registry

P0-012 verifies local, repository-recorded source evidence. It does not browse the internet, refresh a vendor source, make a Product decision, or grant release approval.

Run a deterministic point-in-time check:

```powershell
dotnet run --project src/Tunner.Governance --no-build -- sources check --as-of 2026-09-30 --max-age-days 14
```

The registry at `governance/rd/source-registry.json` maps material work or decision scopes to local JSON evidence. Each entry must have an existing governed scope, an exact SHA-256 evidence hash, a matching review date, `PRIMARY` authority, and non-empty HTTPS primary-source metadata. The caller supplies the evaluation date and freshness window; the tool does not invent a universal expiry policy.