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
- **An action button** is gated with `PermissionStore.has(PermissionKeys.X)` and its key is a generated permission.

## Rules

- No `HttpClient`; data comes through core clients. No raw text; keys under `dashboard.*` in both bundles.
- Every route and action is gated by a typed key from `permission-keys.generated.ts`.

## Gotchas

- `GET api/dashboard` returns an empty object in P2, so the page does not call it yet.
