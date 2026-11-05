# Component showcase (`src/app/showcase`)

## Purpose

A live gallery for developers and designers, at `/showcase` on dev and staging only. It shows every
`@codelab/ng-dga` component, the 8 page patterns with fixture data, the layouts in a frame and the project
branding, in Arabic and English. The generated component catalog links each component to its section
(`/showcase#<selector>`), and the P4 visual and accessibility baselines are taken from these pages.

| Part | What it gives | Files |
| --- | --- | --- |
| Routes | `SHOWCASE_ROUTES`: one flat, lazy route per page at its full path (`showcase`, `showcase/patterns/wizard`...), each behind `showcaseEnabledGuard` and with the pinned clock. `SHOWCASE_PUBLIC_PATHS`: their full paths, for `PUBLIC_ROUTE_PATHS` | `showcase.routes.ts` |
| Guard | `showcaseEnabledGuard` (`canMatch`) is true only when the loaded config has `showcase` on (`"showcase": true` in the deployed `config.json`, or any development build). When it is false, no showcase route matches and the URL falls through to the 404 page | `showcase-enabled.guard.ts` |
| Page registry | `SHOWCASE_PAGES`, `ShowcasePageId`, `ShowcaseGroup`, `showcaseLink(id)`, `showcaseFullPath(segment)` | `showcase-pages.ts` |
| Pinned clock | `SHOWCASE_NOW_ISO`, `provideShowcaseClock()`, `showcaseToday`, `hoursFromNow`, `formatShowcaseDate`. Due dates, "today" limits and the footer year render the same on every run | `showcase-clock.ts` |
| Frame | `app-showcase-frame` (bar, language switcher, page navigation, `h1` + intro, `<main>` unless `landmark` is false), `app-showcase-section` (anchored section, `h2`/`h3`), `app-showcase-demo` (labelled demo, `wide`/`stack`), `app-showcase-state-switch` (ready / loading / empty / error) | `frame/` |
| Data states | `ShowcaseState`, `showcaseListView`, `SHOWCASE_API_ERROR` (a fixed upstream outage with a stable reference code) | `frame/showcase-state.ts` |
| Component gallery | `/showcase`: `COMPONENT_SECTIONS` (selector + catalog category), one demos component per category with default, disabled, error and loading states | `components/` |
| Pattern pages | admin list + editor dialog, tree editor, work queue, staff detail + decision dialog, wizard, requester detail + status tracker, portal content, landing dashboard, with fixtures in `pattern-fixtures.ts` | `patterns/` |
| Layout pages | footer, notifications menu and feed (`ShowcaseNotificationsSource`), the portal shell (signed out) and the requester shell (`ShowcaseSession` sample user, sample navigation), each in a frame | `layouts/` |
| Branding | The configured `branding` block, the components that follow it, and `BrandingPreview` (tries a colour through `applyDgaBranding` and resets when the page is left) | `branding/` |
| Texts | `ShowcaseTranslationKeys` / `SHOWCASE_TRANSLATION_KEYS`, everything under `showcase.*` in `ar.json` and `en.json` | `showcase-translation-keys.ts` |

## Use

- A development build (`npm start`, which `run-local.ps1` and the visual harness on :4280 use) serves it without any
  config edit: `loadRuntimeConfig` applies `DEVELOPMENT_BUILD_OVERRIDES` (`core/config/runtime-config.loader.ts`)
  when `isDevMode()` is true. A production build reads `config.json` only. The shipped `public/config.json` keeps
  `"showcase": false` (guarded by `tools/shipped-config.test.mjs`): never set it to true there. Staging runs the
  production build and turns it on in its own deployed `config.json`. Off, `/showcase` is a 404 and no showcase
  bundle is loaded.
- The app mounts the routes before the staff shell: `...SHOWCASE_ROUTES` in `app.routes.ts` and
  `...SHOWCASE_PUBLIC_PATHS` in `PUBLIC_ROUTE_PATHS`. The routes are flat on purpose. The route gate exempts a public
  path only on the outermost route with that path, so a pathless child page under a `showcase` mount would need a
  permission key.
- To take the visual baselines and run the accessibility scan, start the app and run the harness with
  `docs/evidence/P4/showcase/routes.json`. Each run covers 13 pages × ar/en × 1440/390. See the last rerun
  ([rerun-visual-report.md](../../../../../docs/evidence/P4/showcase/runs/rerun-visual-report.md)) and
  [e2e/README.md](../../../e2e/README.md).

## Extend

- **A new ng-dga component** gets an entry in `COMPONENT_SECTIONS`, plus an `<app-showcase-section anchor="<selector>">`
  in its category's demos component, with its main states. `component-sections.spec.ts` fails until both exist. It
  derives the expected list from the package's runtime exports and from the generated catalog.
- **A new page** gets an id in `ShowcasePageId`, an entry in `SHOWCASE_PAGES` (title and intro keys), a loader in
  `SHOWCASE_PAGE_LOADERS`, a route in `docs/evidence/P4/showcase/routes.json`, and a baseline.
- **A new text** goes in `ShowcaseTranslationKeys` and in both bundles. The spec checks the format, both
  languages, Arabic letters, matching placeholders, and that no `showcase.*` key is left unlisted.

## Rules

- No API calls and no feature imports. The showcase may import core, shared, layouts and patterns (ESLint
  boundaries). Data is fixtures; sample values that are data (record numbers, file names, icon names) stay literal.
- Everything visible goes through `showcase.*` keys, even though the raw-text rules exclude `**/showcase/**`.
- Fixed dates come from the pinned clock, never from `Date.now()`. A region whose content changes between runs gets
  `data-visual-mask`. Mask only content that really changes.
- Demos never open a modal or steal focus on load (dialogs open from buttons; `dga-page-feedback` uses
  `[moveFocus]="false"`), so a full-page screenshot is stable.

## Gotchas

- Visual baselines need a development build (or a staging `config.json` with `"showcase": true`). Against a
  production build with the shipped `config.json`, every showcase route captures the 404 page; never `--update`
  baselines from such a run.

- The framed shells bring their own `<main>`, `dga-header` and footer. Their pages pass `[landmark]="false"` to the
  frame, and the shell's `<router-outlet>` stays empty.
- The requester-shell page replaces `AuthService` for its own subtree only (`ShowcaseSession`), so the user menu has
  a name. The app's real session is never touched.
- `BrandingPreview` writes `--dga-brand-*` on `<html>` for the whole document. It resets in `ngOnDestroy`; a preview
  never outlives the branding page.
- The gallery is one long page (about 11,000 px at 1440 and 15,000 px at 390), because the catalog anchors must all
  live on `/showcase`. Keep demos compact: a full-page screenshot above about 16,000 px is unreliable in Chromium.
