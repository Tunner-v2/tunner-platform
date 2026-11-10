# Values-redacted SameSite diagnostic

`findings.json` retains actual observations from two fresh synthetic WebKit actor contexts. `assessment.json` interprets required omission from **actual response attributes**. No password, verification code, cookie value or full Cookie/Set-Cookie header was recorded.

Actual server responses set User Session **Lax**, StepUp **Strict**; both are Secure and HttpOnly. WebKit projects both as None immediately after the ordinary StepUp options response, **before any route.fetch**, and after ordinary proof. The separate intercepted-proof actor also projects None before/after route.fetch and abort. Interception is not the sole cause of that projection.

The ordinary no-interception behavior probe has valid controls: same-origin browser-clicked top-level GET sends Session, StepUp and public synthetic Lax; a browser-clicked top-level GET from the distinct site `127.0.0.1` to the same `localhost` target also sends all three. StepUp was actually sent cross-site despite its Strict header. User Session is Lax, so its cross-site GET presence is permitted and is not classified as a violation.

The routed scenario's header observer returns no cookie names even for its successful200 same-origin control; its behavior observation is **inconclusive**, not proof of cookie omission. The original helper's raw combined boolean additionally tested Session absence; use the header-derived `assessment.json`, not that raw boolean, to interpret the actual Lax Session.

The owned temporary attacker listened only on127.0.0.1 at an ephemeral port and served one plain navigation link. The target remained `https://localhost:24443/authentication/session`, preserving the localhost cookie domain. Observations were browser navigation request name-presence booleans before landing; APIRequestContext was not used to simulate SameSite sending. A public constant Lax probe was added only to the test context after all pre/post proof snapshots. No real cookie policy/header changed and BFF responses were not mocked.

The diagnostic completed with process exit0 as an **observation**, not a security PASS. The ordinary Strict omission result is FAIL and the original WebKit test failure remains retained. Pinned port/source attribution and compatible WebKit/Safari runner follow-up belong to root. Both contexts/browser and the owned HTTP helper closed; the Tunner HTTPS host stayed running.
