# Attachment service (Codelab.Attachments.*)

## Purpose

The attachment store, released as its own deployable (build plan phase 6). The browser uploads files **directly** to
this service with a one-time ticket the API issued; the API later links a file to a record with the signed receipt the
upload returned, and reads or deletes stored files over the one service-to-service scheme. The service has no database
and no knowledge of records or users: file ids are its own random ids, and the record a file belongs to travels sealed
inside the ticket subject.

| Project | Holds |
| --- | --- |
| `src/Codelab.Attachments.Core` | The file-share store (`IAttachmentFileStore`: `<RootPath>/<yyyy>/<MM>/<fileId>`, staged then moved, sidecar metadata) and the streaming upload pipeline (`IUploadPipeline`: caps, blocked and allowed extensions, magic bytes, SHA-256, receipts, all-or-nothing rollback). No HTTP host. |
| `src/Codelab.Attachments.Api` | The host: `Program.cs`, the two authentication schemes, the three endpoints, the upload CORS policy, readiness, `web.config`. |
| `tests/*` | Unit tests of both, and HTTP security tests that start the real `Program` in memory. |

Built only on the shared packages (no local copies): Codelab.Build.Sdk, Codelab.Security (protocol, tickets, receipts,
S2S, upload primitives, `UploadContentTypes`), Codelab.Observability (Serilog, correlation id, health) and
Codelab.Errors (the problem+json envelope).

## Use

### Run

```powershell
powershell -File tools\devseed\New-LocalSecrets.ps1    # once: both local secrets files (gitignored), shared rings
& E:\codelab-tools\dotnet10\dotnet.exe run --project services\attachment-service\src\Codelab.Attachments.Api   # http://localhost:5185
& E:\codelab-tools\dotnet10\dotnet.exe test services\attachment-service\Codelab.Attachments.slnx
```

`run-local.ps1` starts it after the API (`-NoAttachments` skips it). Health: `/health/live` (process up) and
`/health/ready` (the storage root is reachable).

### Endpoints

| Method and route | Credential | Success | Refusals (problem+json `code`) |
| --- | --- | --- | --- |
| `POST /api/uploads` | upload ticket in `X-{Prefix}-Upload-Ticket` only | 200 `{ "files": [ { "fileId", "fileName", "sizeBytes", "contentType", "sha256", "receipt" } ] }` | 401 `ATTACHMENTS_TICKET_INVALID`, 413 `ATTACHMENTS_FILE_TOO_LARGE` / `ATTACHMENTS_TOO_MANY_FILES`, 415 `ATTACHMENTS_TYPE_NOT_ALLOWED` / `ATTACHMENTS_SIGNATURE_MISMATCH`, 400 `ATTACHMENTS_EMPTY_FILE`, 503 `RETRYABLE` |
| `GET /api/files/{fileId}` | S2S assertion (`CodelabServiceAssertion`) only | 200 bytes, `Content-Type` from `UploadContentTypes`, `Content-Disposition: attachment`, `nosniff`, `no-store` | 401 `UNAUTHENTICATED`, 404 `ATTACHMENTS_FILE_NOT_FOUND` |
| `DELETE /api/files/{fileId}` | S2S assertion only | 204 | 401 `UNAUTHENTICATED`, 404 `ATTACHMENTS_FILE_NOT_FOUND` |

Every refusal is the Codelab.Errors envelope with `code`, `correlationId` and `referenceCode`. Request-shape refusals
reuse the contract codes: a body that is not multipart, or a part that is not a `file` part, is 415
`ATTACHMENTS_TYPE_NOT_ALLOWED`; no file part or an unreadable multipart body is 400 `ATTACHMENTS_EMPTY_FILE`; over the
total cap (or a declared body above the ceiling) is 413 `ATTACHMENTS_FILE_TOO_LARGE`. The browser contract, step by
step, is in [docs/upload-api.md](docs/upload-api.md).

### The flow (ticket, upload, receipt, link)

1. The API checks the user may change the record, then seals a ticket with `UploadTicketRing`, its own ring
   (`Codelab:Security:UploadTickets`), not `ISealedTicketService`:
   `Seal(new TicketRequest("attachment-upload", "{recordType}|{recordPublicId}|{typeKey}|{userSubject}", lifetime))`
   (the record appears only as its PublicId).
2. The browser posts `multipart/form-data` (parts named `file`) to `POST /api/uploads` with the ticket header. The
   service opens the ticket once and signs one receipt per stored file with `Scope` = the ticket subject.
3. The browser posts each receipt to the API; the API verifies it (`UploadReceiptSigner.TryVerify`) and refuses one whose
   scope is not the record it links to.
4. Downloads and deletes go browser → API (object authorization) → service (S2S).

### Configuration

| Key | Where | Notes |
| --- | --- | --- |
| `Attachments:ServiceName` | `appsettings.json` | Log and trace name (product identity, ADR-003). Default `starter-attachments`. |
| `Attachments:Storage:RootPath` | server / local secrets | Fully qualified folder or UNC share; the app pool identity needs modify rights there only. Required. |
| `Attachments:Cors:AllowedOrigins` | `appsettings.{Env}.json` / server | The SPA origins allowed to upload (https only outside Development). Every other endpoint is same-origin only. |
| `Codelab:Security:Protocol:Prefix` | `appsettings.json` | Must equal the API's prefix (tickets, receipts and assertions are bound to it). |
| `Codelab:Security:Tickets` | secret | `Enabled` (must be true) and the API's upload-ticket ring (`Codelab:Security:UploadTickets` in the API settings): the API seals, the service opens. Never the API's own `Codelab:Security:Tickets` ring. |
| `Codelab:Security:Uploads` | caps in `appsettings.json`, ring secret | `ReceiptsEnabled` (must be true), the API's receipt ring, `MaxFileBytes`, `MaxFileCount`, `MaxTotalBytes`, `AllowedExtensions`, `BlockedExtensions`: the same section the API binds (one allowlist). |
| `Codelab:Security:ServiceAssertions:Server` | `Audience` in `appsettings.json`, keys secret | `Audience` = `starter-attachments`; `TrustedServices[]` = `{ ServiceName: starter-api, KeyId, PublicKeyPem }`. |
| `Codelab:Security:ReplayProtection` | `appsettings.json` | `Memory` with `AcknowledgeSingleInstance` = true (one node). A farm sets `Store` = `SqlServer` plus `Codelab:Security:SqlStore` and `Codelab:Security:Sessions:Store`. |
| `Observability:*` | server | As every Codelab host; `Observability:UserIdHash:Key` is required in Production. |

Key material lives only in the environment, a secret store or (Development) the gitignored
`src/Codelab.Attachments.Api/appsettings.Development.local.json` that `tools/devseed/New-LocalSecrets.ps1` writes from
the API's file. Committed settings carry none (`ConfigurationFilesTests`). The csproj sets
`CopyToPublishDirectory="Never"` on `appsettings.*.local.json`, so a publish folder or zip never carries the dev key rings.

## Extend

- **A new file type:** add its magic bytes to `FileSignatureSniffer.KnownSignatures` and its MIME type to
  `UploadContentTypes.ByExtension` (Codelab.Security; a test fails when one is missing), then list the extension in
  `Codelab:Security:Uploads:AllowedExtensions` on BOTH the API and the service. Per-type limits (which extensions a
  record's attachment type takes) belong to the API's attachment types, not here.
- **A new endpoint:** map it in `AttachmentsEndpoints.Map` with an explicit policy
  (`AttachmentsAuthentication.ServiceCallerPolicy` or `UploadTicketPolicy`); an endpoint mapped without one is S2S only
  (fallback policy). Add it to `HostBehaviourTests.Every_endpoint_names_one_credential_and_only_health_is_anonymous`.
- **A new refusal:** add the code to `AttachmentServiceErrorCodes`, the reason to `UploadRefusalReason` (explicit int)
  and `UploadRefusal.For`, `errors.<CODE>` to the SPA's ar and en bundles, and a row to the endpoint table above.
- **Another trusted caller:** add a `TrustedServices[]` entry (its own key pair; the private half stays with the caller).
- **Storage elsewhere (blob, object store):** implement `IAttachmentFileStore` in Core and register it instead of
  `FileShareAttachmentStore`; keep ids service-issued and the staged-then-commit contract.

## Rules

- Two credentials, each on its own endpoints: the upload ticket for `POST /api/uploads`, the S2S assertion for
  everything else. No JWT bearer, API key, cookie or default scheme (`module.rules.yaml`, `HostBehaviourTests`).
- Upload content is streamed through `IUploadPipeline`; no form binding, no `IFormFile` (forbidden pattern).
- One allowlist: `Codelab:Security:Uploads` and `UploadContentTypes`; the service keeps no list of its own.
- Every refusal goes through `AttachmentProblems` (the Codelab.Errors envelope); no hand-written error body.
- Names and keys: no product literal (header names from `SecurityProtocol`, identity from configuration), no key
  material in the repository; tests generate keys at run time. Time from `TimeProvider`.
- Log identifiers only: never a ticket, receipt, client file name or ticket subject.
- Generated files here (`CLAUDE.md`, `.editorconfig`, `BannedSymbols.generated.txt`, `CodeMetricsConfig.txt`) come from
  `module.rules.yaml`; run `node tools/rules-engine/compile.mjs` after editing it.

### Security advisories carried from the source product, each pinned by a test

| Advisory in the source service | Here | Test |
| --- | --- | --- |
| A JWT bearer signed with a shared HS256 secret was the credential; a user token could be minted by anyone holding the secret. | ES256 per-request S2S assertions (jti replay, method/path/body binding); a bearer or API key authenticates nothing. | `FileEndpointsTests.A_replayed_service_assertion_is_refused`, `An_assertion_for_another_method_is_refused`, `A_bearer_token_is_refused`, `An_api_key_is_refused`, `UploadSecurityTests.A_bearer_token_is_not_a_credential`, `An_api_key_is_not_a_credential` |
| Every authenticated caller held upload, download and delete, with no ownership check in the service. | Browsers hold only a one-time ticket for one record and type; download and delete are S2S only, behind the API's object authorization. | `FileEndpointsTests.An_upload_ticket_does_not_reach_a_file_and_is_not_consumed`, `UploadSecurityTests.A_ticket_opens_once`, `HostBehaviourTests.An_expired_ticket_is_refused`, `UploadSecurityTests.A_tampered_ticket_is_refused`, `A_ticket_sealed_for_another_purpose_is_refused` |
| A client-supplied folder id was used verbatim in the storage path (path-injection surface). | Paths are built only from service-issued random ids under the root; client names are sanitized metadata. | `UploadSecurityTests.A_traversal_name_is_reduced_to_its_leaf_and_stored_under_the_root`, `FileEndpointsTests.A_malformed_id_is_not_found`, Core `FileShareAttachmentStoreTests` |
| A failed upload into a reused folder deleted the whole folder. | All-or-nothing per request: only this request's committed files are removed. | Core `UploadPipelineTests` (rollback leaves other requests' files) |
| Only the extension and a fixed list decided the type. | Magic bytes per extension (polyglots refused), blocked list, allowlist, per-file, count and total caps while streaming. | `UploadSecurityTests.A_polyglot_file_is_refused_by_its_magic_bytes`, `A_file_over_the_cap_is_refused_with_a_reference_code`, `More_files_than_the_cap_are_refused`, Core `UploadPipelineTests` |
| The dev secrets (JWT and receipt keys) were checked in and had to match the backend byte for byte. | No key in any committed file; local keys are generated by devseed; a published key refuses to start. | `ConfigurationFilesTests.Committed_settings_carry_no_key_material`, `StartupRefusalTests` |
| The IIS default 30 MB request cap silently cut uploads before the service's own limit. | `UploadBodyCeiling` raises the server limit to exactly what a legal request needs; `web.config` carries a matching `maxAllowedContentLength`. | `ConfigurationFilesTests.The_iis_request_limit_fits_the_upload_ceiling_of_the_shipped_settings`, `UploadSecurityTests.A_declared_body_above_the_ceiling_is_refused_before_it_is_read` |
| 401 and 403 had empty bodies and the service had no `/health`. | Every refusal is problem+json with a code and reference code; `/health/live` and `/health/ready`. | `Samples.AssertProblemAsync` in every refusal test, `HostBehaviourTests.Health_is_anonymous_and_green`, `Readiness_fails_while_the_storage_root_is_unreachable` |
| An unreadable form field was silently dropped. | Any part that is not a `file` part refuses the whole request. | Core `UploadPipelineTests` (non-file parts) |
| Orphaned files stayed on disk when the backend never recorded them. | The API soft-removes a link and deletes the file through its outbox (`attachments.delete-file`). Files uploaded but never linked are still an open item (a sweep job). | API module tests (be-att-*) |

## Gotchas

- **No `UseCodelabSecurity()`.** The service has no user sessions and no request signing, so it does not run the
  session and signing middleware; security headers and the replay guard still come from `AddCodelabSecurity`.
  `Codelab:Security:Cors` must stay empty here (refused at start): the upload endpoint has its own policy.
- **No PublicId ring.** Codelab.Security requires one at start, so `EphemeralPublicIdRing` supplies a random ring that
  lives only in the process; a configured `Codelab:Security:PublicIds` section is refused. The service can never read an
  API identifier.
- **The ticket opens during authorization,** before the body ceiling and the pipeline run, so a refused upload (too
  large, wrong type) has used its ticket: the SPA asks the API for a new one on retry.
- **One ticket covers one request,** with up to `MaxFileCount` files. A second request needs a second ticket.
- **Under IIS** keep `maxAllowedContentLength` in `web.config` at least `UploadBodyCeiling.For(options)` for the
  deployed caps, and keep WebDAV removed (it answers `DELETE` with 405 before the request reaches the service).
- **A 401 on a file endpoint is `UNAUTHENTICATED`** (Codelab.Errors status-code pages); only the upload endpoint answers
  `ATTACHMENTS_TICKET_INVALID`.
- **Prefix and rings must match the API.** A different `Protocol:Prefix`, ticket ring or receipt ring makes every
  ticket invalid and every receipt unverifiable; `New-LocalSecrets.ps1` copies them from the API file.
- **Tests:** `StartupRefusalTests` retries a refused start, because `WebApplicationFactory` sometimes reports one as
  `ObjectDisposedException` and hides the reason.
- **Not carried from the source service:** ZIP and loose-files upload modes, base64 download envelopes, folder ids,
  the module enum, JWT and permission authorization, and local copies of the correlation-id, exception-handler,
  security-headers and failure-classification code.
