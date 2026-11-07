# Recipe: add a page

Adds one page to `frontend/starter-web` on one of the 8 page patterns. It builds on the P4 page-pattern guide,
[docs/design/page-patterns.md](../design/page-patterns.md): the guide says **which** pattern; this recipe says **how**
to put a page on it, from the route to the tests. The `new-screen` skill
([.claude/skills/new-screen/SKILL.md](../../.claude/skills/new-screen/SKILL.md)) runs this recipe, and its design loop
handles a page given as a DGA screenshot or a Figma frame. There is no page generator: a page is a pattern plus
configuration, written by hand.

## 1. Choose the pattern

Answer two questions, then take the row of [page-patterns.md](../design/page-patterns.md#which-pattern):

| Question | Decides between |
| --- | --- |
| **Who acts?** Staff deciding on someone else's record, or a requester following their own? | staff detail vs requester detail; work queue vs "my requests" (a work-queue variant without assignment filters) |
| **What do they do?** List and edit in place, walk steps, decide, read, or land after sign-in? | admin list, wizard, staff detail + decision dialog, portal content, landing dashboard |

Read that pattern's page in [docs/design/patterns/](../design/patterns/) (anatomy, inputs, states, a reference
screen) and pick the components from [component-catalog.md](../design/component-catalog.md). A page that fits no
pattern, or a missing component, is a question for the platform owner: never write a table, stepper, dialog frame or
timeline by hand, and never add another UI library.

| A record page... | Reuse |
| --- | --- |
| shows a record's fields | `RecordDetailCardsComponent` (`shared/records`) with a cards adapter in the area's `data/` (the sample's `serviceRequestCardsAdapter`) |
| shows its history | `RecordTimelineComponent` (`shared/records`) over `GET api/records/{recordType}/{id}/timeline` |
| offers decisions | `DecisionPanelComponent` (`shared/records`) over the server's `allowedActions`; never a status matrix in the page |
| shows a deadline | `SlaBadgeComponent`; status labels through `StatusWordingPipe` (`statusWording` with the audience) |

## 2. Name it

| Item | Rule | Example |
| --- | --- | --- |
| Area | an existing `src/app/features/<area>/`, or a new one with `MODULE.md` and `module.rules.yaml` written **first** (the hook refuses code in a folder without rules), then `node tools/rules-engine/compile.mjs` | `features/permit-applications` |
| Page files | `<area>/<page>/<page>-page.component.{ts,html,scss,spec.ts}`, selector `app-<page>-page`; logic in `<page>.store.ts` with its own spec | `permit-history/permit-history-page.component.ts` |
| Route path | a constant: `RoutePaths.<Page>` in `core/routing/route-paths.ts` (top-level) or the area's paths file (`<area>.paths.ts`) | `PermitPaths.History = 'history'` |
| Permission | an existing key from `core/permissions/permission-keys.generated.ts`, or a new one ([add-permission.md](add-permission.md), then `npm run api:gen -w frontend/starter-web`); public portal pages carry none and are listed in `public-routes.ts` | `PermissionKeys.PermitApplicationsViewOwn` |
| Text keys | `<area prefix>.<page>.*` (portal `portal.*`, layouts `layouts.*`) | `permitApplications.history.title` |

## 3. Route and permission gate

```ts
{
  path: PermitPaths.History,
  canActivate: [permissionGuard()],
  data: { [RouteDataKeys.Permission]: PermissionKeys.PermitApplicationsViewOwn },
  loadComponent: () => import('./permit-history/permit-history-page.component').then((m) => m.PermitHistoryPageComponent),
}
```

- Put the route in the area's routes constant; the area's constant is registered once in `app.routes.ts` (or
  `features/admin/admin.routes.ts`) by the integrator, with `// codelab:module <Module>` on the line when the area
  belongs to a removable module.
- Every action button: `*hasPermission="PermissionKeys.<Key>"`, or `data-public-action` when any visitor may use it.
  A pattern never checks permissions itself.
- Navigation entries (`app-nav.ts`) carry the same key.

## 4. Build the page

- Configure the pattern through its inputs (columns, steps, fields, keys); never copy its markup.
- The component renders state and forwards intent to the store; the store calls the area's API client, which goes
  through `ApiService` only. Route ids are PublicIds.
- Styles: design tokens ([tokens.md](../design/tokens.md)) and logical properties only (`inline-size`,
  `margin-inline-start`, `text-align: start`); no inline styles, so the page mirrors in RTL without code.
- Loading, empty and error states come from the pattern; pass its `emptyKey` and the API error.

## 5. Texts in both languages

Add every key to `src/assets/i18n/ar.json` (Arabic first, the primary wording) and `en.json`, and to the area's typed
translation-keys constant. Templates show text only through the `t` pipe. Never invent Arabic: ask for it, or hand the
list to the `i18n-keeper` agent.

## 6. Tests

| Spec | Proves |
| --- | --- |
| Page (`<page>-page.component.spec.ts`) | renders in `ar` and `en`; gated buttons are hidden without the permission; loading, empty and error states render; user intent reaches the store |
| Store or service | every rule, mapping and API call (mock `ApiService`) |
| Pure helpers (adapters, formatting) | their own spec |
| Route gates | `route-gates.spec.ts` and `npm run lint -w frontend/starter-web` refuse a route or button without a gate |

## 7. Docs and verify

Add the page to the area's `MODULE.md` (Purpose and Use; any new rule or gotcha). Then, from `frontend/starter-web`
with the private Node on `PATH`:

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
npx vitest run src/app/features/<area>
npm run lint          # ESLint, Stylelint (tokens, logical CSS), i18n parity, no inline styles, permission gates, RTL
# with run-local.ps1 running (web :4280):
node e2e/visual.mjs --base-url http://localhost:4280 --routes '[{"name":"<page>","path":"/<path>"}]' `
  --langs ar,en --widths 1440,390 --baseline-dir <baselines> --update --out-dir <out>
```

The visual run must show 0 serious or critical axe violations in both languages and both widths; review the first
baselines by eye. Details of the harness: [frontend/starter-web/e2e/README.md](../../frontend/starter-web/e2e/README.md).
