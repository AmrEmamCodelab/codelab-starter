# Design kit

Everything a screen is built from and checked against. Screens use **only** `@codelab/ng-dga` components and the
8 page patterns, with design tokens, logical CSS and `ar` + `en` text (CLAUDE.md rule 9). No other UI library and
no external DGA package.

| Document | What it gives you | Kept current by |
| --- | --- | --- |
| [component-catalog.md](component-catalog.md) | Every ng-dga component: inputs, outputs, slots, accessibility contract, live example anchor | **Generated** from `packages/npm/ng-dga/src/lib/*/<selector>.catalog.json`; drift fails `npm test -w @codelab/ng-dga` |
| [tokens.md](tokens.md) | Every `--dga-*` design token with its resolved value; colour swatches | **Generated** from `packages/npm/ng-dga/styles/_tokens.scss` and `_branding.scss`; drift-checked the same way |
| [page-patterns.md](page-patterns.md) | The 8 page patterns, which one to pick, and a page per pattern in [patterns/](patterns/) | Hand-written with each pattern |
| Showcase (`/showcase`) | Every component and pattern rendered live in `ar` and `en` (dev and staging only, `showcase: true` in `config.json`) | The showcase feature in the app |
| [Visual and accessibility harness](../../frontend/starter-web/e2e/README.md) | Screenshots `ar`/`en` × 1440/390 against baselines (0.1 %) or a design target (5 %), plus axe (0 serious or critical) | `frontend/starter-web/e2e/` |

Never edit the two generated documents by hand (CLAUDE.md rule 11). Change the source, then:

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
npm run catalog:generate -w @codelab/ng-dga     # docs/design/component-catalog.md
npm run tokens:generate -w @codelab/ng-dga      # docs/design/tokens.md
npm run catalog:check -w @codelab/ng-dga        # exit 1 on drift, a missing entry or an entry that disagrees with its component
npm run tokens:check -w @codelab/ng-dga
```

## Building a screen

Use the `new-screen` skill ([.claude/skills/new-screen/SKILL.md](../../.claude/skills/new-screen/SKILL.md)). In short:

1. **Pick a pattern** from [page-patterns.md](page-patterns.md). A screen that fits no pattern is a design question
   for the platform owner, not a reason to invent a layout.
2. **Pick components** from [component-catalog.md](component-catalog.md); style only with tokens from
   [tokens.md](tokens.md) and logical properties (`inline-size`, `margin-inline-start`, `text-align: start`).
3. **Write the text** as `ar` and `en` keys (Arabic first), never raw text.
4. **Gate** the route and every action button with a typed permission key.
5. **Check** it: unit specs, then `npm run visual --workspace frontend/starter-web` in `ar` and `en` at 1440 and 390
   with 0 serious or critical axe violations.

## Design agents

Five agents in `.claude/agents/` work this kit. The design-capture loop rebuilds a page from a screenshot or a Figma
frame:

| Agent | Job |
| --- | --- |
| [dga-design-capture](../../.claude/agents/dga-design-capture.md) | Image or Figma frame → screen spec (Markdown) + target images per language and width |
| [screen-builder](../../.claude/agents/screen-builder.md) | Screen spec → page on a pattern with catalog components, `ar`/`en` keys, RTL, permission gates, specs |
| [visual-verifier](../../.claude/agents/visual-verifier.md) | Runs the harness against the target, loops fixes until within 5 %, records the iteration count |
| [dga-reviewer](../../.claude/agents/dga-reviewer.md) | Reviews against the catalog, tokens, logical CSS, accessibility and the no-third-party-UI rule |
| [i18n-keeper](../../.claude/agents/i18n-keeper.md) | Key parity, Arabic-first wording, no raw text |

## Adding to the kit

- **A component** goes in `@codelab/ng-dga` with its `<selector>.catalog.json` (schema
  `packages/npm/ng-dga/catalog.schema.json`) and a showcase section anchored `#<selector>`; regenerate the catalog.
- **A token** goes in `styles/_tokens.scss`; regenerate `tokens.md`.
- **A pattern** goes in `frontend/starter-web/src/app/patterns/<name>/`, with a page in [patterns/](patterns/) and a
  row in [page-patterns.md](page-patterns.md).
