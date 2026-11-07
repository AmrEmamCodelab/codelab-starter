# P9 evidence: scaffold-finalize

Task: finalize `new-transition` and `new-lookup`, confirm `new-module`, `new-endpoint`, `new-permission` and
`new-screen`, so that every scaffold emits code + `module.rules.yaml` change + `MODULE.md` update + unit tests +
generated-docs refresh together; skill descriptions in "Use when ..." form for automatic routing; each skill names its
hook/CI refusal counterpart.

## Per-skill result

| Skill | Generator | Code | module.rules.yaml | MODULE.md | Unit tests | Generated-docs refresh | Refusal counterpart named |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `new-module` | `tools/scaffold/new-module.mjs` | yes | yes (created; rule-id prefix fixed) | yes (created) | yes (test project) | yes (run, or `next:` lines) | module folder without rules |
| `new-endpoint` | `tools/scaffold/new-endpoint.mjs` | yes | yes (add-endpoint recipe) | yes (Use note, new) | yes (`<Name>Tests.cs`) | yes (+ api:gen) | endpoint without `[HasPermission]` |
| `new-permission` | `tools/scaffold/new-permission.mjs` | yes | yes (add-permission recipe, new) | yes (Use note, new) | covered by `<Module>ModuleTests` | yes (+ api:gen; `--no-gen`) | literal permission key rules |
| `new-lookup` | `tools/scaffold/new-lookup.mjs` | yes (migration only) | yes (add-lookup recipe) | yes (seeded-types row, new) | yes (seed test + model-test list, new) | yes (note: docs/generated unaffected; `next:` lines) | lookup seeded outside a migration |
| `new-transition` | `tools/scaffold/new-transition.mjs` | yes (catalog) | yes (workflow-in-the-catalog rule + recipe, new) | yes (Use note) | yes (`TransitionFixture` classes) | yes (DocsGen: workflow sheet, statuses.md) | transition outside the catalog |
| `new-screen` | none (decision below) | by steps 3-4 | by step 2 (new area) | by steps 2 and 7 | by step 6 | step 2 (`api:gen` after a new permission) | raw UI text / feature folder without rules |

"new" marks what this task added. All six descriptions now start with "Use when ...".

## Gaps closed

1. `new-module` wrote rule ids with the kebab module name (`service-requests.thin-endpoints`), which the rules schema
   (`ruleId`) and the rules engine prefix check (`moduleRulePrefix`, lower-cased name) refuse for any two-word module.
   Template token `ModuleRulePrefix` added; a test pins the parity with `tools/rules-engine/lib/validate.mjs`.
2. `new-lookup` output failed the Lookups tests: `LookupsModelTests` asserts the exact migration list. The generator
   now appends the new migration there, writes a per-type seed test, and adds the seeded-types row.
3. `new-endpoint` / `new-permission` / `new-lookup` did not touch `MODULE.md` or `module.rules.yaml`; they now do
   (idempotent; a missing file becomes a `note:` naming the line to add by hand).
4. `new-transition` did not add the "flow lives in the catalog" rule to a module that lacked it; it now does once.
5. Uniform `--dry-run` on every generator (`ChangeSet.preview()`), and `--skip-generate` / `--dry-run` print the
   skipped refresh as `next:` lines (`downstreamCommands`).
6. The template render test skipped the nested `transition/*` sets and failed on them (pre-existing failure); fixed.

## new-screen decision

No generator. A page is one of eight patterns configured by judgment; a generator would have to emit eight
pattern-specific Angular shapes that pass strict compile, ESLint and Vitest, provable only with a frontend build in a
throwaway copy (the owner's `ng serve` watches the real tree). The skill now carries an explicit
"What one run emits" checklist mapping each part to the step and file that produces it.

## Verification

- `node --test "tools/scaffold/tests/**/*.test.mjs"` with `CODELAB_SKIP_DOGFOOD=1`: 145 pass, 0 fail.
- Dry runs against the real tree (nothing written) for all five generators: output lists code, rules, MODULE.md,
  tests and `next:` refresh lines.
- Dogfood (`tests/dogfood.test.mjs`, throwaway copy in `%TEMP%`, removed afterwards): PASS. In the copy it ran
  `new-module DemoCases`, GET and POST `new-endpoint`, `new-permission approve`, `new-lookup demo-kinds`, and two
  `new-transition` runs on ServiceRequests (status `on-hold` + `hold` from `under-review`; `resume` back), each with
  its downstream generators; then git status showed only reported or derived files,
  `node tools/rules-engine/check-content.mjs` and `compile.mjs --check` passed, `codelab-starter.slnx` built with
  `-warnaserror`, and the Architecture, Contract, DemoCases, Lookups and generated workflow test classes passed.
- `node tools/extract/scrub-sweep.mjs --root .`: no finding in this task's files (the tree's findings are in other
  tasks' in-progress files).
- `node tools/extract/secret-scan.mjs --root .`: no finding in this task's files (one finding in
  `tools/ai-evals/tests/HeadlessClaude.Tests.ps1`, owned by another task).
