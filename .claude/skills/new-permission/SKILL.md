---
name: new-permission
description: "Use when the work needs a new permission or access right ('only inspectors may approve', 'managers can export'), or gating an action, route, menu or button behind one, and whenever a check flags a permission key written as a string literal. Adds the key to both catalogs in one run (the module's <Module>PermissionKeys constant and catalog description in C#, the MODULE.md note and the add-permission recipe in module.rules.yaml, then the generated docs/generated/permissions.* and frontend PermissionKeys), then gates the endpoint with [HasPermission], the route with data.permission and the buttons with *hasPermission."
---

# New permission

Load the `coding-standards` skill first ([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)). The full
recipe is [docs/recipes/add-permission.md](../../../docs/recipes/add-permission.md).

## When this applies

- Someone must be allowed (or not) to do or see something, and no key in the module's `<Module>PermissionKeys` fits.
  A key for a new endpoint comes with `new-endpoint --permission <action>`; a status flow names an existing key.
- **Refusal counterpart:** no hook refusal names this skill directly. The rules `base.no-string-permission-key`
  (C#) and `base.fe-no-string-permission-key-in-template` (templates) in `rules/base.rules.yaml` refuse a key
  written as a string literal, and `base.fe-routes-gated` / `base.fe-buttons-gated` refuse an ungated route or
  button: the fix is a generated constant, which this skill creates. An endpoint without `[HasPermission]` is
  refused with "Use the `new-endpoint` skill".

## What one run emits

| Part | Emitted |
| --- | --- |
| Code | yes: the `<Module>PermissionKeys` constant and its `<Module>PermissionCatalogSource` description |
| Rules | yes: `docs/recipes/add-permission.md` kept in the module's `module.rules.yaml` `recipes` (added once), then the rules compile |
| MODULE.md | yes: one note at the end of Use (key, constant, description); none when the key already existed |
| Unit tests | covered by the module's existing `<Module>ModuleTests` (every declared key is in the catalog and well formed); add guard and button specs where you wire the key (step 4) |
| Generated docs | rerun automatically (rules compile, `Starter.DocsGen`, `npm run api:gen --workspace frontend/starter-web`); `--no-gen` prints the two catalog commands, `--skip-generate` and `--dry-run` print all three as `next:` lines |

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
and `npm run api:gen --workspace frontend/starter-web` (`frontend/starter-web/src/app/core/permissions/permission-keys.generated.ts`). With `--no-gen`
it writes the C# side only and prints the exact two regenerate commands (`next: ...`); `--dry-run` lists the files it
would write and writes nothing. The frontend constant line comes after the generator lines. Running it again is a no-op
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
5. Read the note the run added to the module's `MODULE.md` Use section and add what the key gates (endpoint, route,
   buttons) once you have wired it.

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
