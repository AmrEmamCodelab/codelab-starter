# Recipe: add a lookup

Adds a bilingual reference-data list (a lookup type and its values) to module 4, Lookups. Lookups are seeded **only
by migrations** (CLAUDE.md rule 10): the generator writes the migration, the ids constants and the type constant; at
run time an administrator can only activate or deactivate a value. The `new-lookup` skill
(`.claude/skills/new-lookup/SKILL.md`) runs this recipe. Module docs:
[backend/src/Modules/Lookups/MODULE.md](../../backend/src/Modules/Lookups/MODULE.md).

## 1. Decide it is a lookup

| The list is... | Use |
| --- | --- |
| Reference data an administrator may retire, shown in a dropdown, stored by id in records (topics, categories, document types) | a **lookup** (this recipe) |
| A closed set the code branches on (a job-run state, a notification channel) | an **int enum** with explicit values and i18n labels (coding standard 13) |
| A status of a workflow | a status flow (`new-transition`, phase 7) |
| A list that needs extra columns (a badge colour, a parent) | a module-owned table whose entity derives from `Starter.Domain.Lookups.LookupEntity`, in that module's schema |

Never add an enum that mirrors seeded lookup ids, and never compare a translated label.

## 2. Choose the names

| Input | Example | Becomes |
| --- | --- | --- |
| `--type` (kebab, at most 64) | `document-kinds` | the key `LookupTypes.DocumentKinds = "document-kinds"` and the route `GET /api/lookups/document-kinds` |
| `--name` (PascalCase, singular) | `DocumentKind` | the constants class `Starter.Domain.Lookups.DocumentKindIds` |
| `--codes` (kebab, comma-separated, at most 100) | `passport,national-card` | the constants `DocumentKindIds.Passport = 1`, `DocumentKindIds.NationalCard = 2` (ids in list order) |
| `--name-ar` (required), `--name-en` | the type's names | the admin screen's type list |
| `--names-ar` (required), `--names-en` | one name per code, separated by `;` | the values' names; English defaults to the code in words |

Sort orders are 10, 20, 30, ... in list order, so a later value can go between two.

## 3. Generate

Run from the repo root, with the private Node on `PATH`:

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
node tools/scaffold/new-lookup.mjs --type document-kinds --name DocumentKind --codes passport,national-card `
  --name-ar "أنواع الوثائق" --name-en "Document kinds" `
  --names-ar "جواز سفر;بطاقة وطنية" --names-en "Passport;National card"
```

It writes three files and prints them:

- `backend/src/Starter.Domain/Lookups/LookupTypes.cs`: the new type constant (above `// scaffold:lookup-types`);
- `backend/src/Starter.Domain/Lookups/DocumentKindIds.cs`: one `const int` per code;
- `backend/src/Modules/Lookups/Infrastructure/Persistence/Migrations/<timestamp>_AddDocumentKindsLookup.cs`: the seed
  migration (`LookupSeed.InsertType` up, `LookupSeed.DeleteType` down). Its timestamp is one minute after the newest
  Lookups migration, so the output is deterministic.

Running it again with the same type is a no-op (`unchanged`, exit 0). An invalid name, a duplicate code, a missing
Arabic name, a type key held by another constant or a half-finished earlier run is refused (exit 1) and nothing is
written. No permission and no i18n key is needed: `lookups.view` and `lookups.manage` cover every type, and the names
are data.

## 4. Use the values

- Read the active values with `GET /api/lookups/<type>` (any signed-in user; `[{id, code, nameAr, nameEn, sortOrder}]`,
  the client picks the name for the language). Ids are PublicId tokens of the family `lookup-items`.
- Compare and assign ids only through the constants: `if (topicId == ContactTopicIds.Request)`, never `== 2`.
- A module that stores a value keeps the int id in its own table (no foreign key across schemas) and reads a token
  with its own `IIdDomain` whose label is `lookup-items`.
- A deactivated value stays valid for stored records; refuse it only for new input.

## 5. Change a lookup later

Never edit a seed migration that has shipped. Write a new migration in the same folder, reshaped like the generated
one (`[DbContext(typeof(LookupsDbContext))]`, `[Migration(MigrationId)]`, `BuildTargetModel` →
`LookupsMigrationModel.Build`):

- **Add values:** `LookupSeed.InsertItems` up, `LookupSeed.DeleteItems` down, and the new constants in `<Name>Ids` with
  the next ids.
- **Rename or reorder:** `migrationBuilder.UpdateData(...)` on `lookups.LookupItems`; ids and codes never change.
- **Retire a value:** let an administrator deactivate it (`PUT /api/admin/lookups/<type>/items/<id>/active`); do not
  delete rows that records may hold.

## 6. Tests to add

- The Lookups module tests already prove that every seed migration's rows match its `<Name>Ids` constants
  (`SeedMigrationTests`); a new generated migration is picked up by reflection.
- In the consuming module: a unit test for every rule that compares a lookup id, written with the constants.
- Update `MODULE.md` of the consuming module (Use: which lookup it reads) and the Lookups `MODULE.md` seeded-types
  table.

## 7. Verify

```powershell
$dotnet = "E:\codelab-tools\dotnet10\dotnet.exe"
& $dotnet test backend/tests/Modules/Starter.Modules.Lookups.Tests
& $dotnet test backend/tests/Starter.Architecture.Tests
node --test "tools/scaffold/tests/**/*.test.mjs"
```

In Development the migration runs on start; every other environment gets it through the DBA scripts. The full gate
is `tools/build.ps1 -Target CI`; work is done when it is green.
