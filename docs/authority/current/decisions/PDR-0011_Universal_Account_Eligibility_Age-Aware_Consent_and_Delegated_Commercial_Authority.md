# PDR-0011 — Universal Account Eligibility, Age-Aware Consent & Delegated Commercial Authority


**Status:** APPROVED — CONSTITUTIONAL BUSINESS DECISION  
**Effective:** 2026-10-17  
**Supersedes:** PDR-0007 — Date-of-Birth-Based 18+ Account Eligibility


## Decision


Tunner registration is available without an 18+ account-eligibility gate. Date of birth is not collected on the initial registration screen.


Canonical onboarding order:


```text
Register
→ Verify email/contact
→ Personal Information
→ collect date_of_birth
→ authoritative server-side age evaluation
→ continue normal Account/Product onboarding
```


Age is a policy input that controls age-sensitive capabilities. It is not, by itself, a reason to reject or terminate a Tunner Account.


For an applicant under 18:


- the Tunner Account may activate and use ordinary Tunner/Product capabilities when all other applicable gates pass;
- the Account is not forced into a separate top-level identity lifecycle merely because of age;
- consent/supervision is obtained where the active jurisdiction/policy requires it;
- guardian/authorized consent is the normal consent mechanism; stronger proof is required only where an active approved policy requires it;
- the user cannot independently act as payer/commercial authority for paid Tunner activity;
- the user cannot independently add/manage payment methods, authorize charges, or accept paid commercial terms as payer;
- paid access may be sponsored through an eligible Billing Account and existing Billing Authority mechanisms, including an authorized guardian/adult, organization, school/education organization, or another supported authorized payer;
- the sponsored user's Product access/entitlement remains separate from payer/Billing Account ownership.


For an applicant 18 or older, the normal commercial eligibility path applies, subject to the same Billing Account, authority, market, risk and provider policies as every other Account.


## Age evaluation


The backend performs calendar-safe evaluation:


```text
is_18_plus = date_of_birth + 18 calendar years <= age_policy_evaluation_date
```


Do not approximate age as `18 * 365` days.


Durable evidence records the age classification/evaluation, policy version, evaluation time and correlation/evidence reference. Age classification must be re-evaluable as the person ages or policy/jurisdiction changes; do not model a permanent `is_minor` business truth that requires Account migration at age 18.


## Identity and account-state principle


Age classification does not create a parallel child identity system.


```text
TunnerAccount identity/state
        +
Age/Consent Policy
        +
Commercial Capability Resolution
```


A user turning 18 keeps the same `account_id`, verified contacts, permitted history and Product relationships. Commercial restrictions are re-evaluated under active policy.


## Consent and supervision


Consent is distinct from billing/payment authority.


```text
Consent to use Tunner/Product
        ≠
Authority to pay or accept commercial terms
```


Tunner supports the minimum supervision/consent relationship required by active policy. The ordinary UX should remain simple. Do not require identity documents or heavy guardian verification unless an active approved policy requires stronger assurance.


School/education-organization authorization may satisfy the applicable supervised-use path when supported by active policy and contract. Such authorization does not make the student the payer and does not transfer the school's Billing Account authority to the student.


## Commercial and payment rule


The existing Tunner Billing Account and delegated Billing Authority system is reused. No separate minor-payment service, minor checkout engine, child billing account type or duplicate subscription system is created.


If a user without independent commercial eligibility requests a paid capability:


```text
Paid capability requested
→ resolve current Account commercial eligibility
→ independent payer allowed?
   YES → normal Billing Account flow
   NO  → resolve eligible sponsored/delegated Billing Account
          → validate BillingAuthority
          → existing Checkout/Payment/Subscription flow
```


If no eligible payer context exists, return the existing governed payer-context resolution path, with user-facing options such as guardian/authorized adult or organization/school sponsorship where supported.


## Data handling


`date_of_birth` remains `RESTRICTED_PERSONAL`.


DOB is collected in the Personal Information form after verified contact, not as an acquisition gate on the initial registration screen. Retention, minimization and disclosure follow the active Processing Governance Contract and Retention Policy.


## Product/marketing principle


Under-18 status should not generally degrade the normal Tunner or Connected Product experience except where age-specific consent, privacy, safety, commercial or jurisdiction policy requires a restriction.


Free/ordinary use remains available when applicable. Paid Product capability may be consumed when an authorized eligible Billing Account sponsors the user.


## Acceptance invariants


- initial registration does not require DOB;
- verified contact precedes the Personal Information form;
- DOB is collected in Personal Information and evaluated server-side;
- under-18 does not return the former global `AGE_INELIGIBLE` account rejection;
- under-18 Account may continue normal non-commercial use subject to active policy;
- under-18 user cannot independently own/operate paid commercial authority;
- existing BillingAccount/BillingAuthority/PaymentMethodReference/ProductBillingRelationship primitives are reused;
- guardian/school/organization sponsorship can fund paid access without transferring payer authority to the user;
- consent and billing authority remain separate;
- turning 18 requires capability re-evaluation, not Account replacement;
- historical PDR-0007 remains immutable decision history but is no longer active authority.