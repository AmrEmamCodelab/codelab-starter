# @codelab/ng-dga

## Purpose

Our own DGA component library for Angular 22 (decision D5: no external DGA package). It gives an app the DGA look and
feel with nothing fetched at runtime:

| Area | What it gives an app | Files |
| --- | --- | --- |
| Design tokens | Every colour, space, type, radius, border, focus, size and motion value as a `--dga-*` custom property on `:root` | `styles/_tokens.scss` |
| Base styles | Reset, base element styles, typography classes, grid and spacing helpers | `styles/_reset.scss`, `_base.scss`, `_typography.scss`, `_grid.scss` |
| Sass helpers | `dga.media-up(md)`, `dga.visually-hidden`, `dga.focus-ring`, and the `$dga-breakpoints`, `$dga-grid-columns` and `$dga-spacing-steps` maps | `styles/_mixins.scss` |
| Fonts | IBM Plex Sans Arabic and IBM Plex Sans (OFL-1.1), self-hosted WOFF2, weights 400 to 700 | `assets/fonts/`, `styles/_fonts.scss` |
| Icons | The owned `Codelab Icons` font, generated from Bootstrap Icons (MIT) SVGs with fixed code points, and `<dga-icon>` | `assets/icons/`, `icons/`, `src/lib/icon/` |
| Components | `dga-button`, `dga-card`, `dga-tag`, `dga-status-tag`, `dga-tabs`, `dga-stepper`, `dga-breadcrumb`, `dga-text-input`, `dga-checkbox`, `dga-radio-group`, `dga-info-field`, `dga-icon` | `src/lib/<component>/` |
| Licences | `assets/LICENCES.json`: every asset, its source, version, licence and sha256 (ADR-009) | `assets/LICENCES.json` |

Every component is standalone and OnPush, takes signal inputs (and signal models for two-way state), uses tokens and
logical CSS only, and is accessible and RTL-correct. Components carry no text of their own: every label arrives
already translated, through an input or projected content.

## Use

1. Dependency, in the app's `package.json` (exact pin, feed mode; ADR-002):

   ```json
   "dependencies": { "@codelab/ng-dga": "0.1.0" }
   ```

2. Styles, in `src/styles.scss` (once, first):

   ```scss
   @use '@codelab/ng-dga/styles';
   // or, to call the helpers: @use '@codelab/ng-dga/styles' as dga;  ...  @include dga.media-up(md) { ... }
   ```

   An app without Sass can list `node_modules/@codelab/ng-dga/dist/ng-dga.css` in `angular.json` `styles` instead.

3. Assets, in `angular.json` under `architect.build.options.assets`. Copy the fonts, the icon font and their licence
   files with the app (the stylesheet's `url()`s are rebased by the Angular build, and this copy keeps the licence
   texts beside the files):

   ```json
   { "glob": "**/*", "input": "node_modules/@codelab/ng-dga/assets", "output": "assets/ng-dga" }
   ```

4. Set `lang` and `dir` on `<html>` (the app's language service does this). Components read the direction from the
   nearest `dir` attribute; layout uses logical CSS, so it mirrors on its own.

5. Import the components you use into a standalone component:

   ```ts
   import { DgaButtonComponent, DgaTextInputComponent } from '@codelab/ng-dga';

   @Component({ imports: [DgaButtonComponent, DgaTextInputComponent], ... })
   ```

   ```html
   <dga-text-input [label]="'auth.userName' | t" autocomplete="username" required [(value)]="userName" />
   <dga-button type="submit" [loading]="busy()">{{ 'auth.signIn' | t }}</dga-button>
   ```

Component notes:

- Form controls (`dga-text-input`, `dga-checkbox`, `dga-radio-group`) are signal-model controls. Bind `[(value)]` (or
  `[(checked)]`) and `[(touched)]`, or bind a signal-forms field. Each one serves as a form value control through its
  `value`/`checked`, `touched`, `disabled`, `required`, `invalid` and `name` members. Errors are a translated
  `error` string, linked by `aria-describedby` and setting `aria-invalid`.
- `dga-breadcrumb` uses `RouterLink`, so the app must provide the router. `dga-tabs` panels link through
  `panelIdPrefix` and `dgaTabPanelId(prefix, value)`.
- `dga-tabs` scrolls sideways by default when its tabs do not fit, which hides tabs on a phone. A short chooser that
  must stay fully visible (the sign-in methods, a two-to-four-way switch) sets `fill`: the tabs share the full width
  and long labels wrap inside their tab.
- An icon is decorative unless it has a `label`. Use `iconOnly` buttons only with a `label`.

Scripts (Node from `.nvmrc`; prepend the pinned Node to PATH):

| Script | What it does |
| --- | --- |
| `npm run build -w @codelab/ng-dga` | ng-packagr (Angular package format, partial compilation) to `dist/`, `dist/ng-dga.css`, then fails on any remote URL |
| `npm test -w @codelab/ng-dga` | AOT-compiles sources and specs (`ngc`, strict templates), then Vitest: components under jsdom, package checks under Node |
| `npm run icons:generate -w @codelab/ng-dga` / `icons:check` | regenerates (or checks) the icon font, its JSON map and `dga-icon-names.generated.ts` |
| `npm run assets:import -w @codelab/ng-dga` / `assets:check` | copies (or checks) the fonts, licences and icon SVGs from the pinned source packages and records sha256 |

## Extend

- **A new component** goes in `src/lib/<name>/` with `dga-<name>.component.{ts,html,css}` and a `.spec.ts` in the same
  change, and is exported from `src/public-api.ts`. Pure logic (keyboard maps, state rules) goes in a separate `.ts`
  file with its own spec, as `tabs/tab-navigation.ts` and `stepper/step-state.ts` do. `tests/package.test.mjs` fails on
  a component that is not exported, not OnPush, uses `@Input()`, has no spec, or has raw text in its template. Add
  the selector to its expected list.
- **A new token** goes in `styles/_tokens.scss` as `--dga-*`. Components never use a literal colour, size or duration.
- **A new icon**: add `{ name, file, codepoint }` to `icons/icons.json` at the next free code point (set `mirrorInRtl`
  for direction glyphs). Run `assets:import`, then `icons:generate`, and commit the SVG, the font, the map and the
  generated TypeScript.
- **A new font or icon source**: follow ADR-009 first, with a row in the ADR table and in `assets/LICENCES.json`, and
  a pinned root devDependency.

## Rules

- No raw text in templates, no `@Input()` decorators, no NgModules, no third-party UI library and no reference to the
  external DGA package. The library is ours.
- Tokens and logical CSS only: `inline-size`, `margin-inline-start`, `inset-inline-start` and `text-align: start`,
  never `left`/`right`/`width`.
- Accessibility is part of the contract. Native elements first (`button`, `input`, `ol`, `nav`), with names, ids
  linked by `for`/`aria-describedby`, the WAI-ARIA keyboard pattern where one applies, and a visible focus ring.
- No asset without a licence entry (ADR-009), and no `http(s)` URL in built CSS or JavaScript. The build and the tests
  enforce both.
- Generated files (`dga-icon-names.generated.ts`, `assets/icons/codelab-icons.*`) are never hand-edited.
- This package depends on no other Codelab package (`mustNotDependOn` in `module.rules.yaml`).

## Gotchas

- The component specs run under jsdom, which has no layout: a spec can assert the computed styles that keep tabs
  inside their list (`fill`), but not their bounding boxes. Check widths at 320px in a real browser (the app's
  Playwright evidence).

- Specs run on AOT output. `npm test` first compiles `src/` into `.angular/spec` with `ngc`, because signal inputs only
  exist through the compiler. A template type error therefore fails the test run before Vitest starts.
- Vitest runs without globals, so `vitest.setup.mjs` resets the TestBed after every test. A spec that configures
  providers does it in `beforeEach` with a block body. Returning the TestBed from the hook makes Vitest call it as a
  teardown.
- The text in an icon's DOM is a Private Use Area character, so `textContent` of an element with an icon includes it.
  Match on `toContain`.
- The source `package.json` version stays `0.0.0`. The pack step stamps it from `packages/versions.json`.
  `src/package.json` is only the ng-packagr input, and a test keeps its peer dependencies equal to the published
  manifest.
- `assets:import` and `icons:generate` need the pinned source packages from the root devDependencies. They run
  offline from `node_modules` and never at app build time.
- An `iconOnly` button without a `label` has no accessible name.
