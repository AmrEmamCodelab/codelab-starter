# Portal content editor (`features/admin/portal-content`)

This folder is part of the `features/admin` module (WebAdmin), which is documented in [MODULE.md](../MODULE.md). It is the
admin edit screen of Module 15 for the public FAQ, service guide, privacy policy and terms of use. It edits the bilingual
content behind `GET api/portal/content/{page}` through `GET` / `PUT api/admin/portal/content/{page}`.

## Purpose

| Screen | Route | Key | What it does |
| --- | --- | --- | --- |
| Portal content | `/admin/portal-content` | `portal.content.view` (writes `portal.content.manage`) | `dga-tabs` page selector (faq, service-guide, privacy, terms). The page's sections in order are shown on the admin-list pattern. The section dialog holds `titleAr`, `titleEn`, `bodyAr`, `bodyEn` as plain text, with paragraphs split by a blank line. Admins can add, edit, remove and move up / down. **Save** sends one `PUT` that replaces the page's sections in list order (sortOrder 1..n). **Discard** drops the draft. A viewer sees everything read-only |

Edits stay a local draft until the admin saves them. The draft is guarded in three places: switching tabs asks first
(choosing "stay" keeps the tab), leaving the route uses `unsavedChangesGuard`, and closing the browser tab uses
`beforeunload`. The leave dialog and guard are reused from `access/common`.

| File | Role |
| --- | --- |
| `data/portal-content.models.ts` | `PortalContentPages` + `PORTAL_CONTENT_PAGES` + label keys, the wire shapes, `SectionFields`, and `PortalContentLimits` (the client copy of the backend limits) |
| `data/portal-content-api.ts` | `PortalContentAdminClient` (`get`, `replace`) over `ApiService`, with quiet calls |
| `section-draft.ts` | Pure draft rules: `draftsOf`, `replaceBodyOf`, `sameSections` (the dirty check), `moveSection`, `removeSection`, `upsertSection`, `trimmedTexts`, `sectionErrors` (required + max lengths as `errors.REQUIRED` / `errors.TOO_LONG`), and `sectionErrorIndexes` (which rows a refused save named in `sections[i].*`) |
| `section-form.ts` | The dialog's fields as signals |
| `portal-content-editor.store.ts` | Load (keeps only the latest answer), `selectPage` with discard confirmation, dialog, edits, save, discard |
| `portal-content-page.component.*` | Renders the store; `LeaveAware` |
| `portal-content.paths.ts`, `portal-content.routes.ts`, `portal-content-permissions.ts` | Mount segment, `PORTAL_CONTENT_ROUTES` (spread into `ADMIN_ROUTES`), and feature-local keys |

## Use

```ts
// admin.routes.ts
...PORTAL_CONTENT_ROUTES,
```

## Extend

- **Another editable page** needs a member in `PortalContentPages` (its value is the API `{page}` segment), its label
  key in `PortalContentPageLabelKeys` and in both bundles, and the backend page.
- **A changed limit** must change the backend validator and `PortalContentLimits` together. The server stays the
  authority.

## Rules

- The client checks only what saves a round trip (required, max lengths, max section count). The server validates
  again, and its field errors mark the matching rows.
- The body is plain text. The portal splits paragraphs on blank lines, so no HTML or Markdown is ever rendered.

## Gotchas

- `PortalContentLimits` (title 200, body 4000, 50 sections) has to match be-portal's validator. Check it whenever the
  backend lands.
- The list order is the sort order. The dialog does not edit `sortOrder` by hand; the move buttons do.
- `PortalContentPermissionKeys` is feature-local until `PermissionKeys` is regenerated (`PortalContentView` /
  `PortalContentManage`).
