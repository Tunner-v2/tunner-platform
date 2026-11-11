# Review candidate v4 — existing system trust

V3 reached actual normal-host readiness with the approved certificate pin and launched pinned Chromium, then failed on the first private HTTPS API request before any account or registration. Its complete failed receipt, helper hashes and cleanup evidence remain immutable in v3. Root's separate public certificate probe found the approved localhost certificate in the existing Windows system store, while Node's default bundled trust did not contain it. This is an environment trust-selection boundary, not an application defect claim.

V4 adds the fixed `NODE_USE_SYSTEM_CA: '1'` only to the spawned private raw child's allowlisted environment. [Node 24.22.1 documents this setting](https://nodejs.org/download/release/v24.22.1/docs/api/cli.html#node_use_system_ca1). It uses the already trusted Windows store; it exports no CA/private key and changes no global trust. It does not inherit arbitrary Node flags, set `NODE_TLS_REJECT_UNAUTHORIZED`, or enable `ignoreHTTPSErrors`. Browser TLS verification and the approved readiness certificate pin remain required. The ordinary executable and all registration/proof/logout expectations are unchanged.

The child and strict parent envelope also add one fixed `failureReason` enum: `NONE`, `TLS_ERROR`, `ACTION_TIMEOUT`, `NETWORK_ERROR`, `PREDICATE_FAILED`, `CLEANUP_FAILED`, `CANCELLED`, `FATAL_ERROR`, or `BOUNDARY_ERROR`. Error classification occurs only inside the protected child. No message, stack, request argument, body or credential is exported. The outer receipt uses `NOT_REPORTED` only when no valid child receipt exists.

After root review and current complete source/build binding, the proposed run is:

```text
node artifacts/p2-008-totp-20261110/legacy-host-smoke/v4/run-reviewed-smoke.mjs --execute-reviewed
```

All prior owned-resource, cleanup, private browser and ordinary-compatibility-only boundaries remain. V4's public readiness lifecycle units pass five cases but do not prove actual system-trusted HTTPS or the pending ordinary-host flow. No actual v4 run has been launched by this preparation.
