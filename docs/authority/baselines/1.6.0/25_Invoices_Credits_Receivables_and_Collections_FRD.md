> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Baseline revision date:** 2026-09-29  

# 25 — Invoices, Credits, Receivables & Collections FRD

## 1. Authority and traceability

Fulfills FigJam **22** and invoice/receivable portions of Subscription, Commercial, Tax and Finance flows. Invoice owns the legal/billing document lifecycle and receivable balance relationships. Ledger remains accounting authority; Payment remains collection finality; Notification remains delivery authority.

## 2. Canonical resources

- `Invoice` and immutable `InvoiceVersion`;
- `InvoiceLine` linked to immutable Commercial/Tax evidence;
- `InvoiceNumberAssignment`;
- `Receivable`;
- `PaymentAllocation` and `AllocationReversal`;
- `CreditNote` / `CreditAllocation`;
- `InvoiceVoid`;
- `WriteOff`;
- `ReceivablesCase` / collection action;
- `InvoiceDelivery` reference to Notification delivery evidence.

## 3. Invoice state model

```text
DRAFT
ISSUED
OPEN
PARTIALLY_PAID
PAID
VOID
WRITTEN_OFF
```

Rules:

- `DRAFT` is mutable only through controlled recalculation/versioning before issue;
- `ISSUED/OPEN` content is immutable except through separate legal correction resources;
- `PARTIALLY_PAID` and `PAID` derive from authoritative allocations;
- `VOID` preserves the issued document and a separate void reason/result;
- `WRITTEN_OFF` is accounting/receivable treatment, not deletion or proof that the customer obligation legally ceased unless policy says so.

## 4. Draft construction

Invoice draft binds:

- legal seller/merchant and legal-entity version;
- Billing Account and Billing Profile version;
- Product/Billing Subscription refs;
- Commercial Snapshot(s);
- line descriptions safe for customer display;
- currency;
- Tax Determination(s);
- totals/rounding evidence;
- invoice policy/version;
- service/billing period where applicable.

The invoice does not reconstruct historical pricing from current catalog data.

## 5. Numbering and issue

`InvoiceNumberPolicy` is versioned by legal entity/jurisdiction/document type and defines numbering series/format/sequence constraints without embedding jurisdiction-specific assumptions in domain code.

Finalization validates:

- all required line/commercial/tax evidence final;
- Billing Profile required fields present;
- numbering policy ready;
- seller/legal entity/registration references ready;
- totals balanced;
- no conflicting finalization operation.

Issue assigns legal invoice number exactly once through concurrency-safe sequence control.

A failed notification after issue does not revert Invoice state.

## 6. Receivable

Issuing an invoice that represents an amount due creates/updates a `Receivable` with:

- original amount;
- allocated amount;
- credit amount;
- outstanding amount;
- due date/policy version;
- collection state;
- currency and legal entity.

Receivable amount is derived from immutable transactions/allocations, not an editable balance field.

## 7. Payment allocation

Payment success does not automatically mean a particular Invoice is paid unless an authoritative allocation exists.

Allocation validates:

- succeeded/eligible Payment amount/currency;
- available unallocated amount;
- Invoice outstanding balance;
- legal entity/Billing Account compatibility;
- allocation policy/order;
- concurrency/version.

Over-allocation is prohibited. Partial allocation produces `PARTIALLY_PAID`. Full settled allocation produces `PAID`.

Allocation reversal is a separate immutable operation and may reopen a receivable state according to policy.

## 8. Credits and Credit Notes

Issued invoice correction that reduces customer liability uses a separate `CreditNote` where required by the applicable invoice/tax policy.

Credit Note states:

```text
DRAFT
ISSUED
APPLIED
VOID
```

It references original invoice/lines/tax evidence and creates compensating receivable/ledger effects. Original issued invoice content remains reproducible.

A `CreditNote` is the governed documentary and financial correction for an issued Invoice. Any separate customer-credit capability requires its own explicitly approved resource, contract and accounting policy before use.

## 9. Invoice void

Void is permitted only under active legal/invoice policy and state eligibility. It never deletes an issued invoice. The void operation records actor/reason/policy, effective result and corresponding ledger/tax correction path.

If correction requires Credit Note rather than void, backend returns the required path; operator cannot select the easier method arbitrarily.

## 10. Write-off

Write-off requires:

- receivable remains outstanding;
- write-off policy/authority;
- reason/classification;
- approval/SoD above configured risk threshold;
- ledger posting rule.

Write-off creates compensating accounting evidence and `WRITTEN_OFF` receivable/invoice state. Later payment uses a `LatePaymentRecovery` operation with new accounting evidence rather than rewriting the write-off.

## 11. Collections/dunning boundary

Subscription dunning governs subscription-linked retry/access-related billing lifecycle. `ReceivablesCase` governs collection operations for an unpaid receivable.

Collection actions are versioned/policy-driven, such as:

- customer notification;
- request payment-method update;
- schedule eligible retry through Payment/Dunning;
- manual finance follow-up;
- write-off review.

No collection workflow directly changes Product entitlement.

## 12. Invoice delivery

Invoice issuance creates a Notification Intent with immutable invoice/document reference. Notification tracks email/in-app delivery.

`InvoiceDelivery` is an evidence link; delivery status does not alter invoice issue/finality.

Customer can retrieve the current legal invoice document securely through Account Center according to authority, even if email delivery failed.

## 13. Documents/object storage

Generated immutable invoice/credit documents may be stored in Cloudflare R2 through `IObjectStorage`. PostgreSQL remains authoritative for:

- document identity/version;
- owner/legal entity;
- content hash/checksum;
- object key/version ref;
- classification;
- retention/hold;
- generation template/version.

R2 object presence alone is not Invoice authority.

## 14. API/events

Operations include:

- create/recalculate draft;
- finalize/issue;
- allocate/reverse allocation;
- issue credit note;
- void when eligible;
- open/manage Receivables Case;
- write off;
- retrieve immutable document metadata/content authorization.

Events include `InvoiceIssued`, `InvoicePartiallyPaid`, `InvoicePaid`, `InvoiceVoided`, `CreditNoteIssued`, `ReceivablePastDue`, `ReceivableWrittenOff`, `LatePaymentRecovered`.

## 15. UI requirements

Admin Finance invoice views must show:

- legal entity/seller;
- Billing Account/Product/Subscription;
- Commercial/Tax snapshots;
- issue/numbering evidence;
- allocations/credits/write-offs;
- delivery status as a separate panel;
- permitted corrective actions from backend eligibility.

No inline editing of an issued invoice exists.

## 16. Acceptance cases

- concurrent finalization assigns one invoice number and one issued document;
- current catalog/tax changes do not change issued invoice content;
- payment without allocation does not mark invoice PAID;
- duplicate allocation is idempotent and cannot over-allocate;
- issued invoice correction creates credit/void evidence instead of mutation;
- notification failure leaves invoice ISSUED/OPEN and customer can retrieve it;
- write-off followed by late payment creates new recovery evidence;
- R2 object deletion/failure cannot silently erase PostgreSQL invoice/document authority.
