# Knowledge wiring: what every builder and reviewer agent reads

The single list of knowledge sources for the agents in `.claude/agents/`. Every builder and reviewer agent links to this
page from its `Knowledge` section and reads the sources its job needs **before the first edit or the first finding**.
An agent never copies UI, rules or wording from memory when one of these sources owns it.

Every path below exists in the repository; the docs gate (DOCS003) fails on a dead link, and the facts-drift gate
(`node tools/facts-drift/check.mjs --root .`) fails on a stale path, command or type in agent, skill and doc text.

## Sources

| # | Source | Where | Read it for | Who reads it |
| --- | --- | --- | --- | --- |
| 1 | Component catalog (generated) | [docs/design/component-catalog.md](../design/component-catalog.md) | The only `@codelab/ng-dga` components a screen may use: selector, inputs, variants, states, showcase anchor | screen-builder, dga-design-capture, dga-reviewer, visual-verifier, standards-reviewer |
| 2 | The 8 page patterns | [docs/design/page-patterns.md](../design/page-patterns.md) and one page per pattern in [docs/design/patterns/](../design/patterns/) | Which pattern a page uses and how it is configured (never copied) | screen-builder, dga-design-capture, dga-reviewer, module-reviewer |
| 3 | The `/showcase` route | [frontend/starter-web/src/app/showcase/](../../frontend/starter-web/src/app/showcase/MODULE.md) (development builds of `frontend/starter-web`, `http://localhost:4280/showcase`) | Every component, pattern and layout rendered live in ar and en with fixture data; the pattern pages are the visual reference | screen-builder, dga-design-capture, visual-verifier, dga-reviewer |
| 4 | Design tokens (generated) | [docs/design/tokens.md](../design/tokens.md) | Semantic `--dga-*` tokens for colour, space, radius, type; never a literal value | screen-builder, dga-design-capture, dga-reviewer |
| 5 | Recipes | [docs/recipes/](../recipes/README.md): [add-endpoint](../recipes/add-endpoint.md), [add-page](../recipes/add-page.md), [add-permission](../recipes/add-permission.md), [add-lookup](../recipes/add-lookup.md), [add-workflow-type](../recipes/add-workflow-type.md), [add-deadline](../recipes/add-deadline.md), [add-attachment-type](../recipes/add-attachment-type.md), [add-notification](../recipes/add-notification.md), [add-integration](../recipes/add-integration.md) | The step-by-step way to add each kind of thing; the scaffold skills follow them | every builder; reviewers check a change followed its recipe |
| 6 | The relevant `MODULE.md` | The nearest `MODULE.md` up from the edited file (see [Finding the module](#finding-the-module)) | Purpose, Use, Extend, Rules, Gotchas of the module the change touches, next to its `module.rules.yaml` and generated nested `CLAUDE.md` | every builder and every reviewer |
| 7 | The sample module | Backend [backend/src/Modules/ServiceRequests/](../../backend/src/Modules/ServiceRequests/MODULE.md), frontend [frontend/starter-web/src/app/features/service-requests/](../../frontend/starter-web/src/app/features/service-requests/MODULE.md) | The reference a new record type copies: workflow catalog, typed endpoints, validators, wizard, staff queue and detail, decision dialogs, tests | every builder; module-reviewer and standards-reviewer compare against it |
| 8 | Generated docs | [docs/generated/](../generated/README.md): [permissions.md](../generated/permissions.md) + [permissions.json](../generated/permissions.json), [endpoints.md](../generated/endpoints.md), [openapi.v1.json](../generated/openapi.v1.json), [error-codes.md](../generated/error-codes.md), [config-keys.md](../generated/config-keys.md), [statuses.md](../generated/statuses.md), [workflows/](../generated/workflows/README.md) | What exists today: permission keys, routes and their permissions, the API contract, error codes, configuration keys, statuses and transitions. Never hand-edited; regenerate with `Starter.DocsGen` | every builder and every reviewer |

Always loaded as well (not repeated per agent): the root `CLAUDE.md` non-negotiables, the routing table, and the
`coding-standards` skill ([docs/standards/SKILL.md](../standards/SKILL.md) with
[ERROR-HANDLING.md](../standards/ERROR-HANDLING.md)).

## Finding the module

The module of a file is the deepest folder above it that holds a `module.rules.yaml`; its `MODULE.md` and its
generated nested `CLAUDE.md` sit in the same folder. Ask the same helper the hooks and CI use instead of guessing:

```
node --input-type=module -e "import { loadCompiledRules, findModuleForPath } from './.claude/hooks/lib/compiled-rules.mjs'; const { rules } = loadCompiledRules('.'); for (const p of process.argv.slice(1)) { const m = findModuleForPath(p, rules); console.log(p + ' -> ' + (m ? m.folder : 'no module (base rules only)')); }" <path> <path> ...
```

A file with no module falls under [rules/base.rules.yaml](../../rules/base.rules.yaml) only. The kernel and package
docs (`frontend/starter-web/MODULE.md`, `packages/dotnet/<Package>/MODULE.md`, `packages/npm/ng-dga/MODULE.md`) are the
nearest `MODULE.md` for files under them.

## Reading order per job

| Job | Read, in order |
| --- | --- |
| Build a screen | 2 (pick the pattern) → 1 → 4 → 3 (open the pattern page live) → 6 → 7 (frontend) → 5 (`add-page`) → 8 (permission keys, endpoints) |
| Build backend | 6 → 7 (backend) → 5 (the recipe of the change) → 8 (statuses, permissions, error codes, config keys) |
| Review | 6 for every touched module → 8 → the source the change should have followed (1-5, 7) |
| Measure a page | 3 (the same page live) → 1 → 4; the design target from the screen spec |

## Rules

- A source here wins over memory or an older agent text. When a source and the code disagree, the code is the truth
  and the source is a finding (stale doc), never a reason to copy the stale text.
- Generated sources (1, 4, 8, nested `CLAUDE.md`) are read, never edited; change their input and rerun the generator.
- A component, token, pattern, permission key or error code that is not in these sources does not exist: report it
  as a gap; never invent it.
- Adding a knowledge source: add a row here, link it, and add the matching step to the agents that need it in the
  same change (see [README.md](README.md), Extend).
