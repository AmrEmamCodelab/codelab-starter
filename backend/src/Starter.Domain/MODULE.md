# Starter.Domain

## Purpose
The kernel Domain project (ADR-004): the types every module's domain layer shares and nothing else.

- **Domain exceptions** for broken invariants where no result can be returned (ERROR-HANDLING row 1):
  `ResourceNotFoundException` (404), `AccessDeniedException` (403) and `BusinessRuleException` (422 with a module
  error code). The host maps them through `StarterFailures` (Starter.Infrastructure).
- **Kernel value types:** `AppLanguage` (Arabic = 1, English = 2; Arabic is the default).
- **User value types** (`Users/`, shared by Identity, Users and Access): `UserKind` (Internal = 1, External = 2;
  selects the virtual roles a user falls under) and `AccountBinding` (Password 1, ActiveDirectory 2, Sso 3: the
  one sign-in mode an account accepts; only Password accounts ever hold a password, set through an activation link).

- **Lookup types** (`Lookups/`, owned with module 4): `LookupEntity` (the base of every lookup value: a fixed int `Id` from a `<Name>Ids` constants class, `Code`, `NameAr`, `NameEn`, `SortOrder`, `IsActive`, `SetActive`), `LookupTypes` (the seeded type keys: `contact-topics`, `service-request-categories`), `LookupLimits` (column sizes, `IsKebab`) and one `<Name>Ids` class per seeded type (`ContactTopicIds`; `ServiceRequestCategoryIds` General 1, Certificate 2, DataUpdate 3, Complaint 4, seeded for the sample by the Lookups migration `AddServiceRequestCategoriesLookup`). `tools/scaffold/new-lookup.mjs` writes `LookupTypes` entries and the `<Name>Ids` classes.
- **Attachment constants** (`Attachments/`, owned with module 13): `AttachmentTypeIds` (fixed int ids the type seed migrations write; `DemoDocument` = 1, `ServiceRequestDocument` = 2, `ServiceRequestSupporting` = 3), `AttachmentTypeKeys` (the matching kebab keys; `demo-document`, `service-request-document`, `service-request-supporting`) and `AttachableRecordTypes` (record types that carry attachments, `Demo` = `attachments-demo`, `ServiceRequest` = `service-request`, and `RecordIdDomainLabel` = `attachable-records`, the PublicId label of record ids in the attachment routes). The sample's rows and constants stay when the sample is removed (shipped migrations are never deleted).
- **Records** (`Records/`, the record kernel of modules 7 to 11): `RecordRef` (internal record id, at least `MinId` = 1, plus a record type key; the identity every Records port takes) and `RecordTypeKeyFormat` (lowercase kebab, at most 64; `IsValid`, `EnsureValid`).
- **Workflow** (`Workflow/`, the data of a record type's status flow, read by the engine in Starter.Application):
  `TransitionCatalog` (record type, `WorkflowStatusDefinition` list, `TransitionRule` list; `RulesFrom`, `FindStatus`,
  `IsTerminal`), `WorkflowStatusDefinition` (status int, kebab key, initial, terminal, `StatusWording` with the
  requester, staff and admin i18n keys), `TransitionRule` (action key, from, to, permission key, `TransitionSide`,
  `ReasonRequirement`, reason field, label key, `NotificationDecision`, `RequiresOwnership`; one rule per from-status),
  `TransitionCatalogValidator` (pure structural check, returns `CatalogViolation`s of `CatalogViolationKind`
  UndefinedState 1 to RecordTypeConflict 11), and the enums `TransitionSide` (Requester = 1, Staff = 2, System = 3),
  `WorkflowAudience` (Requester = 1, Staff = 2, Admin = 3), `ReasonRequirement` (None = 1, Optional = 2,
  Required = 3), plus `WorkflowLimits.MaxReasonLength` (1000).
- **Auditing** (`Auditing/`): `[PersonalData(PersonalDataKind)]` (NationalId = 1, Email = 2, Phone = 3, Name = 4, FreeText = 5; the audit trail stores only the masked form), `[NotAudited]` (class: never audited; property: never recorded), `AuditAction` (Added = 1, Modified = 2, Deleted = 3) and `RecordHistoryEntry` (+ `RecordHistoryFacts`, `RecordHistoryLimits`): the base of every record history table (Id, RecordId, Action, FromStatus?, ToStatus?, Reason?, ActorUserId?, ActorName, At), itself `[NotAudited]`.

## Use
```csharp
throw new BusinessRuleException(RecordsErrorCodes.AlreadyClosed, "Record is closed.");  // 422, code on the wire
throw new ResourceNotFoundException(RecordsResourceNames.Record);                        // 404
```
An expected "no" that a use case can return is a `Refusal` (Starter.Application), never one of these exceptions.

## Extend
- A new kernel value type belongs here only when two or more modules need it; otherwise it stays in the module's
  own `Domain` folder.
- A new exception kind needs a classifier in `Starter.Infrastructure/Errors/StarterFailures.cs` and a row in
  `docs/standards/ERROR-HANDLING.md`.

- A record type's workflow is data: write a `TransitionCatalog` in the owning module and register it
  (`AddRecordWorkflow<TDefinition>()`, Starter.Infrastructure); recipe `docs/recipes/add-workflow-type.md`. A new
  `CatalogViolationKind` needs a check in `TransitionCatalogValidator` and a test.
- Personal data: mark the entity property `[PersonalData(PersonalDataKind.X)]`; a secret `[NotAudited]`. A record history: derive from `RecordHistoryEntry` and map it with `HasRecordHistory<TRecord, THistory>()` (Starter.Infrastructure.Auditing).

## Rules
- Lookup ids are compared only through their <Name>Ids constants, whose values mirror the Lookups seed migrations; never renumber one.
- No reference to ASP.NET Core, EF Core, `Microsoft.Extensions.*` or a Codelab package (`starterdomain.no-frameworks`).
- Enums are integers with an explicit value on every member; labels come from i18n, never from member names.
- Exception messages are logged, never sent; keep them free of personal data and of identifiers a user typed.
- Unit tests for any logic added here live in `backend/tests/Starter.Application.Tests` (same change).

## Gotchas
- `TransitionSide`, `WorkflowAudience` and `ReasonRequirement` values go on the wire (allowed actions, timeline) and
  into record history; never renumber them. A `Staff` rule is never taken by the record's owner (the engine enforces
  segregation of duties), so a catalog cannot rely on a requester approving their own record.
- `UserKind` and `AccountBinding` values are stored by Identity and sent on the wire; they are a contract. Add a member
  with the next value, never renumber, and add its i18n label keys.
- `BusinessRuleException.Code` is the whole client contract: the client renders `errors.<CODE>`. Use a constant of
  the module's `<Name>ErrorCodes` class, never a literal.
- `CLAUDE.md`, `.editorconfig`, `BannedSymbols.generated.txt` and `CodeMetricsConfig.txt` here are generated by
  `node tools/rules-engine/compile.mjs` from `module.rules.yaml`; never edit them by hand.
