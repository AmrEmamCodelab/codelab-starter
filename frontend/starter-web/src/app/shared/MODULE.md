# Web shared (`src/app/shared`)

## Purpose

Presentational building blocks that two or more features use and that are neither page patterns
(`src/app/patterns`) nor `@codelab/ng-dga` components. They render inputs and emit outputs; services and state
belong in `core`, except the app-wired adapters such as `attachments/`.

| Piece | What it gives the app | Files |
| --- | --- | --- |
| `*hasPermission` | Structural directive: renders its template while the user holds a permission key (one key, or several with `PermissionMatchMode.Any` / `All`), its `else` template otherwise, and follows live grants and revokes | `permissions/has-permission.directive.ts` |
| `app-file-picker` | Translated single-file picker: a `dga-button` opens a visually hidden native `<input type="file">`, the chosen name or `shared.file-picker.none` shows beside it; inputs `labelKey`, `accept`, `disabled`; output `fileChange` (`File` or null); `clear()`. Interim until `@codelab/ng-dga` ships a `dga-file-input` | `file-picker/file-picker.component.ts` |
| Permission matching | `holdsPermissions(has, required, mode)`, `PermissionMatchMode`, `PermissionRequirement`, re-exported from core (`core/permissions/permission-match.ts`, which `permissionGuard()` also uses) | `permissions/permission-match.ts` |
| Geo data source | `StaticGeoDataSource` (implements `DgaGeoDataSource`) over the static bilingual seed in `geo-seed-data.ts` (stable slug ids; never store them as lookup ids). Features take `inject(GEO_DATA_SOURCE)`; Phase 5 provides an API-backed source behind the same token | `geo-select/` |
| `app-file-picker` (P4) | Now composed on ng-dga `dga-file-upload`; same inputs and `fileChange` output; a file outside `accept` is still emitted. Keys and test ids in `file-picker-keys.ts` | `file-picker/` |
| Attachments adapter | `RecordAttachmentsAdapter` (types, list, upload: ticket → attachment service → receipt → link with progress, download as a signed fetch saved through `FileSaver`, never a link, remove), `AttachmentsClient`, `AttachmentUploadService`, `AttachmentError` {code, referenceCode, status}; see [attachments/MODULE.md](attachments/MODULE.md) | `attachments/` |
| `app-record-attachments` | The typed attachments of any record: inputs `recordType`, `recordId`, `readonly`; one `dga-file-upload` per attachment type; upload/download/remove through the attachments adapter; gated by attachments.view/upload/remove; the server's required-missing verdict; failures as page feedback with `errors.<CODE>` and the reference code; see [record-attachments/MODULE.md](record-attachments/MODULE.md) | `record-attachments/` |
| Generic record UI | Typed `RecordsClient` / `QueuesClient` over the generic Records and Queues endpoints, the wire models (including `attachmentsRecordId`, the id `app-record-attachments` takes), error codes, the `statusWording` pipe, `app-record-detail-cards`, `app-record-timeline`, `app-decision-panel`, `app-sla-badge`; no record type inside; see [records/MODULE.md](records/MODULE.md) | `records/` |
| DevAutofill | Development-only form filler: `provideDevAutofill`, `<dga-dev-autofill>`, `[devAutofill]`; features register named data sets per form key in the app registry `src/app/dev-autofill.sets.ts`. Production builds swap in stubs, and `tools/ci/no-dev-autofill.mjs` proves the bundle holds none of it; see [dev-autofill/MODULE.md](dev-autofill/MODULE.md) | `dev-autofill/` |

## Use

```ts
import { HasPermissionDirective } from '../../../shared/permissions/has-permission.directive';
import { PermissionMatchMode } from '../../../shared/permissions/permission-match';
import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';

@Component({ imports: [HasPermissionDirective, DgaButtonComponent], ... })
export class UsersPageComponent {
  protected readonly PermissionKeys = PermissionKeys;
  protected readonly PermissionMatchMode = PermissionMatchMode;
}
```

```html
<dga-button *hasPermission="PermissionKeys.UsersManage" (click)="create()">{{ 'admin.users.new' | t }}</dga-button>

<section *hasPermission="[PermissionKeys.AccessRolesView, PermissionKeys.AccessGrantsView]; mode: PermissionMatchMode.All; else readOnly">
  ...
</section>
<ng-template #readOnly>...</ng-template>
```

The directive reads `PermissionStore` as a signal: when `PermissionSyncService` refreshes the keys (focus, navigation,
interval), gated buttons appear or disappear without a reload. It hides what the server would refuse; the server
still authorizes every call, and routes are gated separately by `permissionGuard()`.

## Extend

- A new shared piece gets its own folder (`shared/<area>/`), a spec beside every file with logic, and a row in the
  table above. Check first whether it belongs in `@codelab/ng-dga` (a generic DGA component, platform-owner change)
  or in `patterns/` (a page base).
- A new text goes under a `shared.*` translation namespace in both `ar.json` and `en.json`.

## Rules

- Shared imports only `core` and `@codelab/ng-dga` (ESLint boundaries). Never a feature, a layout or a pattern.
- No business logic. API calls only in app-wired adapters (attachments/), and only through core ApiService; no raw text; design tokens and logical CSS only.
- Permission keys come from the generated `PermissionKeys`, never string literals (`base.no-string-permission-key`).
- An empty key list never renders (fail closed), in either mode.

## Gotchas

- Never render a visible native `<input type="file">`: the browser draws its own "Choose File / No file chosen" in
  the browser's language, not the app's. Use `app-file-picker`.
- `*hasPermission` takes keys typed as `PermissionKey`; a key appears there only after the backend adds it to its
  catalogue and `npm run api:gen` regenerates `permission-keys.generated.ts` (the `/new-permission` skill does both).
- Before the first permission sync the store is empty, so gated content is hidden (and the `else` template shown)
  until the keys arrive.
- The view is created once per transition: a sync that returns the same keys does not re-create gated content, so
  its state (focus, typed text) survives.
