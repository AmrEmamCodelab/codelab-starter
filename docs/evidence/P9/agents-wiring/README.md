# P9 evidence: agents-wiring

Task: knowledge wiring for the AI kit and the final versions of the ten screen, review and gate agents.
Run on 2026-11-07 against the uncommitted P9 working tree (other P9 tasks were editing in parallel), with the private
toolchain (`E:\codelab-tools\dotnet10\dotnet.exe`, Node 24.22.2 from `E:\codelab-tools\node24`).

## What changed

| Path | Change |
| --- | --- |
| [docs/ai-kit/knowledge-wiring.md](../../../ai-kit/knowledge-wiring.md) | New. The single list every builder and reviewer agent reads: component catalog, 8 page patterns (+ `docs/design/patterns/`), `/showcase`, tokens, the 9 recipes, the nearest `MODULE.md` (with the lookup helper), the sample module (backend + frontend), `docs/generated/*`; reading order per job |
| [docs/ai-kit/README.md](../../../ai-kit/README.md) | New. The 16-agent roster mapped to `build-feature` steps 1-9, in the Purpose / Use / Extend / Rules / Gotchas layout |
| `.claude/agents/module-reviewer.md` | Final version (no longer interim): reads `module.rules.yaml` + generated nested `CLAUDE.md` + `MODULE.md` of every touched module; checks `mayDependOn` / `mustNotDependOn`, required and forbidden rules, docs moved with code (docs gate), per endpoint permission + PublicId + `IValidator<T>` + problem+json, ng-dga-only UI, ar+en keys |
| `.claude/agents/visual-verifier.md` | States the exit metric (at most 5 % pixel difference per viewport after masking; ar and en; 1440 and 390; harness on Playwright, Playwright MCP for inspection; loop until match; iteration count) and how masks are declared (dynamic text, dates, reference codes, branding areas) |
| `.claude/agents/commit-gate.md` | Targeted gates table (scrub, secret scan, rules check, content rules, hook tests, docs gate, facts-drift, generated drift, feature-spec check, targeted build/tests) and `tools\build.ps1 -Target CI` accepted as the authoritative gate; `PASS (targeted; CI pending)` when CI runs later |
| the other 7 owned agents | A `Knowledge` section linking the wiring page; stale facts fixed (see below) |

Stale facts fixed: interim and phase wording in `module-reviewer`; "lands during phase 2 / not applicable yet" in
`commit-gate`; the non-existent `common.*` i18n namespace in `i18n-keeper` (real top-level namespaces include
`shared.*`, `core.*`, `errors.<CODE>`, `serviceRequests.*`); stale-term words in `standards-reviewer`; commands that
only resolved from a sub-folder (`npm run lint`, `node tools/i18n-parity.mjs`, `node e2e/visual.mjs`, `-w @codelab/ng-dga`)
now run from the repository root with an explicit workspace or path; output file names written as `<out-dir>/...`
placeholders; the gitignored local secrets file named in prose only.

## Verification

| Check | Command | Result |
| --- | --- | --- |
| Facts-drift, owned paths | `node tools/facts-drift/check.mjs --root . --json` | 0 findings on owned paths (first run: 21, all fixed). 47 findings remain repo-wide, all in files of other tasks (skills, recipes, runbooks, `CONTEXT.md`, `usecase-tester.md`) |
| Docs gate | `dotnet run --project tools/docs-gate --artifacts-path %TEMP%\p9-agents-wiring -- check --root .` | First run: 0 findings on owned paths, 1 elsewhere (a DOCS003 dead link in `.claude/agents/usecase-tester.md`, another task's file). Final rerun: `docs gate: PASSED` |
| Hook tests | `node --test ".claude/hooks/tests/**/*.test.mjs"` | 115 tests, 115 pass, 0 fail |
| Secret scan | `node tools/extract/secret-scan.mjs --root .` | clean |
| Scrub sweep | `node tools/extract/scrub-sweep.mjs --root .` | 0 findings on owned paths; 49 `unc-path` findings in `rules/rules.compiled.json` (a lookup-seeding rule pattern edited by another task) |
| Knowledge section | every `.claude/agents/*.md` has `## Knowledge` linking `docs/ai-kit/knowledge-wiring.md` | 16 of 16 |
| Wiring paths | every path in `knowledge-wiring.md` exists (checked with `ls`; links checked by DOCS003) | all exist |

## Open

- The pass of the visual metric still depends on the P4 decision about targets: masks are rectangles from the built
  page, so a target with branding in another place needs a de-branded or cropped target. `visual-verifier` and the
  README say so; the owner choice is still open.
- No automated check asserts that every agent keeps its `Knowledge` section; facts-drift covers the paths it cites.
