# tools/rules-engine

## Purpose

Turns the pattern rules in `rules/base.rules.yaml` and every `**/module.rules.yaml` into data that the build, CI,
the Claude hooks and the linters read. The engine only emits data. Architecture-test logic lives in
`Codelab.Testing` (P1), the hook logic lives in `.claude/hooks`, and the lint logic lives in `@codelab/ng-tools`.
Nothing parses YAML at runtime.

| Output | Where | Read by |
|---|---|---|
| `rules/rules.compiled.json` | repo | hooks, CI, `Codelab.Testing` architecture tests |
| `rules/eslint-boundaries.compiled.json` | repo | `@codelab/ng-tools` ESLint config (module boundaries, `no-magic-numbers` ignore list) |
| `rules/codelab.globalconfig` | repo | Codelab.Build.Sdk (`GlobalAnalyzerConfigFiles`): base `codelab_*` thresholds for every project, with or without a module |
| `CLAUDE.md` | every module folder | Claude Code (nested CLAUDE.md, auto-loaded) |
| `BannedSymbols.generated.txt` | every .NET module folder | Codelab.Build.Sdk; each entry becomes build error RS0030 |
| `.editorconfig` (not root) | every .NET module folder | Roslyn analyzer severities and the module's `codelab_*` thresholds (Codelab.Build.Analyzers, CLB1001-CLB1004) |
| `CodeMetricsConfig.txt` | every .NET module folder | CA1502 cyclomatic-complexity threshold (see Gotchas) |

## Use

```powershell
npm install                                     # once, at the repo root (ajv, yaml)
node tools/rules-engine/compile.mjs             # validate, then write every generated file
node tools/rules-engine/compile.mjs --check     # validate, then fail when a generated file drifted (CI step 3)
node tools/rules-engine/check-content.mjs --root .  # every forbidden/required rule over every tracked file (CI step 9)
node --test "tools/rules-engine/tests/**/*.test.mjs"   # CI step 4 (needs the .NET 10 SDK for the build test)
```

Options: `--root <dir>` (default: the repo root). Exit codes: `0` OK, `1` invalid rules or drift, `2` bad arguments.
After you change a rules file, run the compiler and commit the rules file together with the regenerated files.

### Writing a module.rules.yaml

```yaml
# yaml-language-server: $schema=../../../rules/rules.schema.json
schemaVersion: 1
module: Records                 # PascalCase; own rule ids start with "records."
kind: dotnet                    # optional; inferred (.csproj in the folder => dotnet, else angular); explicit dotnet works before the .csproj exists
owners: ["@platform-owner"]     # optional; defaults to the base owners
mayDependOn: [Workflow]
mustNotDependOn: [Notifications]
required:
  - id: records.numbering
    statement: Record numbers come only from IRecordNumberAllocator.
    enforcedBy: [architecture-test, review]
forbidden:
  - id: records.no-raw-sql
    description: Raw SQL bypasses the repository.
    pattern: '\bFromSqlRaw\b'   # JavaScript regex over the whole file; flags in `flags` (i, m, s, u)
    appliesTo: ["**/*.cs"]      # relative to the module folder
    excludes: ["**/Migrations/**"]
    # exemptEmittedFiles: true  # skip the engine's own outputs BY PATH (rules/codelab.globalconfig, the module's .editorconfig)
    fix: Add a repository method on the Records port.
    severity: error             # error (hooks refuse) or warning (hooks inform)
bannedSymbols:
  - id: records.no-console
    symbol: T:System.Console    # Roslyn documentation id
    fix: Log through ILogger with the reference code.
thresholds: { methodLines: 30 }  # per-key override of the base thresholds
analyzers: { CA1062: warning }   # per-id override of the base severities
recipes: [docs/recipes/add-endpoint.md]
exemptions:                      # drop a base rule; needs a reason and platform-owner review
  - rule: base.fe-raw-text-in-template
    reason: Backend-only module; it has no HTML templates.
```

### Reading the compiled rules (hooks, CI tools)

`lib/match.mjs` holds the shared helpers. Use them instead of re-implementing glob or regex handling.

- `moduleForPath(compiled, relPath)`: the module that owns a path. The deepest folder wins. Returns `undefined` when no module owns the path.
- `forbiddenRulesForPath(compiled, relPath)`: the forbidden rules that apply to a path. A module's rules include the base rules it inherits. A path outside every module gets the base rules.
- `evaluateFile(compiled, relPath, content)`: returns `[{ rule, violations: [{ line, column, text }] }]`.
- `globToRegExp`, `ruleAppliesToPath`, `findViolations`: the lower-level helpers behind the three above.

In the compiled JSON, every glob is repo-relative and every rule carries `source` (`base` or `module`).

## Extend

- **New base rule:** add it to `rules/base.rules.yaml` (ids start with `base.`). Add a bad/good case to
  `tests/match.test.mjs`, then recompile.
- **New rules field:** add it to `rules/rules.schema.json`. Carry it through `lib/merge.mjs` (inheritance) and the
  emitters that need it, then add tests.
- **New emitter:** add a module under `lib/emitters/` that returns file content. Wire it into `moduleOutputs`
  or `compileRepository` in `lib/engine.mjs`. If it writes into module folders, add its file name to
  `EMITTED_MODULE_FILE_NAMES` in `lib/constants.mjs` so the orphan check covers it.

## Rules

- Generated files are never hand-edited. They start with `GENERATED by tools/rules-engine`, and `--check` fails on
  drift (line endings are ignored) and on generated files whose module was removed (orphans).
- The compiler reports every problem in one run. It checks:
  - the schema
  - YAML syntax and duplicate keys
  - regexes that do not compile, and regexes that repeat a group containing an unbounded quantifier
    (`(\w+\s?)*`, `(a*)+`): they backtrack catastrophically and would stall the hooks and CI
  - `recipes` that point at a file that does not exist
  - duplicate rule ids and rule-id prefixes (`base.` and `<module>.`)
  - exemptions of unknown base rules
  - contradicting dependency lists and self-dependencies
  - duplicate module names
  - `kind: angular` in a folder with a `.csproj` (an explicit `kind: dotnet` without one is allowed, so a module
    folder can be scaffolded before its project)
  - banned symbols listed twice, or already in Codelab.Build.Sdk's global `BannedSymbols.txt`
- `rules/`, `**/module.rules.yaml` and this folder change only with platform-owner review (CODEOWNERS).
- The engine has no runtime dependencies beyond `ajv` and `yaml` (root devDependencies).

## Gotchas

- Folders named `fixtures`, `node_modules`, `bin`, `obj`, `feed`, `dist`, `TestResults`, `.angular`, `.vs` and
  `artifacts` are never searched for `module.rules.yaml`. That keeps `fixtures/SampleModule` out of the real compiled
  rules. The tests copy it into a temp workspace instead.
- Banning a type (`T:`) also flags the type's own members that use `this`. To stop callers from creating an object,
  ban its constructor (`M:Ns.Type.#ctor`), as the fixture does.
- The global bans (`DateTime.Now` and similar, `Thread.Sleep`) live in Codelab.Build.Sdk. Listing them again in a
  rules file is an error (RS0030 would report a duplicate). `base.bannedSymbols` is therefore empty.
- The C# size and magic-number thresholds reach the build as `codelab_*` keys (ADR-012), emitted by
  `lib/emitters/analyzer-settings.mjs` into each module `.editorconfig` and `rules/codelab.globalconfig`. The key
  names must match `packages/dotnet/Codelab.Build.Analyzers/Options/OptionKeys.cs` (an emitter test checks it). A
  module that exempts `base.no-magic-values` gets `codelab_magic_numbers_enforced = false`. Hand-written `codelab_*`
  keys are refused by `base.no-threshold-override`; only files with the GENERATED marker may set them.
- `CodeMetricsConfig.txt` is read by CA1502 only when it is passed as an `AdditionalFiles` item. Codelab.Build.Sdk
  must add it (same pattern as `BannedSymbols.generated.txt`). It was checked by hand: with the file wired in,
  `CA1502: 2` failed a method of complexity 4, and the `#` header line is accepted.
- `tests/fixture-build.test.mjs` runs the real .NET 10 SDK. It resolves dotnet the way `tools/build.ps1` does:
  `CODELAB_DOTNET`, else `E:\codelab-tools\dotnet10\dotnet.exe`, else `dotnet` on PATH. It restores from nuget.org
  (or the local package cache) in a temp folder.
- `check-content.mjs` checks ABSOLUTE state (the hooks use a ratchet): an existing violation fails CI. It reuses
  the hooks' loader and evaluator (`.claude/hooks/lib/compiled-rules.mjs`, `evaluate.mjs`), so there is one
  evaluator. Only the hook and rules-engine test fixtures are skipped; a real exception is a module exemption.
  Rules with `severity: warning` are reported but do not fail.
- Forbidden patterns are JavaScript regexes. `\p{L}` needs the `u` flag, and `^`/`$` per line need the `m` flag.
