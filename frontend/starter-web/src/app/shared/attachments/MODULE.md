# Web shared: attachments (`src/app/shared/attachments`)

## Purpose

The app-wired attachment adapter. It links typed files to any record through two servers:

1. The API issues a single-use **upload ticket**.
2. The browser POSTs the file to the **attachment service** with that ticket.
3. The service answers a signed **receipt**.
4. The API **links** the receipt to the record.

Screens never see these steps. They call `RecordAttachmentsAdapter` and render an `AttachmentError` when a call fails.

| Piece | What it gives the app | File |
| --- | --- | --- |
| `RecordAttachmentsAdapter` | `types(recordType)`, `list(recordType, recordId)`, `upload(recordType, recordId, typeKey, file)` (progress events, then one `Linked` event), `download(attachment)` (saves the file), `remove(id)`. Every failure is an `AttachmentError` | `record-attachments.adapter.ts` |
| `AttachmentsClient` | The API endpoints (types, list, upload ticket, link, content, remove, demo record) over `ApiService`; all calls are quiet (no error dialog) | `attachments.client.ts` |
| `AttachmentUploadService` | The multipart POST to the ticket's `uploadUrl` through `ApiService.uploadMultipart`. It sends only the ticket header, reports progress, and returns the stored file with its receipt | `attachment-upload.service.ts` |
| `AttachmentError`, `attachmentErrorFrom`, `rethrowAsAttachmentError` | `{ code, referenceCode, status }` read from the problem+json of the API **or** the service, including a `Blob` body from a refused download | `attachment-error.ts` |
| `AttachmentErrorCodes` | Every refusal code the attachment flow can surface (service and API module) | `attachment-error-codes.ts` |
| `FileSaver` | Saves a `Blob` under a file name: a transient `<a download>`, with the object URL released after `OBJECT_URL_RELEASE_DELAY_MS` | `file-saver.ts` |
| `acceptAttributeFor`, `hasAllowedExtension`, `extensionOf` | Hints for the file picker (the server decides) | `attachment-files.ts` |
| Wire types and ids | `AttachmentId` (`WireId<'attachment'>`), `AttachableRecordId` (any record's `WireId`), request and response shapes, `AttachmentUploadEventKind` | `attachments.models.ts` |
| Paths and wire constants | `AttachmentsApiPaths`, `AttachmentsQueryParams`, `UPLOAD_FILE_PART`, `UPLOAD_TICKET_HEADER_PATTERN` | `attachments-api.ts` |
| Spec fixtures | `provideAttachmentsTesting()`, test ids, ticket and attachment | `attachments.testing.ts` |

The wire types in `attachments.models.ts` are written by hand to match the Phase 6 contract exactly. Once
`npm run api:gen` emits the attachment schemas into `core/api/generated`, the integrator may switch the client to the
generated types. The adapter's signatures stay the same.

## Use

```ts
private readonly attachments = inject(RecordAttachmentsAdapter);

upload(file: File) {
  this.attachments.upload(this.recordType, this.recordId, typeKey, file).subscribe({
    next: (event) => {
      if (event.kind === AttachmentUploadEventKind.Progress) this.percent.set(event.percent);
      else this.items.update((items) => [...items, event.attachment]); // AttachmentUploadEventKind.Linked
    },
    error: (error: AttachmentError) => this.failure.set(error), // render `errors.${error.code}` + error.referenceCode
  });
}

this.attachments.list(recordType, recordId);  // { items, missingRequiredTypeKeys }
this.attachments.download(attachment);        // fetches through the API and saves under attachment.fileName
this.attachments.remove(attachment.id);       // 204; the stored file is cleaned up by the API later
```

Required permissions:

- `attachments.view` for types, list and download.
- `attachments.upload` for an upload (the ticket and the link).
- `attachments.remove` for remove.

Gate the matching buttons with `*hasPermission`.

## Extend

- **New endpoint:** add its path to `AttachmentsApiPaths` (relative, under `API_PREFIX`, values URL-encoded with
  `segment`). Add a method to `AttachmentsClient` and its case to `attachments.client.spec.ts`. If screens need it,
  expose it on the adapter behind `catchError(rethrowAsAttachmentError)`.
- **New refusal code (service or API module):** add it to `AttachmentErrorCodes`. Add `errors.<CODE>` to both
  `ar.json` and `en.json`.
- **A record type of your own:** nothing changes here. The backend owning module registers its
  `IAttachableRecordAccess` and seeds its types. The screen passes its record type key and its record's `WireId`.

## Rules

- HTTP only through `ApiService`. The upload goes through `ApiService.uploadMultipart`, which:
  - refuses any URL outside `origins.attachment` in `config.json` (`UntrustedUploadUrlError`, so nothing is sent);
  - sends no bearer token, no request signature and no cookies.
- The ticket travels only in the header the ticket names, and only when that name matches
  `X-{Prefix}-Upload-Ticket`. It is never sent to the API, never put in a URL, and never stored.
- The client is never authoritative. Size, type, signature bytes and per-type limits are decided by the service and
  the API. `hasAllowedExtension` is only a hint for the UI.
- No raw text. Errors render as `errors.<code>` and labels as `attachments.*` keys. Ids are `WireId`s, never numbers.
- Every function with logic has a spec beside it (Vitest, `HttpTestingController`).

## Gotchas

- **`origins.attachment` must list the attachment service root.** For local development that is
  `http://localhost:5185`. With an empty list, every upload fails with `INTERNAL_ERROR` before sending a byte. A
  deployment writes its own service root into `config.json`.
- **The service's 401 is not a session expiry.** An upload is sent self-handled, so `ATTACHMENTS_TICKET_INVALID`
  (an expired or reused ticket) never logs the user out. A 401 from the API does still end the session.
- **One ticket covers one upload.** A retry after any failure starts again from `upload(...)`, which gets a new ticket.
- **Download goes through the API, never straight to the service.** The response is signed with `UNSIGNED-BODY`
  (`[UnsignedResponse]`). An error body arrives as a `Blob`; `attachmentErrorFrom` decodes it.
- **A download is a signed fetch, then `FileSaver`; never a link.** `AttachmentsClient.content` calls
  `ApiService.getBlob`, so the bearer token and the request proof go with it, and `FileSaver` saves the Blob from an
  object URL. An `<a href>` or `window.open` to the API can carry neither, and is refused 401. The saved name is the
  list's `fileName`: the API sends no `Content-Disposition`. `attachment-download.signed.spec.ts` proves the whole
  stack (signed request, verified `UNSIGNED-BODY` answer, Blob saved).
- **Download managers break downloads that carry `Content-Disposition`.** Internet Download Manager and similar browser
  integrations intercept such a response in the middle of a `fetch`. The page then gets a bare `204` without
  `Access-Control-Allow-Origin`, and the console shows `MissingAllowOriginHeader`. The manager then replays the URL
  without the request proof, and the API log shows 401 `signature_missing`. That was the phase 6 download blocker.
  The API therefore never sends that header on the content endpoint. If a user still reports this pair of errors,
  look for a download-manager extension before you look at CORS.
- **Progress can report 0 percent.** `percent` is 0 while the browser does not know the total, and progress covers
  only the upload to the service, not the link. Show a busy state until the `Linked` event arrives.
