# Audit module

## Purpose

Module 5, audit and record history. Two trails answer two questions, and this module is the window onto the first:

- **The data-level audit trail** (`audit.AuditEntries`): "which columns of which row changed, from what, to what, by
  whom, when". The kernel writes it (`Starter.Infrastructure/Auditing`, `AuditSaveChangesInterceptor` on every module
  DbContext); this module only reads it, through the kernel `IAuditTrailQueries` port, for the admin viewer.
- **Record history** (a `RecordHistoryEntry` table per record type): the business timeline (submitted, approved,
  returned) a requester and staff read. The base type and its mapping are kernel code; record modules (phase 7) own
  their history tables.

The module has no schema and no DbContext of its own, so removing it removes only the viewer; the trail keeps being
written.

## Use

- Endpoint: `GET api/admin/audit?entityType=&entityId=&before=&take=` (`AuditPermissionKeys.View`, `audit.view`) answers
  `{ items: [{ id, entityType, entityId, action, actor, at, changes: [{ property, oldValue, newValue }] }], hasMore }`,
  newest first.
  - `id` is a `PublicId<AuditEntryIds>`; pass the last item's `id` as `before` to read the next page.
  - `entityId` and every key or foreign key value inside `changes` are `PublicId<AuditedEntityIds>` tokens (one family
    for every entity type; the type travels beside it). A composite key gives `entityId: null`; its parts are in
    `changes`.
  - `action` is the `AuditAction` integer (Added = 1, Modified = 2, Deleted = 3); `actor` is the subject id or
    `SYSTEM` (jobs, startup tasks, anonymous calls); `at` is ISO 8601 UTC.
  - Values are already masked: personal data was masked when the row was written, secrets were never recorded.
  - `take` is 1 to `AuditPaging.MaxTake` (100), default `AuditPaging.DefaultTake` (20). `entityId` needs
    `entityType`. A bad filter is 400 problem+json `AUDIT_INVALID_FILTER` with field errors.
- Error codes: `AuditErrorCodes`. Registration: `AuditModule` calls `AddAuditModule` (`Infrastructure/`).

## Extend

- **Mark personal data where it is declared**: `[PersonalData(PersonalDataKind.Email)]` (or `NationalId`, `Phone`,
  `Name`, `FreeText`) on the entity property; `[NotAudited]` on a secret property or on an entity that is a trail of
  its own. The name conventions (`AuditNamingConventions`) are only a safety net.
- **Add a record history table** (phase 7): `public sealed class ServiceRequestHistory : RecordHistoryEntry` and, in
  its configuration, `builder.ToTable("ServiceRequestHistory");` then
  `builder.HasRecordHistory<ServiceRequest, ServiceRequestHistory>();`. Name it `<Record>History`, so
  `deploy/sql/audit/Grant-AuditAppendOnly.sql` denies UPDATE and DELETE on it without an edit.
- **A filter or a projection** of the viewer: extend `AuditTrailListQuery`, its validator and `IAuditTrailQueries`
  together, with tests.

## Rules

- Read-only: never write, update or delete an audit row (the DBA script denies UPDATE and DELETE to the app login).
- No raw ids on the wire: every internal id becomes a PublicId (`AuditViews`).
- Never reference another module; read through `IAuditTrailQueries`. See `module.rules.yaml`.
- Tests: `backend/tests/Modules/Starter.Modules.Audit.Tests` (viewer) and `backend/tests/Starter.Api.Tests/Auditing`
  (interceptor, masking, record history mapping).

## Gotchas

- **`entityId` is not the module's own PublicId.** A user id from the Users API (`users` family) is not accepted
  here; filter by `entityType` and use the `entityId` tokens this viewer returns.
- **Entity type names are EF short names** (`Organization`, `Role`): keep entity class names unique across
  modules, or their trails share a name.
- **Masking happens at write time.** Adding `[PersonalData]` to a property does not mask rows already written; the
  DBA purges or re-masks old rows if a leak is found.
- The real-SQL proof (FK Restrict refuses a parent delete, the PII canary over the trail) lives in
  `Starter.Integration.Tests`.
