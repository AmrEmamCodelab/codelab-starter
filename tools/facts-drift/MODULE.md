# tools/facts-drift

## Purpose

The facts-drift gate (phase 9): every path, command, type, skill and agent that an agent, a skill or a doc names must
exist in the repository, and no text may describe the platform as it used to be (stale terms) or name the harvest
source. Docs that drift from the code send people and agents to files, scripts and switches that are gone.

| File | Role |
| --- | --- |
| `check.mjs` | The CLI: parse arguments, load the configuration, run the checks, print text or JSON, set the exit code. |
| `facts-drift.config.json` | Data: scope, path extensions, shell fence languages, type suffixes, symbol roots, external names. |
| `stale-terms.txt` | Stale-fact terms (`<id>[/<flags>]: <regex>`, the scrub sweep's format); findings are `stale-<id>`. |
| `allowlist.txt` | Justified exceptions: `<rule id> \| <file glob> \| <subject> \| <reason>`; the reason is required. |
| `lib/checker.mjs` | Runs every check per document, reuses the scrub sweep's term list, applies the allowlist. |
| `lib/markdown.mjs` | Lines with code-fence context (block id, intro paragraph), code spans, table columns. |
| `lib/paths.mjs`, `lib/repo-index.mjs` | Which code spans are paths; resolution against the git-aware file index. |
| `lib/commands.mjs`, `lib/powershell-params.mjs` | npm, node, PowerShell and cd commands; a script's `param()` names. |
| `lib/symbols.mjs` | The symbol index (C#, TypeScript, PowerShell) and which code spans look like types. |
| `lib/references.mjs` | "the `x` skill", "agent `y`" and table Skill/Agent columns, checked against `.claude/`. |
| `lib/config.mjs`, `lib/cli.mjs`, `lib/constants.mjs` | Configuration parsing, argument parsing, rule ids and names. |
| `tests/` | `node:test` units and end-to-end runs over fixture trees written to `%TEMP%` (no fixture files committed). |

## Use

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
node tools/facts-drift/check.mjs --root .          # exit 0 clean, 1 findings, 2 usage or internal error
node tools/facts-drift/check.mjs --root . --json   # { root, fileCount, findings, suppressed, unusedAllowlist }
node --test "tools/facts-drift/tests/**/*.test.mjs"
```

Options: `--config`, `--stale-terms` and `--allowlist` replace the default files next to `check.mjs`.

**Scope:** `.claude/agents/**/*.md`, `.claude/skills/**/*.md`, `CLAUDE.md`, `CONTEXT.md` and `docs/**/*.md`, minus
`docs/adr/`, `docs/evidence/` and `docs/generated/` (history and generated output are not facts to keep current).
Files are listed git-aware (tracked plus untracked-not-ignored), so a gitignored local file never counts as present:
the gate sees what CI sees.

**Checks** (rule ids in brackets):

| Check | What must hold |
| --- | --- |
| Paths in code spans [`path-missing`, `glob-empty`] | A span with a known extension, a trailing `/` or a top-level folder first resolves root-relative, document-relative or as the tail of an existing path (case-sensitive). Globs match something. For `docs/features/<slug>.md` only the folders before the placeholder must exist. |
| npm scripts [`npm-script-missing`, `npm-package-missing`] | `npm run <script>` / `npm test` exists in the nearest `package.json`, or in the one `-w`/`--workspace`/`--prefix` names (a folder or a package name such as `@codelab/ng-dga`). |
| node and PowerShell files [`command-file-missing`, `glob-empty`] | `node <file>`, `node --test <glob>`, `powershell ... -File <file>`, `& .\x.ps1` and a bare `tools\build.ps1` resolve. |
| PowerShell switches [`script-switch-unknown`] | Every `-Switch` after a script is a parameter of its top-level `param()` block (or a unique prefix of one), or a common parameter. |
| Skills and agents [`skill-missing`, `agent-missing`] | `.claude/skills/<name>/SKILL.md` or `.claude/agents/<name>.md` exists. |
| Types [`type-unresolved`] | A PascalCase span with a configured suffix (`ApiService`), generic (`PublicId<T>`), interface (`IUserDirectory`), attribute (`[HasPermission]`) or qualified (`SlaOptions.Max`) form is declared in `backend/`, `packages/`, `frontend/`, `services/`, `tools/` or `deploy/` (types, members, enum members), or listed in `externalSymbols`. |
| Stale terms [`stale-<id>`] | No line (prose or code block) matches `stale-terms.txt`: old Angular majors, role-based auth, Karma, Swashbuckle, the external DGA npm scope. |
| Source-product terms [`term-<id>`] | The scrub sweep's own `tools/extract/modhs-terms.txt` and `scrub-exceptions.txt`, reused (never copied) on the documents in scope. |

Commands are read from inline code spans and from fenced blocks whose language is a shell (`powershell`, `bash`,
none, ...). In a block, `cd`/`Set-Location` moves the working folder for the rest of that block, and the paragraph
right before the block can name it ("Then, from `frontend/starter-web`:"). In prose, a folder named in another code
span on the same line is a working-folder hint ("`npm run lint` in `frontend/starter-web`").

## Extend

- **A stale term:** add `<id>[/i]: <regex>` to `stale-terms.txt` with a comment saying what is true now, and a case
  to the stale-term test in `tests/checker.test.mjs`.
- **A justified exception:** add a line to `allowlist.txt` with the narrowest file glob and subject and a reason a
  reviewer can check (a gitignored file, a run output, a worked example the recipe creates). An entry that suppresses
  nothing is printed as unused: remove it.
- **A framework type or member the docs name:** add it to `types.externalSymbols`. A new naming convention for
  repository types: add the suffix to `types.suffixes` (or the name to `types.extraTypeNames`).
- **A new kind of document in scope:** add its glob to `scope.include` (and history or generated output to
  `scope.exclude`); the scope test in `tests/checker.test.mjs` lists the expected files.
- **A new command form:** recognise it in `lib/commands.mjs` (`analyzeCommand`) and check it in
  `lib/checker.mjs` (`checkCommand`), with a parsing test and an end-to-end case.

## Rules

- Zero dependencies: Node 24 built-ins plus the shared helpers of `tools/extract/lib` (file listing, globs, CLI
  arguments, exit codes, the finding shape, the scrub term parser and scrubber).
- Markdown links are not checked here: the docs gate already does (DOCS003, `tools/docs-gate`). This gate covers
  what the links check cannot: code-span paths, commands, switches, types, skills, agents and stale text.
- Source-product terms live only in `tools/extract/modhs-terms.txt`; never copy them into `stale-terms.txt`.
- Never write a stale or source-product term literally in a test: build it at run time (see
  `tests/checker.test.mjs`), so the scrub sweep and this gate stay clean on the tool itself.
- Every allowlist entry carries a reason; prefer fixing the document.

## Gotchas

- **Fixture trees are not git repositories,** so tests list files by walking the folder; the real run uses
  `git ls-files`. A file you just created but gitignored is absent on purpose.
- **Suffix resolution is lenient** for prose paths (`src/assets/i18n/ar.json` resolves under
  `frontend/starter-web`), strict for commands: `node <file>` and `-File <file>` must resolve from the working
  folder, the root, the document's folder or a folder hint.
- **Type detection is heuristic.** A one-word PascalCase name with no configured suffix (`ServiceRequests`, a module
  name) is not checked; a log property name with a type-like suffix may need an allowlist entry.
- **`npm run lint` at the root fails:** the root `package.json` has no `lint`. Write
  `npm run lint --workspace frontend/starter-web`, or name the folder on the same line or in the block's intro.
- **Parallel edits:** the gate reads the working tree, so a run reports the state of documents other people are
  still writing; rerun before reading too much into a single result.
