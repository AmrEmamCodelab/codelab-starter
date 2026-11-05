# Access administration screens (`features/admin/access`)

Part of the `features/admin` module (WebAdmin; its [MODULE.md](../MODULE.md) is the module's doc). This folder holds
the roles and permissions screens and the per-user grant editor, built on the page patterns
([patterns/MODULE.md](../../../patterns/MODULE.md)) over the Access module's admin endpoints
([backend Access MODULE.md](../../../../../../../backend/src/Modules/Access/MODULE.md)).

## Purpose

| Screen | Route (under `/admin/access`) | Key | Pattern |
| --- | --- | --- | --- |
| Roles list: name in the current language, code, automatic (virtual) or assigned, status, permission count; create (code, English and Arabic names), deactivate and reactivate | `roles` | `access.roles.view` (actions `access.roles.manage`) | admin list + editor dialog |
| Role editor: names and the permission tree (catalog keys by dotted segment); retired keys shown disabled; virtual roles marked automatic; dirty tracking and a leave guard | `roles/:roleId` | `access.roles.view` (save `access.roles.manage`) | tree editor |
| User grants: the user, assigned roles each with a scope, direct permissions (tree) each with a scope, the virtual roles that apply, the effective permissions with their sources | `users/:userId/grants` | `access.grants.view` (save `access.grants.manage`) | tree editor |

A grant or role change is live on the target user's next request: the backend resolves permissions per request and
never bumps the session, and the frontend's permission sync (focus, navigation, interval) repaints the menu without a
relogin; a revoked route answers 403. After its own writes the screen also refreshes the viewer's keys at once.

## Use

- Mount: `ADMIN_ROUTES` (in `../admin.routes.ts`) adds
  `{ path: ACCESS_MOUNT_SEGMENT, loadChildren: () => import('./access/access.routes').then(m => m.ACCESS_ROUTES) }`
  under the `access.admin.view` gate. `ACCESS_MOUNT_SEGMENT` is `'access'`; `AdminPaths.Access` should reuse it.
- Links: `AccessPaths.roles()`, `AccessPaths.roleDetail(roleId)`, `AccessPaths.userGrants(userId)` return absolute
  router commands. The users list links to a user's grants with its row id (the Access and Users modules share the
  `users` PublicId label): `[routerLink]="AccessPaths.userGrants(user.id)"`, gated by
  `*hasPermission="PermissionKeys.AccessGrantsView"`.
- HTTP: `AccessAdminClient` (`data/access-api.ts`) over `ApiService`, typed with the generated openapi schemas
  (`data/access.models.ts`). Reads keep the default interceptors (403 page, error dialog); writes are quiet so the
  page shows the refusal inline.

| File | Role |
| --- | --- |
| `access.paths.ts`, `access.routes.ts` | Route segments and params, `AccessPaths` links, `ACCESS_ROUTES` (each route `data.permission` + `permissionGuard()`) |
| `data/` | Generated type aliases, the int enums (`PermissionScope`, `PermissionSourceKind`, `VirtualRoleKind`, `AccessUserKind`), error and warning codes, the client |
| `common/` | `apiErrorFrom`, label helpers (`localizedName`, `permissionLabelResolver`, scope/source/kind label keys), `LeaveConfirmation`, `unsavedChangesGuard`, the last-manager and leave dialogs, the warnings notice, shared page styles |
| `roles/` | `role-list.ts` + `RoleListStore` + list page; `role-draft.ts` + `RoleEditorStore` + editor page |
| `grants/` | `grants-draft.ts` + `UserGrantsStore` + grants page |
| `access.testing.ts`, `access-pages.testing.ts` | Spec-only fixtures, fakes and the router harness helper |

## Extend

- **A new permission key** (`/new-permission`, recipe `docs/recipes/add-permission.md`): after DocsGen and
  `api:gen`, add `permissions.<key>` to both bundles (flat key inside the `permissions` object); a new top segment or
  branch also gets `admin.access.permission-groups.<branch>`. Without a label the tree shows the catalog's English
  description (keys) or the last segment (groups), so a gap is visible, not blank.
- **A new Access error code** the screens can receive: add it to `AccessErrorCodes` (or `AccessWarningCodes`) and
  `errors.<CODE>` to both bundles.
- **A new virtual role kind**: a member with the backend's value in `VirtualRoleKind` and its label key in
  `access-labels.ts` (`admin.access.virtual-kind.*`).
- Logic goes in the stores and the pure `*-draft.ts` / `role-list.ts` helpers, each with specs; pages only bind.

## Rules

- HTTP only through `AccessAdminClient` (ApiService); paths from `AccessApiPaths`; ids narrowed with `tryAsId`.
- Every route carries `data.permission`; every action button carries `*hasPermission` (the save bars, create and
  toggle) or `data-public-action`. The server authorizes every call regardless.
- No raw text: `admin.access.*`, `permissions.*` and `errors.<CODE>` keys in `ar` and `en`. Tokens and logical CSS.
- Enums are ints mirroring the backend values; labels come from i18n, never from member names.

## Gotchas

- The tree pattern has no per-key disabled state, so retired keys the role still lists are shown in a second,
  read-only tree; a save drops them (the server refuses a retired key with `ACCESS_UNKNOWN_PERMISSION`). They never
  make the draft dirty.
- `ACCESS_LAST_MANAGER` (409) opens a confirmation; confirming resends with `confirmBreakGlassOnly: true`, and the
  success carries the warning `ACCESS_ONLY_BREAK_GLASS_MANAGES`, shown as a notice.
- The grants page loads the roles list and the catalog only for holders of `access.roles.view`; without it the role
  choices are the user's current roles and the direct-permission tree uses the client's generated `PermissionKeys`
  plus the keys the user holds.
- A newly ticked role or direct permission starts at `DEFAULT_GRANT_SCOPE` (Global); narrow it per row before saving.
  Virtual roles always grant at own-organization scope (backend rule).
- The break-glass administrator is shown read-only (the server refuses `ACCESS_BREAK_GLASS_NOT_EDITABLE`).
- The leave guard covers router navigation (`canDeactivate`) and tab close (`beforeunload`, browser text).
