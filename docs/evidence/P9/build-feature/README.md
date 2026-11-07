# P9 evidence: build-feature

Phase 9 task `build-feature`: the feature-spec format and validator, the feature-spec docs and worked example, and
the full `build-feature` skill (the default entry point of the pipeline).

## What was delivered

| Path | What |
| --- | --- |
| `tools/feature-spec/check-spec.mjs` | Zero-dependency Node ESM validator: `node tools/feature-spec/check-spec.mjs <spec.md> [--require-approved] [--json]`, exit 0 ok, 1 findings, 2 usage; re-exports `checkSpec(text)` |
| `tools/feature-spec/lib/` | `check-spec.mjs` (pure checks), `markdown.mjs` (front matter, sections, tables, lists, comments), `constants.mjs` (every literal) |
| `tools/feature-spec/tests/` | `node:test` suites: `check-spec.test.mjs`, `markdown.test.mjs`, `cli.test.mjs`, helper `helpers/spec-builder.mjs` |
| [tools/feature-spec/MODULE.md](../../../../tools/feature-spec/MODULE.md) | Purpose, Use, Extend, Rules, Gotchas |
| [docs/features/README.md](../../../features/README.md) | What a feature spec is, the format, the approval gate, the lifecycle |
| [docs/features/TEMPLATE.md](../../../features/TEMPLATE.md) | The format to copy (passes the check as a draft) |
| [docs/features/examples/site-visit-permit.md](../../../features/examples/site-visit-permit.md) | Worked, valid draft for the routing scenario |
| [.claude/skills/build-feature/SKILL.md](../../../../.claude/skills/build-feature/SKILL.md) + [REFERENCE.md](../../../../.claude/skills/build-feature/REFERENCE.md) | The 9-step pipeline, the human approval gate, the headless pre-approval rule, the hand-edit log rule |

## What the validator checks

Front matter keys `feature` (kebab slug), `status` (`draft` or `approved`), `approvedBy`, `approvedOn` (real
`YYYY-MM-DD`), `inputs` (non-empty list); an approved spec names who and when; `--require-approved` refuses a draft.
The 14 H2 sections present, once, in order, not empty (HTML comments do not count). Exact columns of the
transitions, endpoints, screens and hand-edit tables, no empty cell, `yes`/`no` reasons, HTTP methods, routes that
start with `/`, the 8 page patterns, numeric hand-edit numbers. The skill plan is an ordered list of known skills
(`new-module`, `new-transition`, `deadline-recipe`, `new-permission`, `new-lookup`, `new-endpoint`, `new-screen`) with
`new-module` first. The review log has `Iterations: <n>`. Warnings (do not fail): more than 10 hand edits, a section
that declares work without its skill item, unknown sections or keys.

## Verification (2026-11-07, private Node 24 on PATH)

| Command | Result |
| --- | --- |
| `node --test "tools/feature-spec/tests/**/*.test.mjs"` | 75 tests, 75 pass, 0 fail |
| `node tools/feature-spec/check-spec.mjs docs/features/examples/site-visit-permit.md` | exit 0, status draft, 0 errors, 0 warnings |
| `node tools/feature-spec/check-spec.mjs docs/features/examples/site-visit-permit.md --require-approved` | exit 1, `approval.required` (the example is a draft, as intended) |
| `node tools/feature-spec/check-spec.mjs docs/features/TEMPLATE.md` | exit 0, 0 findings |
| `node tools/feature-spec/check-spec.mjs` | exit 2, usage |
| docs gate (`dotnet run --project tools/docs-gate -- check --root .`, alternate artifacts path) | PASSED |
| `node tools/extract/scrub-sweep.mjs --root .` | 0 findings in this task's paths (repo-wide findings were in `rules/rules.compiled.json` and `tools/ai-evals/`, other tasks' work in progress) |
| `node tools/extract/secret-scan.mjs --root .` | 0 findings in this task's paths (1 repo-wide finding in `tools/ai-evals/tests`, another task) |

`tools\build.ps1 -Target CI` was not run (phase rule: the integrator runs the gates).

## Open

- The feature-spec tests are not yet in a CI step: the integrator adds `tools/feature-spec/tests` to step 10
  (scanner tests) of `tools/build.ps1`.
- The example's escalation to the manager uses the Deadlines "another reaction to a breach" extension point (an
  automatic `System` transition); no scaffold skill covers it, so a real build logs it as a hand edit. It is
  recorded as an open decision in the example's Scenario.
