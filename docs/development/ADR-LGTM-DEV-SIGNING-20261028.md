# ADR-LGTM-DEV-SIGNING-20261028 — dev/test LGTM plugin verification

Status: approved implementation scope; technical verification and adoption pending.
Work item: P1-RV001-001. Revision: P0-RV010. Baseline 1.6.0 remains locked.

## Authority and decision

The direct Product Owner reply “Approve dev/test signing extension” approves the exact five-part packet recorded in governance/evidence/P1-RV001-001-LGTM-SIGNING-APPROVAL-20261028.json. Earlier vendor-only trust findings remain historical. This local ADR implements that bounded extension; it does not edit Drive authority or allocate a canonical upstream ADR number.

Applicable authority: Baseline documents 08/33, ADR-0008/0009, current document09, the maintained-build consent and AMD-0005 authority-resolution receipt. The complete LGTM stack remains required. A changed executable cannot retain an original vendor-signed manifest hash. Latest vendor LGTM0.38.1 has87 fixable HIGH scan entries; no finding is waived by this decision.

## Exact verifier contract

Keep Grafana's existing vendor signature path intact. Before that path, recognize an explicitly named Tunner rebuild manifest only for a compile-time allowlist of exact plugin IDs and versions. A Tunner signature must never use a vendor identity or describe vendor endorsement. Standard cryptographic signatures authenticate the exact manifest bytes; SHA-256 authenticates every plugin file. Reject unknown schema fields, duplicate paths/keys, unsafe paths, symlinks, missing/extra files, wrong plugin/version, unknown keys, forged signatures, modified files and revoked key versions. The public-key fingerprint, key version, allowed plugin/version pairs and revocation state are fixed in the reviewed build; arbitrary runtime key downloads or environment overrides cannot extend trust.

Use a separate explicit Tunner signature filename, preserve the original vendor MANIFEST.txt as provenance and include it in the signed hash inventory. Any invalid Tunner manifest on an allowed rebuilt plugin fails closed; it cannot fall back to a stale vendor manifest. Untouched vendor plugins retain vendor verification. Report Tunner rebuild identity as the signing organization. Keep the production runtime outside this scope; the resulting image is labelled development/test only.

## Key custody and build boundary

Prototype/CI uses a fresh synthetic signing key generated only inside a disposable build process. Its private material exists in a non-exported secret/tmpfs mount, is never logged, never enters a Docker layer, Git, output receipt or final image. Only the public key/fingerprint and signed manifests are exported. Such images establish synthetic dev/test trust only. Operator-managed trust requires a separate reviewed explicit key configuration and custody evidence; no production key is requested or invented here. No global unsigned-plugin setting, development-mode trust bypass or trusted arbitrary key download is permitted.

## Rebuild and adoption contract

Pin official release source artifacts, toolchain and dependency repairs; preserve all plugin IDs, frontend assets, service APIs, ports, configurations, storage semantics, licenses and source provenance. Rebuild the full vulnerable executable inventory, not just the PostgreSQL plugin. Record OCI provenance and an SBOM. Before changing LGTM runtime defaults: pass positive verification plus wrong-key/wrong-plugin/revocation/manifest/file-tamper/extra-file tests, prove unchanged vendor verification, run complete stack/plugin compatibility and actual full-image fixable HIGH/CRITICAL scans, and record architect/security/governance review against the tested committed SHA. Until then the existing failing LGTM/container gate remains enforced.

## Primary sources checked 2026-10-28

- https://grafana.com/developers/plugin-tools/publish-a-plugin/sign-a-plugin
- https://github.com/grafana/docker-otel-lgtm/releases/tag/v0.38.1
- https://raw.githubusercontent.com/grafana/grafana/v13.2.3/pkg/plugins/manager/signature/manifest.go
- https://raw.githubusercontent.com/grafana/grafana/v13.2.3/pkg/plugins/manager/signature/signature.go

No production deployment, publication, protected-main integration or release is authorized.
