# Assignments module

## Purpose

Module 9, assignment and queues, for ANY record type. The module owns:

- **The assignment row history** (`assignments.RecordAssignments`, entity `RecordAssignment`): one row per period a
  user held a record (`RecordType`, `RecordId`, `AssigneeUserId`, `AssignedByUserId`, `AssignedAt`, `ReleasedAt`,
  `ReleaseReason`, `ReleasedByUserId`). The current assignee is the one OPEN row (no `ReleasedAt`); the filtered
  unique index `UX_RecordAssignments_RecordId_Open` allows at most one per record, so the database decides two claims
  at the same moment. A record carries no assignment column. `AssignmentReleaseReason`: `Released = 1` (the assignee
  gave it back), `Reassigned = 2` (a supervisor moved it), `RecordClosed = 3` (it reached a terminal status).
- **The work queue of every record type** a module registers (`IRecordTypeRegistrationSource`) with a workflow
  (`IRecordWorkflowCatalogs`). A queue lists the records in an open status: neither the workflow's initial (draft)
  status nor a terminal one. Seeing a queue needs `assignments.work` AND the registration's `QueuePermissionKey`; the
  caller's organization scope for that key (`IOrganizationScope`: Global or OwnOrganization) narrows every read.
- **Claim, release, assign**, and the **auto-release** on a terminal status (`CloseAssignmentOnTerminal`, an
  `IRecordTransitionObserver` the Records module calls after each committed transition).
- **The kernel `IRecordAssignmentReader`** (`RecordAssignmentReader`): the current assignee and their display name, for
  the record detail views and the queue.

Record facts (number, status, owner, organization, submit time, terminal flag) come ONLY from the kernel
`IRecordDirectory` port (the Records module); this module never reads another module's tables.

Endpoints (JSON camelCase, problem+json errors, ids are PublicIds: records `records`, users `users`):

| Endpoint | Guard | Request | Response |
| --- | --- | --- | --- |
| `GET /api/queues/{recordType}?view=&status=&number=&submittedFrom=&submittedTo=&page=&pageSize=` | `assignments.work` + queue permission | `view`: `unassigned`, `mine` or `all` (required) | `{ items: [{ id, number, status, statusKey, submittedAt, ownerDisplayName, assignee: { userId, displayName, assignedAt } \| null, sla: { state, dueAt } \| null }], page, pageSize, total }` |
| `POST /api/queues/{recordType}/{id}/claim` | `assignments.work` + queue permission | | 204 |
| `POST /api/queues/{recordType}/{id}/release` | `assignments.work` + queue permission | | 204 |
| `POST /api/queues/{recordType}/{id}/assign` | `assignments.assign` + queue permission | `{ userId }` | 204 |

- List: newest submit first; `status` must be one of the queue's open statuses; `number` matches part of the number
  (at most 32 characters); `submittedFrom` <= first submit < `submittedTo`; `page` from 1, `pageSize` 20 by default,
  at most 100 (`RecordPageRequest`, the kernel paging). `sla` is filled by every registered `IRecordSlaReader`
  (Deadlines); none registered, it is null.
- Refusals (`AssignmentsErrorCodes`): 400 `VALIDATION_FAILED`, `ASSIGNMENTS_ASSIGNEE_NOT_ELIGIBLE`; 403
  `ASSIGNMENTS_QUEUE_FORBIDDEN`, `ASSIGNMENTS_NOT_ASSIGNEE`, `ASSIGNMENTS_ACTOR_UNKNOWN`; 404
  `ASSIGNMENTS_RECORD_TYPE_UNKNOWN`, `RECORDS_NOT_FOUND` (kernel code: missing, another record type, or outside the
  caller's scope); 409 `ASSIGNMENTS_ALREADY_CLAIMED`, `ASSIGNMENTS_NOT_ASSIGNABLE` (a draft or a closed record).

Layers are namespaces (ADR-004):

| Layer | Contents |
| --- | --- |
| `Domain` | `RecordAssignment` (+ `AssignmentStart`, `AssignmentEnd`), `AssignmentReleaseReason`, `QueueView`, `AssignmentsLimits` |
| `Application` | keys, catalog, error codes, wire id families (`QueueRecordIds`, `AssigneeUserIds`), ports (`IAssignmentStore`, `IAssigneeEligibility`), `CurrentStaff`; `Queues/`: `RecordQueues` + `RecordQueueContext` (the queue gate), `GetWorkQueuePage`, `WorkQueueViews`, `UnassignedQueuePages`, `WorkQueueEntries`, `RecordAssignmentReader`, `UserDisplayNames`, the request validator; `Commands/`: `ClaimRecord`, `ReleaseRecord`, `AssignRecord` (+ validator), `AssignmentRows` (the one row writer), `CloseAssignmentOnTerminal`, `AssignmentsLog` |
| `Infrastructure` | `AssignmentsDbContext` (schema `assignments`), `AssignmentStore`, the reshaped `InitialCreate` + `AssignmentsMigrationModel`, `UnresolvedAssigneeEligibility`, the registration |
| `Api` | `QueuesController`, `WorkQueueQuery`, response records, `AssignmentsRoutes` |

## Use

- **Show the assignee on a detail view:** resolve `IEnumerable<IRecordAssignmentReader>` (the port is optional) and call
  `CurrentAsync(recordIds)`; an unassigned record is absent from the answer.
- **Make a record type queue-able:** register its `IRecordTypeRegistrationSource` (the `QueuePermissionKey` gates the
  queue) and its workflow (`services.AddRecordWorkflow<TDefinition>()`). Nothing in this module changes.
- **Frontend:** `QueuesClient` in `shared/records` calls these endpoints; the record ids it gets open
  `api/records/{recordType}/{id}` unchanged.
- **Grant a worker:** `assignments.work` plus the record type's queue permission (Global, or OwnOrganization to see
  only their organization's records). A supervisor also needs `assignments.assign`; listing still needs
  `assignments.work`.

## Extend

- **A real assignee check.** `IAssigneeEligibility` ("how far does ANOTHER user hold this key") has no kernel
  implementation yet: the kernel only answers for the current user (`IOrganizationScope`). The registered default
  `UnresolvedAssigneeEligibility` refuses every user (400 `ASSIGNMENTS_ASSIGNEE_NOT_ELIGIBLE`, warning event 9005), so
  assigning fails closed until the Access module offers one; register an adapter BEFORE the module (the module adds the
  default with `TryAdd`). Claims and releases do not need it.
- **A new filter or view.** Add it to `WorkQueueRequest` and its validator, map it in `WorkQueueViews` or the
  `RecordQuery` built by `GetWorkQueuePage`, and add the query parameter to `WorkQueueQuery`.
- **An `ExcludedIds` filter on the kernel `RecordQuery`** would let `UnassignedQueuePages` page in one directory call
  instead of scanning (see Gotchas); change only that class.
- **A schema change.** Freeze `AssignmentsMigrationModel` as `InitialCreateModel`, point `InitialCreate` at it, write
  the new migration in the same reshaped style and update `AssignmentsMigrationModel` until `AssignmentsModelTests`
  reports no pending model change.

## Rules

- **No module reference.** Record facts only through `IRecordDirectory`, names through `IAccountAdministration`, the
  caller through `ICurrentUserContext` + `IUserDirectory`, scope through `IOrganizationScope` (rule
  `assignments.no-module-reference`, `mustNotDependOn`). Record and user ids are plain `bigint` columns, no
  cross-schema foreign key.
- **Rows, never columns.** A claim or an assignment opens a row; a release, a reassignment or a terminal status closes
  it. Rows are never deleted or reopened. `AssignmentRows` is the only writer of rows a person causes; the observer
  closes rows at the transition's own time.
- **One queue gate.** Every use case opens the queue through `RecordQueues` (queue permission, then scope). A record
  outside the scope answers 404 `RECORDS_NOT_FOUND`, the same as a missing one.
- **Claim is exclusive.** A held record is 409 `ASSIGNMENTS_ALREADY_CLAIMED`, also for its own holder; release is
  only by the holder (403 `ASSIGNMENTS_NOT_ASSIGNEE`); moving someone's record is an assignment (`assignments.assign`).
- **Thin controllers.** One use case per action, `[HasPermission]` with an `AssignmentsPermissionKeys` constant,
  `OutcomeResults` for the mapping.

## Gotchas

- **The unassigned view scans.** The kernel `RecordQuery` can include ids, not exclude them, so the unassigned page is
  the directory's matching records minus the open-assignment ids: the total is "matching" minus "matching and
  assigned", and the page is filled from directory pages of 100, skipping assigned ids, for at most
  `AssignmentsLimits.UnassignedScanMaxPages` (50) pages. A page beyond 5,000 matching records comes back short.
- **The host needs an `IRecordDirectory`.** The use cases resolve it; a host without the Records module (or another
  implementation) fails when a queue endpoint is called (and at start in Development, where the container validates).
- **The terminal close trusts the observer call.** It runs after the Records module's commit, in the same request;
  it is idempotent (no open row: nothing happens) and records the transition's actor and time.
- **Ids share labels.** Record ids use the label `records` (the generic record endpoints' label), user ids `users`
  (Identity, Users and Access use it too), so the tokens are interchangeable across those endpoints.
- **The display name of an unknown user is empty** (`UserDisplayNames.Unknown`).
- **The migration is reshaped by hand**, like every module's (no `*.Designer.cs`, ids and names in
  `AssignmentsMigrationIds` / `AssignmentsTableNames`); `AssignmentsModelTests` proves the model matches it.
