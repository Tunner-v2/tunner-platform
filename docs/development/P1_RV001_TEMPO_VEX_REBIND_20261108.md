# Exact Tempo applicability rebind proposal —2026-11-08

Status: PROPOSED; explicit consent pending. No active VEX/approval/binding changed.

The actual x/net issue is repaired by rebuilding the same approved Tempo13.2.4 data-source from cdacb742bae90fbf62c48c5cca0eacdc08fd2acd with x/net0.62.0. This changes its executable hash from963c415f3eb286c4d9a1001cab275d9563d4bd4b548ad5b6dd84953e17be61b9 to **36d85399e4d2fa0e38e8d33ea6f6171ee3fc494184b3275dcd322b1e268d91c7**.

The original approval explicitly excludes “Any other CVE, package version, plugin or binary”. See governance/evidence/P1-RV001-001-TEMPO-VEX-APPROVAL-20261028.json. The existing guard actually rejects this changed executable: “rebuilt executable hash differs from reviewed binary”. A general proceed instruction does not remove that exact scope restriction.

Requested scope: rebind only **CVE-2026-21728 and CVE-2026-28377** to the above exact new executable for private dev/test. Keep the original expiry **2026-11-27T00:00:00Z** and all source/hash/image/context/tests/review guards. This does not waive the real x/net vulnerability, add any CVE, extend expiry or authorize production, publishing, release or main merge.

The same verified Tempo3.1.1 source9db7df79d2af105a90d96484e6403e1fda0d011e has module sum h1:RWARH/pJYWYyhMan2XnvecRlYxnWf4Wa1AmY067hI4I=. The scanner compares its historical Go pseudo-version v1.5.1-0.20261021152436-9db7df79d2af with older ranges. The actual linked-package inventory contains only four Tempo protobuf packages: pkg/tempopb and its common/v1, resource/v1 and trace/v1 children. The affected query/config-serving code is absent. This is an executing-agent applicability assessment, not vendor attestation.

At committed4b0e618, all90 upstream plugin tests and69 helper/signing/rejection tests pass with zero failed/skipped cases. Fresh raw scan covers76 Go packages and contains exactly the two stated Tempo alerts. No filtering was performed. Actual old-approval/new-binary rejection is retained separately. Complete LGTM acceptance remains pending.

Review evidence: governance/evidence/security-20261108/tempo-rebind-proposal/proposal.json binds the raw report, exact source/build metadata, Go sums, full linked-package list, tests, rejection output and proposed inactive VEX by SHA256. The original historical approval/evidence remains immutable.

After specific consent: record its actual provenance, create a separate new source/hash/test/review binding, revalidate the current typed rejection suite and actual full-image consumer, retain raw and corrected reports, then execute all41 private-stack compatibility checks. Other LGTM security builds continue while this proposal is reviewed.
