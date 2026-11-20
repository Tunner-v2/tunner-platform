# Tunner Agent Entry Contract

**Status:** GOVERNED REPOSITORY ENTRY CONTRACT
**Applies from:** repository bootstrap
**Authority:** Baseline 1.6.0 + active approved amendments/ADR/PDR records

## Purpose
This is the concise entry map for every human or AI contributor. It never replaces FRDs, PDRs, ADRs, FigJam, or role skills.

## Authority order
1. Approved Development Documentation + applicable approved amendments/Product Decisions.
2. Synchronized Tunner FigJam architecture/workflow contracts.
3. Approved Figma Admin/User UX as implementation reference.
4. Implementation/code.

Canonical resources before repository creation:
- Development Documentation: https://drive.google.com/drive/folders/1sDBE-k3fdIKw2psi7Ul7Ty1u3yNj1FgH
- Decisions: https://drive.google.com/drive/folders/1Z8hgTUhtANFFgXUnU71f0pnfpQhab6wi
- Architecture FigJam: https://www.figma.com/board/bdJwXt1U7n3iyluSD21OYK/Tunner-%E2%80%94-Platform-Architecture
- Platform UX: https://www.figma.com/design/apNbwvAgdn1xIxYH1oiaSC/Tunner-Platform-UX-Master
- Admin/Support UX: https://www.figma.com/design/3F9tAviUejTYpYvRJt8gM1/Tunner-Admin---Support-UX-Master
- Active amendments:
  - AMD-0001: https://drive.google.com/file/d/15MYKfh7zplOWoz80FvgPJOPi-Y6GfI7H/view
  - AMD-0002: https://drive.google.com/file/d/1dmODTNgtRS7WbKZyuX__FVmuJKQplkxN/view
  - AMD-0003: https://drive.google.com/file/d/1N8GLvNiRkkm5hEgIIvJzJo_7DNpBJYJD/view
  - AMD-0004: https://drive.google.com/file/d/1ZBHXj1ndc5x341fRxEsva06Z2vpUPWzn/view
  - AMD-0005: https://drive.google.com/file/d/1M_B4TYwf5gDKaZ-1w_gMR9Nse-Fs7B87/view

## Global UI contract (AMD-0008 / AMD-0009)

Approved Figma controls visual presentation; FRDs/PDRs control domain behavior. This applies to every existing and future Tunner-owned User, Admin, Billing, Finance, Support, Product self-service and operator UI. Resolve docs/contracts/ui-inventory.json, owning FRD and actual Figma components before frontend work. Independent full screens use canonical pathnames with guarded deep links, reload and browser history; same-screen interactions may use safe local/query/fragment state. Never put secrets or personal information in URLs.

Reuse one typed form/validation capability and approved Figma field/error variants; independent backend validation remains authoritative. Native constraint bubbles and handwritten per-screen error styles cannot substitute. Require URL-001..006, VAL-001..008, and UX-FID-001..008 evidence for each applicable route/form/component, with committed source binding and rendered/reference screenshots. Missing designs/material deviations require explicit Product Owner consent, not AI approval. Record UI_ROUTING_DEFECT, UI_VALIDATION_DEFECT, UI_FIDELITY_DEFECT and UI_REQUIRED; block affected acceptance until resolved. A passing registration form cannot approve another scope. Use the native tunner governance ui check gate. Preserve historical closure through RV lineage and the P0 enablement boundary.

## Mandatory startup
```text
tunner authority verify
tunner governance status
tunner governance next
tunner context build
tunner context verify
```
Then read the current work item, generated context manifest, only relevant authority/decisions/flows/contracts, activated skills, relevant Git history, and current test/evidence state. Do not implement until governance reports the work eligible.

## Mandatory operating rules
- Never invent business semantics, Product decisions, public contracts, legal/compliance values, money rules, provider finality, UX states, or security behavior.
- Material ambiguity becomes a scoped blocker/decision request.
- Before creating any decision/amendment request or `NEEDS_PRODUCT_DECISION` / `CONTRACT_REQUIRED` / `TECH_DECISION_REQUIRED` blocker, perform and record an AMD-0005 Authority Resolution Pass across applicable verified Development Documentation/FRDs, ADR/PDR/amendments, contracts, flows/architecture and governed decision history. Reuse existing authority; if context is insufficient, expand it rather than declaring a gap. An untriggered selective technology is not a missing contract.
- Governance controls orchestration; the orchestrator cannot bypass gates.
- No free-floating TODOs.
- Material external decisions require current primary-source R&D.
- Authority changes use ADR/PDR/amendment workflow.
- Preserve historical closure: later work against a legitimately closed milestone/work item uses governed `RV` Revision lineage (`P0-RV001`, `P0-004-RV001`, etc.) rather than overwriting the original; an RV never substitutes for an authority change.
- Implement small vertical slices.
- Preserve Git/evidence traceability.
- For repository-changing work, commit all work-item-owned changes before `LOCAL_VALIDATED`, `READY_FOR_MERGE`, `DONE` or completed handoff; record the validated/tested SHA, preserve unrelated dirty paths without absorbing them, and revalidate if behavior-affecting files change after the validated SHA.
- Refresh project state/context at handoff.
- Use `INSUFFICIENT_CONTEXT` instead of guessing.
- Repository access and prompt context are separate; load the smallest sufficient context.
- Treat flow-to-code mapping as many-to-many: reuse cohesive shared technical primitives/formatters/canonicalizers instead of reimplementing mechanics per flow/screen/module; preserve domain-owned business semantics and server authority for persisted canonical values.
- Repository paths are location-independent: never hardcode a developer/CI/container absolute checkout path; persist in-repository references relative to `<repo-root>` and resolve the root dynamically through the governed path resolver.
- A PR awaiting human approval is a protected-main integration gate, not a general development stop: run governance next and continue all eligible local/branch, validation, evidence, or independent work; block only scopes whose dependency explicitly requires `MERGED_TO_MAIN` or another genuine human-authority gate.

## Completion contract
Before DONE: repository-changing work is anchored to a validated committed SHA with no work-item-owned uncommitted changes; implementation/docs/contracts complete; required tests pass; mandatory role reviews pass; applicable security/compliance/financial/UX/SDK evidence exists; no blocker remains; TODOs/defects are recorded; traceability/evidence updated; context/project state refreshed; exact next action recorded.

## Handoff
Record branch/commit, work state, changes, tests, failures, blockers, decisions, TODOs, evidence, context freshness, activated skills, and exact next action. A new contributor must resume without chat history.
