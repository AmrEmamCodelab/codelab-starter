# AI development kit (`docs/ai-kit/`)

## Purpose

The map of the starter's AI development kit: the 16 agents in `.claude/agents/`, the step of the `build-feature`
pipeline each one runs, and the knowledge every builder and reviewer reads
([knowledge-wiring.md](knowledge-wiring.md)). The skills (`build-feature` and the scaffold skills) decide *what* to
build; the agents do one job each inside that flow and hand over to the next.

| File | What it gives |
| --- | --- |
| [README.md](README.md) | This map: roster, pipeline, rules |
| [knowledge-wiring.md](knowledge-wiring.md) | The single list of knowledge sources every builder and reviewer agent reads (catalog, page patterns, `/showcase`, tokens, recipes, `MODULE.md`, sample module, generated docs) |

## Use

### The `build-feature` pipeline

A developer describes a business scenario in plain language (or hands over a BRD, a use-case workbook, a DGA
screenshot or a Figma frame); the routing table in the root `CLAUDE.md` sends it to the `build-feature` skill, which
runs these steps:

| Step | What happens | Agents | Gate to the next step |
| --- | --- | --- | --- |
| 1 Input | The input is read and decomposed into modules, statuses and transitions, deadlines, permissions, endpoints and screens | `spec-writer`, `workflow-designer` (transition table + Mermaid); `dga-design-capture` for a screenshot or Figma frame (screen spec + target images) | The facts are in the draft spec |
| 2 Spec | One feature spec `docs/features/<kebab-slug>.md` (front matter `feature`, `status`, `approvedBy`, `approvedOn`, `inputs`; the Skill plan lists the skills in order) | `spec-writer` | `node tools/feature-spec/check-spec.mjs <spec.md> --require-approved` exits 0, and a **human approves** the spec |
| 3 API | The endpoints become the OpenAPI contract; the typed client is regenerated with `npm run api:gen` in `frontend/starter-web` | `api-designer` | `npm run api:gen:check` is clean |
| 4 Build | The Skill plan runs in order (`new-module`, `new-transition`, `new-permission`, `new-lookup`, `new-endpoint`, `new-screen`, the deadline recipe); backend and frontend in parallel | `backend-builder` ‖ `screen-builder` (`i18n-keeper` in fix mode for large text tables) | Targeted build and unit tests green |
| 5 Tests | Unit, contract and integration tests for every use case, validator, transition and component with logic; use-case scenarios from a workbook | `test-writer`; `usecase-tester` when the input is a use-case workbook | Tests green |
| 6 Visual | Each screen measured against its target: at most **5 % pixel difference per viewport after masking**, ar and en, desktop and mobile; loop until it matches, iteration count recorded | `visual-verifier` (drives fixes through `screen-builder`) | Every viewport within 5 %, 0 serious or critical axe violations |
| 7 Review | Module boundaries and docs, standards, security, design system, translations; each finding confirmed before it is fixed | `module-reviewer`, `standards-reviewer`, `security-reviewer`, `dga-reviewer`, `i18n-keeper`; `bug-verifier` confirms or refutes findings | No blocker or major finding open; findings and iteration count go to the spec's Review log |
| 8 Commit gate | Targeted gates (scrub, secret scan, rules, hook tests, docs gate, facts-drift); `tools\build.ps1 -Target CI` is the authoritative gate | `commit-gate` | `GATE: PASS` |
| 9 Docs | Each touched module's `MODULE.md`, the regenerated `docs/generated/`, the spec's Hand-edit log and Review log | the builders; checked by `module-reviewer` and `commit-gate` | Docs gate clean |

### Agent roster

| Agent | Step | Role | Writes |
| --- | --- | --- | --- |
| `spec-writer` | 1-2 | Input → feature spec in the required format | `docs/features/` |
| `workflow-designer` | 1 | Statuses, transitions, reasons, deadlines as a table + Mermaid | the spec's transition section |
| `api-designer` | 3 | Endpoints → OpenAPI contract → generated TS client | contract + generated client (by the generator) |
| `backend-builder` | 4 | Modules, use cases, validators, endpoints through the scaffold skills | backend code, tests, `MODULE.md` |
| `screen-builder` | 4, 6 | Pages from the catalog and the 8 page patterns, ar/en, permission-gated; applies visual fixes | frontend code, specs, i18n keys, `MODULE.md` |
| `dga-design-capture` | 1 | Screenshot or Figma frame → screen spec + target images + mask list | the spec folder only |
| `test-writer` | 5 | Tests for every piece of logic | tests |
| `usecase-tester` | 5 | End-to-end check of each use case of a workbook | report |
| `visual-verifier` | 6 | Harness runs, diff reading, fix lists, iteration record | evidence only |
| `module-reviewer` | 7 | Module rules, boundaries, endpoint contract, docs-moved-with-code | nothing (report) |
| `standards-reviewer` | 7 | Coding standards and the `CLAUDE.md` non-negotiables | nothing (report) |
| `security-reviewer` | 7 | Authorization, sessions, PublicId, signing, secrets, errors, edge | nothing (report) |
| `dga-reviewer` | 7 | Design system, tokens, logical CSS, accessibility; visual sign-off | nothing (report) |
| `i18n-keeper` | 4, 7 | ar/en parity, Arabic-first wording, no raw text | `ar.json`/`en.json` in fix mode |
| `bug-verifier` | 7 | Confirms or refutes each finding with evidence | nothing (report) |
| `commit-gate` | 8 | Runs the gates; stages and commits only when asked and green | the commit, when asked |

`spec-writer`, `workflow-designer` and `api-designer` are the spec and design agents; `backend-builder`, `test-writer`
and `usecase-tester` are the builder agents; the other ten are the screen, review and gate agents.

## Extend

- **A new agent:** add `.claude/agents/<name>.md` with front matter `name`, `description` (when to use it) and
  `tools`; give it Inputs, the job steps, a `Knowledge` section that links [knowledge-wiring.md](knowledge-wiring.md),
  an Output format and Rules. Add its row to the roster and its step to the pipeline table above.
- **A new knowledge source:** add the row to [knowledge-wiring.md](knowledge-wiring.md) and the reading-order line of
  each job that needs it.
- **A new pipeline step:** change the `build-feature` skill first, then the pipeline table here, then the agents it
  calls.

## Rules

- Every agent cites only paths, commands and types that exist now. The facts-drift gate
  (`node tools/facts-drift/check.mjs --root .`) and the docs gate (DOCS003 dead links) enforce it.
- Reviewer agents never edit; builders never review their own work as the final word.
- Agents follow the root `CLAUDE.md` non-negotiables and load the `coding-standards` skill before the first edit or
  finding. Rules are enforced by CI; the hooks only warn early and their refusal message names the skill to use.
- No agent loosens a threshold, adds a mask to hide a real difference, edits a generated file, or commits without
  being asked.
- Starter names only; no secrets, keys or memory files are read or written.

## Gotchas

- The spec approval gate is human: an agent never sets `status: approved` itself. A headless proof run may record
  `approvedBy: pipeline-proof (pre-approved)` only when its run prompt says so.
- The visual harness (`frontend/starter-web/e2e/visual.mjs`) measures; the Playwright MCP browser is for looking at
  the page while fixing. A pass is only what the harness reports.
- Masks are rectangles taken from the built page and painted on both images. A target whose branding sits in a
  different place is not covered by a mask; use a de-branded target or crop it (see `visual-verifier`).
