# OpenBao bootstrap correction

Revision: P0-007-RV001. Original TUN-P0-007 remains DONE.
Branch: `change/TUN-P1-001-platform-foundation-refinement`.
Validated and tested SHA: `4f74987590668437637faffd212a3b169b38dcc6`. Resolve metadata closure with `git log -1 -- governance/revisions/P0-007-RV001.yaml`.

A missing `tunner/` property in an empty/first-run mount listing caused PropertyNotFoundException under StrictMode before the existing approved mount-creation command could run. The helper now checks `PSObject.Properties['tunner/']`, which safely returns null for an absent member. Existing mount/configuration/policies, secret handling and original closed records remain intact.

Actual validation: all nine complete-helper scenarios pass in PowerShell 7.6.5 and Windows PowerShell 5.1.19041.6456 (18 scenario executions). The tests use isolated command doubles and synthetic private input: no real root token, mount/policy mutation or R2 access. Coverage includes absent/existing mounts, provider/JSON failures and unchanged policy content. Secrets source validation, secret scan and SAST pass (1243 files, no matches). Exact local receipt hashes, Git object proofs and prior attempts are retained in [revision validation](../../governance/evidence/P0-007-RV001-VALIDATION.json). Windows PowerShell's final test process uses RemoteSigned; machine/user policies are unchanged. Reviews are executing-agent scoped assessments using the seven required role skills, without an independent human approval claim.

Operator next action: rerun `./tools/dev/openbao-bootstrap.ps1 apply-policies` from the repository root and enter the root token privately at its secure prompt. No reinitialization is needed. Unseal only if `bao status` reports sealed. A successful helper run creates the existing approved local mount when absent and applies its existing policies. Actual operator success has not been observed by these tests.

R2 record provisioning, scoped installer authorization and dedicated non-production bucket confirmation remain separate open prerequisites under DEF-TUN-P1-023-R2-001; P1 acceptance stays pending.

Initial dirty generated context/project paths and pre-existing untracked `.vscode/settings.json` remain preserved and excluded from the revision's commits. Only the helper and its full-script regression test are behavior-owned. Final native context/project state is refreshed at closure; original P0 milestone, TUN-P0-007 and earlier closed revisions are unchanged.
