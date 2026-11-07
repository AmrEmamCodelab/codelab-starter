---
name: new-lookup
description: "Use when the work needs a new dropdown source, category list, topic list, type list or any other reference data with Arabic and English labels that an administrator may retire, and whenever a hook or CI refusal says 'Use the `new-lookup` skill' (lookup rows seeded outside a migration). Adds the bilingual list in one run, seeded only by a migration: the <Name>Ids constants class, the LookupTypes key, the seed migration with Arabic and English names, its seed unit test, the model test's migration list, the seeded-types row in the Lookups MODULE.md and the add-lookup recipe in its module.rules.yaml; served by the cached GET /api/lookups/{type} and the admin lookups screen."
---

# New lookup

Load the `coding-standards` skill first ([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)). The full
recipe is [docs/recipes/add-lookup.md](../../../docs/recipes/add-lookup.md); the module is
[backend/src/Modules/Lookups/MODULE.md](../../../backend/src/Modules/Lookups/MODULE.md).

## When this applies

- The scenario needs a list of values users pick from, stored by id (topics, categories, document kinds). Not a
  closed set the code branches on (an int enum), not a status (`new-transition`), not a list with extra columns
  (a module table deriving `LookupEntity`): recipe step 1 decides.
- **Refusal counterpart:** lookups are seeded only by migrations (CLAUDE.md rule 10). The PreToolUse hook and the CI
  content check (`node tools/rules-engine/check-content.mjs`) refuse lookup rows written any other way (`HasData`,
  an SQL script, a startup seeder) with the message "Use the `new-lookup` skill". Rules
  `base.lookups-via-migrations` (`rules/base.rules.yaml`), `lookups.seeded-by-migrations`, `lookups.no-has-data`
  and `lookups.ids-from-constants` (`backend/src/Modules/Lookups/module.rules.yaml`).

## What one run emits

| Part | Emitted |
| --- | --- |
| Code | yes: `LookupTypes.<Type>`, `Starter.Domain.Lookups.<Name>Ids` and the seed migration `Add<Type>Lookup` (rows through `LookupSeed`; never `HasData`, SQL or a seeder) |
| Rules | yes: `docs/recipes/add-lookup.md` kept in `backend/src/Modules/Lookups/module.rules.yaml` `recipes` (already there in the starter, so normally unchanged) |
| MODULE.md | yes: the row of the seeded-types table in `backend/src/Modules/Lookups/MODULE.md` |
| Unit tests | yes: `Starter.Modules.Lookups.Tests/Infrastructure/Seeds/Add<Type>LookupTests.cs` (seed rows equal the ids constants, one case per code, the type key) and the new migration appended to the exact migration list of `LookupsModelTests` |
| Generated docs | nothing in `docs/generated` changes (no permission, endpoint, status or configuration key); the run prints `next:` lines: the rules compile when the rules file changed, then the Lookups tests |

A tree whose model test, seeded-types table or rules file has another shape gets a `note:` naming the line to add by hand.

## Quick start

From the repo root, with the private Node on `PATH`:

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
node tools/scaffold/new-lookup.mjs --type document-kinds --name DocumentKind --codes passport,national-card `
  --name-ar "أنواع الوثائق" --name-en "Document kinds" `
  --names-ar "جواز سفر;بطاقة وطنية" --names-en "Passport;National card"
```

It writes the type constant (`LookupTypes.DocumentKinds = "document-kinds"`), the ids class
(`Starter.Domain.Lookups.DocumentKindIds`, ids 1, 2, ... in `--codes` order) and the seed migration
(`backend/src/Modules/Lookups/Infrastructure/Persistence/Migrations/<timestamp>_AddDocumentKindsLookup.cs`, sort orders
10, 20, ...), plus the seed test, the model-test list entry and the MODULE.md row (table above). Add `--dry-run`
first to list them without writing. The Arabic names are required; the English ones default to the code in words. Running it again is a
no-op (exit 0); a bad name, a duplicate code or a partial earlier run is refused (exit 1, nothing written). No
permission or i18n key is needed: `lookups.view` / `lookups.manage` cover every type and the names are data.

## Workflow

1. Check it is a lookup (recipe step 1): reference data an administrator may retire. A closed set the code branches on
   is an int enum; a status is a status flow; a list with extra columns is a module table deriving `LookupEntity`.
2. Ask for (or propose) the Arabic and English names of the type and of every value; never invent Arabic text the
   user has not confirmed.
3. Run the command. Read every file it prints (the three code files, the seed test, the model test, the MODULE.md).
4. Use the values: the client reads `GET /api/lookups/<type>`; backend code compares ids only through
   `<Name>Ids.<Code>`, never a bare number; a consuming module stores the int id and reads wire tokens with its own
   `IIdDomain` labelled `lookup-items`.
5. Later changes (more values, renames) are new hand-written migrations (recipe step 5); never edit a shipped seed
   migration, never change an id or a code.
6. The run added the row of the seeded-types table in the Lookups `MODULE.md`; describe the type in the consumer's
   `MODULE.md` (Use) by hand.

## Verify

```powershell
$dotnet = "E:\codelab-tools\dotnet10\dotnet.exe"
& $dotnet test backend/tests/Modules/Starter.Modules.Lookups.Tests   # model, seed migrations vs ids constants
& $dotnet test backend/tests/Starter.Architecture.Tests
node --test "tools/scaffold/tests/**/*.test.mjs"
```

## More

Generator flags and exit codes: [tools/scaffold/README.md](../../../tools/scaffold/README.md). Rules:
`lookups.seeded-by-migrations`, `lookups.ids-from-constants`, `lookups.no-has-data` in
`backend/src/Modules/Lookups/module.rules.yaml`; coding standard 12 (named lookup values).
