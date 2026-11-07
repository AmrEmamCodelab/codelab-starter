---
name: build-feature
description: "Runs the full feature pipeline: turns a business scenario into an approved feature spec (docs/features/<slug>.md), then into the modules, statuses and transitions, deadlines, permissions, lookups, endpoints and screens it lists, through the scaffold skills and the builder, test, visual and reviewer agents, ending at commit-gate with the docs updated. Use whenever the user describes something the business needs in plain language (\"we need...\", \"clients should be able to...\", \"the inspector approves...\", \"after 5 days it escalates...\"), or hands over a BRD, an HLS use-case workbook, a DGA screenshot or a Figma frame, even when no slash command is named. This is the default entry point for any new functionality."
---

# Build feature

The default entry point for new functionality. Load the `coding-standards` skill first
([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)) and read [CONTEXT.md](../../../CONTEXT.md). The agents
are in `.claude/agents/`; what each must read is in [docs/ai-kit/knowledge-wiring.md](../../../docs/ai-kit/knowledge-wiring.md).
Details, hand-offs and failure handling per step: [REFERENCE.md](REFERENCE.md).

## Quick start

> "Clients request a site-visit permit; the inspector approves; undecided after 5 days it escalates to the manager."

1. Steps 1 and 2 write `docs/features/<slug>.md`, here site-visit-permit.md (compare the worked example
   [docs/features/examples/site-visit-permit.md](../../../docs/features/examples/site-visit-permit.md)).
2. **Stop.** Show the spec and wait for a human to approve it.
3. After approval, steps 3 to 9 run the Skill plan and the agents in order.

A single small change to an existing feature (one endpoint, one permission, one status) may use its scaffold skill
directly (routing table in the root `CLAUDE.md`); anything that needs two or more skills comes here.

## Workflow

Copy this checklist into the task list and tick a step only when its gate passes.

```
- [ ] 1 Input   spec-writer + workflow-designer (+ dga-design-capture for an image or Figma frame)
- [ ] 2 Spec    docs/features/<slug>.md, check-spec exit 0, then STOP for human approval
- [ ] 3 API     api-designer: OpenAPI, then `npm run api:gen` in frontend/starter-web
- [ ] 4 Build   backend-builder || screen-builder, the Skill plan in order
- [ ] 5 Tests   test-writer (+ usecase-tester when the input is a use-case workbook)
- [ ] 6 Visual  visual-verifier: <= 5 % per viewport after masking, ar + en, desktop + mobile
- [ ] 7 Review  module-reviewer, standards-reviewer, security-reviewer, dga-reviewer, i18n-keeper
- [ ] 8 Commit  commit-gate
- [ ] 9 Docs    MODULE.md files, docs/generated regenerated, spec Hand-edit log + Review log
```

1. **Input.** Run the `spec-writer` agent on the input (scenario text, BRD, workbook, image or Figma frame) and the
   `workflow-designer` agent on its statuses and decisions (transition table + Mermaid). An image or Figma frame goes
   through `dga-design-capture` first; its target images fill the Screens table's Target image column.
2. **Spec.** One spec, `docs/features/<kebab-slug>.md`, in the format of
   [docs/features/TEMPLATE.md](../../../docs/features/TEMPLATE.md). Validate until it exits 0:
   `node tools/feature-spec/check-spec.mjs docs/features/<slug>.md`.
   **Then STOP.** Present the spec (Scenario and open decisions, the transition table and diagram, the Skill plan)
   and wait for a human to set `status: approved`, `approvedBy` and `approvedOn`. Never approve it yourself. Only
   when the run prompt explicitly says the spec is pre-approved (a headless pipeline proof) record
   `approvedBy: pipeline-proof (pre-approved)` and today's date. Gate: the check with `--require-approved` exits 0.
3. **API.** The `api-designer` agent turns the Endpoints table into new-endpoint runs and, once the backend builds,
   regenerates `docs/generated/openapi.v1.json` (Starter.DocsGen) and the client with
   `npm run api:gen --workspace frontend/starter-web`. Gate: `npm run api:gen:check --workspace frontend/starter-web` is clean.
4. **Build.** `backend-builder` and `screen-builder` in parallel, each invoking the skills with the Skill tool (never
   by reading a `SKILL.md` and running its generator directly) in the spec's Skill plan order:
   `new-module`, `new-transition`, the deadline recipe [docs/recipes/add-deadline.md](../../../docs/recipes/add-deadline.md),
   `new-permission`, `new-lookup`, `new-endpoint`, `new-screen` (a permission a transition names is created before
   it). Never create a module folder, endpoint, screen, transition or lookup seed by hand: the hooks refuse it with
   "Use the `<skill>` skill"; do exactly that (the refused edit never landed, so it is not a hand edit).
5. **Tests.** `test-writer` completes unit, contract and integration tests from the spec's Tests section.
6. **Visual.** `visual-verifier` per screen, looping with `screen-builder` until every viewport is within 5 %.
7. **Review.** The five reviewers on the change set; `bug-verifier` confirms each finding before it is fixed.
   Record the findings and the iteration count in the spec's Review log.
8. **Commit gate.** `commit-gate` runs the targeted gates, including the spec check with `--require-approved`.
   Commit or push only when the user asked for it.
9. **Docs.** Every touched module's `MODULE.md` (Purpose, Use, Extend, Rules, Gotchas), regenerated
   `docs/generated/*`, and the spec's Hand-edit log and Review log are complete.

## Rules

- **Every hand edit after approval is logged** in the spec's Hand-edit log (`# | File | Reason`): any change that
  no skill generator or agent step produced. Target: at most 10 per feature.
- **Scope is the approved spec.** New scope goes back to step 2 (status `draft`, re-approval).
- Generated files are never hand-edited; change the source and rerun the generator.
- Starter names only; no secrets; time from `TimeProvider`; enums as int with explicit values; no magic values;
  unit tests in the same change.
