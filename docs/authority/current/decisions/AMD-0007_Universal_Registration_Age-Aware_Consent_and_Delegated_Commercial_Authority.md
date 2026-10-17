# Tunner Development Authority Amendment


> **Status:** APPROVED / ACTIVE
> **Effective date:** 2026-10-17
> **Target baseline:** Tunner Development Documentation Baseline 1.6.0
> **Applicability:** GLOBAL where Account registration, age/consent, Billing Account authority, checkout/payment, Product access, privacy/UI or release behavior is affected.
> **Decision authority:** PDR-0011 — Universal Account Eligibility, Age-Aware Consent & Delegated Commercial Authority


# AMD-0007 — Universal Registration, Age-Aware Consent & Delegated Commercial Authority


## 1. Reason


The locked baseline and PDR-0007 treated age below 18 as global Account ineligibility. That approach creates unnecessary acquisition friction and prevents legitimate education, school-sponsored, organization-sponsored and guardian-sponsored use.


Tunner now separates Account eligibility from independent commercial/payment authority.


## 2. Change type


`PRODUCT | IDENTITY | BILLING | PAYMENT | PRIVACY | UI/UX | CONTRACT | RELEASE`


## 3. Supersession


PDR-0011 supersedes PDR-0007 prospectively.


Historical PDR-0007 and implementation/evidence created under it remain immutable history. They are not deleted or rewritten.


Any active or future implementation must use PDR-0011 + this amendment.


## 4. Registration and personal-information sequence


Replace the former DOB-first/18+-gate registration rule with:


```text
Initial Registration
→ Contact Verification
→ Personal Information
   → Date of Birth
→ Server-side Age Evaluation
→ Age/Consent/Commercial Capability Policy
→ Normal onboarding continues
```


The initial registration form does not require DOB or an 18+ checkbox.


## 5. Under-18 behavior


Age below 18 does not by itself deny the Tunner Account, authenticated Tunner session or ordinary Product relationship.


Keep the ordinary Account/Product process normal unless an age-specific policy requires a restriction.


Where consent is required, use a minimal governed consent/supervision relationship. Consent is sufficient by default; stronger proof is only introduced when an active approved policy requires it.


Do not create a separate top-level minor Account lifecycle unless a future approved requirement proves one is necessary.


## 6. Commercial authority


For a user who is not independently commercially eligible:


- deny independent payer/commercial capabilities;
- deny independent payment-method management and charge authorization;
- deny independent acceptance of paid commercial terms as payer;
- allow paid Product use when an eligible Billing Account sponsors the user;
- reuse existing Billing Account, BillingAccountMembership, BillingAuthority, PaymentMethodReference and ProductBillingRelationship contracts;
- supported payer sources may include an authorized adult/guardian, organization, school/education organization or another governed eligible payer.


No duplicate minor-payment subsystem is authorized.


## 7. Capability separation


The implementation must preserve:


```text
Account identity ≠ Billing Account
Product access ≠ payer authority
Consent ≠ payment authority
Sponsored entitlement/use ≠ Billing Account ownership
```


Age-sensitive restrictions are resolved as policy/capability decisions rather than encoded as a second identity architecture.


## 8. DOB and privacy


DOB remains `RESTRICTED_PERSONAL` and moves to the Personal Information step after contact verification.


The privacy inventory, Processing Governance Contract, retention policy, consent evidence and age-policy evidence must be updated accordingly.


## 9. Affected authorities


At minimum:


- Development Documentation README
- Decisions README
- 08 Security, Privacy, Compliance & Global Market Governance
- 16 Full-Project Documentation Closure & Gap Register
- 17 Account, Identity, Authentication & Session FRD
- 18 Billing Account & Delegated Billing Authority FRD
- 21 Billing Subscription, Checkout, Renewal & Dunning FRD
- 29 Privacy, Data Lifecycle & Processing Governance FRD
- 31 UI/UX Screen & Control Center Requirements Matrix
- 33 Production Readiness, Compliance & Release Gates
- 34 Architecture-to-Development Traceability Matrix
- 37 Current Authority Index
- applicable API/event/data/policy contracts and tests
- applicable FigJam Identity, Billing Authority and Checkout flows


## 10. Existing P2 implementation


Previously developed P2 Account/Identity work is not discarded.


Implementation must first inspect the governed state and exact code/evidence for P2-004. Apply the existing Revision lifecycle if the work item is historically DONE/CLOSED and this authority requires post-closure behavior change. If it remains active, modify it through the existing governed lifecycle.


Do not reset, rewrite or silently invalidate valid historical evidence. Create only the smallest corrective development lineage required by governance.


## 11. FigJam synchronization rule


Update existing architecture flows in place. Do not create a parallel minor-user architecture.


Identity flow:
- Register → verify contact → Personal Information/DOB → age evaluation;
- 18+ → ordinary onboarding;
- under 18 → ordinary onboarding with consent where required and commercial capability restriction.


Billing/checkout flow:
- paid action → commercial/payer eligibility;
- independent payer eligible → existing Billing Account flow;
- otherwise → resolve eligible guardian/organization/school/other authorized Billing Account → existing BillingAuthority → existing checkout/payment/subscription.


## 12. Acceptance


- no DOB on initial registration;
- no global under-18 Account rejection;
- under-18 ordinary use remains normal except policy-scoped restrictions;
- payment/commercial authority is the primary age-based restriction;
- sponsored paid access reuses existing delegated billing architecture;
- no duplicate payment/subscription domain;
- consent and payment authority remain separate;
- UI, contracts, tests, FigJam and release gates align;
- P2-004 is remediated through current governance without destructive redevelopment.


## 13. Approval


Approved by explicit Product Owner direction in the project conversation on 2026-10-17.