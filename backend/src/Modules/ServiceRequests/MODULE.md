# ServiceRequests module (the deletable sample)

## Purpose

The sample record type `service-request` (numbers `SR-2026-001`), the worked example of the business engine
(modules 7 to 11). It **only configures the engine** and keeps the few fields its wizard collects; everything generic
(record, status, owner, numbering, transitions, history, queues, deadlines, notifications, attachments) lives in the
kernel and the engine modules, which never name it. Delete it, or rename it into a project's first record type.

What it declares (all in this module):

| Piece | Where | What |
| --- | --- | --- |
| Statuses | `Domain/ServiceRequestRecord.cs` `ServiceRequestStatuses` | Draft 1, Submitted 2, UnderReview 3, Returned 4, Resubmitted 5, Approved 6, Rejected 7, Cancelled 8 (6, 7, 8 terminal); keys in `ServiceRequestStatusKeys` (`under-review`, ...) |
| Actions | `ServiceRequestActions` | `submit`, `start-review`, `return`, `resubmit`, `approve`, `reject`, `cancel` |
| Transition table | `Application/Workflow/ServiceRequestWorkflow.cs` | one `TransitionCatalog`: Draft -> Submitted -> Under Review -> Approved / Rejected / Returned; Returned -> Resubmitted -> Under Review; Draft or Submitted -> Cancelled. Reason **required** on `return` and `reject`, optional on `cancel` (field `reason`). Wording `serviceRequests.status.<statusKey>.<requester|staff|admin>`, labels `serviceRequests.action.<actionKey>`, a notification decision on every rule (`NotificationDecision.None` only for cancelling a draft) |
| Registration | `ServiceRequestWorkflowDefinition` via `AddRecordWorkflow<>()` | the kernel validates the catalog at start; a violation stops the boot |
| Record type | `ServiceRequestRecordTypeSource` | `service-request`, prefix `SR`, queue permission `service-requests.queue`, view-own `service-requests.view-own`, attachable record type `service-request` |
| SLA | `ServiceRequestSlaPolicySource` | 3 days on Under Review, due soon 1 day before, escalation template `service-requests.sla-escalation` |
| Submit check | `Application/Drafts/ServiceRequestSubmitValidator.cs` (`IRecordSubmitValidator`) | every field of every wizard step, plus an active category |
| Permissions | `ServiceRequestsPermissionKeys` | `service-requests.create` (also the requester transitions submit, resubmit, cancel), `.view-own`, `.queue`, `.review` (start-review), `.decide` (approve, reject, return) |
| Own data | `Infrastructure/Persistence` | `servicerequests.ServiceRequests` (`RecordId` = the Records record id, by value only; `CategoryId`, `Title`, `Description`, `ContactPhone`, `PreferredDate`, all nullable while a draft), one `InitialCreate` |

Seeds it adds through **new** migrations in other modules (they never reference this one; this module's tests prove
the frozen values equal its constants, `Seeds/SeedMigrationTests`):

- Lookups `AddServiceRequestCategoriesLookup`: lookup `service-request-categories` (`LookupTypes.ServiceRequestCategories`,
  ids `ServiceRequestCategoryIds` in `Starter.Domain/Lookups`): general 1, certificate 2, data-update 3, complaint 4.
- Attachments `AddServiceRequestAttachmentTypes`: `service-request-document` (id 2, required, at most 3) and
  `service-request-supporting` (id 3, optional, at most 5), both `.pdf;.jpg;.png`, record type `service-request`
  (constants in `Starter.Domain/Attachments`).
- Notifications `AddServiceRequestTemplates`: `service-requests.submitted`, `.under-review`, `.returned`,
  `.resubmitted`, `.approved`, `.rejected`, `.cancelled`, `.sla-escalation`, Arabic and English, in-app and e-mail;
  placeholder `{{recordNumber}}` (plus `{{dueAt}}` in the escalation). Keys: `ServiceRequestNotificationKeys`.

## Use

Typed wizard endpoints (`ServiceRequestsController`, root `ServiceRequestsRoutes.Root` = `/api/service-requests`; ids
are `PublicId<ServiceRequestRecordIds>`, the Records family `records`, so the same token works on
`/api/records/service-request/{id}`; problem+json errors):

| Endpoint | Access | Answer |
| --- | --- | --- |
| `POST /api/service-requests` | `service-requests.create` | 201 `{ id }`: a draft owned by the caller (Records creates the record, this module its empty row) |
| `GET /api/service-requests/{id}/draft` | `service-requests.create`, owner | `{ id, categoryId, title, description, contactPhone, preferredDate }` (nulls until saved); 404 `RECORDS_NOT_FOUND` |
| `PUT /api/service-requests/{id}/draft` | `service-requests.create`, owner, Draft or Returned | body = the draft fields + `step` (1, 2 or 3); validates and writes **only that step's fields** (step 3 writes nothing); 204; 400 `VALIDATION_FAILED` `{errors:{field:[codes]}}`; 404; 409 `SERVICE_REQUESTS_NOT_EDITABLE` |
| `POST /api/service-requests/{id}/submit` | `service-requests.create`, owner | 200 `{ number }`; 400 `VALIDATION_FAILED` (a crafted submit that skipped a step fails on that step's fields); 409 `ATTACHMENTS_REQUIRED_MISSING`; 409 `RECORDS_NOT_SUBMITTABLE`; 404 |
| `GET /api/service-requests/{id}` | `[AuthenticatedUser]`; whoever may see the record, decided by the Records module (`IRecordVisibility`): the owner with `view-own`; once submitted, a `service-requests.queue` or `records.oversight.view` holder whose scope reaches the record (a draft is private to its owner) | `{ id, number, categoryId, categoryNameAr, categoryNameEn, title, description, contactPhone, preferredDate }`; names null when the category was retired; 404 otherwise |

Resubmit, cancel and the staff decisions go through the generic `POST /api/records/service-request/{id}/transitions`
(module Records), which runs this catalog. Validation codes (`ServiceRequestValidationCodes`, the client renders
`serviceRequests.errors.<CODE>`): `SERVICE_REQUESTS_{CATEGORY_REQUIRED, CATEGORY_UNKNOWN, TITLE_REQUIRED, TITLE_TOO_SHORT,
TITLE_TOO_LONG, DESCRIPTION_REQUIRED, DESCRIPTION_TOO_SHORT, DESCRIPTION_TOO_LONG, CONTACT_PHONE_REQUIRED,
CONTACT_PHONE_INVALID, PREFERRED_DATE_INVALID, PREFERRED_DATE_IN_PAST, STEP_INVALID}`.

The wizard rules (`Application/Validation/ServiceRequestRules.cs`): category present (and an active value, checked by
the use case); title 5 to 200; description 20 to 2000; contact phone `^05[0-9]{8}$`; preferred date optional, a real
`YYYY-MM-DD` date not before today in the app time zone (`Numbering:TimeZoneId`, `IServiceRequestCalendar`). Text is
missing when null or white space; lengths count the text as sent; one code per field.

## Extend

**Delete the sample** (a fork that does not want it):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\remove-module.ps1 -Name ServiceRequests
```

It removes this folder, its test project, its slnx entries and `Starter.Api.csproj` ProjectReference, every
`codelab:module ServiceRequests` marker line or block in the frontend and backend registration files, the
`serviceRequests.*` translation keys, and `features/service-requests`. What stays, on purpose: the seed rows (lookup
`service-request-categories`, the two attachment types, the eight templates) and their constants in `Starter.Domain`,
because shipped migrations are never deleted; retire them with a new migration (`IsActive` = false for the lookup
values and attachment types) if the catalog should not show them. The engine builds and tests green without the
sample (the deletability check).

**Rename it into your record type** (recipe [add-workflow-type](../../../../docs/recipes/add-workflow-type.md)):

1. Copy or rename the module (`node tools/scaffold/new-module.mjs --name <Name>` gives a clean shell), then change
   `ServiceRequestRecord` (record type key, number prefix, attachable record type), `ServiceRequestStatuses` /
   `ServiceRequestStatusKeys` / `ServiceRequestActions` and the table in `ServiceRequestWorkflow`.
2. Replace the wizard fields: the `ServiceRequest` entity, its configuration, `InitialCreate`, `ServiceRequestRules`
   with its codes, the step enum and the typed endpoints. Keep the client mirror and the parity fixture in step.
3. Add your seeds with new migrations (recipes add-lookup, add-attachment-type, add-notification) and your
   permissions (`new-permission`); keep a notification decision on every rule.
4. The tests show the shape: walk every path with `TransitionFixture`, keep the parity test, the seed tests and the
   HTTP tests.

**Change the workflow:** edit only `ServiceRequestWorkflow` (a new status takes the next value; never renumber), add
its wording keys (ar and en) and, when it notifies, a template migration. The host refuses to start on an unreachable
status, a missing permission or a required reason without a field.

## Rules

- Configuration only: no status matrix, no `switch` on a status to decide an action; allowed actions and decisions
  come from the engine (`servicerequests.workflow-in-the-catalog`).
- Talks to the engine only through kernel ports (`IRecordLifecycle`, `IRecordDirectory`, `IRecordVisibility`,
  `ILookupValues`, `IUserDirectory`); never references another module (`module.rules.yaml`).
- One visibility policy: who may see a request is the Records module's decision (`IRecordVisibility`, the same rule
  as `GET /api/records/service-request/{id}` and the files); `ServiceRequestAccess` adds only the owner check of the
  draft endpoints and never decides visibility itself.
- The client rules are hand-mirrored; `validation-parity.fixtures.json` pins both sides
  (`servicerequests.validation-parity`).
- Every field change is a step save of the owner's draft or returned request; nothing else writes the table.
- Tests that name the sample live only in `backend/tests/Modules/Starter.Modules.ServiceRequests.Tests`, so deleting
  the module deletes them.

## Gotchas

- The record (status, owner, number, dates) is the Records module's; this table has no foreign key to it and a draft
  record may have no row yet (it reads as an empty draft).
- The submit validator checks the preferred date against **today**: a draft saved with tomorrow's date and submitted
  two days later is refused (`SERVICE_REQUESTS_PREFERRED_DATE_IN_PAST`).
- The attachment access of `service-request` files is served by the Records module from the record type registration
  through `IAttachableRecordAccessSource` (owner while Draft or Returned may change them); no host line is needed, and
  a second `IAttachableRecordAccess` for `service-request` breaks every attachments request.
- A requester who also holds `service-requests.review` / `.decide` still cannot start-review, approve, reject or
  return their own request: the engine refuses staff rules to the owner (403 `WORKFLOW_FORBIDDEN`).
- The category is a lookup PublicId token typed `string` on the wire (so an unreadable or retired token is the step's
  field error `SERVICE_REQUESTS_CATEGORY_UNKNOWN`); the PublicId contract test lets these members through by name
  (`EndpointAllowlist.LookupTokenStringMembers`, marked for remove-module).
- The template keys and the lookup and attachment ids are frozen as data in the other modules' migrations; change
  them only through a new migration there and the constants here together (the seed tests fail otherwise).
- HTTP tests (`Http/`, trait `Category=Http`) need the Records module and this module in `Starter.Api.csproj`, and a SQL
  Server (`CODELAB_TEST_SQL`); each creates and drops its own `p7_sr_<guid>` database. They run one host at a time.
- `CLAUDE.md`, `BannedSymbols.generated.txt`, `.editorconfig` and `CodeMetricsConfig.txt` in this folder are generated
  by the rules engine; never edit them by hand. Keep the `// scaffold:...` markers.
