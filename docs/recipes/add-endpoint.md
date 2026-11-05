# Recipe: add an endpoint

Adds one API operation to an existing backend module, with its permission, use case, refusal path, validator,
placeholder data access, unit tests, regenerated docs and regenerated TypeScript client. The `new-endpoint` skill
(`.claude/skills/new-endpoint/SKILL.md`) runs this recipe.

## 1. Generate

Run from the repo root, with the private Node on `PATH`:

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
node tools/scaffold/new-endpoint.mjs --module Records --name GetRecord --method GET --route records
node tools/scaffold/new-endpoint.mjs --module Records --name CreateRecord --method POST --route records
```

| Argument | Meaning |
| --- | --- |
| `--module` | An existing module (`backend/src/Modules/<Name>`). Create one first with `node tools/scaffold/new-module.mjs --name <Name>`. |
| `--name` | PascalCase use case name: a verb and a noun (`GetRecord`, `ApproveRecord`). |
| `--method` | `GET` (one record by PublicId), `POST` (create, body), `PUT` (update by PublicId, body) or `DELETE` (by PublicId, 204). |
| `--route` | The segment under `/api/<module-kebab>/`, lower-case kebab (`records`, `records/archive`). The id segment is added for GET, PUT and DELETE. |
| `--permission` | Optional kebab action (`approve`); the key becomes `<module-kebab>.<action>`. Default: `view` for GET, `manage` for writes. A missing key is added to the catalog. |

The command prints every file it created or changed. Running it again with the same arguments changes nothing.
It refuses (exit 1, nothing written) an unknown module, a bad name, method or route, and a name already used with
other content.

## 2. What you get

In `backend/src/Modules/<Module>/`:

- `Api/<Name>Controller.cs`: a thin `[ApiController]` with `[HasPermission(<Module>PermissionKeys.<Key>)]`, a route
  built from `<Module>Routes`, `PublicId<<Module>Ids>` ids in the route and the response, and the outcome mapped once
  by `OutcomeResults` (problem+json for every refusal).
- `Application/<Name>.cs`: the use case, its input and result records, the data port `I<Name>Store` and, for POST
  and PUT, the `<Name>Command` body with its `<Name>Validator`. The refusal path returns a `Refusal` with a code from
  `<Module>ErrorCodes` (404 for a missing record, 422 for a refused create, 400 for validation).
- `Infrastructure/<Name>Store.cs`: a placeholder implementation of the port (finds nothing, refuses creates).
- `Application/<Module>ErrorCodes.cs`, `Application/<Module>PermissionKeys.cs`, the catalog source and
  `Infrastructure/<Module>ModuleRegistration.cs` gain their lines above the `// scaffold:...` markers.

In `backend/tests/Modules/Starter.Modules.<Module>.Tests/`: `<Name>Tests.cs` with the success path, every refusal,
the validator's limits, the placeholder store and the endpoint's permission and route.

Then the generator reruns `node tools/rules-engine/compile.mjs`, `Starter.DocsGen` (`docs/generated/`) and
`npm run api:gen --workspace frontend/starter-web` when they exist.

## 3. Fill in the business logic (and only that)

1. Replace the body of `<Name>Store` with the real data access (the module's DbContext or another port).
2. Add the business rules to the use case: more refusals with new `<Module>ErrorCodes` constants, never literals.
3. Rename the placeholder `Name` field of the command, result and response to the real fields; the validator checks
   every field (limits come from `<Module>Limits`).
4. Extend `<Name>Tests.cs`: one test per rule and refusal; replace the placeholder-store test once the store is real.
5. Add `errors.<CODE>` for every new error code to the frontend `ar` and `en` bundles.
6. Update the module's `MODULE.md` (Use: the new endpoint; Gotchas: anything a caller must know).

Never edit the controller beyond binding and mapping, never put a `DbContext` in `Api/`, and never write a permission
key or route as a string at the call site.

## 4. Verify

```powershell
$dotnet = "E:\codelab-tools\dotnet10\dotnet.exe"
& $dotnet build backend/src/Modules/<Module>/Starter.Modules.<Module>.csproj -warnaserror
& $dotnet test backend/tests/Modules/Starter.Modules.<Module>.Tests
& $dotnet test backend/tests/Starter.Architecture.Tests
& $dotnet test backend/tests/Starter.Contract.Tests
& $dotnet run --project backend/tools/Starter.DocsGen -- --out docs/generated --check
npm run api:gen:check --workspace frontend/starter-web
node tools/rules-engine/compile.mjs --check
```

The full gate is `tools/build.ps1 -Target CI`; work is done when it is green.
