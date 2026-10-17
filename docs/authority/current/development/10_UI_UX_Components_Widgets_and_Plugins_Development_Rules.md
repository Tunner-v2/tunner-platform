> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  


# 10 — UI/UX, Components, Widgets & Plugins Development Rules


## 1. Source role


Figma Admin & Support UX and Tunner Platform UX are reference sources for:


- information architecture;
- intended journey;
- content;
- visual system;
- operator/user mental model.


They are not backend/business authority.


Google Drive UI/UX documents are out of development scope.


## 1A. Approved Figma fidelity and Product Owner consent — AMD-0008 (ACTIVE, 2026-10-17)


**Normative authority:** [AMD-0008 — Approved Figma Visual Fidelity & Product Owner UX Consent](https://drive.google.com/file/d/1DErZZpc3rIwzfpADnDzD9a361draTuhW/view). Approved Figma is the controlling visual/interaction design specification for approved screens and components; approved FRDs/PDRs remain controlling for business behavior and security. The fact that Figma is not backend authority **does not permit arbitrary screen styling**. No AI/frontend developer may omit backgrounds, glow/gradient/illustration layers, restyle existing fields/buttons, substitute generic TailAdmin UI, or change existing screen composition without the Product Owner's explicit approval.


Existing Figma frames and reusable components require faithful implementation of approved typography, colors, surfaces, background, spacing, alignment, field dimensions, borders/radii/shadows, navigation/footer, iconography, controls, all specified states and matched responsive behavior. Approved login `2:4`, registration `70:270`, verify email `70:539`, and account ready `70:630` (Tunner Platform UX) specifically retain `bg-glow-indigo` / `bg-glow-violet` and full branded screen composition. TailAdmin React is a code foundation only, not a substitute visual authority.


If required screens/forms/fields are absent or conflict with the approved Figma visual/functional state, the AI may prepare a consistent on-theme proposal, but may not accept it or publish production UX until the Product Owner expressly approves the candidate/exception. Preserve FRD functional authority; record and route any conflict through controlled Figma revision and approval. Ordinary fully nonvisual engineering detail remains delegable.


**Mandatory gate:** establish screen ID -> FRD/flow -> approved Figma node/component/token -> implementation -> visual evidence; compare matched Figma and runtime screenshots; test desktop/responsive sizes, keyboard/accessibility and states; record design deviations and approval evidence. Missing background/asset, field/component mismatch, unapproved new pattern and unapproved material deviation are `UI_FIDELITY_DEFECT` / `UI_VISUAL_APPROVAL` blockers. A functional test or generic UI review does not waive this gate.


## 2. Guided interaction rule


No traditional database CRUD as the primary experience.


Sensitive/complex work follows:


`context → eligibility → evidence → candidate action → impact → review → confirmation/step-up/approval → execution → result`


Tables/lists are allowed for navigation/search, but direct arbitrary row editing is not the business workflow.


## 3. UI content rule


User/admin UI uses business/operator language.


Do not expose:


- class names;
- database tables;
- internal queues;
- raw stack traces;
- implementation package names;
- raw JSON policy unless the user role specifically manages a safe domain configuration.


Diagnostics translate technical evidence into operator-appropriate explanations while retaining correlation IDs.


## 3A. Global governed presentation-content architecture


Tunner presentation content is a **global platform capability**. It is not limited to selected pages, labels, notification templates or optional CMS-managed regions. Every applicable user-facing or operator-facing presentation string/content element must resolve through the governed content capability or an explicitly governed deterministic fallback.


This includes, as applicable:


- page, panel, section and dialog titles;
- labels, captions, descriptions and instructions;
- buttons, links and action wording;
- contextual help, tooltips and guidance;
- status and lifecycle explanations;
- validation, problem and error explanations;
- empty, loading, unavailable, blocked and success states;
- warnings, banners, confirmations and impact explanations;
- onboarding and guided-workflow copy;
- notification, email and in-app presentation content;
- consent, disclosure, privacy/legal and security presentation text subject to stronger governance;
- user-visible Product/context/category/locale variants;
- other human-readable presentation content introduced by future components.


### User-oriented language is mandatory


Content must be written for the intended user/operator and the task they are performing. Internal resource/class names, database terminology, event identifiers, queue/broker mechanics, provider implementation details, package/framework names, raw state constants, raw policy JSON and developer diagnostics must not leak into ordinary UI. Technical terminology is permitted only where the intended authorized audience is performing a genuinely technical integration/operations task and the term is necessary to complete that task.


Domain state remains authoritative. Presentation content translates/explains that state; it does not redefine it. For example, an internal `UNKNOWN_EXTERNAL_STATE` may be presented to an ordinary user as “Payment confirmation is still pending” while authorized operations tooling may additionally expose safe diagnostic evidence/correlation references.


### Canonical content capability


The implementation must provide stable semantic `ContentKey` resolution through a Tunner-owned content capability. The minimum governed model is:


`ContentDefinition / ContentKey -> ContentVariant -> ContentVersion -> Publication`


Resolution may consider only approved dimensions, including locale, audience/role, surface/application, page/component context, category, Product scope and environment where required. Content keys are stable semantic identifiers and must not be raw database IDs or brittle visual-layout coordinates.


The frontend component contract is conceptually:


`Universal Component + ContentKey + safe context -> Content Resolver -> effective Published ContentVersion -> rendered user-oriented content`


Universal components must not duplicate mutable copy across screens when the content is intended to be centrally manageable.


### Content administration


Authorized users require a contextual content-management experience supporting, according to permission/content class:


- find content in the page/component context where it is used;
- create/edit a draft;
- preview the candidate in applicable context, locale and audience;
- validate required variables/slots, links and content safety;
- compare current and candidate versions;
- submit for approval where required;
- publish prospectively;
- retain immutable version/audit history;
- rollback by publishing/restoring an approved prior version without deleting history.


The primary editing experience must not be a database-style CRUD console.


### Governance boundary


**Content management controls presentation semantics; it does not control business semantics.** Editable content must never become authority for price, fee calculation, tax determination, entitlement, authorization, eligibility, state transition, workflow execution, retry/finality, compliance decision or other domain behavior. Domain APIs provide authoritative facts/capabilities; content explains them.


Content classes must support different governance strength. Ordinary UX copy may use lightweight publication. Security warnings, payment/tax disclosures, privacy/legal/consent text and other regulated/sensitive content require policy-defined authorization, immutable version evidence and approval/separation-of-duties where required. Historical acknowledgement/delivery evidence retains the exact effective content/version reference.


### Deterministic fallback and availability


A missing content key/variant must never cause the application or an AI model to invent production text. Resolution uses a deterministic documented fallback hierarchy and a safe bundled/default value where permitted; missing mandatory sensitive content blocks the affected presentation/action when policy requires exact approved wording. Cache is non-authoritative and must support version-aware invalidation.


### Model-neutral content assistance


Tunner may expose a provider-neutral content-intelligence interface so an approved model can propose context/category-appropriate copy, rewriting or translation. Model output is always a `ContentProposal`/draft, never direct production mutation. It must pass schema/safety/policy validation and the same authorization/approval/publication controls as human-authored content. Business rules and authoritative domain values must not be generated or overridden through this interface.


## 4. Missing-screen development block


If required UI does not exist:


1. create `UI_REQUIRED`;
2. submit design requirements:
   - actor;
   - business purpose;
   - entry point;
   - data required;
   - permissions;
   - actions;
   - states;
   - validations;
   - errors;
   - confirmation/approval;
   - audit evidence;
   - relevant flows/contracts;
   - acceptance cases;
3. design/approve;
4. unblock implementation.


Developer/AI must not invent production UX.


## 5. Component strategy


Plan reusable components around user interaction, not database entity types.


### Foundation components


- application shell/navigation;
- page header/context;
- status badge;
- alert/banner;
- guided stepper/wizard;
- review summary;
- confirmation panel;
- impact summary;
- evidence timeline;
- activity/audit timeline;
- problem/error panel;
- empty state;
- search/filter;
- pagination;
- accessible form field;
- secure secret-entry/rotation flow;
- approval/step-up prompt;
- environment badge/switcher.


### Domain widgets


- Product environment status;
- SDK compatibility;
- webhook delivery timeline;
- payment finality;
- reconciliation status;
- subscription billing lifecycle;
- provider health;
- incident status;
- policy/version context.


## 6. Consent/Disclosure component/plugin


Create a reusable compact `ConsentDisclosure` capability.


UI modes:


- inline;
- modal;
- wizard step;
- external-link acknowledgment where policy allows.


Contract records:


- consent/disclosure purpose;
- document/version;
- locale;
- jurisdiction/policy;
- affirmative action;
- timestamp;
- withdrawal.


Do not use a single generic “I agree” record for unrelated purposes.


## 7. Plugin concept


“Plugin” must mean a bounded reusable capability with contract/version.


Potential platform plugins/components:


- payment provider adapter;
- notification provider adapter;
- tax engine adapter;
- secrets provider adapter;
- telemetry exporter;
- consent presentation integration;
- SDK generator pipeline plugin.


Do not create a generic arbitrary-code plugin system for MVP.


## 8. Accessibility


WCAG 2.2 AA target:


- keyboard operation;
- focus visibility/order;
- error association;
- semantic labels;
- target size;
- accessible authentication;
- non-color-only status;
- screen-reader status updates;
- reduced motion support where applicable.


## 9. Reference observations


Admin UX already organizes around business domains such as Accounts & Identity, Connected Products, Billing Accounts, Subscriptions, Payments, Finance, Providers, Notifications, Security, Audit, Platform Operations and Internal Access.


User UX already follows guided account creation/verification instead of raw CRUD.


Development should preserve that philosophy while validating every workflow against development authority.


## 10. Acceptance cases


| ID | Scenario | Expected |
|---|---|---|
| UX-001 | destructive/sensitive action | review + impact + governed confirmation before execution |
| UX-002 | missing required screen | work blocked `UI_REQUIRED`; requirements packet created |
| UX-003 | API returns technical error | UI shows safe business explanation + correlation reference |
| UX-004 | consent needed | exact disclosure version/evidence stored |
| UX-005 | keyboard-only user | complete critical wizard successfully |
| UX-006 | ordinary UI renders domain state | user/operator-oriented governed content is shown; raw internal constants/implementation terminology are not exposed |
| UX-007 | published content changes | reusable surfaces resolve the effective version without screen-specific code changes; prior evidence remains version-traceable |
| UX-008 | required content key/variant is missing | deterministic fallback is used where permitted; mandatory sensitive content blocks rather than being invented |
| UX-009 | AI proposes content | proposal remains draft until validation and required authorization/approval/publication complete |


## Frontend runtime and BFF rule


- React + TypeScript + Vite + TailAdmin React is the selected frontend baseline.
- User and Admin/Support are separate web applications.
- Shared components/tokens are packaged deliberately; do not create a shared business-state layer between the applications.
- Each application uses its own ASP.NET Core BFF.
- Access/refresh tokens must not be stored in browser local/session storage.
- UI content remains user/operator oriented; protocol/library/database terminology is hidden unless the user is specifically configuring an integration and the concept is necessary.


## Shared presentation formatting contract


User/Admin applications must not independently reimplement deterministic formatting/representation rules per screen.


Use deliberate shared frontend capabilities/components for applicable concerns such as:


- money/currency;
- numeric/percentage display;
- date/time/timezone and duration display;
- identifier presentation;
- masking/redaction;
- safe links/URLs;
- other repeated presentation transformations.


Rules:


1. Presentation formatting consumes authoritative values from domain/BFF contracts and must not silently reinterpret them.
2. Money formatting receives an authoritative amount plus ISO currency and presentation context. It must not calculate or alter price, discount, fee, tax or FX.
3. Date/time display receives authoritative instants plus explicit presentation timezone/locale context; it must not change business effective time.
4. Shared UI formatting belongs in a deliberate package/capability (for example `web/packages/formatting`) rather than repeated inline expressions across components.
5. Standard browser internationalization capabilities such as `Intl` may be used internally; Tunner shared wrappers/components establish consistent defaults/contracts where needed.
6. Frontend canonicalization that affects persisted identity is preview/advisory only. Server/domain validation remains authoritative.
7. Governed presentation content and value formatting are distinct: content resolves human-readable wording; formatters render authoritative typed values.


Additional UI acceptance cases:


| ID | Scenario | Expected |
|---|---|---|
| UX-010 | same Money appears on User and Admin surfaces | shared formatter/component produces contract-consistent locale-aware output |
| UX-011 | screen developer attempts local currency-symbol concatenation | review/static policy rejects or replaces it with shared formatting capability |
| UX-012 | slug preview is shown before save | client preview may format/canonicalize, but server returns authoritative persisted result |
| UX-013 | UTC timestamp shown in different locale/timezone | only presentation changes; authoritative instant remains unchanged |