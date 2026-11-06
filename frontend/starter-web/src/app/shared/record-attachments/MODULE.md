# Web shared: record attachments widget (`src/app/shared/record-attachments`)

## Purpose

`app-record-attachments`: the typed attachments of ANY record, in one widget. A record screen drops it in with its
record type key and its record's PublicId. It loads the record type's attachment types and the record's linked files,
and shows one ng-dga `dga-file-upload` per type. It uploads, downloads and removes through the shared adapter
([../attachments/MODULE.md](../attachments/MODULE.md)) and reports every outcome as page feedback.

| Piece | What it gives the app | File |
| --- | --- | --- |
| `RecordAttachmentsComponent` | `app-record-attachments`: inputs `recordType`, `recordId` (`AttachableRecordId`), `readonly`; permission gates; one control per type | `record-attachments.component.{ts,html,scss}` |
| `RecordAttachmentsStore` | Per-widget state (provided by the component): load, retry, sequential upload queue, one download and one remove at a time, refresh after every change, feedback | `record-attachments.store.ts` |
| View helpers | Upload row transitions, `errors.<CODE>` fallback, feedback text with the isolated reference code, sizes per language, sections per type | `record-attachments-view.ts` |
| Keys | `RecordAttachmentsTranslationKeys` (`attachments.widget.*`), `ATTACHMENT_ERROR_TEXT_KEYS` (`errors.<CODE>` for every attachment code), test ids | `record-attachments-keys.ts` |
| `AttachmentsPermissionKeys` | `attachments.view`, `attachments.upload`, `attachments.remove`, named over the generated `PermissionKeys.Attachments*` | `attachments-permission-keys.ts` |
| Spec fixtures | `fakeAdapter()`, `recordAttachmentsTestProviders()` (real `ar`/`en` bundles), `useLanguage()` | `record-attachments.testing.ts` |

## Use

```html
<app-record-attachments [recordType]="recordType" [recordId]="record.id" [readonly]="!record.canEdit" />
```

- **Gates.** The whole widget needs `attachments.view` (`*hasPermission`). Picking files needs `attachments.upload`
  (`*hasPermission`; without it the same control renders read-only). The remove button on a row needs
  `attachments.remove`. It sits inside the ng-dga control, so it follows `PermissionStore.has()` (a signal, so live
  grants and revokes still apply). Download is offered on every linked file to any holder of `attachments.view`.
- **`readonly`** hides the drop zone and the remove buttons and keeps the list and the downloads. Use it for a record
  that can no longer change, such as a submitted request.
- **Required types** show the server's verdict: the missing text appears only while the API lists the type in
  `missingRequiredTypeKeys`. The list is loaded again after every upload and remove. The submit interlock
  (`ATTACHMENTS_REQUIRED_MISSING`) stays on the server.
- **Errors.** Every failure (load, upload, download, remove) shows as a `dga-page-feedback` banner. It carries the
  action's title and the bilingual `errors.<CODE>` text, followed by the reference code. A refused upload also stays
  on its row, with the same text and the reference code (left to right). An untranslated code falls back to
  `errors.INTERNAL_ERROR`, never the raw code.
- **Size.** There is no client size cap. The service refuses an oversized file (`ATTACHMENTS_FILE_TOO_LARGE`, 413)
  with a reference code. The extension pre-check (`accept`) stays on as a hint.
- **Uploads** run one file at a time per pick, in pick order. Each file gets its own ticket (the adapter's chain).
  Loading another record cancels the running uploads.

## Extend

- **A new record type** needs nothing here. The owning backend module registers its `IAttachableRecordAccess` and
  seeds its types. The screen passes its record type key.
- **A new refusal code**: add it to `AttachmentErrorCodes` (adapter) and add `errors.<CODE>` to both bundles.
  `record-attachments-translations.spec.ts` fails until both bundles have it.
- **A new text** goes under `attachments.widget.*` in both bundles and into `RecordAttachmentsTranslationKeys`.
- **Per-row confirmation before remove**: the API soft-removes (the file is cleaned up later by the outbox). Add an
  ng-dga confirm dialog in the component if a project wants one; keep the store call unchanged.

## Rules

- HTTP only through the shared adapter (`RecordAttachmentsAdapter`), never `ApiService` or `HttpClient` here.
- The component holds no business rules. The store keeps state, and the view helpers are pure functions with specs.
- No raw text: `attachments.widget.*` and `errors.<CODE>`, `ar` and `en`. ng-dga label templates use `{name}`, which
  the control fills in. Translation placeholders use `{{...}}`.
- Statuses and actions are integer enums (`RecordAttachmentsLoadState`, `AttachmentAction`, ng-dga
  `DgaFileUploadStatus`).

## Gotchas

- `AttachmentsPermissionKeys` only names the generated `PermissionKeys.Attachments*` values; a renamed or removed
  key in the backend breaks the build here after `npm run api:gen`, which is intended.
- `dga-file-upload` computes its missing indicator as required with no uploaded row. The widget passes `missingText`
  only while the server reports the type missing, so the server stays authoritative.
- Upload rows keep the error code, not its text, so a language switch re-translates a failed row; that is why they
  are the widget's own `UploadRow` rather than ng-dga's translated items. Their moves still follow ng-dga's
  transition table (`canMoveFileUpload` in `file-upload-state.ts`), the one owner of that rule.
- The client refusals of a pick (type, size, empty, count) work the same way. The component keeps the raw
  `DgaFileRejection`s and the type's `maxFiles`, and translates them in a `computed`, so the control error follows a
  language switch. Never store their translated text.
- Page feedback does not move focus (`moveFocus` false), because it appears while the user may still be picking files.
  The control's own live region announces each row's status.
