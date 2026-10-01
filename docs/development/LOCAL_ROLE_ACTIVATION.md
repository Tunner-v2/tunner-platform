# Local Role Activation and Review Matrix

TUN-P0-027 makes the repository role policy executable. `tunner governance roles calculate <work-item>` reads `governance/policies/role-activation-policy.yaml`, combines every matching trigger, and reports the required-role union, declared roles, missing mandatory roles, and matched impact classifications.

The policy maps work-item impact fields to the authority-defined triggers: architecture/data-schema/migration, UI/content, API/SDK, finance, compliance/privacy, security, DevOps/infrastructure, provider, admin/support, release, plus work types for governance, business-requirement, and Product-decision work. Every non-documentation implementation activates Full-stack and QA; governance tooling also activates Governance and Auditor.

## Enforcement boundary

`enforcement.required_from` is a migration boundary, not an approval bypass. Records created before it retain their pre-P0-027 recorded evidence. TUN-P0-027 and every later record must declare every calculated mandatory role and provide exactly one schema-versioned `governance/reviews/*.yaml` record per required role. This avoids treating older evidence as silently invalid while making the new gate deterministic going forward.

Each review uses the structured handoff fields mandated by authority: `skill_id`, `skill_version`, `work_item_id`, `result`, authority and context references, findings, required actions, evidence, and `context_expansion_requested`. A review passes only with `result: PASS`, `status: PASS`, traceable authority/context/evidence references, no blocking findings, and no unresolved context-expansion request.

`governance gate check` and `governance next` both apply the calculation. A missing role or review, duplicate review, non-pass result, incomplete traceability, or blocking specialist finding blocks that work item only. A `MERGED_TO_MAIN` dependency remains a separate human integration action under AMD-0002.

## Local validation

```powershell
dotnet build Tunner.Governance.sln --no-restore
dotnet run --project .\tests\Tunner.Governance.FunctionalTests --no-build
dotnet run --project .\src\Tunner.Governance --no-build -- governance roles calculate TUN-P0-027
dotnet run --project .\src\Tunner.Governance --no-build -- governance gate check TUN-P0-027
```