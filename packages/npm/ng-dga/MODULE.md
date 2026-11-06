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
| Components | `dga-button`, `dga-card`, `dga-tag`, `dga-status-tag`, `dga-tabs`, `dga-stepper` (merged: horizontal or vertical, linear or non-linear, navigable with `stepSelect`, compact mode, roving-tabindex keyboard), `dga-breadcrumb`, `dga-text-input`, `dga-textarea`, `dga-select`, `dga-checkbox`, `dga-radio-group`, `dga-info-field`, `dga-icon`, `dga-accordion` / `dga-accordion-item`, `dga-alert`, `dga-header`, `dga-hero`, `dga-hero-slider`, `dga-stamp-strip`, `dga-confirm-dialog`, `dga-error-dialog`, `dga-page-feedback`, `dga-portal-page-header`, `dga-file-upload`, `dga-date-input`, `dga-hijri-date-input`, `dga-unit-input`, `dga-multi-select`, `dga-geo-select` | `src/lib/<component>/` |
| Branding | `DgaBranding`, `DGA_BRANDING`, `provideDgaBranding(b)`, `applyDgaBranding(doc, b)`, `parseDgaBranding(raw)`: the primary colour, logo and ar/en name of a project; sets `--dga-brand-*` (primary + color-mix hover/active/subtle/muted) on :root, which `styles/_branding.scss` maps onto the primary, link, border, focus and progress tokens | `src/lib/branding/`, `styles/_branding.scss` |
| Design docs | docs/design/component-catalog.md and docs/design/tokens.md, generated from each component's `<selector>.catalog.json` (schema `catalog.schema.json`) and from the token sources | `src/lib/*/<selector>.catalog.json`, `catalog.schema.json`, `scripts/generate-catalog.mjs`, `scripts/generate-tokens-doc.mjs` |
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

- Form controls (`dga-text-input`, `dga-textarea`, `dga-select`, `dga-checkbox`, `dga-radio-group`) are signal-model controls. Bind `[(value)]` (or
  `[(checked)]`) and `[(touched)]`, or bind a signal-forms field. Each one serves as a form value control through its
  `value`/`checked`, `touched`, `disabled`, `required`, `invalid` and `name` members. Errors are a translated
  `error` string, linked by `aria-describedby` and setting `aria-invalid`.
- `dga-breadcrumb` uses `RouterLink`, so the app must provide the router. `dga-tabs` panels link through
  `panelIdPrefix` and `dgaTabPanelId(prefix, value)`.
- `dga-tabs` scrolls sideways by default when its tabs do not fit, which hides tabs on a phone. A short chooser that
  must stay fully visible (the sign-in methods, a two-to-four-way switch) sets `fill`: the tabs share the full width
  and long labels wrap inside their tab.
- `dga-select<T>` wraps a native `<select>` on purpose: the browser supplies the keyboard behaviour, the phone picker and screen-reader support. Give `compareWith` when option values are objects that get recreated. While nothing is chosen an empty choice is always shown (the `placeholder` text, or blank).
- `dga-textarea` shows a counter when `maxLength` is set; `counterLabel` is its translated template with `{count}`, `{max}` and `{remaining}`. `autoResize` grows the box with its content.
- `dga-accordion` (`mode` single|multiple, `headingLevel` H2..H6, default 3) holds `dga-accordion-item`s as direct content children; each item has `label` and an `[(expanded)]` model. Up/Down/Home/End move between headers.
- `dga-alert` error and warning are `role="alert"`, info and success are `role="status"`. `dismissLabel` adds a close button that hides the alert and emits `dismissed`.
- `dga-header` takes `brandName`, `logoSrc`, `homeLink`, `navItems: DgaNavItem[]` and translated `navLabel`/`menuLabel`; it uses `RouterLink`. Put the language switcher and user menu in `[dgaHeaderActions]`, a skip link in `[dgaHeaderStart]`. Below 48rem the navigation collapses behind the menu button; Escape on the button or in the panel closes it. A link that another entry links below (a portal home `/portal` next to `/portal/faq`) is current only on its own URL, so one page never marks two entries `aria-current=page`; set `DgaNavItem.exact` to override.
- `dga-hero` projects `[dgaHeroTitle]` (the page `<h1>`), `[dgaHeroSubtitle]` and `[dgaHeroActions]`. `dga-hero-slider` takes `slides`, `[(index)]` and every control label; autoplay is off by default, never runs under prefers-reduced-motion and has a pause control. `dga-stamp-strip` takes translated `text` and `verifyLabel`.
- Branding: the app calls `provideDgaBranding(config.branding)` once (starter-web: `provideAppBranding(config)`). A bad branding throws `DgaBrandingInvalidError(field)` at bootstrap; the logo must be a path the app serves.
- Dialogs (`dga-confirm-dialog`, `dga-error-dialog`) are controlled native modal `<dialog>`s: bind `[open]` and set it back to false on `confirmed`/`cancelled`/`closed`. Copy in the error dialog goes through the `DGA_CLIPBOARD` token. `dga-page-feedback` takes focus for every new `state` object. `dga-portal-page-header` renders the page's only `<h1>`.
- `dga-file-upload` is presentational (no HTTP): it emits `filesSelected` `{ accepted, rejected }` with a `DgaFileRejectionReason` (int); the caller owns `items` (`DgaFileUploadStatus` int enum Queued/Uploading/Uploaded/Failed; move rows with the `file-upload-state.ts` helpers), and handles `download(item)` and `remove(item)`. `maxSizeBytes` is an optional pre-check (null: the server decides); failed rows show `errorText` and `referenceCode`; `required` + `missingText`, `readonly`, `statusLabels` for live announcements. See `src/lib/file-upload/README.md`.
- Date fields hold ISO text, never a Date, and never read the clock: pass `[today]` built with `dgaTodayIso({ instant, timeZone: DGA_KSA_TIME_ZONE })`. Hijri conversion runs on the Umm al-Qura month table (the month lengths of .NET's UmAlQuraCalendar); Intl supplies only month names. `dga-unit-input` holds `number | null` with a required translated `unit`. `dga-multi-select` holds `T[]`. `dga-geo-select` is presentational over a `DgaGeoDataSource`.
- An icon is decorative unless it has a `label`. Use `iconOnly` buttons only with a `label`.

Scripts (Node from `.nvmrc`; prepend the pinned Node to PATH):

| Script | What it does |
| --- | --- |
| `npm run build -w @codelab/ng-dga` | ng-packagr (Angular package format, partial compilation) to `dist/`, `dist/ng-dga.css`, then fails on any remote URL |
| `npm test -w @codelab/ng-dga` | AOT-compiles sources and specs (`ngc`, strict templates), then Vitest: components under jsdom, package checks under Node |
| `npm run icons:generate -w @codelab/ng-dga` / `icons:check` | regenerates (or checks) the icon font, its JSON map and `dga-icon-names.generated.ts` |
| `npm run catalog:generate -w @codelab/ng-dga` / `catalog:check` | regenerates (or checks) docs/design/component-catalog.md; check fails on drift, a schema error, an exported component without an entry, or an entry whose class, inputs or outputs differ from the component |
| `npm run tokens:generate -w @codelab/ng-dga` / `tokens:check` | regenerates (or checks) docs/design/tokens.md from styles/_tokens.scss and _branding.scss |
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
- This package depends on no other Codelab package (`mustNotDependOn` in `module.rules.yaml`). The only exception is
  the lint: `npm run lint` runs `codelab-ng-tools rtl-lint` and Stylelint with the `@codelab/ng-tools/stylelint` preset
  (`.stylelintrc.json`) from the workspace tools. No source, script or built file imports `@codelab/ng-tools`.
- `npm test` ends with `npm run lint` (RTL lint over `src/`, Stylelint over `src/` and `styles/`), so CI step 11
  gates both. `tests/lint.test.mjs` keeps the wiring and runs both checks.

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
- The catalog check finds members by their declarations `readonly x = input(...)`, `model(...)` and `output(...)`. An aliased input or a setter input is not seen, so declare inputs in that form.
- jsdom applies the component CSS but no media queries, so specs run the phone layout; jsdom has no `HTMLDialogElement.showModal`, so `DgaModalDialog` falls back to the `open` attribute. `dga-textarea` auto-resize needs a stubbed `scrollHeight` in specs.
- Text colours must reach WCAG AA (4.5:1) on their surfaces: `tests/contrast.test.mjs` checks every text token pair. Placeholder text is `neutral-500` and muted text `neutral-600`; `--dga-color-text-disabled` is only for native disabled controls (axe exempts them). A plain span inside a disabled control, such as the unit of `dga-unit-input`, keeps an AA colour because axe cannot tell it is disabled.
- A row of form parts that must fit a 390px phone uses a grid with `minmax(0, …fr)` tracks (as `dga-hijri-date-input` does). A flex row reports the longest `<option>` as its minimum width and widens the card and the page.
- `isDgaLogoSrc` resolves the path against a stand-in origin and refuses any backslash (raw or `%5c`): browsers read `/\host` and `\/host` as `//host`, so a prefix check alone lets a remote logo through.
- The Stylelint preset accepts only a plain `var(--token)` on spacing and colour properties, so a Sass-interpolated
  token name (`gap: var(--dga-space-#{$step})`) fails. Generated step classes set the token on a local custom property
  (`--dga-layout-gap` in `_grid.scss`) and use `gap: var(--dga-layout-gap)`.
- Breaking change in 1.0.0-rc.0: `dgaStepState` takes one object `{ index, active, step, linear }`; `stepper/stepper-navigation.ts` is internal.
