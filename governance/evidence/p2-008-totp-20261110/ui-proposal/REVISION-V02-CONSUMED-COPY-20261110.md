# V02 already-shown setup copy clarification

Status: public design proposal revision only; Product Owner consent remains pending. Root requested this bounded change after reviewing the pictured packet and will notify the user before any production implementation.

Only the three `setup-consumed` pictures at 1440/390/320 and their corresponding conditional preview copy are revised. The previous “Scan the QR code” page subtitle, heading and instruction were misleading after the one-time QR/key had disappeared. Revised copy:

- Page title: **Finish setting up your authenticator app**
- Subtitle: **Enter the current code from the app you already added.**
- Section heading: **Setup information was already shown**
- The scan instruction is absent. The existing notice still explains that the QR/key are no longer displayed and permits first-code entry only for the original server-confirmed awaiting-first-code enrollment.

This clarifies the same V02 already-shown/one-time presentation scope. It changes no behavior, policy, profile, route, timing, operation identity, QR/key handling, pending/unknown state, other pictured state or production source. Candidate-first and session/cardinality examples remain unselected preview scenarios; the currently supported reset behavior is `RetireAffectedFactorThenSeparateEnrollment`, and an unsupported candidate-first flow must never appear available in production.

The previous three pictures, `preview.tsx`, `REVIEW.md`, `index.html`, `provenance.json`, geometry and Axe reports are preserved under `history/20261110-v02-consumed-copy-before/`. Old/new hashes and the check boundary are recorded in `REVISION-V02-CONSUMED-COPY-20261110.json`. Original approved Figma source evidence is unchanged. The root must reference this revision in the eventual consent disposition; this addendum is not consent.
