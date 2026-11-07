# P9 evidence: agents-spec-design

Task: the three spec-and-design agents of the `build-feature` pipeline (steps 1 to 3).

| Agent | File | Pipeline step | Writes |
| --- | --- | --- | --- |
| `spec-writer` | [.claude/agents/spec-writer.md](../../../../.claude/agents/spec-writer.md) | 1-2: input to a draft feature spec in `docs/features/`, validated, stops at the human approval gate | the spec only |
| `workflow-designer` | [.claude/agents/workflow-designer.md](../../../../.claude/agents/workflow-designer.md) | 1: statuses to the transition table and a Mermaid `stateDiagram-v2` for client sign-off | the spec's Statuses and transitions section only |
| `api-designer` | [.claude/agents/api-designer.md](../../../../.claude/agents/api-designer.md) | 3: endpoint contract, then DocsGen `openapi.v1.json` and `npm run api:gen` with drift checks | the spec's Endpoints section only; generated files only through their generators |

Hand-off stated in each agent: spec-writer -> workflow-designer -> human approval -> api-designer ->
backend-builder || screen-builder. Each agent has a `Knowledge` section pointing to
[docs/ai-kit/knowledge-wiring.md](../../../ai-kit/knowledge-wiring.md).

## Facts the agents cite (checked to exist on 2026-11-07)

- Commands: `node tools/feature-spec/check-spec.mjs <spec> [--json]`; `.claude/skills/usecase-testing/scripts/Read-Xlsx.ps1 -Path -Sheet -AsJson`;
  `node tools/scaffold/new-endpoint.mjs` / `new-transition.mjs`; `dotnet run --project backend/tools/Starter.DocsGen -- --out docs/generated [--check]`
  (private SDK, `--artifacts-path` accepted by `dotnet run`); `npm run api:gen` / `npm run api:gen:check` in `frontend/starter-web`.
- Types: `PublicId<TDomain>` (Codelab.Security.PublicIds), `ServiceRequestRecordIds`, `TransitionCatalogValidator`,
  `OutcomeResults`, `PlatformErrorCodes.Validation` (`VALIDATION_FAILED`), `<Module>Limits` (scaffold template),
  `backend/tests/Starter.Contract.Tests/EndpointAllowlist.cs`.
- Spec format: aligned to `docs/features/TEMPLATE.md` and the validator constants (exact table columns, `Reason required`
  yes/no, routes start with `/`, the 8 page-pattern names, `Iterations: 0`, `new-module` first).

## Checks run

| Check | Result |
| --- | --- |
| Every Markdown link and backticked repo path in the three agents resolves | ok (only the `docs/generated/*` glob is not a file) |
| A spec in the exact shape the agents instruct (5-column transition table plus an H3 detail table, endpoint, screen, skill plan), validated with `node tools/feature-spec/check-spec.mjs` (scratch file outside the repo) | exit 0, 0 errors, 0 warnings |
| `node --test ".claude/hooks/tests/**/*.test.mjs"` | 115 pass, 0 fail |
| `node tools/extract/scrub-sweep.mjs --root .` | clean when first run; a later rerun (other parallel tasks' files present) reports 52 findings, none in this task's files (all in `rules/rules.compiled.json` and `tools/ai-evals/Invoke-KitEval.ps1`) |
| `node tools/extract/secret-scan.mjs --root .` | clean |
| `node tools/facts-drift/check.mjs --root .` (once it landed from its owning task) | 0 findings in the three agents and this note (52 elsewhere, owned by other files) |

Not run: headless
runs (budget 0 for this task), `tools/build.ps1` CI (integrator).
