# Start Tunner for local browser testing

Double-click **start-browser.cmd** in the repository root. It prepares and starts the current registration application and opens two tabs: the application and its Mailpit verification inbox. Run **stop-browser.cmd** to stop this checkout's test host and its two containers. PostgreSQL data and protected keys are preserved. No reset or deletion is included.

The first run downloads pinned packages/images if needed. Required installed prerequisites: Windows PowerShell5.1 or PowerShell7, .NET SDK10 matching global.json, Node>=24.15 and<25, pnpm11.19.0, Docker Desktop with Compose. The starter launches Docker Desktop when it can locate the installed application and waits for readiness. It does not install system runtimes or change Docker projects belonging to other applications.

Local HTTPS is required by the real BFF. On the first run Windows may ask you to trust the existing ASP.NET localhost development certificate. Approve that prompt for normal browser use. The starter changes no machine-wide execution policy; the CMD launchers set script policy only in their own PowerShell process. Readiness pins the exact development certificate with SHA256 and never changes global TLS validation.

## Browser test sequence

1. Open the application address printed by the starter. The preferred address is https://localhost:24443/registration.
2. Use a unique test email (for example a new name at example.test), a password of at least15 characters, confirmation and the terms/privacy checkbox. The local fixture blocklist rejects passwords containing the word password.
3. Open the local Mailpit inbox tab and copy the six-digit verification code. All email stays in this local test sink.
4. Enter the code, complete names and required DOB within Personal Information, then continue to Account Ready.

Available now: registration, email verification, Personal Information/DOB and Account Ready. Sign-in, authenticated sessions, connected Product access and later P2 features remain under development. This starter does not present an unfinished sign-in flow as complete.

## Services and ports

The starter starts the services actually required by this isolated registration composition: PostgreSQL, Mailpit and the ASP.NET User BFF serving the already approved packaged UI. It derives image/health defaults from infra/docker/compose.yaml and its committed .env.example. It does not read operator .env.local, reuse the foundation's volume names, or require unrelated RabbitMQ/Redis/OpenBao/Grafana services for this synthetic host. The existing full foundation starter remains tools/dev/tunner-dev.ps1 start for other development scopes.

| Service | Preferred host port | Container port |
|---|---:|---:|
| HTTPS application |24443|Host .NET process|
| PostgreSQL18.6 (current pinned image)|25432|5432|
| Mailpit SMTP|21025|1025|
| Mailpit inbox|28025|8025|

If a preferred port is occupied on first setup, the starter chooses a free nearby port and prints/saves the actual address. It never stops an unrelated port owner. Subsequent starts reuse saved ports; an unexpected later conflict is reported. Each checkout has an isolated Compose project and volume; the database is tunner_browser_testing.

## Commands

From PowerShell in the repository:

~~~powershell
./tools/dev/tunner-browser.ps1 start
./tools/dev/tunner-browser.ps1 status
./tools/dev/tunner-browser.ps1 restart
./tools/dev/tunner-browser.ps1 logs
./tools/dev/tunner-browser.ps1 stop
~~~

Start is idempotent. Use restart to rebuild after code changes. The host runs hidden; the launcher console may close after startup. Stop verifies recorded process identity/start time and targets only this checkout's Compose project. No volumes or keys are removed. Runtime state, keys and diagnostics live in ignored artifacts/browser-testing.

The -NoBrowser -SkipCertificateTrust switches support automated local validation: browser opening and OS trust installation are skipped, while starter readiness still pins the exact certificate. Manual browser use should use the normal trusted startup path.

## Isolation and authority

The user explicitly selected isolated local browser testing. The separate tools/browser-testing host opts into Development and a local-test command; Production execution is rejected. Existing real BFF HTTPS/origin/CSRF/registration/migration behavior is reused. Synthetic policy, content-publication, jurisdiction/consent and password-list adapters are test fixtures derived from existing tests, with no production policy/legal publication implied. The content proposal stays DRAFT. Personal data should be synthetic. Data Protection keys are isolated and protected with the current Windows user; the network key is stored only as a protected local payload. No live email/provider credentials are requested.

No production BFF composition, approved Figma layout or business workflow is changed. Existing P2 production/visual/release gates remain in force.

## Validation

The dedicated browser suite uses real HTTPS requests, real PostgreSQL migrations and SMTP delivery into Mailpit; it does not mock registration responses. It also verifies original origin/CSRF guards. The PowerShell safety suite verifies port collision handling, PID protection, exact TLS pinning, Production refusal and isolated services. Committed results and source SHAs are recorded in governance/evidence/TUN-P2-004-BROWSER-STARTER-VALIDATION.json.
