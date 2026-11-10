# P2-008 execution regressions, phase 2

This immutable history supplements [phase 1](../../TUN-P2-008-EXECUTION-PROGRESS-20261110.json). The controlling phase 2 receipt is [TUN-P2-008-EXECUTION-REGRESSIONS-PHASE2-20261110.json](../../TUN-P2-008-EXECUTION-REGRESSIONS-PHASE2-20261110.json). Final P2-008 acceptance remains pending.

| Committed source/input | Actual execution | Passed | Failed | Skipped |
| --- | --- | ---: | ---: | ---: |
| 70d19f4 | Focused Identity | 6 | 0 | 0 |
| 70d19f4 | Full Identity, historical fixture failure retained | 253 | 1 | 0 |
| b583842 | Corrected historical upgrade fixture | 1 | 0 | 0 |
| b583842 | Full Identity | 254 | 0 | 0 |
| 183b557 | Expected-red final-authority transitions | 0 | 3 | 0 |
| e79f482 | Same transitions after fix | 3 | 0 | 0 |
| e79f482 | Full Identity | 257 | 0 | 0 |
| Installer input 19617c5, execution HEAD 183b557 | Full Installer/shared vault | 113 | 0 | 0 |

These are actual prior execution artifacts, reparsed and copied without rerunning tests. Source and portable SHA256 values are in the receipt. Normalization removes BOM/trailing whitespace, writes LF, and replaces the dynamically resolved checkout path with a portable marker. TRX markers are XML-escaped. Every TRX remains valid XML with identical counters, test names and outcomes; JSON copies remain parseable. The two Installer test-name occurrences of public invalid endpoint query `secret=x` are source-proven fixtures in `tests/Tunner.Installer.Tests/InstallerTests.cs:48`.

The Installer proof covers seven exact committed input blobs and preserves all old R2 assertions. Passing tests require scoped revocation, policy removal, immutable version behavior, disabled log persistence, loopback binding, no volumes, cached images and verified fixture disposal. The extra local Docker CLI check found zero fixture-owned containers. The initial supplementary URI-spelling failure and corrected check are both retained. Embedded hashes in the copied Installer `validation.json` refer to the original artifacts; the phase 2 manifest separately binds every normalized copy. No source file, image or external system was changed to prepare this evidence.

At reviewed source e79f482, the operation-proof and pending-primary final-authority fixes have three expected-red then green regressions. A separate confirmed consumer defect remains open: `AuthenticationApplication.VerifyTerminationAsync` compares native passkey assurance to a passkey+TOTP session's composite assurance during own-passkey causal termination. Its owner is preparing bounded tests and a fix. The 257 passing cases do not establish coverage of that combination.

The next runtime slice is still unimplemented: finite composition in the existing isolated browser host, the shared disposable vault with only scoped `Secrets` supplied to the real adapter, and a private-pipe native OTP oracle reused from the existing framework helper. The protected raw-Playwright child must evaluate real BFF/native assertions internally and emit only its reviewed fixed predicates. No secret command-line/URL/log/evidence value, HTTP OTP bypass, response mock, alternate seed store or production policy activation is authorized by this receipt.

Chromium public privacy canaries remain separately bounded evidence; Edge's automatic log check remains open without an allowlist or matrix waiver. No real TOTP browser journey, final role acceptance, Product/milestone acceptance, production activation, merge or release is claimed.
