# Ordinary host compatibility smoke — prepared only

This artifact runner targets the unchanged `--local-browser-testing` executable and its default `Totp:null` composition. It does not build or modify source and does not invoke the starter, image ensure, pulls, logs or container environment inspection. Root review is required before the explicit execution command below.

`prepare-review.py` records exact runner/import hashes, current DLL observation and the 13 unrelated dirty-path checks. It runs only Node syntax checks. The runtime also requires the root-generated complete source/output build binding before creating resources and rechecks it after the actual journey. The preparation manifest is not runtime evidence.

After root review and current build binding, the proposed command is:

```text
node artifacts/p2-008-totp-20261110/legacy-host-smoke/run-reviewed-smoke.mjs --execute-reviewed
```

The runner creates a fresh positively labeled PostgreSQL container using the exact cached c6a image, loopback random port, `POSTGRES_USER=tunner`, synthetic trust authentication, and tmpfs at the PostgreSQL 18 data root. No password is supplied or discovered. The ordinary executable receives an owned empty pgpass file instead of any operator passfile. Its hardcoded `tunner_browser_testing` database is isolated inside this new container. The cached 0306 Mailpit receives separate loopback random SMTP/API bindings, no mounts and logging none. Both containers use `--pull=never`, exact immutable local image IDs and explicit owner-checked removal. Existing PostgreSQL is observed through filtered Docker metadata only and compared after cleanup; it receives no SQL or lifecycle action.

A generated canonical owned temp directory contains only public normal-host config, the empty private Docker config/pgpass, and normal encrypted Data Protection/network-key persistence. The executable remains unmodified. Its stdout/stderr are consumed without display or persistence. Readiness uses the same approved-certificate pin boundary as the starter. The raw browser independently requires normal trusted HTTPS and never sets `ignoreHTTPSErrors`.

The protected raw Chromium child creates account/password/email-verification values in memory, drives actual registration, owned Mailpit verification, Personal Information, password login/reload, generic password proof, completed-owner recovery and logout guards. No route interception, API bypass, screenshot, trace, video, HAR, test reporter, exported browser state or diagnostic attachment is used. Only fixed checks and bounded metadata leave the child. Browser residue is scanned after browser close, including the in-memory private values, then the canonical owned surface is removed. Fixed SQL booleans from the owned database check one account, one owner commit, one consumed proof and no active session after logout.

Cleanup attempts every stage and requires actual process close, owned container absence, unchanged borrowed PostgreSQL metadata and removal of both temporary surfaces. The ordinary executable has no shutdown API; the runner stops only its captured process and reports process exit without claiming graceful ASP.NET shutdown on Windows. It does not add a shutdown route or reflect into host internals.

The application work deadline is four minutes, each CLI/network call is bounded, and the private browser gets a separate two-minute deadline plus bounded cancellation/tree cleanup. Cleanup is separately bounded and always attempted. Any unknown outcome, invalid envelope, raw child output, missing check or cleanup failure rejects the smoke. A successful smoke would prove only this ordinary composition compatibility path, not final TOTP real-journey, Product or release acceptance.
