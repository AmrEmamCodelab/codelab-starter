---
name: new-endpoint
description: "Scaffold an API endpoint in an existing backend module: a thin controller with [HasPermission], PublicId ids, the Application use case with its data port and problem+json refusal path, a validator for writes, a placeholder store, unit tests, the error code, the permission key and the DI registration, then regenerate docs/generated and the TypeScript client. Use when the work needs a new action, command, query or API operation on an existing module."
---

# New endpoint

Load the `coding-standards` skill first ([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)).
The full recipe is [docs/recipes/add-endpoint.md](../../../docs/recipes/add-endpoint.md).

## Quick start

From the repo root, with the private Node on `PATH`:

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"
node tools/scaffold/new-endpoint.mjs --module Records --name GetRecord --method GET --route records
node tools/scaffold/new-endpoint.mjs --module Records --name CreateRecord --method POST --route records --permission create
```

- `--method`: `GET` (by PublicId), `POST` (create, body), `PUT` (update by PublicId, body), `DELETE` (by PublicId, 204).
- `--route`: kebab segment(s) under `/api/<module-kebab>/`; the id segment is added for GET, PUT and DELETE.
- `--permission`: kebab action; default `view` (GET) or `manage` (writes). A missing key is added to the catalog.

Same arguments again: no change (exit 0). A clash with existing content, an unknown module or a bad name, method or
route is refused (exit 1, nothing written). It then reruns the rules compile, `Starter.DocsGen` and
`npm run api:gen --workspace frontend/starter-web`.

## Workflow

1. Pick the module (create it first with the `new-module` skill), a verb-noun name and the permission. Run the command.
2. Hand-fill business logic only:
   - `Infrastructure/<Name>Store.cs`: replace the placeholder body with the real data access.
   - `Application/<Name>.cs`: business rules and extra refusals (new `<Module>ErrorCodes` constants), and the real
     fields of the command, result and response (the validator checks every one, limits from `<Module>Limits`).
   - Never add logic to the controller; never use a literal for a key, route or code.
3. Extend `backend/tests/Modules/Starter.Modules.<Module>.Tests/<Name>Tests.cs`: one test per rule and refusal;
   replace the placeholder-store test once the store is real.
4. Add `errors.<CODE>` for each new error code to the frontend `ar` and `en` bundles.
5. Update the module's `MODULE.md` (Use: the endpoint; Gotchas: what callers must know).

## Verify

```powershell
$dotnet = "E:\codelab-tools\dotnet10\dotnet.exe"
& $dotnet build backend/src/Starter.Api/Starter.Api.csproj -warnaserror
& $dotnet test backend/tests/Modules/Starter.Modules.<Module>.Tests
& $dotnet test backend/tests/Starter.Architecture.Tests
& $dotnet test backend/tests/Starter.Contract.Tests
& $dotnet run --project backend/tools/Starter.DocsGen -- --out docs/generated --check
npm run api:gen:check --workspace frontend/starter-web
node tools/rules-engine/compile.mjs --check
```

An endpoint that must stay anonymous or authenticated-only is not scaffolded this way: it carries
`[AllowAnonymous]` or `[AuthenticatedUser]` and an entry in `backend/tests/Starter.Contract.Tests/EndpointAllowlist.cs`.

## More

Generator flags, outputs and exit codes: [tools/scaffold/README.md](../../../tools/scaffold/README.md).
