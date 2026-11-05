# Starter web app (`frontend/starter-web`)

## Purpose

The Angular 22 frontend of the starter: standalone components, signals, zoneless, Arabic-first and right-to-left,
with the DGA look from `@codelab/ng-dga`. It talks only to the starter API, through `ApiService` in core, and signs
its calls with `@codelab/ng-security`. It is an npm workspace of the repository root.

| Folder | Owner area | What lives there |
| --- | --- | --- |
| `src/app/core/` | core | config, API, session, permissions, i18n, routing constants ([MODULE.md](src/app/core/MODULE.md)) |
| `src/app/layouts/` | features | the app shell and other page frames |
| `src/app/features/` | features | `auth`, `dashboard`, `admin` (users, organizations, import and access; routes in `features/admin/admin.routes.ts`), `errors` screens |
| `src/app/shared/` | shared | the `*hasPermission` directive ([MODULE.md](src/app/shared/MODULE.md)) |
| `src/app/patterns/` | patterns | page patterns: admin list + pager, editor dialog, tree editor ([MODULE.md](src/app/patterns/MODULE.md)) |
| `public/` | core | `config.json` (runtime configuration, strict JSON) |
| `src/assets/i18n/` | features | the public `ar` and `en` translation bundles |

## Use

Prepend the private Node to `PATH` first (`E:\codelab-tools\node24` on the build machine), then from the repo root:

```powershell
npm install                                         # once, at the repo root (workspaces)
npm start --workspace frontend/starter-web          # ng serve on http://localhost:4280
npm test --workspace frontend/starter-web           # Vitest on jsdom (no Karma)
npm run build --workspace frontend/starter-web      # ng build (application builder)
npm run lint --workspace frontend/starter-web       # ESLint, Stylelint, i18n parity, no inline styles
npm run api:gen --workspace frontend/starter-web    # regenerate the API types and permission keys
```

The API runs on `http://localhost:5180` (see `public/config.json`). Each environment's deployment overwrites
`config.json`; one build serves every environment.

## Extend

- New screens go under `src/app/features/<area>/` and are routed in `app.routes.ts` with `RoutePaths`,
  `authGuard` and `permissionGuard()`.
- New API calls go through a client in `src/app/core/api/clients/`.
- A new dependency is pinned exactly in this `package.json` and installed from the repo root; the root lock file is
  derived output.
- A new `config.json` key needs a field in `AppConfig`, a check in `parseAppConfig` and the fork model's
  prefixTargets review (the file must stay strict JSON).

## Rules

- No third-party UI library and no external DGA package; UI comes from `@codelab/ng-dga` and the page patterns.
- HTTP only through `ApiService`. Route, API, storage, permission and translation keys come from typed constants.
- Every string has `ar` and `en` keys. Design tokens and logical CSS only.
- No inline styles: no component `styles`, no `style=`, `[style.*]`, `[attr.style]` or `[ngStyle]` in templates or
  `host`, no `NgStyle`. Stylelint checks only .scss and .css files, so styles go in the component .scss file
  (ESLint `starter-styles/no-inline-styles` on .ts, `tools/no-inline-styles.mjs` on .html).
- No magic numbers outside tests, and enums are integers: ESLint `starter/no-magic-numbers` (base.no-magic-values;
  allowed numbers from rules/eslint-boundaries.compiled.json; `*.spec.ts`, `*.testing.ts`, `*.test.ts` and `tests/`
  are not checked) and `starter/enum-int-values` (every enum member has an explicit, non-string value). Name a number
  as a const (`const RETRY_SECONDS = 5;`) instead of writing it inline. See tools/lint/magic-values.mjs.
- Unit tests for every service, guard, interceptor and component with logic, in the same change.
- Generated files (`src/app/core/api/generated/**`, `permission-keys.generated.ts`) are never hand-edited.

## Gotchas

- The workspace's dependencies are hoisted to the repo root `node_modules`, outside this workspace root, so
  `angular.json` cannot copy `@codelab/ng-dga/assets` (the build refuses an asset path outside the workspace).
  Instead `preserveSymlinks: true` lets the build resolve the `@font-face` `url()`s through the workspace link and
  bundle the fonts and icon font into `media/`. The licence texts stay in the package (ADR-009).
- Specs compile components in JIT; `vitest.config.mts` inlines `templateUrl` files and drops style files, and
  enables legacy decorators. Production builds are AOT and unaffected.
- Specs run the Angular CLI JIT transform (`vitest.config.mts` plugin `starter-angular-signal-metadata`, from
  `@angular/compiler-cli/private/tooling`, a private CLI API that can change on an Angular major) so
  `input()`/`model()`/`output()`/signal queries work in JIT; without it bindings to them are silently ignored.
- `openapi-typescript` declares a TypeScript 5 peer; the root `package.json` overrides it to the repo's TypeScript.
- Angular 22 needs Node 24.15 or newer; an older Node refuses to install (`engine-strict`).
