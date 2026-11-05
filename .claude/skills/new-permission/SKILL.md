---
name: new-permission
description: "Add a permission key to both catalogs (the module's <Module>PermissionKeys constant and catalog description in C#, and the generated frontend PermissionKeys), then gate the endpoint with [HasPermission], the route with data.permission and the buttons with *hasPermission. Use when the work needs a new permission, a new access right, or gating an action, route, menu or button."
---

# New permission

Load the `coding-standards` skill first ([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)). The full
recipe is [docs/recipes/add-permission.md](../../../docs/recipes/add-permission.md).

## Quick start

From the repo root, with the private Node on `PATH`:

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
node tools/scaffold/new-permission.mjs --module Records --action approve --description "Approve submitted records."
```

The key is `<module-kebab>.<action>` (`records.approve`), the C# constant is the PascalCase action
(`RecordsPermissionKeys.Approve`) and the frontend constant is the PascalCase of every word
(`PermissionKeys.RecordsApprove`, printed as the last line). The command updates both catalogs: it writes the C#
constant and the catalog description, then reruns the rules compile, `Starter.DocsGen` (`docs/generated/permissions.*`)
and `npm run api:gen` (`frontend/starter-web/src/app/core/permissions/permission-keys.generated.ts`). With `--no-gen`
it writes the C# side only and prints the exact two regenerate commands (`next: ...`). Running it again is a no-op
(exit 0). A non-kebab action, an unknown module or a constant that already holds another key is refused (exit 1,
nothing written).

## Workflow

1. Choose the action as a kebab verb (`approve`, `export-report`). Pass `--description` with what holding the key
   allows; the catalog, the role editor and the generated docs show it.
2. Run the command. `new-endpoint --permission <action>` also adds a missing key, so a key for a new endpoint
   needs no separate run. After `--no-gen`, run the two printed commands before building the frontend.
3. Wire the key where it gates something (generated constants only, never string literals):
   - the endpoint: `[HasPermission(<Module>PermissionKeys.<Action>)]`;
   - the route: `canActivate: [permissionGuard()]` and `data: { [RouteDataKeys.Permission]: PermissionKeys.<Name> }`
     on the route or an ancestor. A new lazy area adds its child array to `LAZY_ROUTE_TABLES` in
     `src/app/route-gates.spec.ts`; a page every visitor may open goes in `src/app/public-routes.ts` instead;
   - every `<dga-button>` and `<button (click)>` under `features/**`: `*hasPermission="PermissionKeys.<Name>"` on the
     button or an element around it, or `data-public-action` for an action that needs no permission.
4. Tests: the module's `<Module>ModuleTests` already check that every declared key is in the catalog and well
   formed; add a test for each endpoint guard and a component spec showing the button with the key and hiding it
   without. `route-gates.spec.ts` covers the route table.
5. Update the module's `MODULE.md` (Use: the key and what it gates).

## Verify

```powershell
$dotnet = "E:\codelab-tools\dotnet10\dotnet.exe"
& $dotnet test backend/tests/Modules/Starter.Modules.<Module>.Tests
& $dotnet test backend/tests/Starter.Architecture.Tests
& $dotnet test backend/tests/Starter.Contract.Tests
& $dotnet run --project backend/tools/Starter.DocsGen -- --out docs/generated --check
npm run api:gen:check --workspace frontend/starter-web
npm test --workspace frontend/starter-web     # route-gates.spec.ts: every page route gated or public
npm run lint --workspace frontend/starter-web # permission-gates: every action button gated or public
```

Granting the key to users is data, not code: the Access module's role and grant editors grant it.

## More

Generator flags and exit codes: [tools/scaffold/README.md](../../../tools/scaffold/README.md). The checks:
`@codelab/ng-tools` (`assertRoutesGated`, `codelab-ng-tools permission-gates`), rules `base.fe-routes-gated` and
`base.fe-buttons-gated` in `rules/base.rules.yaml`.
