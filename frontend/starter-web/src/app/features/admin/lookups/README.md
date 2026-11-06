# Lookups administration screens (`features/admin/lookups`)

This folder is part of the `features/admin` module (WebAdmin), which is documented in [MODULE.md](../MODULE.md). It holds the
admin screens of Module 4 (Lookups) on the admin-list and editor-dialog patterns. The backend contract is
`GET api/admin/lookups`, `GET api/admin/lookups/{type}` and `PUT api/admin/lookups/{type}/items/{id}/active`.

## Purpose

| Screen | Route (under `/admin`) | Key | What it does |
| --- | --- | --- | --- |
| Lookup types | `lookups` | `lookups.view` | Every type with its name in the current language, its key and its value count. It has a client-side search, and each name links to the type's values |
| Values of one type | `lookups/:type` | `lookups.view` (action `lookups.manage`) | Code, Arabic and English names, sort order and an active / deactivated tag, ordered by sort order and then code. Activate or deactivate asks for confirmation (`PUT …/active`) and then reloads. An unknown type shows the 404 as the list error |

Both screens carry a notice: values are added, renamed or removed **only through a migration** (the `/new-lookup`
skill, rule 10). This page only switches `isActive`.

| File | Role |
| --- | --- |
| `lookups.paths.ts`, `lookups.routes.ts` | `LOOKUPS_MOUNT_SEGMENT`, `LookupsRouteSegments`, `LookupsRouteParams`, `LookupsPaths` (absolute links), and `LOOKUPS_ROUTES` (spread into `ADMIN_ROUTES`; each route has `data.permission` + `permissionGuard()`) |
| `lookups-permissions.ts` | `LookupsPermissionKeys` (feature-local until `PermissionKeys` is regenerated; see Gotchas) |
| `data/lookup.models.ts` | `LookupType`, `AdminLookupItem`, `LookupItemId` (the seeded integer), `localizedName()` |
| `data/lookups-api.ts` | `LookupsAdminClient` over `ApiService` (quiet calls), `LookupApiPaths` |
| `data/whole-list.ts` | `WholeListState`: rows / loading / error of an unpaged list; it keeps only the latest answer, and a failure clears the rows |
| `lookup-list.ts` | Search (every word, case-insensitive), order (sort order, then code) and client paging (`pageOf`) |
| `lookup-types-page.*`, `lookup-items-page.*` | Store per page + component |
| `lookups.testing.ts` | Spec-only fixtures |

## Use

```ts
// admin.routes.ts
...LOOKUPS_ROUTES,
// a link
<a [routerLink]="LookupsPaths.items('contact-topics')">…</a>
```

## Extend

- **A new lookup type** needs no change here. Its migration seeds it, and it shows in the list once the API lists it.
- **A new text** is a key under `admin.lookups.*` in both `ar.json` and `en.json`. `lookups-translations.spec.ts`
  scans this folder's sources and fails until both bundles have it.

## Rules

- These screens hold no create, rename, delete or reorder actions. Those are migrations.
- Search, order and paging happen on the client because the endpoints answer whole lists. Server paging is needed only if
  one type grows past a few hundred values.

## Gotchas

- `LookupsPermissionKeys` casts its strings to `PermissionKey` only until `npm run api:gen` emits
  `PermissionKeys.LookupsView` / `LookupsManage`. Then swap the uses and delete the file.
- Lookup ids on the wire are the seeded integers, not PublicIds. They are reference data that clients name by constant
  (for example `ContactTopicIds` in the portal contact form).
- The values page also loads the types list, but only to title the page with the type's names. Until that list arrives,
  the title shows the type key.
