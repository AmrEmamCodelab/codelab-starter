---
name: new-endpoint
description: "Use when the work needs a new action, command, query or API operation on an existing backend module (create, read, update, delete, submit, export...), and whenever a hook or CI refusal says 'Use the `new-endpoint` skill' (an endpoint without [HasPermission] or an anonymous-allowlist entry). Scaffolds in one run: a thin controller with [HasPermission], PublicId ids, the Application use case with its data port and problem+json refusal path, a validator for writes, a placeholder store, unit tests, the error code, the permission key, the DI registration, the MODULE.md note and the add-endpoint recipe in module.rules.yaml, then regenerates docs/generated and the TypeScript client."
---

# New endpoint

Load the `coding-standards` skill first ([docs/standards/SKILL.md](../../../docs/standards/SKILL.md)).
The full recipe is [docs/recipes/add-endpoint.md](../../../docs/recipes/add-endpoint.md).

## When this applies

- The scenario needs the server to do or answer something new on a module that exists (create it first with
  `new-module`).
- **Refusal counterpart:** the PreToolUse hook and the CI content check (`node tools/rules-engine/check-content.mjs`)
  refuse an endpoint without `[HasPermission]` (or an `[AllowAnonymous]`/`[AuthenticatedUser]` endpoint without its
  `EndpointAllowlist` entry), with the message "Use the `new-endpoint` skill"; the contract tests
  (`backend/tests/Starter.Contract.Tests`) refuse the same in CI. Rules `base.endpoint-contract`,
  `base.thin-controllers` and `base.public-id-routes` in `rules/base.rules.yaml`.

## What one run emits

| Part | Emitted |
| --- | --- |
| Code | yes: controller, use case + port + validator (writes), placeholder store, error code, permission (when missing), DI registration |
| Rules | yes: `docs/recipes/add-endpoint.md` kept in the module's `module.rules.yaml` `recipes` (added once), then the rules compile |
| MODULE.md | yes: one note at the end of Use naming the route, use case, permission, refusal code and test file (plus a permission note when the key was new) |
| Unit tests | yes: `backend/tests/Modules/Starter.Modules.<Module>.Tests/<Name>Tests.cs` |
| Generated docs | rerun automatically (rules compile, `Starter.DocsGen`, `npm run api:gen --workspace frontend/starter-web`); with `--skip-generate` or `--dry-run` printed as `next:` lines |

A module without `MODULE.md` or `module.rules.yaml` (hand-made) gets a `note:` line naming what to add by hand.

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

`--dry-run` lists the files it would write and writes nothing. Same arguments again: no change (exit 0). A clash with existing content, an unknown module or a bad name, method or
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
5. Read the Use note the run added to the module's `MODULE.md`; add what callers must know (Gotchas) and the real
   request and answer shape.

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
