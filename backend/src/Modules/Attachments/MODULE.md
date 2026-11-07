# Attachments module

## Purpose

Attachments (module 13): typed files on any record. The bytes live on the separate attachment service
(`services/attachment-service`, `Codelab.Attachments.*`); this module keeps the catalog of attachment types and the
receipt-verified metadata of every file linked to a record, decides who may see or change a record's files, and
relays downloads and deletions to the service. It owns, in schema `attachments`:

- **`ServiceAttachment`**, the attachment type catalog: a fixed int `Id` (`Starter.Domain.Attachments.AttachmentTypeIds`),
  a unique kebab `Key` (`AttachmentTypeKeys`), `NameAr`/`NameEn`, the `RecordType` it belongs to
  (`AttachableRecordTypes`), `IsRequired`, `MaxFiles` (1 to 50), the allowed extensions (stored as `.pdf;.png`,
  `AttachmentExtensions`) and `IsActive`. Never edited by users: this module's own types are seeded by its
  migrations; a record type's types are **declared by the module that owns the record type** through the kernel seam
  `IAttachmentTypeSeedSource` and written here at start (see "Declared types" below).
- **`RecordAttachment`**, the typed link: `RecordType` + `RecordId` (the record's internal id; no foreign key across
  modules), `ServiceAttachmentId`, the service `FileId`, `FileName`, `SizeBytes`, `Sha256Hex`, `ContentType`,
  `UploadedBySubject`, `UploadedAt`, and `RemovedAt`/`RemovedBySubject` for the soft removal.

The flow: the browser asks this API for an **upload ticket** (sealed, single-use, scoped to record + type + user),
uploads straight to the service with only that ticket, gets back a signed **receipt** per file, and asks this API to
**link** the receipt to the record. Downloads and deletions go API to service over the ES256 service assertion; the
browser never reads from the service.

Types this module seeds by migration (`InitialCreate`, `AttachmentTypeSeed`):

| Id | Key | Record type | Required | Max files | Extensions |
| --- | --- | --- | --- | --- | --- |
| `AttachmentTypeIds.DemoDocument` (1) | `demo-document` | `attachments-demo` | yes | 5 | `.pdf`, `.png`, `.jpg`, `.jpeg` |

Types other modules declare in the starter (present only while that module is in the host):

| Id | Key | Record type | Required | Max files | Extensions | Declared by |
| --- | --- | --- | --- | --- | --- | --- |
| `AttachmentTypeIds.ServiceRequestDocument` (2) | `service-request-document` | `service-request` | yes | 3 | `.pdf`, `.jpg`, `.png` | ServiceRequests (`ServiceRequestAttachmentTypes`) |
| `AttachmentTypeIds.ServiceRequestSupporting` (3) | `service-request-supporting` | `service-request` | no | 5 | `.pdf`, `.jpg`, `.png` | ServiceRequests (`ServiceRequestAttachmentTypes`) |

**Declared types (the seed seam).** A module that owns a record type declares its attachment types by implementing
`Starter.Application.Attachments.IAttachmentTypeSeedSource` (`Types`: `AttachmentTypeDefinition` records: `Id`, `Key`,
`RecordType`, `NameAr`, `NameEn`, `IsRequired`, `MaxFiles`, `AllowedExtensions`; ids and keys from the
`Starter.Domain.Attachments` constants) and registering it
(`services.TryAddEnumerable(ServiceDescriptor.Singleton<IAttachmentTypeSeedSource, MyTypes>())`). This module never
names the declaring module and writes no SQL for it:

1. `AttachmentTypeDeclarations` (Application, singleton) collects every source and validates once: each definition well
   formed (`ServiceAttachment.Seeded`), ids and keys unique across sources, none taking an id or key of the types this
   module seeds by migration (`AttachmentTypeSeed.Rows`). A violation stops the host.
2. `AttachmentTypeSeedStartupTask` (Infrastructure, `StartupPhase.ReferenceData`, registered before
   `AttachmentCatalogStartupCheck`) checks the declared extensions against the upload allowlist first, then runs
   `AttachmentTypeSynchronizer`: one rule, `AttachmentTypeSeeding.Plan` (Domain), matched **by key**: a missing type is
   inserted, a type whose names or rules differ is refreshed (`ServiceAttachment.Refresh`), an equal one is left
   alone. The id, key and record type of a stored type never change (a mismatch is a conflict that stops the host) and
   `IsActive` is never touched (a retired type stays retired). Idempotent; a node that loses an insert race re-plans
   once against what the winner wrote. Log event 13003 reports added, refreshed and unchanged.

## Use

Permissions (`AttachmentsPermissionKeys`, declared by `AttachmentsPermissionCatalogSource`): `attachments.view`,
`attachments.upload`, `attachments.remove`. The key opens the endpoint; the record's own `IAttachableRecordAccess`
still decides per record. Errors are problem+json with a `code` and a `referenceCode`.

| Endpoint | Access | Use case | Answer |
| --- | --- | --- | --- |
| `GET /api/attachments/types?recordType={key}` | `attachments.view` | `ListAttachmentTypes` | `[{key, nameAr, nameEn, isRequired, maxFiles, allowedExtensions[]}]`, active types by key |
| `GET /api/records/{recordType}/{recordId}/attachments` | `attachments.view` | `ListRecordAttachments` | `{items: [{id, typeKey, fileName, sizeBytes, contentType, uploadedAt}], missingRequiredTypeKeys: []}` |
| `POST /api/records/{recordType}/{recordId}/attachments/upload-ticket` `{typeKey}` | `attachments.upload` | `IssueUploadTicket` | `{ticket, ticketHeader, uploadUrl, expiresAt, allowedExtensions[], maxFileCount}` (`maxFileCount` = slots left) |
| `POST /api/records/{recordType}/{recordId}/attachments` `{typeKey, receipt}` | `attachments.upload` | `LinkUploadedFile` | 201 with the item |
| `GET /api/attachments/{id}/content` | `attachments.view`, unsigned response | `DownloadAttachment` | the bytes, name and type from this database |
| `DELETE /api/attachments/{id}` | `attachments.remove` | `RemoveAttachment` | 204 |
| `GET /api/attachments/demo-record` | `attachments.view`, Development only | (constants `DemoRecord`) | `{recordType: "attachments-demo", recordId}` |

Ids on the wire: attachment ids are `PublicId<AttachmentIds>` (label `attachments`); record ids in the routes are
`PublicId<AttachableRecordIds>` (label `attachable-records`, `AttachableRecordTypes.RecordIdDomainLabel`), one family
for every record type.

**Ports between modules** (kernel, `Starter.Application.Attachments`):

| Port | Implemented by | Used by |
| --- | --- | --- |
| `IAttachableRecordAccess` (`RecordType`, `CanViewAsync`, `CanChangeAsync`) | the module that owns each record type, or the Records module for every registered record type through `IAttachableRecordAccessSource` (Attachments itself only for the Development `attachments-demo` type, `DemoRecordAccess`) | this module, through `AttachableRecords` (single registrations plus every source's accesses; two for one record type is a wiring fault), on every use case |
| `IRequiredAttachments` (`FindMissingTypeKeysAsync`) | this module (`RequiredAttachmentsCheck`) | modules that move a record on, for example the Records submit (P7), which refuses with `AttachmentErrorCodes.RequiredMissing` |
| `IAttachmentTypeSeedSource` (`Types`: `AttachmentTypeDefinition`) | the module that owns a record type (ServiceRequests: `ServiceRequestAttachmentTypes`) | this module, through `AttachmentTypeDeclarations` and the start-up `AttachmentTypeSynchronizer` |

**Module ports** (Application, implemented in Infrastructure): `IAttachmentStore` (EF repository and unit of work),
`IAttachmentReceiptVerifier` (offline HMAC check of the `r1.` receipt under the shared receipt ring),
`IAttachmentStorageClient` (`OpenReadAsync`, `DeleteAsync` over the S2S assertion), `IAttachmentFileCleanup` (outbox
message `attachments.delete-file`, payload `DeleteAttachmentFilePayload`), `IUploadTicketIssuer` (sealed ticket,
purpose `attachment-upload`, `UploadTickets.Purpose`).

**The interlock.** `RequiredAttachmentRule.MissingTypeKeys` is the one rule: active, required types of the record type
with no live attachment. The list endpoint returns it as `missingRequiredTypeKeys`; `IRequiredAttachments` returns the
same keys to the module that submits, which calls it through `RequiredAttachmentsGate.RefuseWhileMissingAsync`
(Starter.Application: 409 `ATTACHMENTS_REQUIRED_MISSING` or null). No record has a submit yet: the first one is the
Records submit (P7). Until then the gate is proven on real SQL by
`AttachmentsHttpTests.The_interlock_refuses_the_demo_record_until_its_required_file_is_linked`. A retired type is never
required.

**Configuration keys** (bound by Infrastructure; no key material in committed appsettings):

| Key | Meaning |
| --- | --- |
| `Attachments:ServiceBaseUrl` | the attachment service root (Development `http://localhost:5185`); the ticket's `uploadUrl` is `<root>/api/uploads` |
| `Attachments:TicketLifetimeSeconds` | upload ticket lifetime, default 300 |
| `Codelab:Security:UploadTickets` | the upload-ticket key ring (`Enabled`, `ActiveKeyId`, `Keys[]`, `MaxLifetimeSeconds` default 3600), bound by `UploadTicketRingOptions` and validated on start; the service holds the same ring as its `Codelab:Security:Tickets`. Never the API's own `Codelab:Security:Tickets` ring (SSO, activation), which stays in the API |
| `Codelab:Security:Uploads` | the ONE upload allowlist (`AllowedExtensions`, `BlockedExtensions`, caps) and the receipt key ring, the same as the service |
| `Codelab:Security:ServiceAssertions:Client` | `ServiceName` `starter-api`, `Audience` `starter-attachments`, `KeyId`, `PrivateKeyPem` (gitignored `appsettings.Development.local.json`, written by `tools/devseed/New-LocalSecrets.ps1`) |

**Infrastructure** (`Infrastructure/`): `AttachmentsDbContext` (schema `attachments`: `ServiceAttachments` with fixed ids, a unique key and the stored `AllowedExtensions` column; `RecordAttachments` with a Restrict FK to the type, a unique `FileId` and the (RecordType, RecordId) index), the reshaped `InitialCreate` + `AttachmentsMigrationModel` (target model and snapshot) seeding the demo type (`AttachmentTypeSeed`), the empty `AddServiceRequestAttachmentTypes` (kept for the history, see Gotchas), `AttachmentTypeSynchronizer` + `AttachmentTypeSeedStartupTask` (the declared types, above), `EfAttachmentStore`, `ReceiptVerifier` (UploadReceiptSigner over the `Codelab:Security:Uploads` ring; MIME from `UploadContentTypes`), `UploadTicketIssuer` (over `UploadTicketRing`, the module's own SealedTicketService on `Codelab:Security:UploadTickets`, registered as that concrete type only, never as `ISealedTicketService`; header `X-{Prefix}-Upload-Ticket`, URL `<root>/api/uploads`), `HttpAttachmentStorageClient` (typed HttpClient + `AddCodelabServiceAssertion`; GET/DELETE `api/files/{fileId}`; 404 = gone, anything else = `AttachmentStorageUnavailableException`, 30 s timeout), `OutboxAttachmentFileCleanup` + `DeleteAttachmentFileHandler` (outbox `attachments.delete-file`), `AttachmentsFailures` (503 classifier ahead of the generic 502), `AttachmentCatalogStartupCheck` (allowlist check at start; warns when receipts are off), `AttachmentsOptions` and `UploadTicketRingOptions` (validated on start; the upload ring is refused when it shares key material with `Codelab:Security:Tickets`).

## Extend

- **New attachment type, or attachments on a new record type:** the recipe
  [docs/recipes/add-attachment-type.md](../../../../docs/recipes/add-attachment-type.md): the owning module declares the
  type (`IAttachmentTypeSeedSource`, ids and keys in `Starter.Domain.Attachments`), with its required flag and
  extensions inside the upload allowlist, its `IAttachableRecordAccess` and the interlock call. Only a type that
  belongs to this module itself (like the demo) is a migration here.
- **New endpoint or permission:** `new-endpoint` / `new-permission` with `--module Attachments`.
- **A new rule on links** (for example a per-type size cap): put it in `ServiceAttachment` (Domain) and call it from
  `LinkUploadedFile.LinkNewAsync`, with tests in `Domain/` and `Application/`.

## Rules

- **The record decides** (`attachments.record-access-gate`): every use case passes `AttachableRecords.RefuseAsync`
  (View for list and download, Change for ticket, link and remove). A missing record and a denied one answer the
  same 404 `ATTACHMENTS_NOT_FOUND`, so existence never leaks; an unregistered record type answers 404
  `ATTACHMENTS_RECORD_TYPE_UNKNOWN`.
- **Only verified metadata is stored** (`attachments.receipt-only-metadata`): the receipt must verify and its scope
  must be the scope `UploadScopes.For(record, type, user)` issued (`{recordType}|{recordPublicId}|{typeKey}|{userSubject}`;
  the record part must authenticate as that record's PublicId); the MIME type comes from the extension
  (`UploadContentTypes`), never from the client.
- **No internal id in the scope.** The receipt is signed, not encrypted, and reaches the browser: the record appears
  only as its PublicId (rule 8), the subject is the uploader's own (already in their access token). Never add an
  internal number or anyone else's data to the scope (`UploadScopesTests` checks the decimal id never appears).
- **One allowlist:** the API and the service bind the same `Codelab:Security:Uploads`; a type's extensions must be a
  subset (`AttachmentTypeCatalog.FindViolations`, run at start by Infrastructure).
- **Types are never edited by users** (`attachments.types-seeded-by-migrations`, `attachments.no-has-data`): this
  module's own types by migration, a record type's types declared by its owning module through
  `IAttachmentTypeSeedSource` and synchronized at start; ids and keys only through `AttachmentTypeIds` /
  `AttachmentTypeKeys`, never reused. No migration here names another module's record type any more.
- **No bytes in the database** (`attachments.no-file-bytes-in-database`).
- **Removal is soft, cleanup after the save** (`attachments.cleanup-after-save`).
- Never reference another module (`attachments.no-module-reference`); every use case, validator and domain rule has
  unit tests in `backend/tests/Modules/Starter.Modules.Attachments.Tests`.

## Gotchas

- **`20261106150100_AddServiceRequestAttachmentTypes` is an empty migration on purpose.** It used to insert the
  sample's two types; they moved to the ServiceRequests module (declared through `IAttachmentTypeSeedSource`) so that
  removing the sample seeds nothing. The id stays in the code so every database has the same history: a database that
  applied the old body keeps its rows 2 and 3 and the synchronizer finds them by key (unchanged, never a second
  insert); a new database records the empty migration and the synchronizer inserts the rows. `Down` is empty as well
  (the rows belong to the synchronizer). Never delete the class, reuse the id or put a body back; the DBA script of
  this context only carries the empty history row for it.
- **Declared types are written by the API at start, not by the DBA script.** Production runs the start-up tasks even
  with migrations off, so the API's database login needs `INSERT` and `UPDATE` on `attachments.ServiceAttachments` (it
  already writes `RecordAttachments`). A login that cannot write there fails the start with the SQL error.
- **Removing a declaring module leaves the rows it already wrote** (existing databases): they stay active but no
  record of that type can be reached (no access is registered). Retire them with `IsActive` = false (a migration or a
  DBA update) if the catalog should not list them. Their ids stay reserved in `AttachmentTypeIds`.
- **A declaration may only change names and rules.** Changing a declared type's id, key or record type stops the host
  (the stored row keeps its identity, and links point at the id); give a new type a new id and key and retire the old
  one instead.

- **Linking is idempotent per file id.** The same receipt linked twice answers the existing attachment; a file id
  that is linked to another record or type, or was removed, is refused as `ATTACHMENTS_RECEIPT_INVALID`.
- **Max files is checked twice** (ticket and link) but not under a lock: two links racing for the last slot can both
  pass. The unique index on `FileId` stops duplicates, not the race; the cap is a business limit, not a security one.
- **Uploads that are never linked stay on the service** (the browser closed, the link was refused). There is no
  retention sweep yet; removed files are deleted through the outbox.
- **Receipts are mandatory.** With `Codelab:Security:Uploads:ReceiptsEnabled` false nothing verifies, so nothing links.
- **Download names come from this database**, never from the service's echo; a row whose bytes the service lost
  answers 404, not 500. A service outage is `AttachmentStorageUnavailableException` (503
  `ATTACHMENTS_STORAGE_UNAVAILABLE`).
- `CLAUDE.md`, `BannedSymbols.generated.txt`, `.editorconfig` and `CodeMetricsConfig.txt` in this folder are generated
  by the rules engine; never edit them by hand. The `// scaffold:...` markers show the generators where to add keys,
  codes and registrations; keep them.
- **The API does not start without the attachment service settings.** `Attachments:ServiceBaseUrl` (no default outside Development; https outside Development) and `Codelab:Security:ServiceAssertions:Client` (KeyId, PrivateKeyPem from `New-LocalSecrets.ps1` locally) are validated on start; test hosts get them from `StarterHostSettings`, DocsGen from `DocsHostSettings`.
- **`Attachments:ServiceBaseUrl` is used by the API and by the browser** (the ticket's upload URL), so it must be an address both reach; a split internal/public URL is not supported yet.
- **The demo type is seeded everywhere** but only Development registers `DemoRecordAccess`, so outside Development the `attachments-demo` record type answers `ATTACHMENTS_RECORD_TYPE_UNKNOWN`.
- **The demo record endpoint follows the registration.** `GET /api/attachments/demo-record` answers only while the `attachments-demo` record type is registered (`DemoRecordAccess`, Development only); elsewhere it is 404 `ATTACHMENTS_RECORD_TYPE_UNKNOWN`.
- **Downloads** are streamed (`AttachmentFileResult`: no range requests, `X-Content-Type-Options: nosniff`, `Content-Security-Policy: sandbox; default-src 'none'`) and marked `[UnsignedResponse]`; the response signature still covers status, content type and request binding.
- **A download never carries `Content-Disposition`.** The content endpoint answers only the SPA's signed `fetch` (a link or a navigation cannot carry the bearer token or the request proof), and the SPA saves the Blob under the list's `fileName`. Download managers (Internet Download Manager and similar browser integrations) intercept any response with `Content-Disposition` (`attachment` or `inline`) mid-`fetch`: the page gets a bare `204` without `Access-Control-Allow-Origin` (`MissingAllowOriginHeader`) and the manager replays the URL without the request proof (401 `signature_missing`). That was the phase 6 browser download blocker; the CORS policy and the signing were never at fault. Do not add the header back, and do not add download links to the API.
- **Upload tickets need `Codelab:Security:UploadTickets:Enabled` and its own ring**; `Attachments:TicketLifetimeSeconds` (30-3600) may not exceed `Codelab:Security:UploadTickets:MaxLifetimeSeconds`. The attachment service gets only this ring (as its `Codelab:Security:Tickets`); giving it the API's `Codelab:Security:Tickets` instead refuses every upload, and would let a compromised service open SSO and activation tickets. `LocalSecretsUploadPairTests` proves a fresh `New-LocalSecrets.ps1` pair uploads.
