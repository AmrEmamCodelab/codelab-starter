# Upload API (browser → attachment service)

The contract a client (the SPA's attachment adapter) follows to upload files. The service's purpose, configuration and
rules are in [MODULE.md](../MODULE.md).

## 1. Get a ticket from the API

`POST /api/records/{recordType}/{recordId}/attachments/upload-ticket` with `{ "typeKey": "..." }` (API, permission
`attachments.upload`) answers:

```json
{ "ticket": "t1....", "ticketHeader": "X-STARTER-Upload-Ticket", "uploadUrl": "http://localhost:5185/api/uploads",
  "expiresAt": "...", "allowedExtensions": [".pdf"], "maxFileCount": 10 }
```

Send `ticket` in the header named by `ticketHeader` (`X-{Prefix}-Upload-Ticket`; the prefix is the deployment's
protocol prefix, so never hard-code it). A ticket is good for ONE request until `expiresAt`, refused or not.

## 2. Upload

```
POST {uploadUrl}
X-STARTER-Upload-Ticket: t1....
Content-Type: multipart/form-data; boundary=...

--...
Content-Disposition: form-data; name="file"; filename="scan.pdf"
Content-Type: application/pdf

%PDF-...
--...--
```

- One to `maxFileCount` parts, each named `file` with a file name. Any other part refuses the whole request.
- Send nothing else: no cookie, no `Authorization` header (they are ignored, and the CORS policy allows no credentials).
- All or nothing: if any file is refused, none of the request's files is kept.
- The client file name is metadata only (reduced to its last segment); the service stores files under its own ids.

**200:**

```json
{ "files": [ { "fileId": "Q2x...22 chars", "fileName": "scan.pdf", "sizeBytes": 48211, "contentType": "application/pdf",
               "sha256": "9f2c...", "receipt": "r1...." } ] }
```

## 3. Link

Post each `receipt` to the API (`POST /api/records/{recordType}/{recordId}/attachments` with `{ typeKey, receipt }`).
The receipt is signed for the ticket's record, type and user; the API refuses it for anything else
(`ATTACHMENTS_RECEIPT_INVALID`).

## Refusals

Every refusal is `application/problem+json` with `code` and `referenceCode`; render `errors.<code>` and show the
reference code.

| Status | `code` | When |
| --- | --- | --- |
| 401 | `ATTACHMENTS_TICKET_INVALID` | No ticket, or it is malformed, forged, expired, already used or for another purpose. Ask the API for a new one. |
| 413 | `ATTACHMENTS_FILE_TOO_LARGE` | A file is over `MaxFileBytes`, the request is over `MaxTotalBytes`, or the declared body is over the ceiling. |
| 413 | `ATTACHMENTS_TOO_MANY_FILES` | More file parts than `MaxFileCount`. |
| 415 | `ATTACHMENTS_TYPE_NOT_ALLOWED` | The extension is blocked or not allowed, the name is unusable, the body is not multipart, or a part is not a `file` part. |
| 415 | `ATTACHMENTS_SIGNATURE_MISMATCH` | The content does not start with the magic bytes of its extension (a renamed or polyglot file). |
| 400 | `ATTACHMENTS_EMPTY_FILE` | An empty file, no file part, or a multipart body that cannot be read. |
| 503 | `RETRYABLE` | The ticket store could not answer; retry with a new ticket. |

Downloads and deletes never come from the browser: the API calls `GET` / `DELETE /api/files/{fileId}` with a service
assertion after its own object authorization.
