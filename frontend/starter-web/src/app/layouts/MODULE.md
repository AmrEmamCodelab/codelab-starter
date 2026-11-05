# Web layouts (`src/app/layouts`)

## Purpose

The frames pages render in. Layouts hold no business rules: they read core stores and forward user intent to core
services.

| Piece | What it does | Files |
| --- | --- | --- |
| `app-shell` | Staff layout: brand link, the main navigation filtered by the user's permission keys, the language switcher, the user menu and the page outlet. Mirrors with `<html dir>` through logical CSS | `app-shell/` |
| Shell navigation | `SHELL_NAV_ITEMS` (path, label key, icon, permission key), `visibleNavItems()` and `appLink()` | `app-shell/shell-nav.ts` |
| `app-language-switcher` | One button offering the other language, named in its own script; calls `LanguageService.set()` | `language-switcher/` |
| `app-user-profile-menu` | The user's display name and a menu with logout (`AuthService.logout()`); closes on Escape and outside click | `user-profile-menu/` |
| `app-error-dialog` | Renders `ErrorDialogStore` (message key, reference code, dismiss). Hosted once by the root `App` | `error-dialog/` |
| Spec helpers | `fakeAuth()`, `fakeLanguage()`, `layoutTestProviders()`; translations echo their key | `layout.testing.ts` |

## Use

```ts
// app.routes.ts: gated pages are children of the shell
{ path: '', canActivate: [authGuard], loadComponent: () => import('./layouts/app-shell/app-shell.component')
    .then(m => m.AppShellComponent), children: [ ... ] }
```

```html
<!-- a full-page screen outside the shell can still offer the language switch -->
<app-language-switcher />
```

Features link to top-level pages with `appLink(RoutePaths.X)` (absolute router commands, the dashboard at `/`).

## Extend

- **A new page in the main navigation:** add its path to core `RoutePaths`, add the route as a shell child in
  `app.routes.ts` with `permissionGuard()` and its key, then add one `SHELL_NAV_ITEMS` entry with the same key, a
  `layout.nav.<name>` label in both `ar.json` and `en.json`, and an icon from the ng-dga icon set.
- **A new header action:** a component in its own folder here, with a spec; gate it with `PermissionStore.has()` when
  it leads to a gated page.
- Every component with logic gets a `*.spec.ts` beside it. Specs use `layout.testing.ts`.

## Rules

- Layouts import only core, shared and `@codelab/ng-dga` (ESLint boundaries); never a feature.
- No raw text: every label is a translation key in both bundles. No literal colours or physical CSS (`left`, `right`,
  `margin-left`): tokens and logical properties only.
- A navigation entry is shown only when the user holds the entry's key; the route is gated by the same key, and the
  server still authorizes every call.

## Gotchas

- Specs run the app's components in JIT (see `vitest.config.mts`), where signal `input()`/`model()` on the app's own
  components are not wired. Read router state through `ActivatedRoute` and keep app components input-free, or test
  the logic in a plain function.
- The text of an element that contains a `dga-icon` includes the icon's Private Use Area character; assert on the
  label `span`, or use `toContain`.
- The error dialog lives in the root `App`, not the shell, so failures on pages outside the shell show too.
