# PDR-0007 — Date-of-Birth-Based 18+ Account Eligibility

**Status:** APPROVED — CONSTITUTIONAL BUSINESS DECISION  
**Date:** 2026-09-21

## Decision
Tunner Account registration asks the applicant for their **date of birth** and the authoritative Tunner backend calculates whether the applicant is at least 18 years old.

There is no separate “I am 18+” attestation checkbox.

## Eligibility rule
The baseline rule is calendar-safe:

```text
eligible = date_of_birth + 18 calendar years <= age_policy_evaluation_date
```

Do not approximate age as `18 * 365` days.

If the applicant is under 18:
- return stable `AGE_INELIGIBLE`;
- do not activate a Tunner Account;
- do not create an authenticated Tunner session;
- do not activate a Product Relationship;
- show a clear user-facing message that Tunner is available only to users aged 18 or older.

## Data handling
`date_of_birth` is `RESTRICTED_PERSONAL`.

The durable eligibility evidence includes:
- `age_eligibility_status`;
- `age_evaluated_at`;
- `AgeAssurancePolicy` version;
- evidence/correlation reference.

Full DOB is retained only when an approved Processing Governance Contract and Retention Policy establish a necessary purpose. Otherwise, after the eligibility result is authoritatively committed, the raw DOB input is discarded.

Stronger identity/age proof may be required by a future market/risk policy, but it cannot be silently introduced as a normal registration requirement.
