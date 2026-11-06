# Recipe: add an attachment type

Adds a typed attachment slot (an entry of the attachment type catalog, `ServiceAttachment`) to module 13, Attachments,
and, when needed, makes a new record type able to carry attachments. Types are seeded **only by migrations**, like
lookups; at run time nothing edits them. Module docs:
[backend/src/Modules/Attachments/MODULE.md](../../backend/src/Modules/Attachments/MODULE.md).

## 1. Decide what you need

| You want... | Do |
| --- | --- |
| Another kind of file on a record type that already takes attachments | steps 2, 3, 5 and 6 |
| Attachments on a new record type (a new module's records) | steps 2 to 6, including step 4 |
| A file that is only shown, never uploaded by users | not an attachment type: ship it as portal content or a static asset |

## 2. Choose the names and rules

| Input | Example | Becomes |
| --- | --- | --- |
| Type id (next free int, never reused) | `2` | `Starter.Domain.Attachments.AttachmentTypeIds.MedicalReport = 2` |
| Type key (kebab, at most 64, unique) | `medical-report` | `AttachmentTypeKeys.MedicalReport = "medical-report"`; the `typeKey` the client sends |
| Record type (kebab, at most 64) | `service-request` | an `AttachableRecordTypes` constant, the `{recordType}` route segment |
| Arabic and English names (at most 200) | `تقرير طبي` / `Medical report` | shown by the upload widget |
| Required | `true` | the record cannot submit until it has one (the interlock, step 5) |
| Max files per record | `3` (1 to `AttachmentsLimits.MaxFilesCeiling` = 50) | the ticket and the link refuse the next one (`ATTACHMENTS_MAX_FILES_REACHED`, 409) |
| Allowed extensions | `.pdf`, `.jpg` | must be a subset of `Codelab:Security:Uploads:AllowedExtensions` and outside `BlockedExtensions` |

The extensions must stay inside the ONE upload allowlist the API and the attachment service both bind
(`Codelab:Security:Uploads`). `AttachmentTypeCatalog.FindViolations` checks the catalog against it at start, so a
type can never promise a file the service refuses. To accept a new extension anywhere, first add it to the allowlist
of both deployables (and give it a magic-byte signature and a MIME type in Codelab.Security), then to the type.

## 3. Seed the type by a migration

1. Add the constants: the id to `backend/src/Starter.Domain/Attachments/AttachmentTypeIds.cs` and the key to
   `AttachmentTypeKeys.cs` (and, for a new record type, the record type to `AttachableRecordTypes.cs`).
2. Add a migration to the Attachments context (never edit one that shipped):
   ```powershell
   & E:\codelab-tools\dotnet10\dotnet.exe ef migrations add AddMedicalReportAttachmentType --project backend\src\Modules\Attachments --startup-project backend\src\Starter.Api --context AttachmentsDbContext --output-dir Infrastructure\Persistence\Migrations

   Then reshape it like `InitialCreate`: name the ids and types through `AttachmentsMigrationIds` / `AttachmentsSqlTypes`, keep no generated marker or Designer file, and keep `BuildTargetModel` => `AttachmentsMigrationModel.Build`.
   ```
   An empty migration is generated; write its `Up` with `migrationBuilder.InsertData` on the
   `ServiceAttachments` table of schema `attachments` (the columns of the entity's EF configuration), using the
   constants above, never bare literals. Write the allowed extensions in their stored form, which
   `AttachmentExtensions.ToStorage(["pdf", ".JPG"])` returns (`.pdf;.jpg`); `Down` deletes the row by id.
3. Never `HasData` in the model (rule `attachments.no-has-data`).

To change a type later, write a new migration with `UpdateData` (names, required flag, max files, extensions); keep
the id and the key. To retire one, set `IsActive` to false in a migration: no new uploads, not required any more,
existing attachments stay readable.

## 4. Register a new record type

Only when the record type is new. In the module that **owns** the record (never in Attachments):

1. Implement `Starter.Application.Attachments.IAttachableRecordAccess` for the record type: `CanViewAsync` answers
   "may the current user see this record" and `CanChangeAsync` "may they add or remove files now" (for example only
   while it is a draft). Both answer false for a record that does not exist. Use the same rule as the record's own
   detail endpoint, so the attachments are never more visible than the record.
2. Register it in that module's `Add<Name>Module`:
   `services.AddScoped<IAttachableRecordAccess, ServiceRequestAttachmentAccess>();` (one per record type; a second
   registration for the same record type fails the first request with a wiring error).
3. Send the client the record id under the label `AttachableRecordTypes.RecordIdDomainLabel`
   (`attachable-records`): declare your own `IIdDomain` returning that label and expose the record's id as a
   `PublicId` of it, so the generic routes `/api/records/{recordType}/{recordId}/attachments` can read it.
4. On the screen, place `<app-record-attachments [recordType]="..." [recordId]="..." [readonly]="...">`.

## 5. Enforce required types (the interlock)

In the use case that moves the record on (for example submit), ask the kernel port and refuse while something is
missing:

```csharp
var missing = await requiredAttachments.FindMissingTypeKeysAsync(AttachableRecordTypes.ServiceRequest, request.Id, cancellationToken);
if (missing.Count > 0)
{
    return Refusal.Conflict(AttachmentErrorCodes.RequiredMissing);
}
```

The record attachment list returns the same keys (`missingRequiredTypeKeys`), computed by the same rule
(`RequiredAttachmentRule`), so the screen and the submit never disagree.

## 6. Tests and docs

- A unit test in the owning module for its `IAttachableRecordAccess` (view and change, allowed and denied, a missing
  record), and one for the submit refusal with `ATTACHMENTS_REQUIRED_MISSING`.
- The Attachments tests already cover the link rules for any type; add a domain test only when the new type relies
  on a rule no test covers yet.
- Update the seeded-types table in the Attachments `MODULE.md` and the owning module's `MODULE.md` (Use: which types
  its records carry).

## 7. Verify

```powershell
$dotnet = "E:\codelab-tools\dotnet10\dotnet.exe"
& $dotnet test backend/tests/Modules/Starter.Modules.Attachments.Tests
& $dotnet test backend/tests/Starter.Architecture.Tests
```
