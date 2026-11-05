# Recipe: add a permission

Adds one permission key to both catalogs (the C# constant and catalog description in a backend module, and the
generated frontend `PermissionKeys`), then gates the endpoint, the route and the buttons with it. The
`new-permission` skill (`.claude/skills/new-permission/SKILL.md`) runs this recipe. Rules: `base.fe-permission-gated`,
`base.fe-routes-gated`, `base.fe-buttons-gated`, `base.no-string-permission-key` and
`base.fe-no-string-permission-key-in-template` (`rules/base.rules.yaml`).

## 1. Choose the key

The key is `<module-kebab>.<action>`: lower-case words joined by dots, each word kebab-case (`records.approve`,
`records.export-report`, at most 128 characters; `PermissionKeyFormat.Pattern` in `Starter.Application`). The action
is a verb for what holding the key allows: `view`, `manage`, `approve`, `export-report`. One key per decision a role
editor must be able to make; never reuse a key for an unrelated action because the same role holds both today.

The names follow from the key:

| Where | Name for `records.export-report` |
| --- | --- |
| C# constant | `RecordsPermissionKeys.ExportReport` in `backend/src/Modules/Records/Application/RecordsPermissionKeys.cs` |
| Catalog entry | `new(Key: RecordsPermissionKeys.ExportReport, Description: "...")` in `RecordsPermissionCatalogSource.cs` |
| Frontend constant | `PermissionKeys.RecordsExportReport` in `core/permissions/permission-keys.generated.ts` (generated) |

## 2. Generate both catalogs

Run from the repo root, with the private Node on `PATH`:

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
node tools/scaffold/new-permission.mjs --module Records --action export-report --description "Export the records report."
```

It adds the constant and the catalog entry, then reruns `node tools/rules-engine/compile.mjs`, `Starter.DocsGen`
(`docs/generated/permissions.json` and `permissions.md`) and `npm run api:gen --workspace frontend/starter-web`
(`permission-keys.generated.ts`), and prints the frontend constant name. Running it again changes nothing.
`new-endpoint --permission <action>` adds a missing key the same way.

With `--no-gen` it writes only the C# side and prints the two regenerate commands to run later, for example after
several keys:

```powershell
E:\codelab-tools\dotnet10\dotnet.exe run --project backend/tools/Starter.DocsGen/Starter.DocsGen.csproj -- --out docs/generated
npm run api:gen --workspace frontend/starter-web
```

Never hand-edit `docs/generated/**` or `permission-keys.generated.ts`; they are rebuilt from the C# catalog, and CI
fails when they drift (`--check`). The catalog is synced into the database at startup, so a new key needs no
migration; granting it to users is data (the Access role and grant editors), not code.

Then add the key's label to both bundles, `src/assets/i18n/ar.json` and `en.json`: `permissions.<key>` (a flat key
inside the `permissions` object, for example `"records.export-report": "Export the records report"`), and
`admin.access.permission-groups.<branch>` for a new top segment or branch. The role editor and the grant tree label
keys from these entries; a missing label falls back to the English catalog description.

## 3. Gate the endpoint

```csharp
[HasPermission(RecordsPermissionKeys.ExportReport)]
```

on the controller action, never a string (`base.no-string-permission-key`). The contract tests fail an endpoint
without `[HasPermission]` unless it is listed as anonymous in `backend/tests/Starter.Contract.Tests/EndpointAllowlist.cs`.

## 4. Gate the route

Every route that renders a page carries the key in its route data, on itself or on an ancestor, and
`permissionGuard()` (on the route or an ancestor, in `canActivate`, `canActivateChild` or `canMatch`) reads it. The
gate counts only a guard tagged with `Symbol.for('codelab.ng-tools.permission-guard')`, which `permissionGuard()`
sets; a route with a key but no tagged guard fails as `missing-guard`:

```ts
{
  path: RecordsPaths.Report,
  canActivate: [permissionGuard()],
  data: { [RouteDataKeys.Permission]: PermissionKeys.RecordsExportReport },
  loadComponent: () => import('./report/report-page.component').then(m => m.ReportPageComponent),
}
```

`frontend/starter-web/src/app/route-gates.spec.ts` runs `assertRoutesGated` from `@codelab/ng-tools/routes` over the
app's route table and fails, with the full path, every page route without a key on itself or an ancestor. Two cases
need a matching change there:

- **A lazily loaded area** (`loadChildren`): add its child array to `LAZY_ROUTE_TABLES` in that spec, keyed by the
  full path of the route that loads it (`admin`, `admin/access`). A `loadChildren` route missing from the list fails.
- **A page every visitor may open** (sign-in, activation, an error page): add its full path to `PUBLIC_ROUTE_PATHS`
  in `src/app/public-routes.ts` instead of a key. An entry exempts only the outermost route with that path.
  Never list the empty path: the shell is a layout, exempted by identity via `LAYOUT_ROUTES` and `routesForGate()` in
  `src/app/public-routes.ts`; pass `routesForGate(routes)` to `assertRoutesGated`.

The spec also fails a key that is not in the generated `PermissionKeys` (a retired or mistyped key).

## 5. Gate the buttons

Every action element under `features/**` (templates and inline templates) carries the structural directive or an
explicit public marker. An action element is a `<dga-button>`; a `<button>` with any event binding, a submit button,
or a typeless button inside a form that binds `(submit)`/`(ngSubmit)`; a `role="button"` element with an event
binding; and any element that binds `(click)` or `(confirm)`, such as an `<a>` or an editor-dialog host:

```html
<dga-button *hasPermission="PermissionKeys.RecordsExportReport" (click)="export()">{{ 'records.export' | t }}</dga-button>

<!-- one gate for a group of actions -->
<div class="toolbar" *hasPermission="PermissionKeys.RecordsManage">
  <dga-button (click)="edit()">{{ 'records.edit' | t }}</dga-button>
  <dga-button (click)="archive()">{{ 'records.archive' | t }}</dga-button>
</div>

<!-- an action every visitor may take -->
<dga-button data-public-action (click)="retry()">{{ 'records.retry' | t }}</dga-button>
```

The component exposes the keys to its template (`protected readonly PermissionKeys = PermissionKeys;`) and imports the
`HasPermissionDirective` from `shared/permissions/has-permission.directive.ts`. An `<ng-template [hasPermission]="...">`
wrapper also counts. The key is always a generated constant: `*hasPermission="'records.manage'"` is refused.

`npm run lint --workspace frontend/starter-web` runs `tools/permission-gates.mjs` (the `@codelab/ng-tools`
`permission-gates` check) and prints `file:line:column` for every button without a gate. Use `data-public-action` only
for actions that need no permission (sign in, switch language, sign out, retry); hiding a button is a convenience,
never the security boundary, so the endpoint behind it must still carry `[HasPermission]`.

## 6. Tests to add

- Backend: the module's `<Module>ModuleTests` already check that every declared key is in the catalog and well
  formed. Each endpoint test asserts its `[HasPermission]` key; an integration test proves a caller without the key
  gets 403.
- Frontend: a spec for each gated component that renders the button with the key granted and hides it without the
  key (a fake `PermissionStore`). The route table is covered by `route-gates.spec.ts`; add the lazy table or public
  path there when step 4 needs it.
- Update the module's `MODULE.md` (Use: the key and what it gates).

## 7. Verify

```powershell
$dotnet = "E:\codelab-tools\dotnet10\dotnet.exe"
& $dotnet test backend/tests/Modules/Starter.Modules.<Module>.Tests
& $dotnet test backend/tests/Starter.Contract.Tests
& $dotnet run --project backend/tools/Starter.DocsGen -- --out docs/generated --check
npm run api:gen:check --workspace frontend/starter-web
npm test --workspace frontend/starter-web
npm run lint --workspace frontend/starter-web
```

The full gate is `tools/build.ps1 -Target CI`; work is done when it is green.
