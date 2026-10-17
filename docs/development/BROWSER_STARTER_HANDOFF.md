# Browser starter handoff

Branch: codex/p2-004-registration. Tested source: ccb82f64a12cfed08c4f30a0ca763e8205b704ce.

User explicitly selected isolated local browser testing. Root start-browser.cmd prepares dependencies/builds, starts isolated PostgreSQL/Mailpit and the real HTTPS BFF, applies platform then Identity migrations, checks readiness and opens application/inbox tabs. stop-browser.cmd preserves data and touches only this checkout's resources. Status/restart/logs commands are in docs/development/LOCAL_BROWSER_TESTING.md.

Current local addresses: https://localhost:24443/registration and http://127.0.0.1:28025. PostgreSQL18.6 uses127.0.0.1:25432 ->5432, database tunner_browser_testing. These are generated local state; the script dynamically resolves the checkout and chooses free preferred ports on first setup. Runtime state/keys/Compose JSON/logs are ignored under artifacts/browser-testing.

Current validation:2 real HTTPS/PostgreSQL/SMTP browser cases,7 Windows PowerShell5.1 safety checks,237 host architecture tests, idempotent start and data-preserving stop/restart/build/migration readiness all pass. Retained data count3 confirms synthetic registrations survive restart. No Production/User BFF entry point or UI source changed;59 original behavior paths match the original validated SHA. Old395 .NET/26 web/21 browser receipts retain that SHA and are not reattributed as new test execution. New dedicated test and receipt hashes are committed.

The first restricted architecture run failed certificate import; the unchanged host-authorized fixture passes237. Development packaging, Compose-array and migration omissions were corrected; final browser testing explicitly checks HTTP202 and actual Mailpit code delivery. No expectation was relaxed.

Normal browser certificate trust is complete after the user explicitly approved the existing localhost certificate. The initial auto-review rejection remains recorded as history; the original normal start command succeeded without a workaround. dotnet dev-certs confirms the existing CN=localhost certificate is trusted. Default Windows HTTPS validation returns200 for the application; Mailpit returns200 and the test host reports ready. Setup receipt: artifacts/browser-starter-20261017/approved-certificate-receipt.json; its hash and direct authorization are recorded in the committed browser-starter validation.

Native readiness is READY at evidence checkpoint1ed2203; authority verifies195 artifacts and governance validates672 records without diagnostics. Source tests remain bound to ccb82f6.

All work-item-owned code/evidence is committed; unrelated dirty context/project/editor files remain preserved. Refresh context only into artifacts/browser-starter-20261017/context. Exact next action: user manual browser testing through start-browser.cmd and the documented registration/verification/Personal Information sequence; use stop-browser.cmd to stop only this checkout services while preserving data. P2-005 authentication/session refinement and TODO-AMD0008-GOV001 remain separately governed future work; no milestone closure, legal/content publication or production acceptance is implied.
