# Web attachments (`features/attachments`)

## Purpose

The attachments demo page (module 13 UI sample): `/attachments/demo` inside the staff shell. It exists only in a
development build and needs `attachments.view`. It asks the API for the Development demo record
(`GET api/attachments/demo-record` → `{ recordType: "attachments-demo", recordId }`). Then it hosts the shared
`app-record-attachments` widget on that record inside the requester-detail page pattern. It is the page the Phase 6
Playwright exit check runs on: upload a PDF, see it listed, download it, and have an oversized file refused with a
bilingual message and its reference code.

| Piece | File |
| --- | --- |
| Route table `ATTACHMENTS_ROUTES` (`ATTACHMENTS_DEMO_ROUTE_PATH`, `ATTACHMENTS_DEMO_LINK`), spread into the shell's children in `app.routes.ts` | `attachments.routes.ts` |
| `attachmentsDemoEnabledGuard` (canMatch) and the `ATTACHMENTS_DEMO_ENABLED` token (default `isDevMode()`) | `attachments-demo-enabled.guard.ts` |
| `AttachmentsDemoPageComponent`, `demoRecordApiError` | `demo-page/` |
| Texts `attachments.demo.*` | `attachments-translation-keys.ts` |

## Use

Run the stack with `run-local.ps1` (API :5180, attachment service :5185, web :4280). Sign in as a user who holds
`attachments.view` and `attachments.upload` (and `attachments.remove` to try removing). Open
`http://localhost:4280/attachments/demo`. The visual harness route file is `e2e/routes/p6-attachments.json`.

## Extend

- **A real record screen** does not copy this page. It puts `<app-record-attachments [recordType]="..."
  [recordId]="record.id" />` in its pattern's attachments slot (`[detailAttachments]` of the requester-detail pattern).
  See [shared/record-attachments/MODULE.md](../../shared/record-attachments/MODULE.md).
- A new page under this feature gets a route in `ATTACHMENTS_ROUTES` with `permissionGuard()` and a key in its data,
  and its texts under `attachments.<page>.*`.

## Rules

- No `HttpClient`. The demo record comes through the shared `AttachmentsClient`; the attachment flow through the
  widget and its adapter.
- Every route carries a permission key (`route-gates.spec.ts`). Every action is gated (`tools/permission-gates.mjs`);
  this page has no action of its own except the pattern's retry.
- No raw text: `attachments.demo.*` in both bundles.

## Gotchas

- **Development only, twice.** The route's `canMatch` refuses it in an optimized build (`ATTACHMENTS_DEMO_ENABLED`),
  and the API answers `demo-record` only in its Development environment (404 elsewhere, shown with the pattern's
  error state).
- The route's key is `AttachmentsPermissionKeys.View`, which is the generated `PermissionKeys.AttachmentsView`, so
  `route-gates.spec.ts` (generated keys only) accepts it.
- The page is not in the shell navigation (`app-nav.ts`); open it by URL.
- `e2e/visual.mjs` does not sign in, so it captures the login page for this route unless the run reuses a signed-in
  browser profile. The Phase 6 exit check is run with Playwright MCP after signing in.
