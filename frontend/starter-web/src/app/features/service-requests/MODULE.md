# Web Service Requests (`features/service-requests`)

## Purpose

The frontend of the deletable **Service Request** sample (record type `service-request`, numbers `SR-2026-001`). It
shows one complete record type built on the generic engine: a requester files a request through a 3-step wizard, and
staff work it from a queue. The backend half is `backend/src/Modules/ServiceRequests`; the generic parts it
configures are `shared/records` (detail cards, timeline, decision panel, SLA badge, records and queues clients) and the
page patterns.

| Piece | Folder |
| --- | --- |
| Data layer both audiences use: `ServiceRequestsClient`, wire models, permission keys, wizard steps, validators, parity fixture, card adapter | `data/` |
| Requester screens (dashboard, my requests, wizard, detail with status tracker, cancel/resubmit) | `requester/` |
| Staff screens (work queue, detail with the decision panel and reason dialogs) | `staff/` |
| Dev-only wizard autofill set (`formKey 'service-request-wizard'`) | `dev/` |

`data/` (import from `features/service-requests/data`, its `index.ts`):

| Export | File |
| --- | --- |
| `ServiceRequestsClient` (`create`, `getDraft`, `saveDraft`, `submit`, `get`) and `ServiceRequestApiPaths` | `service-requests-api.ts` |
| `SERVICE_REQUEST_RECORD_TYPE`, `ServiceRequestId` / `ServiceRequestCategoryId` (WireId), `ServiceRequestStatus` (1-8, mirrors `ServiceRequestStatuses`), `ServiceRequestActions`, `ServiceRequestDraftStep`, `ServiceRequestAttachmentTypes`, `SERVICE_REQUEST_CATEGORY_LOOKUP_TYPE`, wire shapes | `service-request.models.ts` |
| `SERVICE_REQUESTS_PERMISSION_KEYS` (`service-requests.create / view-own / queue / review / decide`) | `service-requests-permissions.ts` |
| `ServiceRequestLimits`, `CONTACT_PHONE_PATTERN`, `ServiceRequestValidationCodes`, `validateServiceRequest(input, scope, today)`, the per-field rule functions, `serviceRequestFieldErrors` / `serviceRequestFieldErrorsFrom` | `service-request-rules.ts` |
| `serviceRequestValidators` (reactive-forms `ValidatorFn`s over the same rules), `serviceRequestControlMessageKey` | `service-request-validators.ts` |
| `todayInTimeZone(epochMs)`, `SERVICE_REQUEST_TIME_ZONE` (`Asia/Riyadh`) | `service-request-today.ts` |
| `SERVICE_REQUEST_STEPS`, `SERVICE_REQUEST_STEP_IDS`, `SERVICE_REQUEST_FIELD_STEPS` (the wizard's `[fieldSteps]`), `SERVICE_REQUEST_FIELD_LABEL_KEYS` | `service-request-steps.ts` |
| `serviceRequestCardsAdapter(details, language)` → the shared/records detail cards | `service-request-cards.ts` |
| `ServiceRequestAttachmentsTarget`, `serviceRequestAttachmentsTargetOf(record)` (the record's `attachableRecordType` + `attachmentsRecordId`), `SERVICE_REQUEST_ATTACHMENTS_UNAVAILABLE`, `serviceRequestAttachmentsFeedback(error, translate)` | `service-request-attachments.ts` |
| `validation-parity.fixtures.json`: the FE/BE parity contract | `data/` |

## Use

```ts
// The wizard: one form group per step, validators from the data layer, today from the injected clock.
const today = () => todayInTimeZone(inject(APP_CLOCK)());
const request = new FormGroup({
  categoryId: new FormControl<string | null>(null, serviceRequestValidators.categoryId),
  title: new FormControl<string | null>(null, serviceRequestValidators.title),
});
const details = new FormGroup({
  description: new FormControl<string | null>(null, serviceRequestValidators.description),
  contactPhone: new FormControl<string | null>(null, serviceRequestValidators.contactPhone),
  preferredDate: new FormControl<string | null>(null, serviceRequestValidators.preferredDate(today)),
});
// The pattern's steps: SERVICE_REQUEST_STEPS plus each step's valid signal; [fieldSteps]="SERVICE_REQUEST_FIELD_STEPS".
// A control's message: {{ serviceRequestControlMessageKey(control.errors) | t }}

// Save, resume, submit:
const { id } = await firstValueFrom(client.create());
await firstValueFrom(client.saveDraft(id, { fields, step: ServiceRequestDraftStep.Request }));
const { number } = await firstValueFrom(client.submit(id)); // 'SR-2026-001'
// A refusal: serviceRequestFieldErrorsFrom(parseApiError(error)) → the wizard's field errors (opens the owning step);
// 409 ATTACHMENTS_REQUIRED_MISSING has no field: show it on step 3.

// The detail page: the request's own fields as generic cards.
cards = computed(() => serviceRequestCardsAdapter(this.details(), this.language.current()));
```

- Status, timeline, allowed actions, resubmit, cancel and every staff decision come from the generic
  `api/records/service-request/...` endpoints (shared/records `RecordsClient`), never from this client.
- Attachments: the widget gets the **Records-kernel attachments id**, never the request id. Every page reads the
  generic record (`GET api/records/service-request/{id}`, shared/records `RecordsClient.detail`) and takes
  `serviceRequestAttachmentsTargetOf(record)` → `{ recordType, recordId }` (`attachableRecordType`,
  `attachmentsRecordId`): `<app-record-attachments [recordType]="target.recordType" [recordId]="target.recordId" />`.
  The detail stores (requester, staff, admin) read it with the record they already load and expose `attachments` /
  `attachmentsError`; the wizard store reads it once the draft exists (created on the first save, or resumed) and
  offers `retryAttachments()`. The widget is hidden until the target is known; a failed read (or a record without a
  usable id, `SERVICE_REQUEST_ATTACHMENTS_UNAVAILABLE`) shows `<dga-page-feedback>` built by
  `serviceRequestAttachmentsFeedback` (`errors.<CODE>` text and the reference code, both bundles). The required type
  is `ServiceRequestAttachmentTypes.Document`.
- Categories: `GET api/lookups/service-request-categories` (`SERVICE_REQUEST_CATEGORY_LOOKUP_TYPE`); the value `id` is
  the opaque token the draft's `categoryId` carries.

## Extend

- **Rename the sample into your record type:** follow `docs/recipes/add-workflow-type.md`. Keep the shape: a `data/`
  folder with the client, models, steps, rules + validators, the parity fixture and the card adapter.
- **A new wizard field:** add it to `ServiceRequestFields`, its rule function and code in `service-request-rules.ts`,
  the step's scope in `SERVICE_REQUEST_SCOPE_FIELDS`, a validator in `serviceRequestValidators`, its label key, and
  **parity cases in the fixture**; then the same rule in the backend validator. Both parity tests must be green.
- **A new limit:** a member of `ServiceRequestLimits` (and the backend twin), never a literal in a rule.
- **A new action or status:** the backend catalog owns it; mirror the key in `ServiceRequestActions` or the integer in
  `ServiceRequestStatus`, and add the `serviceRequests.action.<key>` / `serviceRequests.status.<key>.<audience>` texts.
- **Delete the sample:** `tools/remove-module.ps1 -Name ServiceRequests`. It removes this folder, the backend module,
  its tests and every line marked `// codelab:module ServiceRequests` (or inside `codelab:module-begin/-end`) in the
  shared files (routes, nav, config, csproj), and the `serviceRequests.*` translation keys.

## Rules

- **Parity rule: change both sides and the fixture together.** The FE rules are hand-mirrored from the backend
  FluentValidation validators (no metadata generator). `data/validation-parity.fixtures.json` is the contract:
  `validation-parity.spec.ts` (FE) and the backend parity test run every case and must give exactly the expected
  `valid` and `errorFields` (sorted ordinally, camelCase). A rule change without a fixture case is incomplete.
- Shared semantics: a text is missing when null, absent or white space only; lengths count the value as sent; each
  field reports one code (required, then format, then length or range); `preferredDate` is optional, a real
  `YYYY-MM-DD` date and not before today in `SERVICE_REQUEST_TIME_ZONE`; `categoryId` is checked for presence only.
- The client checks are a convenience; the server re-validates every step on submit (a crafted submit that skipped
  step 2 is refused with 400 `VALIDATION_FAILED`).
- HTTP only through `ApiService` (the feature clients). Every call is quiet; the screens show refusals inline.
- No literals used as keys: permission keys, action keys, statuses, record type, lookup type, attachment types,
  field names, step ids, limits and translation keys come from the constants in `data/`.
- Every text has `ar` and `en` keys under `serviceRequests.*`; validation messages are
  `serviceRequests.errors.<CODE>`.
- Tests that name the sample live only in this folder (and the backend `Starter.Modules.ServiceRequests.Tests`).
- Imports: core, shared, patterns and `@codelab/ng-dga` only (ESLint boundaries); generic code never imports this
  feature.

## Gotchas

- **The attachments id is not the request id.** `attachmentsRecordId` is a PublicId in another family
  (`attachable-records`); passing the request id (or `record.id`) to `app-record-attachments` lists nothing and
  uploads are refused. The shared `RecordDetail` type does not declare the two attachment fields, so read them only
  through `serviceRequestAttachmentsTargetOf`. A decision (transition) does not move the files: the stores keep the
  target read on load. The spec fixtures (`aRecord`, `staffRecord`) carry `TEST_ATTACHMENTS_RECORD_ID`, which
  differs from `TEST_SERVICE_REQUEST_ID`, so a page that passes the wrong id fails its spec.

- `today` is the date in the app time zone, not the browser's: `todayInTimeZone(APP_CLOCK())`. Never `new Date()`
  in a rule, and pass `today` as a function to `serviceRequestValidators.preferredDate` so a wizard left open past
  midnight re-checks against the new day.
- `categoryId` is a lookup PublicId token (family `lookup-items`), not the lookup's integer id; the fixture uses a
  shape-valid sample token. Whether the token names an active category is a server check outside the parity contract.
- The server may send a platform code (`REQUIRED`) for a field; `serviceRequestFieldErrorsFrom` renders codes it does
  not own as `errors.<CODE>`.
- `serviceRequestCardsAdapter` returns plain `{ titleKey, fields: { labelKey, value }[] }` cards, structurally the
  shared/records `RecordCard`; values are display text, so pass the current language.
- `SERVICE_REQUESTS_PERMISSION_KEYS` and the staff/admin key maps point at the generated `PermissionKeys` members
  (`permission-keys.generated.ts`); route-gates.spec accepts generated keys only. After a backend key change, run
  `npm run api:gen` (check with `npm run api:gen:check`) before touching the screens; never cast a string to
  `PermissionKey`.
- The requester shell hangs on the empty path at the app root, before the staff shell. An empty-path layout matches
  `/` even when no child does (it renders an empty frame), so the shell carries `canMatch: [requesterShellMatch]`
  (`requester/requester-landing.ts`) and claims only `/service-requests/...`. On `/`, `requesterLandingGuard` runs
  before the dashboard's `permissionGuard()`: a user without `dashboard.view` who holds `service-requests.view-own`
  is redirected to the requester dashboard; staff and admins stay on the staff dashboard.
