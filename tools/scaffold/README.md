# tools/scaffold

## Purpose

Deterministic Node generators for the backend module anatomy (ADR-004): a new module, a new endpoint, a new
permission, a new lookup (module 4) and a new status or transition (module 8). Every later module is created with
them. They are the only sanctioned path that edits the solution (`codelab-starter.slnx`) and the host project's
module list (`backend/src/Starter.Api/Starter.Api.csproj`). The scaffold skills (`.claude/skills/new-module`,
`new-endpoint`, `new-permission`, `new-lookup`, `new-transition`) and the fork-model test
(`tools/fork/Test-ForkModel.ps1`) call them.

Each run emits the code **together with** its rules change, its `MODULE.md` update, its unit tests and the
generated-docs refresh (run, or printed as `next:` lines):

| Generator | Code | module.rules.yaml | MODULE.md | Unit tests | Generated docs |
| --- | --- | --- | --- | --- | --- |
| `new-module` | module anatomy, slnx, host reference | created (rule ids prefixed with the lower-cased name) | created (5 sections) | test project + `<Name>ModuleTests` | rules compile, DocsGen |
| `new-endpoint` | controller, use case, validator, store, error code, registration | `add-endpoint` recipe (once) | Use note | `<Name>Tests.cs` | rules compile, DocsGen, api:gen |
| `new-permission` | key constant + catalog entry | `add-permission` recipe (once) | Use note (new key only) | covered by `<Module>ModuleTests` | rules compile, DocsGen, api:gen |
| `new-lookup` | `LookupTypes` key, `<Name>Ids`, seed migration | `add-lookup` recipe in Lookups (once) | seeded-types table row | `Seeds/Add<Type>LookupTests.cs` + model-test migration list | none change (printed note); `next:` rules compile when needed, Lookups tests |
| `new-transition` | statuses, actions, catalog rules, notification key, ar/en keys | `<module>.workflow-in-the-catalog` rule + recipe (once) | Use note per status/transition | `TransitionFixture` test classes | rules compile, DocsGen (workflow sheet, statuses.md) |

There is no `new-screen` generator: a page is one of eight patterns configured by judgment, and generated Angular
code could only be proved by a frontend build in a throwaway copy. The `new-screen` skill's steps are the checklist.

## Use

Run from the repo root with the private Node (`E:\codelab-tools\node24` first on `PATH`):

```powershell
node tools/scaffold/new-module.mjs --name <Pascal>
node tools/scaffold/new-endpoint.mjs --module <Name> --name <Pascal> --method <GET|POST|PUT|DELETE> --route <segment|.|{id}/<segment>> [--permission <kebab>]
node tools/scaffold/new-permission.mjs --module <Name> --action <kebab> [--description <text>] [--no-gen]
node tools/scaffold/new-lookup.mjs --type <kebab> --name <Pascal> --codes <a,b,...> --name-ar <text> --names-ar <a;b;...> [--name-en <text>] [--names-en <a;b;...>]
node tools/scaffold/new-transition.mjs --help
```

`new-endpoint --route`: kebab segment(s) under `/api/<module-kebab>/`, with the id added last for GET, PUT and DELETE;
`.` for the module root itself (`POST /api/<module>`; `/` and an empty value work too, but Git Bash rewrites `/` and
Windows PowerShell 5.1 drops `""`); or a route that places the id once for GET, PUT and DELETE (`{id}/draft`,
`widgets/{id}/archive`). A POST route never holds `{id}`. `new-transition --side system` writes tests that act from
the system side (`.OnSide(TransitionSide.System)`), and so does any side `TransitionFixture.Do` would not pick itself.

Every command also takes `--root <dir>` (default: the repo this script lives in) and `--dry-run` (print
`would create <path>` / `would change <path>` and the refresh commands, write nothing); the module, endpoint,
permission and transition generators take `--skip-generate` (skip the downstream generators and print them as
`next:` lines). Each prints `created <path>` / `changed <path>` for every file it wrote, `note:` lines for what to
do by hand (for example a hand-made module without `MODULE.md`), then one line per downstream generator (`ran ...`,
`skipped ...: reason` or `next: <command>`).

| Exit code | Meaning |
| --- | --- |
| 0 | Done, or already present (identical arguments again change nothing). |
| 1 | Refused; nothing was written. |
| 2 | Usage error, or the files were written but a downstream generator failed (the message names it). |

`new-permission` updates both permission catalogs: it writes the C# constant and the catalog entry, then the
downstream generators rebuild `docs/generated/permissions.*` and the frontend `PermissionKeys`; its last line names
the frontend constant (`frontend key: PermissionKeys.<Name> ("<key>")`). With `--no-gen` it runs no generator and
prints the exact two commands instead (`catalogCommands` in `lib/workspace.mjs`). Recipe:
[docs/recipes/add-permission.md](../../docs/recipes/add-permission.md).

`new-lookup` adds a lookup type to module 4 (recipe [docs/recipes/add-lookup.md](../../docs/recipes/add-lookup.md)): the
`LookupTypes` constant (above `// scaffold:lookup-types` in `backend/src/Starter.Domain/Lookups/LookupTypes.cs`), the
`<Name>Ids` constants class next to it (ids 1..n in `--codes` order) and the seed migration
`<timestamp>_Add<Type>Lookup.cs` in `backend/src/Modules/Lookups/Infrastructure/Persistence/Migrations` (rows through
`LookupSeed`, sort orders 10, 20, ...). Lookups are seeded only by that migration: it never writes `HasData`, an SQL
script or a seeder. It also writes the seed test, appends the migration to the exact list `LookupsModelTests`
expects (`LOOKUP_MIGRATION_LIST` in `lib/constants.mjs`) and adds the seeded-types row to the Lookups `MODULE.md`.
The timestamp is one minute after the newest migration in that folder, never the clock, so the output is
deterministic. A second run for a type whose constant, ids class and migration exist is `unchanged`; a partial
earlier run is refused.

`new-transition` edits only the record type's module and its test project (see `.claude/skills/new-transition/SKILL.md`).

Downstream generators, each only when it exists: `node tools/rules-engine/compile.mjs` (needs `node_modules` at the
root), `Starter.DocsGen --out docs/generated` (`backend/tools/Starter.DocsGen`), and for endpoints and permissions
`npm run api:gen --workspace frontend/starter-web` (needs `docs/generated/openapi.v1.json`).

Tests: `node --test "tools/scaffold/tests/**/*.test.mjs"`. The dogfood test (`tests/dogfood.test.mjs`) copies the
working tree into `%TEMP%`, runs `new-module DemoCases`, two endpoints, a permission, a lookup and two transitions on
the ServiceRequests sample, then runs `tools/rules-engine/check-content.mjs`, `compile.mjs --check`, builds
`codelab-starter.slnx -warnaserror` and runs the Architecture, Contract, DemoCases, Lookups and generated workflow
tests; set `CODELAB_SKIP_DOGFOOD=1` to skip it in local loops (never in CI).

## Extend

- Templates live in `templates/<set>/`, laid out as repo-relative paths. File names carry path tokens
  (`__Module__`, `__Name__`), contents carry `{{Token}}`s; an unknown token fails the render. Sets: `module`,
  `endpoint-get`, `endpoint-post`, `endpoint-put`, `endpoint-delete`, `lookup`, `transition/status`, `transition/rule`.
- Every name, path, pattern and message is in `lib/constants.mjs` (new-transition: `lib/transition/constants.mjs`);
  `lib/names.mjs` validates and converts names; `lib/project-files.mjs` edits the slnx and the host csproj;
  `lib/csharp-edit.mjs` inserts constants, catalog entries, registrations and usings; `lib/module-docs.mjs` writes
  MODULE.md notes, recipes, required rules and table rows; `lib/scaffold.mjs` holds the module, endpoint and
  permission plans, `lib/lookup.mjs` the lookup plan and `lib/transition/` the transition plan; `lib/workspace.mjs`
  writes the change set (`commit`, `preview`) and runs or lists the downstream generators.
- A new template token: add it to the plan's values and to the render test in `tests/generators.test.mjs`.

## Rules

- Generated C# must build with `-warnaserror` and pass the architecture and contract tests unmodified (the dogfood
  test proves it). Change a template, rerun the dogfood test.
- A refusal writes nothing: plans record their writes in a `ChangeSet` and commit only after every check passed.
- Code, rules, MODULE.md, tests and the docs refresh land in the same run (CLAUDE.md rules 7 and 12).
- Edits are idempotent and keep the target file's line endings and indentation; they never reorder existing lines.
- Starter names only, written whole (ADR-003), so a fork's rename rewrites them.

## Gotchas

- Generated files carry `// scaffold:permission-keys`, `// scaffold:permission-catalog`, `// scaffold:error-codes`
  and `// scaffold:registrations` markers (and `LookupTypes.cs` carries `// scaffold:lookup-types`). Hand-written
  modules without them use fallbacks (before the class's closing brace, before `];`, before `return services;`).
- MODULE.md notes go before `<!-- scaffold:use -->` when the file has it, else at the end of the Use section.
- Rule ids take the rules engine's prefix, the lower-cased module name (`servicerequests.`), never the kebab name.
- The generated `<Name>Store` classes are placeholders (a read finds nothing, a create is refused) so the module
  builds and its endpoints answer with a refusal until the data access is written.
- A module never gets a DbContext from the scaffolder; add one when the module stores data (module anatomy).
- On Node 24, `node --test <directory>` runs the directory as a script; always pass the glob.
- The frontend constant name (`frontendPermissionKeyName` in `lib/names.mjs`) mirrors `permissionKeyName` in
  `frontend/starter-web/tools/api-gen/lib/permission-keys.mjs` (the scaffold may not import frontend code); change
  both together. The dogfood test checks that the generated `permission-keys.generated.ts` carries `DemoCasesApprove`.
