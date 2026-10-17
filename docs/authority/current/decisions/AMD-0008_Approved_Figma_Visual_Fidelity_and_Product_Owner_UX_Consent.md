# Tunner Development Authority Amendment


> **Status:** APPROVED / ACTIVE — Product Owner direction in this conversation, 2026-10-17
> **Effective date:** 2026-10-17
> **Target baseline:** Tunner Development Documentation Baseline 1.6.0
> **Applicability:** GLOBAL across User/Account, Admin/Support and Connected Product-facing Tunner UI/UX, frontend components, prototyping, AI-assisted design and UI release acceptance.
> **Change class:** UX | VISUAL-SYSTEM | GOVERNANCE | QUALITY
> **Non-goal:** No alteration of existing business/domain/security/compliance authority. The immutable baseline archive and prior decision history remain unchanged.


# AMD-0008 — Approved Figma Visual Fidelity & Product Owner UX Consent


## 1. Product Owner instruction and governing principle


Approved Tunner Figma design is the controlling **visual/interaction design specification** for the corresponding screens and shared components. Approved development FRDs/ADRs/PDRs remain controlling for business behavior, security, eligibility, data, workflow and other nonvisual semantics; neither visual fidelity nor frontend preferences may override those rules. **The authority hierarchy is domain-sensitive: FRD controls behavior; approved Figma controls visual presentation.** A conflict requires a documented resolution and Product Owner approval of the proposed design change, not unilateral AI/developer substitution.


The entire screen design must be faithfully reproduced. Backgrounds, visual atmosphere and decorative layers are required parts of the approved composition whenever shown in Figma, not discretionary embellishments.


## 2. Approved references and initial traceability


- User Platform Figma: https://www.figma.com/design/apNbwvAgdn1xIxYH1oiaSC/Tunner-Platform-UX-Master
- Admin/Support Figma: https://www.figma.com/design/3F9tAviUejTYpYvRJt8gM1/Tunner-Admin---Support-UX-Master
- Existing User Figma screen examples: Login frame `2:4`, registration-create-account `70:270`, registration-verify-email `70:539`, registration-account-ready `70:630` (these are traceability examples, not the full screen inventory).
- Approved login and registration layouts include glow/background layers `bg-glow-indigo` and `bg-glow-violet`, full-page composition, navigation/header/footer, cards, fields, buttons and visual states. Omission of these approved visuals is `UI_FIDELITY_DEFECT`.


A Figma file/node link, its approved version or reviewed snapshot, matched UI screen ID and relevant component/token references must be attached to each screen implementation work item. If approval/version is ambiguous, verify the intended approved design before implementation; do not substitute a different style.


## 3. Protected visual design system


For every approved screen/component, implementation **MUST** match the approved design at its applicable target viewport in all material visual dimensions: page background, glow/gradient, illustrations/assets, layering and opacity; overall composition/grid; panels/cards; typography, fonts, weights, sizes, tracking, line-height; colors/contrast; spacing and margins; alignment; borders, radius and shadows; icons; navigation, header and footer; responsive intent; labels, placeholders and helper/error text presentation; field width/height/padding and focus/error/success/disabled states; buttons, checkboxes, selection controls and states; modals, loading/empty/result views and motion when defined.


Do not silently omit, simplify, replace, restyle, or genericize approved elements. Do not swap an approved Tunner style for TailAdmin default styles. React + TypeScript + Vite + TailAdmin React remains the locked implementation foundation, but **Tunner's approved Figma visual system overrides TailAdmin visual defaults** through governed shared components and design tokens.


Existing approved field components must be reused wherever their semantic behavior matches. New fields/forms/screens inherit approved components and visual tokens. A lack of an exact reusable component is not permission to invent a new incompatible pattern.


## 4. Product Owner consent and AI authority boundary


- **Existing approved screen or element:** reproduce it; no unilateral redesign, simplification, background omission or visual substitution.
- **Existing screen requiring an additional field, state or flow:** reuse approved components when suitable; if the approved Figma layout must materially change, prepare a design diff/proposal and obtain explicit Product Owner consent before treating the deviation as approved.
- **Missing Figma screen or novel visual pattern:** use the existing `UI_REQUIRED` gate; AI may research, recommend and prepare candidate design(s) consistent with the approved system, but cannot self-approve, mark the proposed design APPROVED, or implement it as final production UX without the Product Owner's explicit approval.
- **Conflict between Figma visual representation and a new/active FRD/PDR requirement:** do not implement visually contradictory states as-is and do not overwrite business authority. Record the exact mismatch, propose the least disruptive on-theme change, obtain Product Owner design approval and then update Figma/traceability before the affected production UI is unblocked.
- **Purely internal engineering details with no visible impact:** remain delegated to engineering under existing development authority.
- **Accessibility/responsiveness:** must be implemented; if an adjustment materially alters approved composition, take it through the same controlled visual-approval process. Avoid using accessibility or responsive adaptation as an undocumented redesign pretext.


Product Owner approval must identify the screen/component, proposed before/after or Figma node, rationale, approved scope, approver, date and approval evidence. Silence, a generic AI/QA signoff, an automatically generated design, or absence of rejection is NOT approval. AI may draft and recommend changes but cannot impersonate or presume Product Owner consent.


## 5. Fidelity verification and release gate


For each implemented User/Admin/Connected Product screen:


1. Resolve current FRD/business requirements and the approved Figma screen/component/node IDs. Build a screen-to-Figma-to-FRD traceability map.
2. Extract/confirm Figma tokens, component geometry, states, assets/background effects; implement governed shared tokens/components rather than approximating them independently per screen.
3. Compare actual rendered screenshots side-by-side against Figma at the matched reference viewport, plus test documented responsive breakpoints and interaction states.
4. Review backgrounds/gradients/glow, typography, field styles, spacing/alignment, color, shadows/radii, icons, navigation/footer, button/field content alignment, text clipping/overlap, and visibility of mandatory components.
5. Run functional, accessibility, keyboard/focus and responsive UI tests; ensure correct behavior and content under applicable FRD.
6. Record fidelity evidence with links to the Figma node/version, actual screenshot(s), test results, approved exception(s), issue IDs and reviewer identities. Visual diff thresholds may be configured only with documented approval; no tolerance permits an entirely missing visual layer or a generic substitute.
7. **Block `UI_VISUAL_APPROVAL` / production UI acceptance** for an unapproved material mismatch, missing Figma reference, missing required background/asset/state, unapproved novel component or missing Product Owner consent. A successful build/functional test alone cannot pass the UI release gate.


For already-implemented nonconforming screens create scoped `UI_FIDELITY_DEFECT` work items and preserve history; do not imply prior screen completion means the approved design has been reproduced. When no matching design exists, use `UI_REQUIRED`; do not label missing design as a purely cosmetic issue.


## 6. Initial application and known contract mismatch


Login, Registration, Verify Email and Account Ready are **existing designed screens**, not blank-slate frontend assignments. The background-glow layers and full composition are required. The login Figma snapshot shows a GitHub social sign-in button, whereas current U-AUTH-01/Identity requirements specify Google and Microsoft first external identity adapters. This is a **verified visual/content-versus-functional contract difference**, not a license to choose either behavior autonomously. The identity FRD controls supported provider behavior; the Product Owner must approve how Figma is adapted visually before the production screen differs from the current approved layout.


PDR-0011/AMD-0007 requires age-aware post-contact-verification Personal Information and no DOB-first/18+ registration gate. Preserve the approved registration visual language while resolving any missing/changed fields/states through the consent and Figma revision path. Hidden obsolete layers are not active visual authorization.


## 7. Documentation & tooling propagation


Add this rule to Document 10 (component/style system and UX development), Document 31 (screen matrix, `UI_REQUIRED` and fidelity acceptance), Document 12 (AI agent permission/mandatory UX reviewer duties), Document 00 (domain-sensitive authority and Product Owner UX approval) and README/Document 37 current effective authority index. Update governed agent instructions, UI/UX/frontend roles, work-item schema, review/CI visual evidence and repository authority mirror when development bootstrap/governance is materialized.


Acceptance controls: `UX-FID-001` approved existing screen faithfully reproduced; `UX-FID-002` background/glow omitted => defect/block; `UX-FID-003` new field reused existing design tokens => pass when matching; `UX-FID-004` new pattern without consent => block; `UX-FID-005` UI/FRD conflict => documented and approved before affected UI release; `UX-FID-006` TailAdmin default overrides Figma style => defect; `UX-FID-007` responsive/accessibility work proven without unapproved design drift; `UX-FID-008` visual regression record links approved Figma/FRD/screenshots/consent.


No bulk Figma redesign and no change to production code is authorized by this amendment alone. Implementations and Figma changes must be separately governed and evidence-backed.