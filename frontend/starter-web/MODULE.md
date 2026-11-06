# Starter web app (`frontend/starter-web`)

## Purpose

The Angular 22 frontend of the starter: standalone components, signals, zoneless, Arabic-first and right-to-left,
with the DGA look from `@codelab/ng-dga`. It talks only to the starter API, through `ApiService` in core, and signs
its calls with `@codelab/ng-security`. It is an npm workspace of the repository root.

| Folder | Owner area | What lives there |
| --- | --- | --- |
| `src/app/core/` | core | config, API, session, permissions, i18n, routing constants ([MODULE.md](src/app/core/MODULE.md)) |
| `src/app/layouts/` | features | the staff shell, the portal and requester shells, the footer, the notifications menu and feed, the error dialog ([MODULE.md](src/app/layouts/MODULE.md)) |
| `src/app/features/` | features | `auth`, `dashboard`, `admin` (users, organizations, import and access; routes in `features/admin/admin.routes.ts`), `errors` screens, and `portal` (the public pages under `/portal`, routes in `features/portal/portal.routes.ts`, [MODULE.md](src/app/features/portal/MODULE.md)), `notifications` (the API-backed notifications source and the `/notifications` page, [MODULE.md](src/app/features/notifications/MODULE.md)) |
| `src/app/shared/` | shared | the `*hasPermission` directive, the file picker and the geo data source ([MODULE.md](src/app/shared/MODULE.md)) |
| `src/app/patterns/` | patterns | the 8 page patterns: admin list + pager, editor dialog, tree editor, work queue, staff detail + decision dialog, wizard, requester detail + status tracker, portal content, landing dashboard ([MODULE.md](src/app/patterns/MODULE.md)) |
| `src/app/showcase/` | showcase | the `/showcase` gallery of every component, pattern and layout; mounted when `config.json` has `"showcase": true` (staging) or in any development build (`npm start`; `DEVELOPMENT_BUILD_OVERRIDES` in `core/config/runtime-config.loader.ts`); the shipped `public/config.json` keeps it false ([MODULE.md](src/app/showcase/MODULE.md)) |
| `src/app/app.routes.ts`, `public-routes.ts`, `app.config.ts` | app root | the route table (staff shell, portal frame, showcase, sign-in and error pages), `PUBLIC_ROUTE_PATHS` and `PUBLIC_LAYOUT_ROUTES` for the route gate (`route-gates.spec.ts`), and the providers (branding from `config.json`) |
| `e2e/` | app root | the visual and accessibility harness and the scripted Phase-5 user flows (`e2e/flows.mjs`; needs the app running via run-local.ps1, report to docs/evidence/P5/flows; [README.md](e2e/README.md)) |
| `public/` | core | `config.json` (runtime configuration, strict JSON) |
| `src/assets/i18n/` | features | the public `ar` and `en` translation bundles |

## Use

Prepend the private Node to `PATH` first (`E:\codelab-tools\node24` on the build machine), then from the repo root:

```powershell
npm install                                         # once, at the repo root (workspaces)
npm start --workspace frontend/starter-web          # ng serve on http://localhost:4280
npm test --workspace frontend/starter-web           # Vitest on jsdom (no Karma)
npm run build --workspace frontend/starter-web      # ng build (application builder)
npm run lint --workspace frontend/starter-web       # ESLint, Stylelint, i18n parity, no inline styles, permission gates, RTL, icon subset
npm run api:gen --workspace frontend/starter-web    # regenerate the API types and permission keys
npm run visual --workspace frontend/starter-web -- --base-url http://localhost:4280 --routes e2e/routes/p4-public.json `
  --baseline-dir ../../docs/evidence/P4/baselines # screenshots ar/en x 1440/390 against baselines, plus axe
```

The API runs on `http://localhost:5180` (see `public/config.json`). Each environment's deployment overwrites
`config.json`; one build serves every environment.

## Extend

- New screens go under `src/app/features/<area>/` and are routed in `app.routes.ts` with `RoutePaths`,
  `authGuard` and `permissionGuard()`. A page every visitor may open is listed in `PUBLIC_ROUTE_PATHS`
  (`public-routes.ts`) instead; public portal pages hang under `PORTAL_ROUTE` and come in through `PORTAL_PUBLIC_PATHS`.
- Build a screen on one of the 8 page patterns and only `@codelab/ng-dga` components (`docs/design/`, the
  `new-screen` skill), add it to the showcase when it is a new pattern or component, and prove it with `npm run visual`.
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
  enables legacy decorators. Production builds are AOT and unaffected. The file-system module cache is off
  (`fsModuleCache: false`) because it does not key on the inlined `.html`, so a cached spec would keep an old
  template. Vitest also runs `e2e/**/*.test.mjs` (the harness's own unit tests).
- Specs run the Angular CLI JIT transform (`vitest.config.mts` plugin `starter-angular-signal-metadata`, from
  `@angular/compiler-cli/private/tooling`, a private CLI API that can change on an Angular major) so
  `input()`/`model()`/`output()`/signal queries work in JIT; without it bindings to them are silently ignored.
- `openapi-typescript` declares a TypeScript 5 peer; the root `package.json` overrides it to the repo's TypeScript.
- Angular 22 needs Node 24.15 or newer; an older Node refuses to install (`engine-strict`).
- `npm run visual` needs the app running and drives the installed Microsoft Edge (`playwright-core`, channel
  `msedge`); nothing is downloaded. It is not part of `npm test`.
