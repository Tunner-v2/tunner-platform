# Tunner Agent Entry Contract

**Status:** PRE-P0 AUTHORITATIVE TEMPLATE
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

## Mandatory startup
```text
tunner authority verify
tunner governance status
tunner governance next
tunner context build --current
tunner context verify
```
Then read the current work item, generated context manifest, only relevant authority/decisions/flows/contracts, activated skills, relevant Git history, and current test/evidence state. Do not implement until governance reports the work eligible.

## Mandatory operating rules
- Never invent business semantics, Product decisions, public contracts, legal/compliance values, money rules, provider finality, UX states, or security behavior.
- Material ambiguity becomes a scoped blocker/decision request.
- Governance controls orchestration; the orchestrator cannot bypass gates.
- No free-floating TODOs.
- Material external decisions require current primary-source R&D.
- Authority changes use ADR/PDR/amendment workflow.
- Implement small vertical slices.
- Preserve Git/evidence traceability.
- Refresh project state/context at handoff.
- Use `INSUFFICIENT_CONTEXT` instead of guessing.
- Repository access and prompt context are separate; load the smallest sufficient context.
- Repository location is runtime context, never authority: persist in-repository paths relative to `<repo-root>` with `/` separators and resolve the root through the governed resolver.
- A PR awaiting human approval is a protected-main integration gate, not a general development stop: run governance next and continue all eligible local/branch, validation, evidence, or independent work; block only scopes whose dependency explicitly requires `MERGED_TO_MAIN` or another genuine human-authority gate.

## Completion contract
Before DONE: implementation/docs/contracts complete; required tests pass; mandatory role reviews pass; applicable security/compliance/financial/UX/SDK evidence exists; no blocker remains; TODOs/defects are recorded; traceability/evidence updated; context/project state refreshed; exact next action recorded.

## Handoff
Record branch/commit, work state, changes, tests, failures, blockers, decisions, TODOs, evidence, context freshness, activated skills, and exact next action. A new contributor must resume without chat history.
