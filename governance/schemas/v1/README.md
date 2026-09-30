# Governance Schemas v1

These JSON Schema Draft 2020-12 documents define the versioned, repository-native record shapes required by Baseline 1.6.0 and AMD-0001. Every record requires a non-empty identifier and an integer `schema_version` of at least `1`.

The schemas are split into a shared definition library and sixteen small entry schemas:

- `milestone`, `sprint`, `work-item`, `dependency`, `todo`, `gate`, `defect`, and `reopen`;
- `product-decision`, `amendment`, `adr-reference`, and `release`;
- `evidence-manifest`, `specialist-review`, `context-selection`, and `handoff`.

Schema URNs are logical identifiers only. They are not claims that a public schema-hosting endpoint exists.

## Scope boundary

This B1/P0-003 deliverable establishes record shapes and a dependency-free structural validation check. It does not parse YAML, calculate lifecycle legality, prove identifier immutability across Git history, resolve references, or emit precise instance locations. Those runtime behaviors belong to `TUN-P0-004` (`tunner-governance` MVP) and its acceptance tests.

`additionalProperties: false` keeps each v1 record deliberate and reviewable. A breaking record-shape change requires a new schema version and governed amendment; schema rules alone cannot prove that an ID was unchanged between commits, so that history check is explicitly assigned to the future validator.

## Manual validation

Run from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File tools/validation/Validate-GovernanceSchemas.ps1
```

The check parses every schema document, verifies the expected v1 entry schemas, their Draft 2020-12 declaration, shared-definition references, and the required `schema_version`/identifier contract. It is intentionally not a substitute for the future YAML instance validator.
