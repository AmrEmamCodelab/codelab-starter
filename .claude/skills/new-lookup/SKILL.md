---
name: new-lookup
description: "Add a bilingual reference-data list (a lookup type and its values) seeded by a migration: the <Name>Ids constants class, the LookupTypes key and the seed migration with Arabic and English names, served by the cached GET /api/lookups/{type} and the admin lookups screen. Use when the work needs a new dropdown source, category list, topic list, type list or any other reference data with Arabic and English labels."
---

# New lookup

Load the `coding-standards` skill first ([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)). The full
recipe is [docs/recipes/add-lookup.md](../../../docs/recipes/add-lookup.md); the module is
[backend/src/Modules/Lookups/MODULE.md](../../../backend/src/Modules/Lookups/MODULE.md).

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
10, 20, ...). The Arabic names are required; the English ones default to the code in words. Running it again is a
no-op (exit 0); a bad name, a duplicate code or a partial earlier run is refused (exit 1, nothing written). No
permission or i18n key is needed: `lookups.view` / `lookups.manage` cover every type and the names are data.

## Workflow

1. Check it is a lookup (recipe step 1): reference data an administrator may retire. A closed set the code branches on
   is an int enum; a status is a status flow; a list with extra columns is a module table deriving `LookupEntity`.
2. Ask for (or propose) the Arabic and English names of the type and of every value; never invent Arabic text the
   user has not confirmed.
3. Run the command. Read the three files it prints.
4. Use the values: the client reads `GET /api/lookups/<type>`; backend code compares ids only through
   `<Name>Ids.<Code>`, never a bare number; a consuming module stores the int id and reads wire tokens with its own
   `IIdDomain` labelled `lookup-items`.
5. Later changes (more values, renames) are new hand-written migrations (recipe step 5); never edit a shipped seed
   migration, never change an id or a code.
6. Add the type to the seeded-types table in the Lookups `MODULE.md`, and the consumer's `MODULE.md` (Use).

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
