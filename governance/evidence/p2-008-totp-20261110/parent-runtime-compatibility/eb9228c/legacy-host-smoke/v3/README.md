# Review candidate v3 — readiness fixture correction

V2 actually failed at HOST before any browser/account ran. Its failed receipt and all original helper hashes remain unchanged. Its owned containers/config were removed, the owned host exited, and the borrowed PostgreSQL identity was preserved. This failure is retained as evidence; it is not classified as an application defect.

V3 uses a new HTTPS agent-free connection per readiness probe. It captures and verifies the exact approved peer-certificate pin in the response callback while that response owns its TLS socket. It never reads a detached socket at `end` and installs no `secureConnect` listener. Fixed readiness counters/booleans record only the status, pin and expected metadata checks. No body, key, certificate, raw argument or diagnostic text is exported. The browser still independently requires ordinary trusted TLS.

The child adds observed cookie values to its private in-memory residue markers and uses overlap sized to the longest bounded marker. The owned config directory uses `sameCanonicalPath` for its parent boundary. These are artifact boundary repairs; domain expectations and production source are unchanged.

`readiness-public-units.mjs` exercises the actual helper function with public-only socket lifecycle mocks: detached socket at end, already-connected socket, wrong pin, wrong instance and oversized status. Those units create no network, application or fixture resource and do not prove actual readiness. Actual ordinary-host registration through logout remains pending a reviewed run.

After root review and current complete build binding, use this version only:

```text
node artifacts/p2-008-totp-20261110/legacy-host-smoke/v3/run-reviewed-smoke.mjs --execute-reviewed
```

The existing cached-image, ownership, private browser, cleanup and compatibility-only boundaries remain in force. No TOTP real-journey or Product/release acceptance is claimed by this preparation.
