---
name: new-permission
description: "Add a permission key to a module's <Module>PermissionKeys and its IPermissionCatalogSource, then regenerate docs/generated/permissions.* and the frontend PermissionKeys, and wire the key to endpoints, routes and buttons. Use when the work needs a new permission, a new access right, or gating an action, route or button."
---

# New permission

Load the `coding-standards` skill first ([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)).

## Quick start

From the repo root, with the private Node on `PATH`:

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
node tools/scaffold/new-permission.mjs --module Records --action approve --description "Approve submitted records."
```

The key is `<module-kebab>.<action>` (`records.approve`), the constant is the PascalCase action
(`RecordsPermissionKeys.Approve`). Running it again is a no-op (exit 0). A non-kebab action, an unknown module or a
constant that already holds another key is refused (exit 1, nothing written). It then reruns the rules compile,
`Starter.DocsGen` (`docs/generated/permissions.json` and `permissions.md`) and `npm run api:gen`, which regenerates
`frontend/starter-web/src/app/core/permissions/permission-keys.generated.ts`.

## Workflow

1. Choose the action as a kebab verb (`approve`, `export-report`). Pass `--description` with what holding the key
   allows; the catalog and the generated docs show it.
2. Run the command. `new-endpoint --permission <action>` also adds a missing key, so a key for a new endpoint
   needs no separate run.
3. Hand-wire the key where it gates something (constants only, never literals):
   - an endpoint: `[HasPermission(<Module>PermissionKeys.<Action>)]`;
   - a route: route data `{ permission: PermissionKeys.<Generated> }` with `permissionGuard`;
   - a button: `PermissionStore.has(PermissionKeys.<Generated>)`.
4. Tests: the module's `<Module>ModuleTests` already check that every declared key is in the catalog and well
   formed; add a test for each endpoint guard or frontend gate you wire.
5. Update the module's `MODULE.md` (Use: the key and what it gates).

## Verify

```powershell
$dotnet = "E:\codelab-tools\dotnet10\dotnet.exe"
& $dotnet test backend/tests/Modules/Starter.Modules.<Module>.Tests
& $dotnet test backend/tests/Starter.Architecture.Tests
& $dotnet test backend/tests/Starter.Contract.Tests
& $dotnet run --project backend/tools/Starter.DocsGen -- --out docs/generated --check
npm run api:gen:check --workspace frontend/starter-web
```

Granting the key to users is data, not code: the Access module grants it (role and grant editor in phase 3).

## More

Generator flags and exit codes: [tools/scaffold/README.md](../../../tools/scaffold/README.md).
