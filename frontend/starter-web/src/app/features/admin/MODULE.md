# Web administration (`features/admin`)

## Purpose

The administration area under `/admin`: the admin home, user accounts, organizations, the Excel user import,
the access screens (roles and per-user grants), the lookups screens, the portal content editor, the runtime settings
screen and the background jobs screens. `app.routes.ts` mounts `ADMIN_ROUTES` (`admin.routes.ts`) with
`loadChildren`, open at the mount to a holder of any key in `ADMIN_AREA_PERMISSIONS` (`access.admin.view` or any
page key below), so a role granted only `users.view` reaches the users page without `access.admin.view`; every page
is gated again by its own key.

| Area | Route (`AdminPaths` / `AccessPaths`) | Page key | Folder |
| --- | --- | --- | --- |
| Admin home: a tile per area, each under `*hasPermission` | `/admin` | any key in `ADMIN_AREA_PERMISSIONS` (`access.admin.view` or a tile's key) | `admin-page/` |
| Users: list (search; organization, kind and status filters; status tags), create / edit dialog, deactivate / reactivate / unlock / new activation link (confirmed), link to the user's grants | `/admin/users` | `users.view` (writes `users.manage`) | `users/` |
| Organizations: list (kind and status filters), create / edit dialog with the active toggle, deactivate / reactivate (confirmed) | `/admin/organizations` | `organizations.view` (writes `organizations.manage`) | `organizations/` |
| Excel import: pick one .xlsx (the shared `app-file-picker`), preview every row with its reasons, commit the valid rows, hand over the activation links | `/admin/import` | `users.import` | `import/` |
| Access (owned by `features/admin/access`, see its own docs): roles list and editor, a user's grants | `/admin/access/roles`, `/admin/access/roles/:roleId`, `/admin/access/users/:userId/grants` | `access.roles.view`, `access.grants.view` | `access/` |
| Lookups: types list (name, key, value count) -> values of a type (code, ar/en names, sort order, active tag) with a confirmed activate / deactivate; structure changes only by migration (/new-lookup) | /admin/lookups, /admin/lookups/:type | lookups.view (writes lookups.manage) | lookups/ ([README](lookups/README.md)) |
| Portal content: page tabs (FAQ, service guide, privacy, terms), ordered sections, section dialog (ar/en title and plain body), add / edit / remove / reorder, save = one PUT replacing the page; unsaved-changes guard | /admin/portal-content | portal.content.view (writes portal.content.manage) | portal-content/ ([README](portal-content/README.md)) |
| Runtime settings: every setting grouped (group, key) with group filter and search, value by type, description in the current language, last change; type-aware edit dialog (text, whole number, on/off, seconds) with "Use the default" | /admin/settings | settings.view (edit settings.manage) | settings/ ([README](settings/README.md)) |
| Background jobs: catalog with schedule and last-run status tag, confirmed "Run now" (runs as SYSTEM) that opens the run history; run history (status, trigger, actor, triggered by, started/finished, reference code), auto-refresh while a run is unfinished | /admin/jobs, /admin/jobs/:jobKey/runs | jobs.view (run jobs.run) | jobs/ ([README](jobs/README.md)) |

Screens are built on the page patterns (`app-admin-list-pattern`, `app-editor-dialog-pattern`) and `@codelab/ng-dga`.
Each screen has a store provided per page (state, rules, API calls) and a component that renders it.

| Piece | What it does | File |
| --- | --- | --- |
| `UsersAdminClient`, `OrganizationsAdminClient`, `UserImportClient` | Thin wrappers over `ApiService` with their own path constants; every call is quiet (the screens show refusals inline) | `*/data/*-api.ts` |
| `UsersPageStore`, `OrganizationsPageStore`, `UserImportStore` | The list query (reloads on change, back to page 1 on a filter), the dialogs, the confirmed actions | `*/*.store.ts` |
| `UserForm`, `OrganizationForm` | The editor fields as signals and the request bodies (trimmed, blank optional text as null) | `users/user-form.ts`, `organizations/organization-form.ts` |
| `PagedListState`, `ActiveFilter` | Rows / total / loading / error of one paged list; only the latest request's answer is kept, and a failed load clears the rows (an unverifiable 403 becomes a `FORBIDDEN` error) | `organizations/data/paged-list.ts` |
| `apiErrorFrom`, `adminErrorMessageKey`, the error-code constants | problem+json parsing, the `errors.<CODE>` texts the area has | `organizations/data/admin-api-error.ts` |
| `app-activation-link` | A one-time activation link: read-only field, copy button, expiry in the current language | `users/activation-link/` |
| `ADMIN_ENTRIES`, `ADMIN_AREA_PERMISSIONS`, `adminEntryLink()` | The ways into the area: every admin key leads to the home (the shell's admin entry), which lists only the tiles the user holds, so a scoped admin without `access.admin.view` finds every page they hold; the keys that open the mount and the home | `admin-entry.ts` |
| `importFileProblem` + limits | The client check of a workbook (.xlsx, not empty, at most 5 MiB like the server) | `import/import-file.ts` |

## Use

```ts
// app.routes.ts (shell child):
{ path: RoutePaths.Admin, canActivate: [permissionGuard()], data: { [RouteDataKeys.Permission]: PermissionKeys.AccessAdminView },
  loadChildren: () => import('./features/admin/admin.routes').then(m => m.ADMIN_ROUTES) }

// Links: router commands, never literals.
<a [routerLink]="AdminPaths.users()">…</a>      <a [routerLink]="AccessPaths.userGrants(user.id)">…</a>
```

- The users list links each row to its grants page through `AccessPaths.userGrants(user.id)`: the Users and Access
  endpoints share the `users` PublicId label, so the list's id opens the grants page.
- Password-bound accounts never get a password from an admin: creating one, or "New activation link", shows a
  one-time link (`Users:Activation:LinkBaseUrl/activate#ticket=…`) to hand over. Directory (AD) and SSO accounts get
  none. The link is shown once and never stored.

## Extend

- **A new admin page:** a folder here with a store, a component on a pattern, a feature client in `data/`, specs for
  each; a route in `ADMIN_ROUTES` with `permissionGuard()` and `data: { [RouteDataKeys.Permission]: … }`, a path in
  `AdminRouteSegments` / `AdminPaths`, and a tile in `admin-page/admin-tiles.ts` gated by the page's key.
- **A new row action:** a `UserAction` member (explicit value), its texts in `USER_ACTION_TEXTS`, a branch in
  `UsersPageStore.perform`, a `userRowActions` rule, and a `*hasPermission`-gated button.
- **A new error code the area renders:** add it to the matching constants in `admin-api-error.ts` (mirroring the
  backend) and `errors.<CODE>` to both bundles; `admin-translations.spec.ts` fails until both have it.
- **A new text:** a key under `admin.users.*`, `admin.organizations.*`, `admin.import.*`, `admin.home.*`,
  `admin.common.*`, `admin.activation.*`, `admin.lookups.*`, `admin.portal-content.*`, `admin.settings.*` or
  `admin.jobs.*` in both `ar.json` and `en.json` (the translations spec scans the templates).

## Rules

- HTTP only through the feature clients (which wrap `ApiService`); paths from their `*ApiPaths` constants.
- Every route carries `data.permission`; every action button carries `*hasPermission="PermissionKeys.X"` (copying an
  activation link is display-only and is the one `data-public-action`).
- No business rules in components: they render the store and forward intent. The server owns validation; the client
  only checks what saves a round trip (the workbook's type and size) and offers what applies (row actions,
  organizations of the matching kind).
- Enums mirror the backend with explicit integer values (`UserKind`, `AccountBinding`, `OrganizationKind`); labels come
  from i18n, never from member names. No raw text; tokens and logical CSS (`organizations/_admin-controls.scss`).
- No national-ID fields, no migration from a legacy identity source.

## Gotchas

- settings/, jobs/, lookups/ and portal-content/ each keep a small `*PermissionKeys` const (View/Manage/Run) for their
  templates; its values are the generated `PermissionKeys` (no cast), so a renamed key breaks the build.
- The enum members are `InternalUser` / `InternalOrganization` (not a bare `Internal`): the scrub sweep reads a dotted
  name ending in "internal" as an internal host name, so translation keys avoid that ending too. Likewise the secret scan
  reads an enum member named after a password, assigned a value, and a JSON pair keyed by that word as secrets: the
  binding member carries an inline comment (as the backend's does) and the keys are `password-binding` and
  `awaiting-activation`.
- The patterns translate a refusal with ng-security's default code set, so a module code (`USERS_*`, `IDENTITY_*`) in
  a list or dialog alert shows the generic message with its reference code until the patterns accept a code set;
  the import page uses `adminErrorMessageKey` and shows the specific text.
- Organizations load (one page of up to 100) only for a holder of `organizations.view`; without it the users filter
  and the editor's picker are hidden and the server still refuses an external user without an organization.
- The import result lists show every row on one page; the preview pages on the client (`DEFAULT_PAGE_SIZE`).
- `ApiService` posts the workbook as `FormData`; ng-security leaves a multipart body's hash unsigned by design.
- Specs share fixtures and helpers in `organizations/admin.testing.ts` (spec-only); `settle()` runs a few
  tick / whenStable rounds so click -> async store call -> render chains complete.
- The activation link field shows a URL, so it keeps `dir="ltr"` marked with `data-fixed-direction` (the RTL lint allows a fixed direction only with that marker).
