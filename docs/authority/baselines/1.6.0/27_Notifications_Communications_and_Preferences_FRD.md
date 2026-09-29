> **Tunner Development Authority Package**  
> **Status:** ACTIVE — LOCKED DEVELOPMENT BASELINE 1.6.0  
> **Closure revision date:** 2026-09-29  

# 27 — Notifications, Communications & Preferences FRD

## 1. Authority and traceability

Fulfills FigJam **15, 17, 93–99**. Tunner owns shared notification orchestration and delivery evidence. Connected Products may request/use notification capability through SDK/API but remain free to own Product-local communications where their contract says so.

Initial channels:

- **EMAIL** — Resend primary production provider;
- **IN_APP** — Tunner-native Account/Admin notification inbox where applicable.

SMS and mobile push are supported by the channel/provider abstraction but **not enabled initially** until a provider/capability decision is approved. This is not a runtime ambiguity: requests for a disabled channel resolve `CHANNEL_NOT_ENABLED` or approved fallback policy.

## 2. Canonical resources

- `NotificationIntent` — stable Tunner business communication identity;
- `NotificationClassPolicy`;
- `NotificationTemplateVersion`;
- `NotificationRecipientResolution`;
- `RecipientPreference` / `ConsentEvidence` / `Suppression`;
- `NotificationRouteResolution`;
- `DeliveryAttempt`;
- `ProviderDeliveryEvidence`;
- `InAppNotificationItem` — Tunner-owned in-app item/state exposed to authorized surfaces;
- `NotificationIncident` / DLQ evidence.

## 3. Notification classes

Canonical classes:

```text
MANDATORY_SECURITY
MANDATORY_TRANSACTIONAL
SERVICE_OPERATIONAL
PRODUCT_OPTIONAL
MARKETING
```

Rules:

- mandatory security/transactional messages are not suppressed by optional marketing preference, but must still respect contact safety/legal policy;
- marketing requires applicable consent/permission evidence and unsubscribe/preferences handling;
- Product optional communications obey Product capability plus recipient preferences;
- class selection is controlled by versioned policy/template contract, not caller-supplied free text.

## 4. Intent lifecycle

```text
CREATED
READY
DISPATCHING
PARTIALLY_DELIVERED
COMPLETED
FAILED
SUPPRESSED
CANCELED_BEFORE_SEND
```

`NotificationIntent.COMPLETED` means all policy-required channel outcomes for that intent are terminal. It does **not** collapse provider `ACCEPTED` into `DELIVERED`; channel/attempt evidence remains independently explicit through `DeliveryAttempt`.

Business domains create a stable Notification Intent **after their authoritative commit** unless a synchronous user challenge specifically requires immediate message creation.

Notification state never changes Payment/Invoice/Subscription authority.

## 5. Template governance

Templates are immutable/versioned and contain:

- notification class;
- allowed channels;
- locale;
- subject/body structure;
- variable schema and classification;
- Product branding scope if allowed;
- mandatory legal/footer/unsubscribe components from active policy;
- effective interval;
- approval/version lineage.

Runtime renders only declared variables and rejects missing/extra sensitive variables according to schema.

No arbitrary HTML/email body supplied by Connected Product is executed as a trusted template without a registered template contract.

### 5A. Global content-management integration

Notification/email/in-app presentation content participates in the same global governed Tunner content architecture as UI content. `NotificationTemplateVersion` remains the notification-domain delivery/rendering contract, but its human-readable subject/body/footer/content slots must reference or snapshot the effective governed presentation content/version as required so central content management does not weaken notification class, variable schema, consent, delivery or historical evidence.

Authorized content editors may manage user-oriented notification copy through contextual draft/preview/approval/publication workflows. They cannot change notification class, recipient eligibility, consent requirements, provider routing, delivery finality or other notification-domain semantics through content editing. Sensitive/legal/security templates use stronger policy-defined approval/version evidence. Model-generated notification copy is a proposal only and follows the same validation/publication controls.

## 6. Recipient resolution

Recipients resolve from authoritative verified-contact references or approved address contract. Tunner does not copy an unverified Product email into “verified” state.

Resolution checks:

- contact verified/eligible for message class;
- contact not superseded/revoked;
- preference/consent/suppression policy;
- locale;
- Product/channel capability;
- market/marketing policy when applicable.

The exact contact value used for a delivery is retained as protected delivery evidence according to privacy/retention policy; current Account email changes do not rewrite history.

## 7. Resend adapter

Resend is primary production email adapter through official .NET SDK.

Tunner provides:

- stable `delivery_id`;
- provider idempotency key derived from delivery/attempt identity;
- canonical provider message ID;
- webhook signature verification;
- event deduplication;
- normalized states;
- durable retry/outbox identity beyond provider's idempotency retention window.

Provider idempotency is an additional safeguard, not Tunner's long-lived duplicate-prevention authority.

## 8. DeliveryAttempt state model

```text
PREPARED
SUBMITTED
ACCEPTED
DELIVERED
BOUNCED
COMPLAINED
FAILED
UNKNOWN_EXTERNAL_STATE
CANCELED_BEFORE_SUBMISSION
```

Provider acceptance is not delivery. For message classes where inbox delivery rather than remote receipt is the contract, UI wording must reflect the available evidence.

A transport timeout after submission can become `UNKNOWN_EXTERNAL_STATE`; Tunner does not immediately send the same business message through Mailgun/another provider because that can duplicate customer communication.

## 9. Retry/failover

Retry policy considers:

- definitive provider error class;
- message class;
- attempt age;
- idempotency/provider support;
- recipient suppression changes;
- template/route version;
- unknown outcome.

Unknown external outcome routes to reconciliation/provider-event wait. Alternate provider failover is permitted only when active policy proves duplicate-send risk is controlled.

Mailgun is an approved deferred adapter and is not implemented/activated merely to create nominal redundancy.

## 10. Suppression, bounce and complaint

Hard bounce/complaint/provider suppression evidence creates or updates `Suppression` according to policy. Suppression changes are audited and class/channel scoped.

Security-critical communication failure can open a Support/Security notification exception but cannot silently bypass a known unsafe/suppressed recipient path.

## 11. Preferences and consent

`RecipientPreference` supports per channel/class/Product scope without treating Product preference as Account identity authority.

Marketing consent evidence stores:

- subject/contact/account ref;
- scope/purpose;
- source;
- timestamp;
- policy/notice version;
- jurisdiction/market context;
- withdrawal timestamp/evidence when applicable.

CASL/CAN-SPAM/global requirements are represented through `CommunicationPolicy`; developers do not encode one global consent rule.

## 12. In-app notifications

`InAppNotificationItem` is owned by Tunner and supports authorized Account Center/Admin/Connected Product surfaces through the Tunner SDK/API contract with:

```text
UNREAD
READ
ARCHIVED
```

Read/archive is presentation/user state; it does not acknowledge a legal/financial obligation unless a specific workflow separately records acknowledgment.

Sensitive in-app content is permission/Account/Product scoped and expires/retains according to classification. A Connected Product may render/manage its Product experience, but it does not become the authority for Tunner's NotificationIntent, delivery evidence or in-app item history.

## 13. Product integration

Connected Products request Tunner-managed notifications only through Tunner SDK/API contracts. A Product using Tunner notification service does not call Resend/Mailgun/another Tunner notification provider directly, does not hold provider credentials and does not depend on provider-native message IDs/statuses.

Connected Product capability specifies:

- allowed canonical event types/templates/classes;
- channel enablement;
- branding/approved template variables;
- fallback policy;
- event/result callback if contracted;
- authorized in-app retrieval/read/archive scope.

A Product can request a registered NotificationIntent but cannot use Tunner as an unrestricted bulk-email relay or submit arbitrary trusted provider payload/body content unless an explicit governed contract allows it.

A Product may separately own Product-local communications only where its contract explicitly says so; such communication is outside Tunner delivery authority unless it is requested through Tunner.

## 14. APIs/events/UI

APIs include create intent from trusted owner-domain or Connected Product SDK/API contract, query intent/delivery, recipient preferences, consent withdrawal, Tunner-owned in-app item read/archive and provider webhook intake.

Events include `NotificationIntentCreated`, `NotificationSuppressed`, `DeliveryAccepted`, `DeliveryDelivered`, `DeliveryBounced`, `DeliveryComplained`, `DeliveryFailed`, `DeliveryOutcomeUnknown`.

Account Center exposes communication preferences and in-app history. Admin Notification Control Center exposes templates, route/provider health, delivery exceptions, suppressions and immutable attempt evidence—not raw provider secret config.

## 15. Acceptance cases

- Payment succeeds and email fails: Payment remains succeeded;
- Resend webhook replay does not duplicate state transition;
- Resend timeout with unknown result does not immediately send via alternate provider;
- unverified contact cannot be used as verified security destination;
- marketing message without required consent is suppressed before provider call;
- unsubscribe does not suppress mandatory security message unless policy/contact safety requires it;
- template update never alters historical rendered-delivery evidence;
- Product cannot send arbitrary unregistered bulk message through Tunner;
- Product using Tunner notification service cannot supply provider credentials/provider-native IDs or call the configured notification provider directly;
- provider `ACCEPTED` evidence cannot cause Tunner to report `DELIVERED`;
- in-app item state is Tunner-owned and Product UI actions use the authorized Tunner SDK/API contract.
