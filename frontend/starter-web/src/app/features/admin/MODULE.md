# Web access administration (`features/admin`)

## Purpose

The access administration area (route `RoutePaths.Admin`), gated by `PermissionKeys.AccessAdminView`
(`access.admin.view`). A placeholder in P2; user and role management arrive with the Access module's admin endpoints.

## Use

Registered as a shell child in `app.routes.ts` with `permissionGuard()` and `data: { permission: AccessAdminView }`.
A user without the key is sent to the 403 page, and the shell does not offer the link.

## Extend

- **A screen** (user list, role editor): a child route of `RoutePaths.Admin` with its own permission key from the
  Access module, a service in this feature calling core's `AccessClient`, and specs for both.
- Each action button is gated by its own typed key, not only by the page key.

## Rules

- No `HttpClient`; no raw text (keys under `admin.*` in both bundles); typed permission keys only.

## Gotchas

- `GET api/access/admin` returns an empty placeholder in P2, so the page does not call it yet.
