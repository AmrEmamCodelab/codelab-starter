# Web layouts (`src/app/layouts`)

## Purpose

The frames pages render in, and the header pieces those frames share. Layouts hold no business rules: they read core
stores and forward user intent to core services or to an injected source.

| Piece | What it does | Files |
| --- | --- | --- |
| `app-shell` | Staff layout: the brand link (`LayoutBrand` name and logo from `config.json` branding), the main navigation filtered by the user's permission keys, the notifications bell, the language switcher, the user menu and the page outlet. Mirrors with `<html dir>` through logical CSS | `app-shell/` |
| Shell navigation | `SHELL_NAV_ITEMS` (an injection token: path, label key, icon, and the targets the entry may open, each a permission key and a link), `visibleNavItems()` and `appLink()`. The app provides the items (`APP_NAV_ITEMS` in `src/app/app-nav.ts`) on `SHELL_ROUTE`, since they point into features | `app-shell/shell-nav.ts` |
| `app-portal-shell` | Public frame of the portal pages: `dga-stamp-strip`, `dga-header` (brand, the portal navigation: landing, service guide, FAQ, contact; language switcher; a sign-in link for a visitor, the user menu once signed in), a skip link, the page outlet and the footer. Needs no session | `portal-shell/` |
| Portal navigation | `PORTAL_NAV_ENTRIES`, `portalLink(PortalPaths.X)` (`/portal`, `/portal/faq`), `rootLink(RoutePaths.X)`, `toDgaNavItems()` | `portal-shell/portal-nav.ts` |
| Frame texts and styles | `LayoutChromeTranslationKeys` (brand fallback, stamp strip, skip link, header labels), `PortalNavTranslationKeys`; the `layout-frame` Sass mixin both shells include | `portal-shell/layout-chrome-translation-keys.ts`, `portal-shell/_layout-frame.scss` |
| `app-requester-shell` | Signed-in requester frame: stamp strip, `dga-header` with the requester navigation (permission-filtered, collapses behind the menu button on phones), the notifications bell, the language switcher, the user menu, the page outlet and the footer. **Not routed in P4** (P7 mounts it) | `requester-shell/` |
| Requester navigation | `RequesterNavItem` (label key, link, icon, optional `permission` + `permissionMode`, children), `visibleRequesterNav()`, `REQUESTER_SHELL_OPTIONS` (nav items, brand link, notifications feed link; empty default) | `requester-shell/requester-nav.ts` |
| `app-footer` | Brand logo and name, links to the portal privacy, terms and contact pages, the copyright line (year from `APP_CLOCK`, Saudi time). Input-free | `app-footer/` |
| Brand | `LayoutBrand`: the name in the current language and the logo, from the `branding` block of `config.json`; without one, the `layouts.brand.name` text and no logo | `app-footer/layout-brand.ts` |
| `app-notifications-menu` | The header bell: unread badge (`99+` cap), a polite live announcement of the unread count, a dropdown of the newest 5 with mark-read, and a "view all" link. Escape closes it, on the bell or inside the panel. Inputs `items`, `viewAllLink`, optional `unreadTotal` (the server's unread total; else counted from `items`); output `markRead` | `notifications-menu/` |
| Notifications model | `NotificationItem` `{ id, titleText, bodyText, at, read, link? }`, the `NotificationsSource` interface and `NOTIFICATIONS_SOURCE` (default `EmptyNotificationsSource`; the app provides `ApiNotificationsSource` from features/notifications), the optional `unreadCount` signal, `NOTIFICATIONS_FEED_LINK` (the feed page link the app provides; default null), `unreadCount`, `badgeText`, `filterNotifications`, `NotificationFilter` (int enum), `formatNotificationTime` | `notifications-menu/notifications.ts` |
| `app-notifications-bell` | The bell wired to the app's notifications: `app-notifications-menu` over `NOTIFICATIONS_SOURCE` (items, server unread total) with the `NOTIFICATIONS_FEED_LINK` "view all" link; input-free, the staff shell places it | `notifications-menu/notifications-bell.component.ts` |
| `app-notifications-feed` | The full list for a feed page: read/unread filter (`aria-pressed`), empty state per filter, mark-read and load-more. Inputs `items`, `hasMore`, `loading`; outputs `markRead`, `loadMore` | `notifications-feed/` |
| `app-language-switcher` | One button offering the other language, named in its own script; calls `LanguageService.set()` | `language-switcher/` |
| `app-user-profile-menu` | The user's display name and a menu with logout (`AuthService.logout()`); closes on Escape and outside click | `user-profile-menu/` |
| `app-error-dialog` | Renders `ErrorDialogStore` (message key, reference code, dismiss). Hosted once by the root `App` | `error-dialog/` |
| Spec helpers | `fakeAuth()`, `fakeLanguage()`, `layoutTestProviders()` (translations echo their key), `fixedClock()`, `aNotification()`, `BOTH_DIRECTIONS` + `useDirection()` for RTL/LTR runs, `ANY_URL_ROUTES` | `layout.testing.ts` |

## Use

```ts
// app.routes.ts: the staff pages are children of the shell
{ path: '', canActivate: [authGuard], loadComponent: () => import('./layouts/app-shell/app-shell.component')
    .then(m => m.AppShellComponent), children: [ ... ] }

// app.routes.ts: the public portal frame around the portal pages (their paths are in PUBLIC_ROUTE_PATHS)
{ path: RoutePaths.Portal, component: PortalShellComponent, children: PORTAL_ROUTES }

// P7: the requester frame, a layout route behind authGuard listed in LAYOUT_ROUTES, with its options
{
  path: RoutePaths.Requests,
  canActivate: [authGuard],
  providers: [{ provide: REQUESTER_SHELL_OPTIONS, useValue: REQUESTER_SHELL }],
  loadComponent: () => import('./layouts/requester-shell/requester-shell.component').then(m => m.RequesterShellComponent),
  children: [ ... ],
}
```

```ts
// a requester navigation (in src/app, since it points into features)
export const REQUESTER_SHELL: RequesterShellOptions = {
  homeLink: rootLink(RoutePaths.Requests),
  notificationsLink: rootLink(RoutePaths.Notifications),
  navItems: [
    { id: 'my-requests', labelKey: 'requests.nav.mine', link: '/requests', icon: 'file', permission: PermissionKeys.RequestsView },
    { id: 'faq', labelKey: 'layouts.portal-nav.faq', link: portalLink(PortalPaths.Faq) },
  ],
};
```

```html
<!-- a notifications feed page wires the source to the feed -->
<app-notifications-feed [items]="source.items()" [hasMore]="source.hasMore()" [loading]="loading()"
  (markRead)="source.markRead($event)" (loadMore)="more()" />

<!-- a full-page screen outside the shells can still offer the language switch -->
<app-language-switcher />
```

Features link to top-level pages with `appLink(RoutePaths.X)` (router commands) or `rootLink()` / `portalLink()` (URL
strings, what `dga-header` takes). (`RoutePaths.Requests`, `RoutePaths.Notifications` and `PermissionKeys.RequestsView`
above are illustrations of P7 names.)

## Extend

- **A new page in the staff navigation:** add its path to core `RoutePaths`, add the route as a shell child in
  `app.routes.ts` with `permissionGuard()` and its key, then add one `APP_NAV_ITEMS` entry (`src/app/app-nav.ts`) whose
  target carries the same key, a `layout.nav.<name>` label in both `ar.json` and `en.json`, and an icon from the ng-dga
  icon set.
- **A new requester navigation entry:** one `RequesterNavItem` in the app's `REQUESTER_SHELL_OPTIONS` with the same
  `permission` as the route it opens. Leave `permission` out only for a page every visitor may open.
- **A new portal page in the header:** add its path to core `PortalPaths`, the route to `PORTAL_ROUTES`, then one
  `PORTAL_NAV_ENTRIES` entry with a key in `PortalNavTranslationKeys` and both bundles.
- **Notifications:** the app wires `provideApiNotifications()` (features/notifications: the API source as `NOTIFICATIONS_SOURCE`, the feed page as `NOTIFICATIONS_FEED_LINK`); a shell shows the bell with `<app-notifications-bell />`. A source implements `NotificationsSource` (`markRead`/`loadMore` never reject; the HTTP error interceptor reports a refusal) and may add `unreadCount` for the badge.
- **A new layout route** (a frame behind `authGuard`, holding pages as `children`): export the route object and add it
  to `LAYOUT_ROUTES` in `src/app/public-routes.ts`. The route gate exempts a layout by identity (`routesForGate`), never
  by path; never add its path to `PUBLIC_ROUTE_PATHS`. A layout route without pages fails the gate, which is why the
  requester shell is not mounted before P7.
- **A new header action:** a component in its own folder here, with a spec; gate it with `PermissionStore.has()` when
  it leads to a gated page, and project it into `dga-header` with the `dgaHeaderActions` attribute.
- Every component with logic gets a `*.spec.ts` beside it, run once per direction (`describe.each(BOTH_DIRECTIONS)`).
  Specs use `layout.testing.ts`. Texts live in a `*-translation-keys.ts` catalogue, which the i18n check reads.

## Rules

- Layouts import only core, shared and `@codelab/ng-dga` (ESLint boundaries); never a feature. What points into
  features (staff nav items, requester options) is provided by the app through an injection token.
- No raw text: every label is a translation key under `layouts.*` (P2 pieces: `layout.*`) in both bundles; ng-dga
  components get translated strings through their inputs. Notification texts arrive already translated from the
  source. No literal colours or physical CSS (`left`, `right`, `margin-left`): tokens and logical properties only.
- A navigation entry is shown only when the user holds its key(s); the route it opens is gated by the same key, and
  the server still authorizes every call.
- No `Date.now()` / `new Date()` for "now": the clock is `APP_CLOCK`. Times show in Saudi time (`LAYOUT_TIME_ZONE`).
- The notifications components never call the API; data enters through inputs, intent leaves through outputs.

## Gotchas

- Specs run in JIT; the starter's Vitest plugin adds the metadata signal `input()`/`output()`/`viewChild()` need, so
  set inputs with `fixture.componentRef.setInput()`. Router state is still easiest to read through `ActivatedRoute`.
- Vitest caches transformed files by the `.ts` content, and the component's template is inlined at that step: after
  editing only an `.html` file, change the `.ts` too (or clear the cache) or the spec runs the old template.
- The text of an element that contains a `dga-icon` includes the icon's Private Use Area character; assert on the
  label element (`:scope > span`, a class), or use `toContain`.
- jsdom applies no media queries, so `dga-header` specs see the phone layout: the menu button is present and the
  navigation panel opens from it.
- A spec that clicks a router link needs a route for the URL (`ANY_URL_ROUTES`), or the router rejects with NG04002.
- The error dialog lives in the root `App`, not the shell, so failures on pages outside the shell show too.
- `formatNotificationTime` uses `Intl` with `ar-SA-u-ca-gregory-nu-latn` / `en-GB`: Gregorian dates with Latin digits
  in both languages.
- Text on the dark footer must set `--dga-color-text-on-color` itself: the global paragraph colour (dark neutral)
  otherwise wins on a `<p>` and drops the contrast to about 1.8:1.
