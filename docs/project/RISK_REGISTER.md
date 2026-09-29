# RISK_REGISTER

| Risk ID | Area | Description | Likelihood | Impact | Owner role | Mitigation | Status | Evidence |
|---|---|---|---|---|---|---|---|---|
| EXT-DRIVE-001 | context/governance drift | Canonical Drive sources could not be inventory-checked during bootstrap. | Medium | Medium | governance-engineer | Use only the verified immutable bundle; record external verification as pending and re-check before import/update. | Open | `governance/evidence/BOOT-P0-001-integrity.json` |
| P0-BOOT-001 | delivery | Governance tool cannot enforce its own pre-tool history until B3. | Certain | High | governance-engineer | Limit bootstrap mode to control-plane records, capture evidence, then replay at B3. | Controlled | `docs/authority/amendments/AMD-0001/36_P0_Repository_Bootstrap_Governance_and_Context_Implementation_Spec.md` |
