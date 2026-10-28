# AMD-0009 — URL-Addressable Screen Routing & Governed Form Validation




> Status: APPROVED / ACTIVE — explicit Product Owner approval, 2026-10-17.
> Effective date: 2026-10-17.
> Applies globally: Tunner User, Account, Registration/Login, Admin/Support, Billing, and Connected Product-facing UI.
> Authority: locked development baseline 1.6.0 + current active amendments, especially AMD-0008 (approved Figma visual fidelity).
> Change scope: Frontend navigation, form validation, interaction-state implementation, evidence and release gates. No change to domain/identity/business policy, eligibility, API contracts or security authority.




## 1. Binding decision




Every independently navigable, full-screen workflow view MUST have its own meaningful, canonical **pathname** (URL-driven client-side routing using browser History API), not merely a local component-state swap under a stale URL, and not hash-only routing for primary screens. Single-page application navigation MUST synchronize the URL and visible route, support browser Back/Forward, deep linking, reload, direct entry, and safe server fallback to the SPA shell. Route guards MUST enforce identity/permissions/workflow prerequisites on direct entry; they must not fabricate a completed step. Do not place secrets, verification codes, access tokens, personal data or sensitive internal identifiers into URLs. Avoid query strings for sensitive state. URLs are not evidence of workflow authorization or step completion.




Only within the same screen, ordinary UI interactions (modal/dialog, anchored section, panel, tab, filter) MAY use controlled local state, relevant query parameters, or fragment identifiers where appropriate. `#section-id` is valid for an in-page anchor; a hash or query MAY identify an in-page dialog or state where deep linking/back-forward restoration is desired and safe. It MUST NOT be used as a substitute for a full-screen pathname. Not every ephemeral popup needs a URL mutation. Browser History entries should preserve intuitive back/forward behavior and avoid redundant entries.




Initial **illustrative** route map (not a new locked URL naming/API contract; resolve final paths against existing routes, link mappings and Product Owner acceptance):
- `/login` — Sign in, U-AUTH-01;
- `/register` — Account creation, U-AUTH-02;
- `/register/verify-email` — contact verification, U-AUTH-03;
- `/register/complete` — completion/result when legitimate, distinct full screen;
- `/account/personal-information` — DOB/personal information **after verified contact**, U-AUTH-03A.
- `/register?dialog=terms` (when approved) — in-page modal, same registration screen;
- `/register#privacy` — same-page anchor.




An existing app may use appropriately named equivalent semantic routes; do not create duplicate routes or silently break existing public/callback/bookmarked URLs. Existing OAuth/Product-origin return parameters must remain governed, allowlisted, validated and protected against open redirects; navigation must not drop the approved integration context.




## 2. Validation contract




HTML input types/required/autocomplete/maxlength and browser constraint hints are supplementary accessibility/semantics, **not sufficient form-validation authority or the sole user feedback mechanism**. Form validation MUST use reusable, typed, schema-based client validation integrated with controlled form state and governed field errors, plus independent authoritative ASP.NET Core/backend validation. Recommended frontend default where compatible with approved stack: React Hook Form + Zod and resolver; this is an engineering recommendation, not a Product/business requirement or an unreviewed mandatory library switch. Existing compatible approved validator may remain if it meets all acceptance criteria.




Provide consistent per-field and form-level feedback for required values, correct type/format, confirmation equality, cross-field constraints, server errors, valid/invalid transitions, submit/loading/error states and applicable async checks. Validation timing must support practical on-blur/on-submit and correction-on-change behavior without premature noisy errors. Failed submission must preserve safe user-entered data, focus the first invalid field, associate accessible descriptions/status, avoid double submit and avoid native browser tooltips replacing Figma feedback. Validation and message content must follow approved governance/content keys/localization. Duplicate email/Account checks must not disclose arbitrary Account existence; required password policy is resolved from current backend identity authority, **not from old Figma text or an invented minimum length**. Do not rely on client code for authorization, account-eligibility decisions or workflow finality.




## 3. Approved Figma validation state is normative




The existing Figma `Tunner Platform UX Master` contains **registration-validation-errors** frame `70:1257`, with error state UI including error texts `70:1310` (email), `70:1311` (password), `70:1312` (Terms/Privacy), and `70:1313` (form summary). It includes the existing `bg-glow-indigo` / `bg-glow-violet` background, branded page chrome, form fields, card and button. Reuse the approved **actual Figma visual treatment** of these field/error controls, placement, spacing, color, typography, focus/invalid states, form summary, background and responsive behaviors; do not substitute HTML5 browser speech bubbles, generic library defaults, or arbitrary new alert/error styles.




Figma is visual authority, not business/identity policy. Some layers/copy in historical validation compositions may be superseded by newer FRD/PDR-0011/AMD-0007 registration contracts (for example Product entitlement text, legacy providers, full-name acquisition, password-complexity wording). Preserve the correct visual primitives but render only current approved fields/messages and policies; submit any required material Figma visual adaptation for Product Owner approval under AMD-0008. A field/error design not covered by approved Figma must be proposed for consent, not invented as final UX.




## 3A. Global platform-wide routing and form-validation component contract — approved clarification, 2026-10-17


**This amendment applies universally to ALL existing and future Tunner-owned frontend applications, pages, screens, forms, form controls, validation states, and navigable workflows**: public/auth/account, User, Admin/Support, Billing/Checkout, Payments/Finance, Connected Product self-service/admin, internal/operator tools, settings/configuration, wizards, dialogs and future modules. Registration is the initial defect report and Figma example only; it is **not** an applicability boundary. Products' independently owned applications and provider-hosted secure payment forms remain under their own applicable contracts; do not claim Tunner can restyle a third-party/provider-controlled iframe. Tunner-owned shells and applicable shared components still comply.


### Global reusable validation design system


A single governed Tunner **form/field/validation UI capability** MUST be implemented and reused across the User and Admin applications and all Tunner-owned surfaces, avoiding per-screen handwritten error styles. It covers text/email/password/number/currency/date/time, selects, autocomplete, checkboxes, radio groups, toggles, textarea, upload, arrays/dynamic items, secure domain-specific fields, step-based workflows, nested fields, editable tables and other applicable controls. For each, use the appropriate **approved Figma component/state variant** as the visual authority; default treatment follows Tunner design tokens derived from Figma validation examples (including registration-validation-errors `70:1257`) and the shared error system. **This Figma frame is a visual-pattern exemplar for reuse, not a requirement to paste the registration page or its textual messages into other forms.** Where another screen already has an approved Figma-specific error/state design, preserve that exact approved variant; reconcile any material inconsistency through AMD-0008 Product Owner consent.


The shared capability must represent required/optional, pristine/dirty/touched, normal/focus/valid/invalid/disabled/read-only/pending/success, inline field error, field-group error, form-level summary, server/domain rejection, multi-step navigation error, cross-field and async-validation states. Error presentation MUST consistently reuse the approved Figma error geometry, text treatment, typography, spacing, iconography, accessible association/focus/announcements and brand styling across all forms. Errors must never be presented by ad hoc TailAdmin/React library defaults or native browser bubbles as the primary experience. The error messages, validation predicates, security, monetary and legal/regulated semantics are sourced from each owning FRD/domain policy and governed content architecture; do not copy registration-specific password or privacy text into unrelated forms. Client validation improves experience; every authoritative action remains independently checked by the server, including validation race/conflict, permission and version gates.


### Global route inventory and route ownership


Every full-screen, independently navigable Tunner view across **all modules**, including detail screens, dashboards, control centers, onboarding steps, configuration/review/result pages and governed workflow stages, MUST have its own stable canonical **pathname**. Establish and maintain a central route manifest linking screen ID, owning module/app, pathname/parameter schema, access policy/guard, Figma node, FRD/flow, safe deep-link/refresh/back-forward behavior and redirects/migration aliases. When switching the full-screen view, the browser URL MUST change; keeping the old URL or using hash pseudo-routing is a defect. Example coverage (illustrative and not frozen path naming): `/account/security`, `/billing/subscriptions`, `/admin/products`, `/admin/payments`, `/admin/support/cases/:caseId`, in addition to the registration examples. Preserve existing valid public paths and callbacks and never infer permission from the URL. Same-screen tabs, anchors, modals and filters MAY remain local/query/fragment state as appropriate and are not required to become distinct routes.


### Universal enforcement and acceptance


The `URL-001..006` and `VAL-001..008` acceptance suite is a **global, parameterized compliance gate**, not just a registration test list. QA MUST sample and validate **each affected route and form** of each application/module, with automated per-component/field-variant tests, visual-regression evidence against relevant approved Figma nodes and responsive variants, plus backend bypass/security tests. Every new screen/form must enter the route/form inventory before release. Deviations are `UI_ROUTING_DEFECT`, `UI_VALIDATION_DEFECT` and/or `UI_FIDELITY_DEFECT` regardless of module. A form or page is not `DONE` just because another form passed. Only a Product Owner-approved new visual pattern/exception can alter the approved visual components; no AI self-approval. Preserve AMD-0008 and all domain approval requirements.


**Clarification classification:** interpretation-explicit / global propagation of the already approved AMD-0009 scope. It does not reopen the immutable baseline, create a registration-specific exception, invent business rules, alter product-specific portal ownership or authorize feature implementation before the P0 gate.


## 4. Workflow and direct-navigation requirements




Route access checks resolve persisted/authoritative workflow state and valid resumable context, never mere in-memory step counters. Refresh or deep link either renders the authorized stage or safely redirects to a meaningful recovery/starting screen; no blank page, 404 from SPA hosting, false confirmation, skipped verification, or PII in URLs. Browser Back must not replay side effects or enable unsafe resubmission. After irreversible actions, navigation must display accurate status and route transitions; sensitive forms may need safe unsaved-change confirmation and correct focus/scroll restoration. Links/CTA/actions must navigate to correct owning screens, and URL must change when the full screen changes, including Control Centers and detail routes.




## 5. Acceptance, testing and remediation




Mandatory QA at representative desktop/tablet/mobile supported breakpoints, with screenshots at Figma reference dimensions and tested responsive layouts. `URL-001`: each full-screen screen transition produces correct pathname; `URL-002`: full-screen navigation using only `#route` or stale pathname fails; `URL-003`: refresh/deep link/Back/Forward reconstruct safe correct view; `URL-004`: same-screen modal/anchor state safe and consistent; `URL-005`: direct unauthorized/expired stage is blocked or redirected; `URL-006`: existing OAuth Product-origin and redirects preserved securely; `VAL-001`: required/format/confirmation errors have field-level Figma-matched appearance; `VAL-002`: browser-native validation is not sole feedback; `VAL-003`: backend independently rejects malformed/bypassed requests; `VAL-004`: server error mapped to governed field/form errors without existence leakage; `VAL-005`: error clearing, first-invalid focus, accessible associations, safe value preservation, resubmit protection; `VAL-006`: approved validation visual states/screenshots align with `70:1257` or approved adapted equivalent; `VAL-007`: no old Figma text overrides current password/identity/provider authority; `VAL-008`: end-to-end cross-device route and validation regression.




Any missing/stale screen pathname or hash-as-screen-router is `UI_ROUTING_DEFECT`. Browser-only validation, inconsistent errors, or bypassable backend checks are `UI_VALIDATION_DEFECT` (security impact as applicable). Figma validation style mismatch is also `UI_FIDELITY_DEFECT`. These defects block the affected `UI_VISUAL_APPROVAL` and functional/release acceptance; unapproved novel designs still require `UI_REQUIRED` and explicit Product Owner visual consent under AMD-0008. A green build, apparent happy-path screen flow or nonvisual UI test alone is not adequate evidence.




## 6. Propagation and boundaries




Update the active UI/UX development rules (10), Screen & Control-Center Matrix (31), AI development protocol (12), Release gates (33), current authority index (37), master README, and any relevant testing/QA/agent role/repository templates or CI checks when materialized. Preserve immutable baseline/archive. P0 remains the authorized implementation plane; P1–P8 remain subject to the P0 gate. This documentation approval is not proof that current production/frontend code has been remediated or tested.