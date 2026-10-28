# ADR-LGTM-TEMPO-VEX-20261028 — exact dev/test applicability correction

Status: Product Owner scope authorized; guard/review validation required before consumption.
Work item P1-RV001-001; P0-RV010 preserves historical closure.

The direct reply “Approve these scoped dev/test VEX corrections” extends only the scanner boundary in the earlier maintenance approval for CVE-2026-21728 and CVE-2026-28377 on the exact reviewed Tempo data-source executable. It does not extend the separately approved signing scope into production or authorize filtering another component. See governance/evidence/P1-RV001-001-TEMPO-VEX-APPROVAL-20261028.json and the AMD-0005 resolution receipt.

Source is the official3.1.1 commit9db7df79d2af105a90d96484e6403e1fda0d011e. Its Go pseudo-version is compared to older semantic-version ranges. Actual dependency-graph evidence contains only four Tempo protobuf packages; affected HTTP query/config-serving code is absent. The vendor S3-key redaction regression also passes. The two original alerts remain preserved in the unfiltered report.

The corrected document is in governance/evidence/security-20261028/tempo-vex/tempo-vex.json. A typed validator must verify the exact source sum/commit, plugin identity/version, executable SHA-256, linked package set, inspected/scanned image identity, exact CVE/product/subcomponent set and unchanged document bytes. Reject changed source/hash/package/CVE/context, missing specialist review, changed reviewed evidence or validator source, and expired consent. Expiry is2026-11-27T00:00:00Z; no extension is implied. A zero-match scan uses its unfiltered outcome. Every unrelated finding retains the unchanged HIGH/CRITICAL failure.

Before consumption, source-bound architect/security/governance review and all positive/adversarial tests must pass at committed source. This is an executing-agent technical assessment under activated contracts; protected-main approval remains an independent human gate. Complete LGTM rebuilds, whole-stack compatibility, licenses/SBOM/provenance and final scans remain required before adoption. No production deployment, registry publication, protected-main merge, general ignore file or vulnerable-code risk waiver is authorized.

Primary references: https://grafana.com/security/security-advisories/cve-2026-21728/ ; https://grafana.com/security/security-advisories/cve-2026-28377/ ; https://raw.githubusercontent.com/openvex/spec/main/OPENVEX-SPEC.md ; https://trivy.dev/docs/dev/supply-chain/vex/file/ .
