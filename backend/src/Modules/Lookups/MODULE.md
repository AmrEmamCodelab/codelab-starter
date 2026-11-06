# Lookups module

## Purpose

Lookups and reference data (module 4): the bilingual lists behind dropdowns (contact topics, categories, document
kinds). The Lookups module owns:

- **The lookup tables** in schema `lookups`, one generic pair for every type:
  - `lookups.LookupTypes`: `Key` (kebab-case, the primary key, a `Starter.Domain.Lookups.LookupTypes` constant),
    `NameAr`, `NameEn`.
  - `lookups.LookupItems`: key (`TypeKey`, `Id`), where `Id` is a fixed integer (never generated) from the type's
    `<Name>Ids` constants class in `Starter.Domain/Lookups`; `Code` (kebab-case, unique per type), `NameAr`, `NameEn`,
    `SortOrder`, `IsActive`. A Restrict foreign key ties each value to its type. `LookupItem` derives from the kernel
    `Starter.Domain.Lookups.LookupEntity`.
- **Seeding by migrations only.** Every type and value is written by a migration (`LookupSeed.InsertType` /
  `InsertItems`; `tools/scaffold/new-lookup.mjs` writes it). The model carries no seed data.
- **A cached read API** for signed-in users, and **the admin view** with activate/deactivate. Names, codes and order
  change only by migration.

**Design choice: one generic pair of tables, not a table per type.** The admin screen and the read API are generic
(a type is a route segment, not a C# type), so a new list is one migration and one constants class, with no new
entity, DbSet, configuration, DTO or endpoint. Modules never share foreign keys across schemas, so a table per type
would buy no referential integrity. A list that needs more columns (a badge colour, a parent) is a module-owned table
in that module's schema whose entity derives from `LookupEntity`.

Seeded types:

| Type | Ids | Values |
| --- | --- | --- |
| `contact-topics` | `ContactTopicIds` | `general` 1, `request` 2, `account` 3, `suggestion` 4 (migration `AddContactTopicsLookup`) |

## Use

Permissions (`LookupsPermissionKeys`, declared by `LookupsPermissionCatalogSource`): `lookups.view`, `lookups.manage`.
Value ids on the wire are PublicIds of the family `lookup-items` (`LookupItemIds`), one family for every type (an id is
unique within its type and every route names the type). Errors are problem+json with a `code`.

| Endpoint | Access | Body / answer |
| --- | --- | --- |
| `GET /api/lookups/{type}` | `[AuthenticatedUser]` (allowlisted) | `[{id, code, nameAr, nameEn, sortOrder}]`, active values only, ordered by sort order then id |
| `GET /api/admin/lookups` | `lookups.view` | `[{type, nameAr, nameEn, itemCount}]` ordered by type (`itemCount` counts inactive values too) |
| `GET /api/admin/lookups/{type}` | `lookups.view` | `[{id, code, nameAr, nameEn, sortOrder, isActive}]` |
| `PUT /api/admin/lookups/{type}/items/{id}/active` | `lookups.manage` | `{isActive: boolean}` -> 204; 400 when `isActive` is missing |

Other modules read a value through the kernel port `ILookupValues` (`Starter.Application.Lookups`), implemented by
`ActiveLookupValues` over the same cached active list as `GET /api/lookups/{type}`; they never read the `lookups`
tables or reference this project (the Portal contact topics are the first consumer).

An unknown or malformed `{type}` answers 404 `LOOKUPS_TYPE_NOT_FOUND` (a malformed key without a query); an unknown
value 404 `LOOKUPS_ITEM_NOT_FOUND`. Both names always travel; the client picks the one for its language.

In code, compare lookup ids only through the constants: `ContactTopicIds.Request`, never `2`. A module that stores a
value keeps the int id in its own table and reads a wire token with its own `IIdDomain` whose label is `lookup-items`.

Caching: `GetActiveLookupValues` serves each type's active list from `ILookupCache` (`MemoryLookupCache` on the host's
`IMemoryCache`, key `lookups:active:<type>`) for `LookupsLimits.CacheTtl` (60 seconds, a constant; no configuration
key). An activation change drops that type's entry on the node that made it; other nodes see it within the TTL. The
admin reads are never cached.

## Extend

- **New lookup type:** `node tools/scaffold/new-lookup.mjs --type <kebab> --name <Pascal> --codes <a,b> --name-ar <text>
  --names-ar <a;b>` (recipe [docs/recipes/add-lookup.md](../../../../docs/recipes/add-lookup.md), skill `new-lookup`).
  Add a row to the seeded-types table above.
- **New values, renames, reorders:** a new hand-written migration next to the others, shaped like the generated one
  (`LookupSeed.InsertItems` / `DeleteItems`, or `UpdateData`), and new constants in the `<Name>Ids` class. Never edit
  a shipped seed migration; never change an id or a code.
- **Schema change:** a new migration reshaped like `InitialCreate`, with `LookupsMigrationModel` updated (seed
  migrations share it as their target model; a schema migration freezes its own copy first). `LookupsModelTests`
  proves the context has no pending change.
- **New endpoint or permission:** `new-endpoint` / `new-permission` with `--module Lookups`.

## Rules

- **Seeded only by migrations** (CLAUDE.md rule 10, `lookups.seeded-by-migrations`). At run time an administrator
  may only activate or deactivate a value; there is no create, rename or delete endpoint. The model never declares seed
  data (`lookups.no-has-data`).
- **Ids from constants** (`lookups.ids-from-constants`): the `<Name>Ids` values mirror the migration exactly
  (`SeedMigrationTests` proves it for `ContactTopicIds`); never a bare number, never an enum that mirrors seeded ids.
- **Retire, do not delete.** A deactivated value is no longer offered, but stored records that hold its id stay valid;
  a consumer refuses it only for new input. The Restrict foreign key refuses deleting a type that has values.
- **Cache freshness** (`lookups.cache-invalidated`): every activation change invalidates the type after the save; no
  entry outlives `LookupsLimits.CacheTtl`.
- Every seeded value has an Arabic and an English name (`SeedMigrationTests`).
- Never reference another module; enums are integers with explicit values; keys, routes and limits are constants.

## Gotchas

- **The read API needs a session.** `GET /api/lookups/{type}` is `[AuthenticatedUser]`, so a public (anonymous) page
  cannot call it; a public form that needs a list (the portal contact form's topics) gets it from its own anonymous
  endpoint or keeps a client copy of the ids.
- **Other nodes are stale for up to 60 seconds** after a deactivation (only the node that made the change drops its
  entry). Do not shorten the window by caching elsewhere.
- The PublicId family is shared by every type: the token of id 1 is the same for every type, so the type in the route
  is what makes it unique. Never accept a value id without its type.
- A `{type}` route value is not constrained by the route; the use case checks its shape (`LookupTypeKeys.IsWellFormed`)
  and answers 404, so the route table and the endpoint allowlist key stay `GET /api/lookups/{type}`.
- Seed migrations compute their timestamp from the newest Lookups migration (one minute later), not from the clock, so
  `new-lookup` is deterministic; two branches that each add a lookup get distinct class names and merge cleanly.
- Migrations run on start only in Development (`Database:ApplyMigrationsOnStartup`); every other environment uses the
  DBA scripts.
- `CLAUDE.md`, `BannedSymbols.generated.txt`, `.editorconfig` and `CodeMetricsConfig.txt` here are generated by the
  rules engine; never edit them by hand. Keep the `// scaffold:...` markers.
