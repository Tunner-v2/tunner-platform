# Review candidate v2 — not executed

This additive version preserves the original helper and its manifest SHA256 `71e5a575ac90d5a8fadefedcb8df7557131f011c0fee2eae66ad38bebd38b4de`. Root review found the original artifact assertion used the resource ID prefix for `operationReference`. The actual generic operation supplies `OP-LOCAL-STEP-UP-<sessionN>` as both operation ID and correlation ID; `LOCAL-VERIFICATION-*` is its resource ID. V2 asserts the actual correlation shape and retains the exact options-to-completion comparison. It also requires `totpAvailable === false` and `passwordAvailable === true` in ordinary options. No source, Product rule or expected domain result changes.

V2 changes only these assertions and the relative imports/paths needed for this version directory. The first candidate was never executed and remains immutable. `prepare-v2.py` records both lineages and checks both Node scripts without launching resources.

After explicit root review and a current complete source/build binding, the proposed execution command is:

```text
node artifacts/p2-008-totp-20261110/legacy-host-smoke/v2/run-reviewed-smoke.mjs --execute-reviewed
```

The resource, privacy, cleanup and scope boundaries in the original preparation README still apply. This file and the preparation manifest are not runtime evidence.
