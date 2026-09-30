# RISK_REGISTER

| Risk ID | Area | Description | Likelihood | Impact | Owner role | Mitigation | Status | Evidence |
|---|---|---|---|---|---|---|---|---|
| EXT-DRIVE-001 | context/governance drift | Canonical Drive sources were not inventory-checked during bootstrap; live root inventories and controlling reads now pass, while full file-by-file reconciliation remains pending. | Low | Medium | governance-engineer | Retain the verified immutable bundle, use the successful Drive readback, and complete TUN-P0-033 before claiming full Drive-to-mirror equivalence. | Controlled | `governance/evidence/BOOT-P0-001-integrity.json`; `governance/evidence/DRIVE-AUTHORITY-READBACK-20260930.json`; `governance/evidence/DRIVE-SOURCE-MAP-READBACK-20260930.json` |
| P0-BOOT-001 | delivery | Governance tool cannot enforce its own pre-tool history until B3. | Certain | High | governance-engineer | Limit bootstrap mode to control-plane records, capture evidence, then replay at B3. | Controlled | `docs/authority/amendments/AMD-0001/36_P0_Repository_Bootstrap_Governance_and_Context_Implementation_Spec.md` |
