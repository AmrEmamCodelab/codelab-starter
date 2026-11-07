# tools/feature-spec

## Purpose

Validates a feature spec (`docs/features/<feature>.md`, format in [docs/features/README.md](../../docs/features/README.md)
and [TEMPLATE.md](../../docs/features/TEMPLATE.md)): the front matter, the 14 required H2 sections in order, the
transition, endpoint, screen and hand-edit tables, the skill plan and the review-log iteration count. With
`--require-approved` it is the human approval gate of the `build-feature` pipeline: builders and `commit-gate` refuse
a spec it rejects. Zero dependencies (runs before `npm ci`), Node 24 ESM.

| File | What |
| --- | --- |
| `check-spec.mjs` | CLI and the library entry (re-exports `checkSpec`) |
| `lib/check-spec.mjs` | `checkSpec(text, { requireApproved, allowPreApproval, agentNames })`, pure: text in, findings out |
| `lib/markdown.mjs` | the Markdown subset: front matter, H2 sections, pipe tables, ordered lists, HTML comments |
| `lib/constants.mjs` | every section name, column, pattern, skill name, limit, rule id and exit code |
| `tests/` | `node:test` suites; `tests/helpers/spec-builder.mjs` builds valid and broken specs |

## Use

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
node tools/feature-spec/check-spec.mjs docs/features/<feature>.md [--require-approved] [--json]
node --test "tools/feature-spec/tests/**/*.test.mjs"
```

Exit codes: `0` no error finding (warnings may be printed), `1` error findings, `2` usage or unreadable file.
`--json` prints `{ file, findings, status, handEditCount, ok }`.

As a library (the pipeline-proof harness, eval checks):

```js
import { checkSpec } from '../feature-spec/check-spec.mjs';
const { findings, status, handEditCount, ok } = checkSpec(text, { requireApproved: true });
// findings: [{ ruleId, severity: 'error'|'warning', message, line }]; status: 'draft'|'approved'|null
```

Rule ids (`RuleId` in `lib/constants.mjs`): `front-matter.*` (missing, syntax, key-missing, unknown-key [warning],
feature-slug, status-invalid, approval-incomplete, approved-on-invalid, inputs-invalid), `approval.required`,
`approval.not-human`, `approval.pre-approved-outside-proof`,
`section.*` (missing, order, duplicate, empty, unknown [warning]), `table.*` (missing, columns, cell-count,
cell-empty, value-invalid), `skill-plan.*` (empty, unknown-skill, module-first, coverage [warning]),
`hand-edits.too-many` [warning], `review-log.iterations-missing`.

## Extend

- **A new section or column:** change `Section` / `TableColumns` in `lib/constants.mjs`, then
  `docs/features/TEMPLATE.md`, `docs/features/README.md`, the worked example, the `spec-writer` agent and the
  `build-feature` skill in the same change. The tests read the template and the example, so drift fails them.
- **A new skill-plan entry** (a new scaffold skill): add it to `PlanSkill` in the order the pipeline runs it, and to
  the Skill plan lists in the template, the README and the `build-feature` skill.
- **A new value check:** a function next to `checkTransitionRows` / `checkEndpointRows` in `lib/check-spec.mjs`,
  with a test in `tests/check-spec.test.mjs` for the accepted and the refused value.

## Rules

- Pure and deterministic: `checkSpec` never reads files or the clock; only the CLI reads the one file it is given.
- No literal outside `lib/constants.mjs`; findings carry a stable `ruleId` other tools may match on.
- **`approvedBy` names a person.** An agent's name (`.claude/agents/<name>.md`, read by the CLI; separators and
  case ignored, so `Spec Writer` is `spec-writer`), a word such as agent, bot, claude or pipeline (look-alike digits
  folded, so `C1aude` counts), an approver made only of role words (`Platform Owner`, `The Reviewer`; a name with a
  role beside it is fine) and `pipeline-proof (pre-approved)` are errors. The CLI accepts the pre-approval value only
  when `CODELAB_PIPELINE_PROOF_APPROVAL=1`, which only `Invoke-PipelineProof.ps1 -PreApproveSpec` sets for its own
  throwaway run; never set it in a project, a shell profile or CI, and no prompt sentence ("the spec is pre-approved")
  stands in for it. **These name checks are an early warning, not the gate:** a made-up person's name passes them.
  The gate is the PreToolUse hook rule on approval edits under `docs/features/` and CODEOWNERS review of that folder.
- Only error findings fail the check. Warnings are for judgment calls a human resolves (more than 10 hand edits, a
  section without a matching skill item, an unknown extra section or key).
- Every check has a unit test (`tests/check-spec.test.mjs`); the template and the example must pass (`tests/cli.test.mjs`).

## Gotchas

- **Only the first table of a section is checked.** Put the required table first; extra tables (a deadlines table,
  a permissions table, H3 detail) may follow.
- **Headings and tables inside fenced code blocks are ignored**, and so are HTML comments: a section whose only
  content is a comment is empty.
- **The front-matter reader is a YAML subset:** `key: value`, quoted scalars, block lists (`  - item`) and flow lists
  (`[a, b]`). Nested maps and multi-line scalars are reported as syntax findings.
- **The hand-edit limit is a warning, not an error,** so the spec of a proof that needed 11 edits still validates;
  the proof report reads `handEditCount` and fails the exit criterion itself.
- **Target images and permission keys are not resolved** against the repository (the check is text-only); the
  builders and reviewers do that.
