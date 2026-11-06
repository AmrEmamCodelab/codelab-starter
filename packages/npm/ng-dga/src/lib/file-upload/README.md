# dga-file-upload

The presentational file upload of `@codelab/ng-dga`. It draws a drop zone, a browse button and the list of files a
caller is uploading. It sends nothing and holds no text of its own: an app adapter (starter-web
`shared/attachments`) does the HTTP and feeds the list back through `items`, and every string arrives translated.

## Files

| File | What it holds |
| --- | --- |
| `dga-file-upload.component.{ts,html,css}` | The component: inputs, outputs, template, tokens-only logical CSS |
| `file-upload-item.ts` | `DgaFileUploadStatus` (int enum), `DgaFileUploadItem`, progress clamping, `{name}` label templates, the `data-status` names |
| `file-upload-state.ts` | The row transitions and their helpers (`queuedFileUpload`, `uploadingFileUpload`, `uploadedFileUpload`, `failedFileUpload`, `requeuedFileUpload`, `canMoveFileUpload`) |
| `file-validation.ts` | The client pre-checks: `DgaFileRejectionReason` (int enum), `accept` matching, optional size, empty and count |
| `dga-file-upload.catalog.json` | The catalog entry behind `docs/design/component-catalog.md` |

## Row states

`DgaFileUploadStatus` is an integer enum: `Queued = 0`, `Uploading = 1`, `Uploaded = 2`, `Failed = 3`. The row
shows its state as `data-status="queued|uploading|uploaded|failed"` (styling and test hook).

```
Queued ──> Uploading ──(progress 0..100, any number of times)──> Uploaded (final)
   │            │
   └────────────┴──> Failed ──(retry)──> Queued
```

Move rows with the helpers in `file-upload-state.ts`. A move the table does not allow returns the row unchanged, so a
late progress event after a failure cannot bring the row back to life.

```ts
let row = queuedFileUpload(id, file.name, sizeText);
row = uploadingFileUpload(row, percent);                          // per progress event; null for indeterminate
row = uploadedFileUpload(row);                                    // when the server stored it
row = failedFileUpload(row, { errorText: t(`errors.${code}`), referenceCode });  // from the problem+json
```

## Use

```html
<dga-file-upload
  [label]="'attachments.documents' | t" [browseLabel]="'attachments.browse' | t" [dropLabel]="'attachments.drop' | t"
  accept=".pdf,.png,.jpg,.jpeg" [maxFiles]="type.maxFiles"
  required [requiredLabel]="'attachments.required' | t" [missingText]="'attachments.missing' | t"
  [items]="rows()" [statusLabels]="statusLabels()" [referenceCodeLabel]="'attachments.referenceCode' | t"
  [removeLabel]="'attachments.remove' | t" downloadable [downloadLabel]="'attachments.download' | t"
  [readonly]="readonly()" [error]="problem()"
  (filesSelected)="adapter.upload($event)" (download)="adapter.download($event)" (remove)="adapter.remove($event)" />
```

- **Size is the server's call.** `maxSizeBytes` is an OPTIONAL pre-check. Leave it null (the default) and every
  accepted file goes to the server, which refuses an oversized one with a problem+json `code` and `referenceCode`;
  the adapter turns that into a failed row. Set it only where an early refusal is worth a second copy of the limit.
- **The extension pre-check stays on**: `accept` filters the dialog and refuses other types (`DgaFileRejectionReason.Type`).
- **Reference codes** show on failed rows only, left-to-right (`dir="ltr"`), after `referenceCodeLabel`.
- **Announcements**: the list is a polite live region. Give `statusLabels` (for example
  `{ [DgaFileUploadStatus.Uploaded]: '{name} uploaded', [DgaFileUploadStatus.Failed]: '{name} was not uploaded' }`,
  translated) and screen readers hear each change; the error text and reference code are announced as they appear.
- **Required**: `required` draws an aria-hidden asterisk (`requiredLabel` speaks it). While no row is `Uploaded`,
  `missingText` is shown and describes the group. The server stays authoritative (`ATTACHMENTS_REQUIRED_MISSING`).
- **Download and remove** emit the whole row. Download is offered on uploaded rows only, with `downloadable` and a
  `downloadLabel`, and works while disabled or read-only. Remove needs a `removeLabel` and is blocked while disabled
  or read-only. `readonly` also hides the drop zone.

## Gotchas

- Statuses are integers: compare with `DgaFileUploadStatus.Uploaded`, never with a string. Breaking in 1.0.0:
  `Pending` became `Queued`, values became 0..3, and the `removed` (id) output became `remove` (row).
- `DgaFileRejectionReason` is an integer enum as well (`Type = 0`, `Size = 1`, `Empty = 2`, `Count = 3`).
- The component specs run on AOT output (`npm test` compiles first); see the package `MODULE.md`.
