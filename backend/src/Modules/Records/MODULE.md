# Records module

## Purpose

Records (module 7), with the generic execution of the workflow (module 8) and the record read API (module 11). It
owns, in schema `records`, the base row of every record of every record type and its timeline, and it is the only
place a record changes status. It names no record type: a record type is configured by its own module (the sample
or a fork's own) through kernel ports, and everything here works for any of them.

- **`Record`** (`records.Records`): `Id` (identity; the wire carries `PublicId<RecordsIds>`, label `records`),
  `RecordType` (kebab key), `Number` (`PREFIX-YYYY-NNN`, null until the first submit, unique when set), `Status` (the
  record type's int value), `OwnerUserId`, `OrganizationId` (the owner's organization at creation), `CreatedAt`,
  `SubmittedAt` (first submit), `UpdatedAt`, `RowVersion` (a concurrent change makes the second save fail with 409
  `RECORDS_CONCURRENCY`). A record type keeps its own fields in its **own subtype table keyed by `RecordId`** in its
  own schema (an owned subtype table, no cross-schema foreign key); this row never carries record-type data.
- **`RecordHistory`** (`records.RecordHistory`, a `Starter.Domain.Auditing.RecordHistoryEntry`): one append-only row
  per action with the actor (id and display name at the time), the `Side` (`TransitionSide`: requester 1, staff 2,
  system 3), from/to status, action key and reason. Mapped with `HasRecordHistory`: **ON DELETE RESTRICT**, so a record
  with history can never be deleted. The first row of every record is `create` (no from-status).

What it does for every record type:

- **Lifecycle** (kernel port `IRecordLifecycle`, as the current user): `CreateDraftAsync` (needs the permission of the
  type's submit rule; the record takes the catalog's initial status), `SubmitAsync` / `ResubmitAsync`, `CancelAsync`,
  `TransitionAsync`. Submit and resubmit run, in order: the loader (caller, type, visibility), the workflow decision,
  every `IRecordSubmitValidator` of the type, `RequiredAttachmentsGate.RefuseWhileMissingAsync` (409
  `ATTACHMENTS_REQUIRED_MISSING`), then the number from `IRequestNumberAllocator` with the registration's prefix (a
  resubmit keeps the first number; a refused submit burns none) and the commit.
- **Transitions**: every action goes through `RecordWorkflowEngine.Decide`; the status change and its history row are
  saved in ONE transaction; then every `IRecordTransitionObserver` and the rule's notification (template from its
  `NotificationDecision`, to the requester and/or the current assignee read through `IRecordAssignmentReader`,
  channels in-app + e-mail, placeholders `recordNumber`, `statusKey`, `actionKey`, `reason`), each isolated.
- **Directory** (kernel port `IRecordDirectory`): record summaries for Assignments and Deadlines (no access check;
  queries leave drafts out).
- **Attachments**: `RecordAttachmentAccess`, the `IAttachableRecordAccess` of each registered record type (keyed by its
  registration's `AttachableRecordType`): whoever may see the record may view its files; only the owner may change
  them, and only while the record is editable (its initial status, or a status a `resubmit` rule leaves).
  `RecordAttachmentAccesses` publishes one per registered record type as the kernel `IAttachableRecordAccessSource`,
  so a record type's files work with no host wiring.
- **Visibility** (kernel port `IRecordVisibility`, `RecordVisibility`): the `RecordLoader` decision for a record-type
  module that serves its own fields (the sample's details card), so those fields are never more visible than the record.

## Use

Permissions (`RecordsPermissionKeys`, declared by `RecordsPermissionCatalogSource`): `records.oversight.view` (the
admin "all requests" list, read-only). Everything else is per record type: its registration's `ViewOwnPermissionKey`
and `QueuePermissionKey`, and the permission keys of its workflow rules.

| Endpoint | Access | Use case | Answer |
| --- | --- | --- | --- |
| `GET api/records/{recordType}/mine?status=&page=&pageSize=` | signed in + the type's view-own key (403 `RECORDS_VIEW_FORBIDDEN`) | `ListMyRecords` | `{items: [{id, recordType, number, status, statusKey, createdAt, submittedAt, updatedAt, isTerminal}], page, pageSize, total}`, the caller's own records (drafts included), newest first |
| `GET api/records/{recordType}/{id}` | signed in; owner (view-own), staff (queue key reaching the record's organization) or oversight | `GetRecordDetail` | `{id, recordType, number, status, statusKey, wording {requesterKey, staffKey, adminKey}, audience, isTerminal, ownerDisplayName, createdAt, submittedAt, updatedAt, lastReason, allowedActions [{actionKey, labelKey, reason, reasonFieldKey, toStatus, toTerminal}], assignee {userId, displayName, assignedAt}, sla {state, dueAt}, attachableRecordType, attachmentsRecordId}` |
| `GET api/records/{recordType}/{id}/timeline` | as the detail | `GetRecordTimeline` | `[{at, actorDisplayName, side, actionKey, fromStatus, toStatus, toStatusKey, reason}]`, oldest first |
| `POST api/records/{recordType}/{id}/transitions` `{action, reason?}` | as the detail + the rule's permission (and ownership) | `TakeRecordTransition` | 200 the new detail; 400 `WORKFLOW_REASON_REQUIRED` / `WORKFLOW_REASON_TOO_LONG` / `VALIDATION_FAILED`, 403 `WORKFLOW_FORBIDDEN`, 404 `RECORDS_NOT_FOUND` / `WORKFLOW_ACTION_UNKNOWN`, 409 `WORKFLOW_ACTION_NOT_ALLOWED` / `ATTACHMENTS_REQUIRED_MISSING` / `RECORDS_CONCURRENCY` |
| `GET api/records/{recordType}/oversight?status=&number=&page=&pageSize=` | `records.oversight.view` (narrowed to the caller's organization at that scope) | `ListRecordOversight` | the list item shape plus `ownerDisplayName`; submitted records only; no actions |

- A missing record and one the caller may not see answer the same 404 `RECORDS_NOT_FOUND`; an unknown record type
  too. A token that names no active account answers 403 `RECORDS_ACTOR_UNKNOWN`.
- `allowedActions` are computed by the engine for the caller's audience: requester actions for the owner, staff
  actions for staff, none for oversight and none on a terminal record. Screens render these and nothing else.
- `attachmentsRecordId` is the same record id in the attachment routes' family (`attachable-records`): open the files
  with `api/records/{attachableRecordType}/{attachmentsRecordId}/attachments`.
- Error codes: the kernel's `RecordsErrorCodes` (`RECORDS_NOT_FOUND`, `RECORDS_NOT_SUBMITTABLE`, `RECORDS_CONCURRENCY`),
  `WorkflowErrorCodes`, `AttachmentErrorCodes.RequiredMissing`, and this module's `RecordsModuleErrorCodes`
  (`RECORDS_ACTOR_UNKNOWN`, `RECORDS_VIEW_FORBIDDEN`).

## Extend

**Add a record type** (in its own module; never here):

1. Statuses, actions and permission keys as constants; a `TransitionCatalog` with exactly one initial status whose
   submit rule is keyed `submit` (`RecordLifecycleActions.Submit`), a resubmit rule keyed `resubmit` and cancel rules
   keyed `cancel` when the type has them. Register it with `AddRecordWorkflow<TDefinition>()`.
2. An `IRecordTypeRegistrationSource` with the `RecordTypeRegistration` (record type, number prefix, queue and view-own
   permission keys, attachable record type).
3. Optional: an `IRecordSubmitValidator` (field errors that block a submit), attachment types for its attachable
   record type (seeded by an Attachments migration, `docs/recipes/add-attachment-type.md`), an `ISlaPolicySource`.
4. Its own subtype table keyed by `RecordId` in its own schema, and typed endpoints (create draft, save draft, submit)
   that call `IRecordLifecycle`; every other action goes through the generic transitions endpoint.
5. Nothing to wire for the attachments of the type: Records serves its `IAttachableRecordAccess` through
   `IAttachableRecordAccessSource`. Serve the type's own fields only after `IRecordVisibility.CanViewAsync`.

**Change this module:** a new endpoint with `new-endpoint --module Records`; a new permission with `new-permission`.
Keep every rule in the record-type-neutral classes (`RecordLoader`, `RecordStanding`, `RecordSubmissions`,
`RecordCommits`, `TransitionEffects`), with tests in `backend/tests/Modules/Starter.Modules.Records.Tests` (the
test-only record type `test-record` of `Starter.Workflow.Testing`) and real-SQL HTTP tests in
`backend/tests/Starter.Integration.Tests/Records`.

## Rules

- **The engine decides** (`records.engine-decides`): every transition through `RecordWorkflowEngine.Decide`, every
  offered action from `RecordWorkflowEngine.AllowedActions`; no status matrix and no record-type name in this module
  (the engine vocabulary sweep in `Starter.Architecture.Tests/Engine` reads this folder).
- **Submit checks** (`records.submit-checks`): validators, then the interlock, then the number, also when `submit` or
  `resubmit` arrives through the generic transitions endpoint.
- **History in the same save** (`records.history-same-save`), Restrict, append-only.
- **Side effects after the commit, isolated** (`records.isolated-side-effects`): an observer or a notification that
  throws is logged (ids and keys only) and the transition stays saved.
- **Visibility**: the owner sees their record with the view-own key; anyone else only once it was submitted, with the
  queue key (staff) or the oversight key (admin), and only when the key's scope reaches the record's organization. A
  requester key (view-own and the keys of the requester-side rules, `RegisteredRecordType.RequesterPermissionKeys`)
  counts for the owner at any scope; every other key only when its scope reaches the record's organization.
- **Segregation of duties**: an owner never reviews or decides their own record. The engine takes only rules of the
  caller's side (requester for the owner with view-own) and never a staff rule for the owner, so the generic
  transitions endpoint answers 403 `WORKFLOW_FORBIDDEN` to a requester who also holds the staff keys.
- Data only through `IRecordStore`; no reference to another module (`records.no-module-reference`).

## Gotchas

- **Do not also register an `IAttachableRecordAccess` for a record type**: Records already serves one through the
  access source, and two accesses for one type fail every attachments request (`AttachableRecords`, a wiring fault).
- **Display names**: the timeline keeps the actor's name at the time; `ownerDisplayName` is read live through
  `IAccountAdministration` ("-" for a deleted account).
- **Drafts are private**: staff and oversight never see a record before its first submit, and `IRecordDirectory`
  queries leave drafts out.
- **The number is taken just before the commit**: a submit refused by validation or the interlock burns no number; one
  lost to a concurrent change leaves a gap (the allocator's contract), never a duplicate.
- **Notifications are best-effort**: a template missing for the asked channels throws in the Notifications module, is
  logged here and does not fail the transition. A cancel from a draft has no number, so its template must not use
  `{{recordNumber}}`.
- **No system actor yet**: every lifecycle call acts as the signed-in user; inside a system scope (a job) the caller is
  unknown and the call is refused. A job that must move records needs a system-actor entry point first.
- `CLAUDE.md`, `BannedSymbols.generated.txt`, `.editorconfig` and `CodeMetricsConfig.txt` in this folder are generated
  by the rules engine; never edit them by hand. Keep the `// scaffold:...` markers.
