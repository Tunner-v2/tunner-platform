> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Closure revision date:** 2026-09-29  
































# 31 — UI/UX Screen & Control-Center Requirements Matrix
































## 1. Purpose and authority
































This document closes the development requirement that **developers must not invent screens, omit required operational states, or implement generic CRUD in place of governed workflows**.
































Authority order remains:
































1. Development documentation/business decisions;
2. FigJam architecture/workflows;
3. approved Figma User/Admin UX as implementation reference.
































Figma references:
































- User UX: `Tunner Platform UX Master` — `apNbwvAgdn1xIxYH1oiaSC`;
- Admin/Support UX: `Tunner Admin & Support UX Master` — `3F9tAviUejTYpYvRJt8gM1`.
































Existing Figma screens are visual/workflow references. If they conflict with a domain FRD, the FRD wins and the UI must be updated before implementation of the affected flow.
































## 1A. Figma visual-fidelity authority and Product Owner approval — AMD-0008








**Active UX governance:** https://drive.google.com/file/d/1DErZZpc3rIwzfpADnDzD9a361draTuhW/view








Every approved Figma User/Admin screen is the **visual specification**, not optional inspiration. Existing UI must reproduce all approved visible components, including full-page backgrounds/glows/gradients/illustrations, layout, field styles/sizes, typography, visual hierarchy, borders/radii, shadows, colors, icons, navigation/footer, controls and their states. TailAdmin React defaults must not displace Tunner Figma styling. Screen implementation must map: Screen ID → active FRD/flow → Figma file/node/version → tokens/components/assets → runtime screenshot/evidence.








For missing/changed forms, fields, states or screens, the AI may propose a brand-consistent design but requires explicit Product Owner approval before the new visual variation or materially changed layout is approved for production. Existing fields must reuse matching Figma components and tokens. Business/security behavior remains controlled by applicable FRDs; if Figma is inconsistent with a current requirement, record the precise difference, propose minimal visually compatible revision, secure Product Owner design approval, update Figma and then resume affected UI implementation. A background omission or arbitrary field restyling is `UI_FIDELITY_DEFECT`, not acceptable engineering discretion.








Existing login `2:4`, registration `70:270`, verify-email `70:539` and account-ready `70:630` frames are approved design references with branded `bg-glow-indigo` and `bg-glow-violet` visual elements; do not omit them. Where Figma login provider labels differ from approved identity providers (Google/Microsoft first), or PDR-0011 registration behavior differs from older Figma content, preserve the visual language and submit the exact adaptation for consent.








**Additional acceptance:** `UX-FID-001..008` under AMD-0008 are mandatory visual-evidence and Product Owner consent gates. Compare implementation screenshots at matching Figma viewport and documented responsive breakpoints; inspect backgrounds, fields, states, alignment/clipping, keyboard/accessibility. Material mismatch or unapproved deviation blocks `UI_VISUAL_APPROVAL` and production UI release. Existing implementations must be audited; missing approved styling is a defect even if functional tests pass. The `UI_REQUIRED` gate still applies to unapproved missing screens.








## 1B. Per-screen URLs and governed validation — AMD-0009




Authority: https://drive.google.com/file/d/1VeaNofocTrsbXwdePg8VSVuembRQxr7A/view. Each independently navigable screen (including registration steps, login, account pages, Control Centers and detail workflows) MUST correspond to a meaningful pathname and react to Back, Forward, refresh, direct link, session resume/expiry and safe navigation guards. Hash fragments are only suitable for in-screen anchors/dialog contexts, not for full-screen route identity; query state for same-page panels/modals may be used when safe. Full-screen component swaps with unchanged URL are `UI_ROUTING_DEFECT`. Do not store sensitive data/one-time codes in URLs or break approved Product-origin OAuth context. Illustrative paths: `/register` → `/register/verify-email` → `/register/complete`; `/account/personal-information` after verification. Final path mapping must respect existing registered route contracts.




Forms MUST have shared application-level typed/schema validation in addition to independent backend validation; native HTML5 required tooltips cannot be the sole error UX. The existing Figma **registration-validation-errors** frame `70:1257` contains field errors (`70:1310` email, `70:1311` password, `70:1312` Terms/Privacy) and summary (`70:1313`). Copy approved appearance, field geometry/error placement/colors/states/background, subject to current domain/identity policy; historical Figma validation wording cannot redefine password rules or registration fields. Test all `URL-001..006` and `VAL-001..008`, first-invalid focus, server-error association, accessible messages, form resubmission and responsive visual screenshots. Unapproved novel styling requires Product Owner approval under AMD-0008. `UI_ROUTING_DEFECT`, `UI_VALIDATION_DEFECT`, `UI_FIDELITY_DEFECT` block release.




## 1C. GLOBAL module-wide routing and validation scope — AMD-0009 clarification


AMD-0009 covers **all existing and future Tunner-owned routes, pages, screens and forms**, including Account, Admin Control Centers, Billing/Checkout, Finance, Payment, Security/Support and Connected Product self-service. Registration and Figma `registration-validation-errors` frame `70:1257` are examples, **not** the boundary. Implement one governed reusable Figma-derived form/field/error design system across applicable forms; preserve each page's separately approved Figma component variants. Use typed client validation AND authoritative per-domain server validation for all controls (not registration-specific messages), with consistent inline errors, group/form summary, focus and accessible states. Browser-native required bubbles cannot be sole feedback.


Every independent full-screen view, control center, detail page or workflow stage has an owning canonical path, mapped to screen ID, FRD, Figma and guard in a route registry. Screen transitions change pathname; in-screen modal/tab/anchor may use suitable local/query/fragment state. Direct URL, reload, Back/Forward and authorization must work for **every** screen. QA must validate each applicable route/form against `URL-001..006` and `VAL-001..008`, Figma fidelity and responsive/accessibility standards. Violations block acceptance as `UI_ROUTING_DEFECT`, `UI_VALIDATION_DEFECT` or `UI_FIDELITY_DEFECT` respectively.


## 2. Universal screen rules
































Every actionable screen must define/display as applicable:
































- actor and current scope (Account, Product, environment, Billing Account, case);
- current authoritative state/version;
- operation eligibility from backend, not frontend guesses;
- required inputs with validation;
- impact preview for sensitive changes;
- step-up/approval state;
- external/nonterminal state (`PROCESSING`, `WAITING_EXTERNAL`, `UNKNOWN_EXTERNAL_STATE` etc.);
- exact blocker/owner-domain resolution path;
- immutable result/evidence/reference IDs;
- error state with stable error/correlation reference;
- audit/activity link where actor is allowed;
- accessible focus/errors/status behavior;
- no secret values or raw sensitive evidence unless a dedicated reveal policy permits it.
































Buttons/links must not be enabled for actions the backend will always reject. A pending/approved request must not be presented as executed success.
































## 3. User Account Center required screens
































| ID | Screen | Core requirements / authoritative actions |
|---|---|---|
| U-AUTH-01 | Sign in | Email/password, passkey where supported, Google, Microsoft; safe errors; Product-origin OAuth context preserved |
| U-AUTH-02 | Registration | Minimum Account fields only; no DOB/18+ acquisition gate; terms/privacy acknowledgement refs; no Product entitlement implication |
| U-AUTH-03 | Verify Email | challenge status/resend/rate-limit; verification result |
| U-AUTH-03A | Personal Information / Age Evaluation | after verified contact: normal personal/profile fields including DOB; authoritative server age evaluation; under-18 continues normal Account/Product onboarding subject to age/consent policy; no global age rejection |
| U-AUTH-04 | MFA / Authenticator Setup | TOTP QR/secret one-time presentation; verify first code before activation |
| U-AUTH-05 | Passkey Setup | WebAuthn ceremony; no private-key visibility |
| U-AUTH-06 | Recovery Codes | one-time reveal/download and replacement warning |
| U-AUTH-07 | Account Recovery | safe account-existence behavior, recovery case/next proof, denial/escalation status |
| U-AUTH-08 | Product Authorization | requested Product/scopes/assurance and registered callback; authorize/deny |
| U-ACCT-01 | Account Overview | Account state, security posture, verified contact, recent safe activity, relevant billing summaries |
| U-ACCT-02 | Profile & Verified Contact | profile edit vs verified-contact change kept separate; candidate verification state |
| U-ACCT-03 | Sign-in Methods | password/passkeys/Google/Microsoft/TOTP links with safe add/remove eligibility |
| U-ACCT-04 | Sessions & Devices | current/other sessions, device trust, revoke actions; Product-session boundary copy |
| U-ACCT-05 | Security Activity | permission-safe Account/Security events and correlation refs |
| U-ACCT-06 | Linked Products | Tunner Product Relationships, authorization scopes/status; not Product membership/entitlement |
| U-ACCT-07 | Notification Preferences | optional/marketing preferences and in-app history; mandatory-class explanation |
| U-ACCT-08 | Privacy Requests | request type eligibility, identity verification, owner-task progress, export/result |
| U-ACCT-09 | Account Closure | exact blockers by owner domain, consequences, step-up, closure status |
































## 4. Billing Account / user billing screens
































| ID | Screen | Requirements |
|---|---|---|
| U-BILL-01 | Billing Accounts | visible Billing Accounts, membership role, authority mode/capabilities and payer/commercial summaries sourced only from authoritative Billing/Finance resources |
| U-BILL-02 | Billing Account Detail | Billing Profile, members, authority, Product relationships, subscriptions/invoices/payment refs |
| U-BILL-03 | Billing Profile | versioned payer details; verified-contact/tax dependencies; impact/effective date |
| U-BILL-04 | Members | membership invite/change/remove status; authority explicitly separate |
| U-BILL-05 | Billing Authority | effective capabilities/source and managed-by-Product explanation; eligible exception path only |
| U-BILL-06 | Payment Methods | provider-tokenized method summary, setup/challenge states, default/disable dependency checks |
| U-BILL-07 | Subscriptions | Tunner Billing Subscription lifecycle with Product-owned entitlement disclaimer/contract-driven wording |
| U-BILL-08 | Subscription Change | plan/item/quantity/effective-date preview, proration/scheduled-change evidence, authority gate |
| U-BILL-09 | Dunning/Payment Resolution | amount/Invoice/attempt status, payment-method update, safe retry state; no duplicate retry on unknown outcome |
| U-BILL-10 | Invoices & Credits | immutable documents, allocations/credit notes, delivery status separate |
| U-BILL-11 | Billing Account Closure | exact blockers and prospective closure effects |
| U-CHK-01 | Tunner Checkout | Tunner-owned Product/order summary, explicit price/currency, tax, Billing Account/customer details, provider-neutral checkout state, confirmation/processing/result; selected route may use provider-secure embedded collection or an approved provider-hosted handoff and must return/converge on the same CheckoutOperation |
































## 5. Founder/Owner/Admin landing dashboard
































`A-OVR-01 Platform Dashboard` is mandatory and must aggregate **read models only**:
































- Account/Identity health and security actions requiring review;
- Connected Product/environment readiness;
- Billing/Subscription financial summaries;
- Payment/Invoice/Finance exceptions;
- Provider/merchant health;
- notification delivery incidents;
- Support/Risk queues;
- Security/Audit/Internal Access alerts;
- platform health/SLO/incidents;
- pending approvals and overdue governed work;
- graphs/trends with freshness timestamp;
- drill-through to owning Control Center.
































Dashboard actions route to governed owning screens; it is not a cross-domain mutation console.
































## 6. Admin Control Centers
































| ID | Control Center | Mandatory subviews/actions |
|---|---|---|
| A-ID-01 | Accounts & Identity | Account search/detail; sessions/devices; recovery/security evidence; restricted guided protection actions; no credential reveal |
| A-PRD-01 | Connected Products | Product/environment registry; client/OAuth; capabilities; event subscriptions; SDK/API compatibility; self-service/readiness |
| A-BIL-01 | Billing Accounts | Billing Account/detail; membership; authority; profile; Product relationship; closure/migration governed workflows |
| A-SUB-01 | Billing Subscriptions | lifecycle, scheduled changes, renewal/dunning operations, commercial/financial refs; Product authority boundary |
| A-PAY-01 | Payments | transactions/attempts, finality, reconciliation, refund/void/dispute actions |
| A-INV-01 | Invoices & Credits | draft/issue/read-only issued document, allocations, credit/void/write-off, delivery evidence |
| A-FIN-01 | Financial Control | journals, trial balance, posting exceptions, reconciliation, period control, TunnerTreasuryAccounts, FxConversions, evidence lineage |
| A-FEE-01 | Tunner Fees & Economics | fee policies/calculations, provider-cost attribution, Product economics/payable evidence, payout-currency preference and guided FX economic-treatment configuration with templates, previews, effective versions, approvals and audit evidence |
| A-TAX-01 | Tax | market/tax readiness, Tax Determinations/exceptions, registration evidence, remittance obligations/cases |
| A-MER-01 | Product Compliance & Provider Readiness | ProductKycCase lifecycle/evidence, Product payment/payout eligibility, Tunner ProviderAccountBinding readiness, provider-route evidence and effective versions; Product economics remain Product-scoped |
| A-PRV-01 | Providers | provider instances/accounts/capabilities, onboarding/remediation, credentials rotation, routes, incidents |
| A-NTF-01 | Notifications | templates/classes/routes, delivery attempts, suppressions, incidents, provider health |
| A-SUP-01 | Support | queues, case workspace, evidence, communication, escalation/action request, SLA |
| A-RSK-01 | Compliance & Risk | risk cases, IDV review, merchant review, exceptions, evidence packages |
| A-SEC-01 | Security Operations | signals/investigations/account protection/session trace; owner-domain execution results |
| A-AUD-01 | Audit & Governance | audit explorer, policies, approvals, evidence packages, Governor conformance |
| A-PLT-01 | Platform Operations | health, incidents, maintenance/change, queues/workers/timers/DLQ, DR, reliability budgets |
| A-IAM-01 | Internal Access | workforce identities, assignments, access requests, JIT privilege, certification, SoD, offboarding |
| A-PRVY-01 | Privacy & Data Governance | requests, inventory, processing activities, retention/holds, processors, incidents |
































## 7. Finance operational case screens
































Finance must use stable case/operation threads for nontrivial actions, including:
































- `PaymentReconciliationCase`;
- `PostingException`;
- `LedgerReconciliationCase`;
- `TaxException` / `TaxRemittanceObligation`;
- `ReceivablesCase`;
- `Settlement` and payout reconciliation;
- `ProductPayable`, derived `ProductPayableBalance`, immutable `Settlement`, `ProductPayoutPreference`, `Payout`, `ProductPayoutDestinationVersion`, `FxConversion` and `PayoutReceipt`;
- `PayoutProcessingCase` only for failure/return/unknown/mismatch/reconciliation exceptions;
- refund/dispute review;
- write-off/adjustment/period close/reopen approval.
































Every case screen must show current state, owner, due/escalation, evidence timeline, permitted next actions and completion evidence. It cannot be closed merely by a free-text “resolved” toggle when a reconciled finality requirement exists.
































## 8. Connected Product self-service screens
































Product operators (where customer-facing portal exists) require:
































- Product/environment overview;
- client/redirect configuration;
- SDK/API version compatibility;
- capability assignments;
- webhook/event subscription endpoints/signing rotation;
- delivery diagnostics/DLQ/redrive where permitted;
- synthetic integration tests;
- activation/readiness gate and exact blockers;
- ProductKycCase/payment eligibility and payout-destination readiness through Tunner; provider merchant/account routing remains Tunner-internal infrastructure;
- Product payout preference configuration for eligible currency/destination corridors, effective FX-treatment summary and indicative payout/FX preview; Product cannot override commercial FX responsibility unless explicitly authorized by the active ProductCommercialAgreement.
































Self-service cannot grant itself capabilities, production activation or exceptions beyond backend policy.
































### Guided FX / payout configuration
































Financial configuration must use a guided workflow rather than a raw policy-form/JSON editor as the primary operator experience. The workflow must:
































1. select Product/environment/agreement and FX context;
2. show the resolved Standard Cause-Based default and available templates (`Product bears`, `Tunner absorbs`, `Shared/custom` where authorized);
3. show eligible source/target currencies, payout destinations and provider corridors;
4. configure cost components, percentage split and optional approved caps only when the selected mode requires them;
5. preview example source amount, evidenced/estimated conversion cost, allocation and target payout outcome with clear `estimate` vs `actual` labeling;
6. validate missing routes, unsupported currencies and contradictory/overlapping policy scope before submission;
7. show current vs candidate policy version, effective date and impacted Products/corridors;
8. require reason/approval/step-up according to commercial-governance policy;
9. activate prospectively and preserve immutable prior versions/audit evidence.
































The UI should provide recommended defaults and contextual explanations so ordinary configuration requires minimal manual interpretation. Advanced/custom fields remain progressive-disclosure controls rather than cluttering the standard path.
































## 9. Required state patterns
































All applicable screens support:
































- loading with freshness indication where read model used;
- empty/no-access/not-found distinct states;
- optimistic UI only when operation semantics permit; otherwise show submitted/processing state;
- stale/version conflict -> refresh/re-review, never hidden overwrite;
- dependency unavailable -> no false completion;
- `UNKNOWN_EXTERNAL_STATE` -> explicit reconciliation wording and duplicate-action suppression;
- scheduled/future-effective -> current vs candidate version and effective time;
- partial operation -> completed vs unresolved steps;
- blocked -> owning blocker and navigation/reference;
- closed/historical -> immutable evidence mode.
































## 10. Content requirements
































User-facing copy must describe business outcome, not internal implementation. Examples:
































- say “Payment is still being confirmed” rather than exposing queue/broker terminology;
- say “Managed by your Product” only when authority contract resolves Product-managed action;
- never promise Product access continues/ends because Tunner billing state changed unless Product contract explicitly supplies that guarantee;
- do not display “legal holding period” as a universal constant;
- do not imply a provider `accepted` response means bank/payment finality.
































### Global managed-content requirement
































The content rule applies globally to every applicable User, Admin/Support and Connected Product-facing Tunner surface; it is not limited to selected CMS regions or individual labels. Page/section/dialog titles, labels, descriptions, instructions, actions, help, statuses, validation/errors, empty/loading/result states, warnings, confirmations, guided-workflow explanations, consent/disclosure presentation and other human-readable presentation content must use the governed Tunner content capability and reusable component contracts defined by document 10.
































Ordinary users must receive user-oriented language rather than technical implementation terminology. Admin/Support surfaces may expose technical terms only when necessary for the authorized operational task, and must still provide a comprehensible business/operator explanation. Raw domain constants may remain in diagnostic evidence where permission requires them, but they are not the primary presentation.
































Every screen/component specification must identify its semantic content keys/slots or governed fallback contract. Content changes must not alter business eligibility, permissions, financial calculation, state transition or workflow behavior. Missing mandatory sensitive content must block the affected action where exact approved wording/evidence is required.
































`A-CNT-01 Content Management` is a required governed Admin capability providing contextual search/edit, draft, preview, locale/audience/context variants, validation, version comparison, approval where required, publish, history and rollback. AI/model assistance, when enabled, creates proposals/drafts only and cannot publish directly.
































## 11. Accessibility and responsive behavior
































- WCAG 2.2 AA engineering target;
- keyboard complete workflows;
- visible focus;
- labels not placeholders-only;
- status/error not color-only;
- dialogs trap/restore focus correctly;
- tables have accessible headers and responsive alternative/detail layout;
- wide Admin layouts remain usable at supported desktop breakpoints without overlap/truncation;
- validation messages associate to controls;
- realtime updates use polite announcement and do not disrupt user input.
































## 12. UI_REQUIRED gate
































If implementation encounters an operation required by an FRD but no approved screen/component exists:
































1. create `UI_REQUIRED` work item;
2. document actor, purpose, workflow/FRD refs, inputs, actions, states, validation, permissions, evidence, responsive/accessibility requirements;
3. update Figma/approve design;
4. resume implementation.
































Developer may create nonvisual backend contract/tests but may not invent production UX.
































## 13. Prototype/linking acceptance
































- every actionable navigation/button links to the correct owning workflow/screen;
- no dead-end except intentional external/provider handoff;
- back/cancel preserves safe operation semantics;
- sensitive actions return to authoritative result screen;
- every Control Center is reachable from role-appropriate navigation;
- Platform Operations is present in relevant Admin navigation;
- dashboards drill to actual detailed state, not placeholder pages;
- visual overlap/alignment/text clipping is a release defect, not a frontend-developer assumption.
































## Age-aware registration and payer UX — PDR-0011 / AMD-0007
















Registration UI must keep acquisition simple: initial registration does not ask DOB or present an 18+ gate. After verified contact, Personal Information collects DOB and the server evaluates age. Under-18 users continue the ordinary Account/Product experience except where active age/consent/privacy/safety policy requires a scoped restriction.
















For paid actions, a user without independent commercial eligibility sees payer-resolution UX rather than Account rejection. The UI may offer supported paths such as authorized parent/guardian, organization/school access or an already eligible Billing Account. The sponsored user must never be presented as owner of the payer's payment method/Billing Account. Consent and payment authorization are separate user journeys/evidence.