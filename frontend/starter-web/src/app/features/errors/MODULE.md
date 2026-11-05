# Web error pages (`features/errors`)

## Purpose

The 404 and 403 pages. Both render without a session, outside the shell.

| Page | Route | What it offers | Files |
| --- | --- | --- | --- |
| `NotFoundPageComponent` | `**` (the last route) | A link home | `not-found/` |
| `UnauthorizedPageComponent` | `RoutePaths.Unauthorized` | Signed in: home, or sign in as another user (logout). Signed out: the login link | `unauthorized/` |

Both share `error-page.scss`.

## Use

`permissionGuard` and core's forbidden interceptor navigate to the 403 page with `unauthorizedTree()`. Any unknown URL
falls through to the 404 page.

## Extend

- **Another error page** (for example a maintenance notice): a component in its own folder here, a route in
  `app.routes.ts` before the `**` catch-all, keys under `error-pages.*` in both bundles and a case in
  `error-pages.spec.ts`.

## Rules

- No raw text; no physical CSS; links come from `appLink(RoutePaths.X)`, never path literals.
- The 403 page never reveals which permission was missing.
- Both routes are in `PUBLIC_ROUTE_PATHS` (`src/app/public-routes.ts`): they render without a permission key. Their
  buttons carry `data-public-action` (the template permission gate, `base.fe-buttons-gated`), because every visitor
  may take them: "sign in as another user" is a logout.

## Gotchas

- An authenticated user without any reachable page would bounce between the dashboard and this page; "sign in as
  another user" ends the session so they can.
