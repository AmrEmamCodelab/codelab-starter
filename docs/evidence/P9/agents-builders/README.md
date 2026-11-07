# P9 evidence: agents-builders

Phase 9 task `agents-builders`: the builder agents of the `build-feature` pipeline.

## What was delivered

| File | Role in the pipeline |
| --- | --- |
| `.claude/agents/backend-builder.md` | Step 4 (next to `screen-builder`): implements an approved feature spec on the backend through the scaffold skills and recipes in Skill plan order; logs hand edits in the spec. |
| `.claude/agents/test-writer.md` | Step 5: unit, workflow (`TransitionFixture`), contract, architecture, throwaway-SQL integration tests and Vitest specs. |
| `.claude/agents/usecase-tester.md` | On demand: one use case of an HLS-format workbook through the `usecase-testing` skill; no credentials, no findings samples. |
| `provenance/origins/p9-agents-builders.json` | Origin of the rewrite (batch `p9-agents-builders`, entry `agent-feature-builder`). |

Every agent has a **Knowledge** section pointing to `docs/ai-kit/knowledge-wiring.md` and a **Pipeline hand-offs**
section (backend-builder and screen-builder, then test-writer, visual-verifier, the reviewers, commit-gate).

## Harvest

- Source: `.claude/agents/` feature-builder agent of the harvest commit `a6a4b134`, extracted only through
  `tools/extract/extract.ps1 -Batch p9-agents-builders -Manifest <temp manifest copy> -Clean`. Staging scrub sweep
  reported the expected source-product terms (45) and the secret scan was clean; nothing was copied, the agent was
  rewritten as `backend-builder` (and in part `test-writer`). `usecase-tester` is new content (the skill it runs is
  owned by the `usecase-testing` task).
- The batch entry for `tools/extract/allowlist.manifest.yaml` is returned to the integrator as a shared-file request.

## Checks run (repo root, private Node 24)

| Check | Result |
| --- | --- |
| `node tools/extract/check-origins.mjs --root .` | 0 findings (48 origins files) |
| `node tools/extract/scrub-sweep.mjs --root .` | 0 findings in this task's 5 files (whole-repo findings at run time were in other tasks' files: `rules/rules.compiled.json`) |
| `node tools/extract/secret-scan.mjs --root .` | this task's 5 files clean (one whole-repo finding at run time, in another task's `tools/ai-evals` test) |
| `node tools/facts-drift/check.mjs --root .` | 0 findings in the three agents (other findings belong to other documents) |

## Dependencies on parallel tasks

The agents cite paths that other phase 9 tasks create: `docs/ai-kit/knowledge-wiring.md` (agents-wiring),
`tools/feature-spec/check-spec.mjs` (feature-spec), `.claude/skills/usecase-testing/SKILL.md` and
`.claude/skills/usecase-testing/scripts/Read-Xlsx.ps1` (usecase-testing). All of them existed in the working tree at
the end of this task.
