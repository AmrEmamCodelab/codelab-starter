---
name: new-screen
description: "Use when the work needs a new page, screen, form, dialog flow or view in the web app, when the user hands over a DGA screenshot, a set of images or a Figma frame to build, and whenever a hook or CI refusal says 'Use the `new-screen` skill' (raw UI text in a template, or a feature folder without module.rules.yaml). Builds the Angular page in frontend/starter-web from one of the 8 page patterns with only @codelab/ng-dga components: the area's module.rules.yaml and MODULE.md, the page component, a permission-gated route with a path constant, ar and en i18n keys, RTL-safe logical CSS with design tokens and Vitest specs, then the visual + axe check in ar/en at 1440/390. Runs the design loop (dga-design-capture, screen-builder, visual-verifier, dga-reviewer) for a screenshot or Figma frame."
---

# New screen

Load the `coding-standards` skill first ([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)), then read
[docs/design/README.md](../../../docs/design/README.md). A screen is a pattern plus configuration: if you are writing
a table, a stepper, a dialog frame or a status timeline by hand, you picked the wrong starting point.

## When this applies

- The scenario needs something a person sees or uses in the browser: a list, a form, a wizard, a detail page with a
  decision, a portal page. The data behind it comes first (`new-endpoint`, then `npm run api:gen --workspace frontend/starter-web`).
- **Refusal counterpart:** the PreToolUse hook and the CI content check (`node tools/rules-engine/check-content.mjs`)
  refuse raw text in a template (`base.fe-raw-text-in-template`, `base.fe-raw-text-in-inline-template`) and code in a
  feature folder without `module.rules.yaml` (`hooks.module-without-rules`), with the message "Use the `new-screen`
  skill". Lint refuses an ungated route or button (`base.fe-routes-gated`, `base.fe-buttons-gated`), a UI library other
  than `@codelab/ng-dga` (`base.fe-ui-library-only`) and physical CSS or raw colours (`base.fe-logical-css`,
  `base.fe-design-tokens`).

## What one run emits

There is **no screen generator**: the pattern is chosen and configured by judgment, so the steps below are the
scaffold and each one names the file it writes. Decision (phase 9): a new-screen generator in tools/scaffold would have to
emit eight pattern-specific component shapes that compile under Angular strict mode, ESLint and Vitest, and that can
only be proved by a frontend build in a throwaway copy; the cheap parts it could do (keys, route snippet) are already
one-line edits. The checklist below is the contract instead.

| Part | Emitted by | Files |
| --- | --- | --- |
| Code | steps 3 and 4 | `features/<area>/<page>/<page>-page.component.{ts,html,scss}`, the store, the route entry and its path constant |
| Rules | step 2 (new area only) | `features/<area>/module.rules.yaml`, then `node tools/rules-engine/compile.mjs` (nested `CLAUDE.md`) |
| MODULE.md | steps 2 and 7 | `features/<area>/MODULE.md` (new area: all five sections; else the page in Purpose and Use) |
| Unit tests | step 6 | `<page>-page.component.spec.ts`, the store spec, helper specs |
| i18n | step 5 | `src/assets/i18n/ar.json` and `en.json` keys + the area's translation-keys constant |
| Generated docs | step 2 (new permission) | `npm run api:gen --workspace frontend/starter-web` after `new-permission`; nothing else under `docs/generated` changes for a page |

## Quick start

| You have... | Do |
| --- | --- |
| A description of the page | Steps 1 to 8 below |
| A screenshot, a set of images or a Figma frame | The design loop at the end (it runs steps 1 to 8 through the agents) |
| A change to an existing page | Steps 3 to 8 for the parts you touch |

Environment: the private Node first on `PATH` (`$env:PATH = "E:\codelab-tools\node24;$env:PATH"`); commands run
from `frontend/starter-web` unless stated.

## Workflow

### 1. Pick the pattern

Open [docs/design/page-patterns.md](../../../docs/design/page-patterns.md) and choose exactly one of the 8 by who acts
and what they do (admin list + editor dialog, tree editor, work queue, staff detail + decision dialog, wizard,
requester detail + status tracker, portal content, landing dashboard). Read that pattern's page in
`docs/design/patterns/`. Write down the pattern, the components from
[docs/design/component-catalog.md](../../../docs/design/component-catalog.md) you need, and any gap. A gap goes to
the platform owner; never build a replacement component.

### 2. Decide names, route and permission

- **Area:** the feature folder `src/app/features/<area>/`. An existing area, or a new one shaped like
  `features/dashboard/` with its own `MODULE.md` and `module.rules.yaml`; then run
  `node tools/rules-engine/compile.mjs` from the repo root to generate its nested `CLAUDE.md`.
  Write `module.rules.yaml` and `MODULE.md` before any other file: the PreToolUse hook refuses code in a
  feature folder without `module.rules.yaml` (`hooks.module-without-rules`). There is no screen generator (see
  "What one run emits"), so these steps are done by hand, in this order.
- **Page:** `src/app/features/<area>/<page>/<page>-page.component.{ts,html,scss,spec.ts}`, selector `app-<page>-page`.
- **Route path:** a constant, never a literal in the router: `RoutePaths.<Page>` in `core/routing/route-paths.ts` for a
  top-level page, or the area's paths file (like `features/admin/admin.paths.ts`) for a page inside an area.
- **Permission:** an existing key from `core/permissions/permission-keys.generated.ts`, or a new one through the
  `new-permission` skill (backend catalogue, then `npm run api:gen --workspace frontend/starter-web`). Public portal pages carry no key and are
  listed in `public-routes.ts` instead.

### 3. Build the page on the pattern

```ts
@Component({
  selector: 'app-<page>-page',
  imports: [/* the pattern, the ng-dga components, TranslatePipe, HasPermissionDirective */],
  templateUrl: './<page>-page.component.html',
  styleUrl: './<page>-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class <Page>PageComponent {
  protected readonly store = inject(<Page>Store);       // logic and data live in the store, with its own spec
  protected readonly PermissionKeys = PermissionKeys;
}
```

- Configure the pattern (columns, steps, fields, content keys) through its inputs; never copy its markup.
- Components only from `@codelab/ng-dga`, with the inputs the catalog lists.
- The `.scss` uses tokens ([docs/design/tokens.md](../../../docs/design/tokens.md)) and logical properties only:
  `inline-size`, `margin-inline-start`, `padding-block`, `inset-inline-end`, `text-align: start`. No inline styles.
- Data only through the area's API client and `ApiService`; PublicId route parameters.
- Put `data-visual-mask` on regions whose content changes between runs (dates, clocks, counters).

### 4. Gate the route and the actions

```ts
{
  path: <Area>Paths.<Page>,
  canActivate: [permissionGuard()],
  data: { [RouteDataKeys.Permission]: PermissionKeys.<Key> },
  loadComponent: () => import('./<page>/<page>-page.component').then(m => m.<Page>PageComponent),
}
```

Inside the staff shell the route is a child of `SHELL_ROUTE` in `app.routes.ts` (or of the area's routes file), which
already applies `authGuard`. Every action button carries `*hasPermission="PermissionKeys.<Key>"`, or
`data-public-action` when any visitor may use it. `npm run lint --workspace frontend/starter-web` (permission gates) and `route-gates.spec.ts` refuse
anything else.

### 5. Add the texts in both languages

- Keys under the area's namespace: `<area>.<page>.*` (portal pages `portal.*`, layouts `layouts.*`).
- Add every key to `src/assets/i18n/ar.json` (Arabic first, the primary wording) and `en.json`, and to the area's
  typed translation-keys constant. Templates show text only through the `t` pipe, never raw text.
- More than a handful of keys, or any doubt about the Arabic: hand the list to the `i18n-keeper` agent.

### 6. Write the specs

- Page spec: renders in `ar` and `en`; gated actions are hidden without the permission; loading, empty and error
  states render; user intent reaches the store.
- Store or service spec: every rule, mapping and API call (mock `ApiService`).
- Pure helpers (formatting, mapping): their own spec.

### 7. Update the docs

The area's `MODULE.md`: the page in Purpose and Use, any new rule or gotcha (CLAUDE.md rule 12). A new area also gets
its row in `frontend/starter-web/MODULE.md`.

### 8. Verify

```powershell
Set-Location frontend/starter-web
npx vitest run src/app/features/<area>
npm run lint          # ESLint, Stylelint (tokens, logical CSS), i18n parity, no inline styles, permission gates
# with run-local.ps1 running (web :4280):
node e2e/visual.mjs --base-url http://localhost:4280 --routes '[{"name":"<page>","path":"/<path>"}]' `
  --langs ar,en --widths 1440,390 --baseline-dir <baselines> --update --out-dir <out>
```

The first visual run records baselines: review every image, then keep them with the feature's evidence. It must
show 0 serious or critical axe violations in both languages and widths. A protected page needs a signed-in session:
check it through the Playwright MCP after signing in, or through the showcase example of its pattern. Finally ask
the `dga-reviewer` agent for a review.

## Design loop (screenshot or Figma frame)

1. **`dga-design-capture`** with the images and the area: writes `docs/specs/screens/<page>/screen-spec.md` and
   `targets/<page>.<lang>.<width>.png`. Resolve its Gaps with the user before building.
2. **`screen-builder`** with the spec: steps 1 to 8 above.
3. **`visual-verifier`** with the route and `--target docs/specs/screens/<page>/targets/<page>.{lang}.{width}.png`:
   loops with `screen-builder` until every screenshot is within 5 % and axe shows 0 serious or critical
   violations, at most 5 iterations, and records the iteration count in `iterations.md`.
4. **`dga-reviewer`** signs off; **`i18n-keeper`** checks the texts.

## Rules

- Only `@codelab/ng-dga` and the 8 patterns: no other UI library, no external DGA package, no copied pattern markup.
- No magic strings or numbers: route paths, permission keys, API paths, storage keys and limits from typed constants.
- Never edit generated files: `permission-keys.generated.ts`, the generated API client,
  `docs/design/component-catalog.md`, `docs/design/tokens.md`, nested `CLAUDE.md`.
- Never loosen a visual threshold or mask a real difference to make a run pass.

## More

- Page patterns: [docs/design/page-patterns.md](../../../docs/design/page-patterns.md); pattern code:
  [patterns/MODULE.md](../../../frontend/starter-web/src/app/patterns/MODULE.md).
- Harness: [frontend/starter-web/e2e/README.md](../../../frontend/starter-web/e2e/README.md).
- Agents: `.claude/agents/{dga-design-capture,screen-builder,visual-verifier,dga-reviewer,i18n-keeper}.md`.
