# Web dashboard (`features/dashboard`)

## Purpose

The landing page inside the shell (route `RoutePaths.Dashboard`, `/`), gated by `PermissionKeys.DashboardView`
(`dashboard.view`). Empty in P2: it greets the user by display name.

## Use

Registered as a shell child in `app.routes.ts` with `permissionGuard()` and `data: { permission: DashboardView }`. The
shell's navigation offers it to users holding the same key.

## Extend

- **Summary cards:** a `dga-card` per area. Load data through core's `DashboardClient` (or a new client in core/api),
  from a service in this feature with its own spec; the component only renders the service's signals.
- **An action button** carries `*hasPermission="PermissionKeys.X"` (a generated key) or, for an action every visitor
  may take, `data-public-action`; `npm run lint` refuses any other button (`base.fe-buttons-gated`,
  docs/recipes/add-permission.md).

## Rules

- No `HttpClient`; data comes through core clients. No raw text; keys under `dashboard.*` in both bundles.
- Every route and action is gated by a typed key from `permission-keys.generated.ts` (route: `route-gates.spec.ts`;
  buttons: `tools/permission-gates.mjs` in `npm run lint`).

## Gotchas

- `GET api/dashboard` returns an empty object in P2, so the page does not call it yet.
