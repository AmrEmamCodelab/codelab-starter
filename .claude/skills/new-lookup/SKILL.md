---
name: new-lookup
description: "Add a bilingual reference-data list seeded by a migration, with its ID constants class, read API with caching, admin screen entry and tests. Use when the work needs a new dropdown source, category list, type list or any other reference data with Arabic and English labels."
---

# New lookup

**Status: planned for phase 5.** This skill is a stub so the routing table in the root `CLAUDE.md` resolves. Its full workflow is written when phase 5 delivers what it depends on.

## What it will produce

The skill will produce the `LookupEntity` subtype, a seeding migration, the `<Name>Ids` constants, the cached read API and tests. Every scaffold emits the unit tests, the `module.rules.yaml` changes and the `MODULE.md` update together with the code.

## Until then

There is no lookups module in phase 0. Tell the user that lookup scaffolding arrives in phase 5.

Always load the `coding-standards` skill before any change.
