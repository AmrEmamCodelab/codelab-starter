# tools/scaffold

## Purpose

Deterministic Node generators for the backend module anatomy (ADR-004): a new module, a new endpoint and a new
permission. Every later module is created with them. They are the only sanctioned path that edits the solution
(`codelab-starter.slnx`) and the host project's module list (`backend/src/Starter.Api/Starter.Api.csproj`). The
scaffold skills (`.claude/skills/new-module`, `new-endpoint`, `new-permission`) and the fork-model test
(`tools/fork/Test-ForkModel.ps1`) call them.

## Use

Run from the repo root with the private Node (`E:\codelab-tools\node24` first on `PATH`):

```powershell
node tools/scaffold/new-module.mjs --name <Pascal>
node tools/scaffold/new-endpoint.mjs --module <Name> --name <Pascal> --method <GET|POST|PUT|DELETE> --route <segment> [--permission <kebab>]
node tools/scaffold/new-permission.mjs --module <Name> --action <kebab> [--description <text>]
```

Every command also takes `--root <dir>` (default: the repo this script lives in) and `--skip-generate` (skip the
downstream generators; for tests). Each prints `created <path>` / `changed <path>` for every file it wrote, then one
line per downstream generator (`ran ...` or `skipped ...: reason`).

| Exit code | Meaning |
| --- | --- |
| 0 | Done, or already present (identical arguments again change nothing). |
| 1 | Refused; nothing was written. |
| 2 | Usage error, or the files were written but a downstream generator failed (the message names it). |

Downstream generators, each only when it exists: `node tools/rules-engine/compile.mjs` (needs `node_modules` at the
root), `Starter.DocsGen --out docs/generated` (`backend/tools/Starter.DocsGen`), and for endpoints and permissions
`npm run api:gen --workspace frontend/starter-web` (needs `docs/generated/openapi.v1.json`).

Tests: `node --test "tools/scaffold/tests/**/*.test.mjs"`. The dogfood test (`tests/dogfood.test.mjs`) copies the
working tree into `%TEMP%`, runs the three generators, builds `codelab-starter.slnx -warnaserror` and runs the
Architecture, Contract and Demo tests; set `CODELAB_SKIP_DOGFOOD=1` to skip it in local loops (never in CI).

## Extend

- Templates live in `templates/<set>/`, laid out as repo-relative paths. File names carry path tokens
  (`__Module__`, `__Name__`), contents carry `{{Token}}`s; an unknown token fails the render. Sets: `module`,
  `endpoint-get`, `endpoint-post`, `endpoint-put`, `endpoint-delete`.
- Every name, path, pattern and message is in `lib/constants.mjs`; `lib/names.mjs` validates and converts names;
  `lib/project-files.mjs` edits the slnx and the host csproj; `lib/csharp-edit.mjs` inserts constants, catalog
  entries, registrations and usings; `lib/scaffold.mjs` holds the three plans; `lib/workspace.mjs` writes the
  change set and runs the downstream generators.
- A new template token: add it to the values in `lib/scaffold.mjs` and to the render test in
  `tests/generators.test.mjs`.

## Rules

- Generated C# must build with `-warnaserror` and pass the architecture and contract tests unmodified (the dogfood
  test proves it). Change a template, rerun the dogfood test.
- A refusal writes nothing: plans record their writes in a `ChangeSet` and commit only after every check passed.
- Edits are idempotent and keep the target file's line endings and indentation; they never reorder existing lines.
- Starter names only, written whole (ADR-003), so a fork's rename rewrites them.

## Gotchas

- Generated files carry `// scaffold:permission-keys`, `// scaffold:permission-catalog`, `// scaffold:error-codes`
  and `// scaffold:registrations` markers. Hand-written modules without them use fallbacks (before the class's
  closing brace, before `];`, before `return services;`).
- The generated `<Name>Store` classes are placeholders (a read finds nothing, a create is refused) so the module
  builds and its endpoints answer with a refusal until the data access is written.
- A module never gets a DbContext from the scaffolder; add one when the module stores data (module anatomy).
- On Node 24, `node --test <directory>` runs the directory as a script; always pass the glob.
