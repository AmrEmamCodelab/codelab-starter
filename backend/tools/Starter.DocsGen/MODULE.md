# Starter.DocsGen

## Purpose
Generates `docs/generated/` from the code, so the API contract, the permission catalog, the endpoint list, the error
codes and the configuration keys can never drift from what the host actually runs. It starts the real `Starter.Api`
host in-process (`WebApplicationFactory<Program>` on TestServer, Development, no database), fetches
`/openapi/v1.json`, reads the permission catalog sources and the endpoint data source from the host's services, and
reads every `*ErrorCodes` class and every options class with a `SectionName` by reflection over the host, its modules
(discovered from `ModuleAssemblyAttribute`, like the host) and the Codelab packages they reference.

Outputs: `openapi.v1.json`, `permissions.json`, `permissions.md`, `endpoints.md`, `error-codes.md`, `config-keys.md`
and `README.md`. `openapi.v1.json` and `permissions.json` are the inputs of the frontend `npm run api:gen`.

## Use
```powershell
# from the repository root (the .NET 10 SDK is private here: E:\codelab-tools\dotnet10\dotnet.exe)
dotnet run --project backend/tools/Starter.DocsGen -- --out docs/generated           # write
dotnet run --project backend/tools/Starter.DocsGen -- --out docs/generated --check   # exit 1 and list drift
npm run api:gen --workspace frontend/starter-web                                     # then the frontend client
dotnet test backend/tools/Starter.DocsGen.Tests
```
Exit codes: 0 written or up to date, 1 drift (`missing:`, `changed:`, `unexpected:` per file), 2 usage, 3 the host
could not be read. The tool needs no local secrets file and no SQL Server.

## Extend
- **A new generated file:** add its name to `GeneratedFileNames`, a renderer in `Rendering/` (pure: surface in, text
  out, ordinal sorting, built with `MarkdownText`), an entry in `GeneratedDocSet.Render`, and renderer tests on
  fixtures. Rerun the tool and commit the output.
- **A new convention to document:** collect it in `Collection/` by reflection over `SolutionAssemblies.Discover`, add it
  to `ApiSurface`, and test the scanner on fixture types in the test project.
- **A new required start-up setting:** when the host refuses to start without a new secret, add a throwaway value in
  `DocsHostSettings` (a constant key; never a module type).

## Rules
- Output is deterministic: ordinal sorting everywhere, LF only, one trailing newline, UTF-8 without BOM, no timestamp,
  no `servers` entry in the OpenAPI document (`starterdocsgen.deterministic`).
- DocsGen never references a module type (`starterdocsgen.no-module-namespace`), so `tools/remove-module.ps1` keeps it
  building; module keys it must set are plain constants in `DocsHostSettings`.
- `docs/generated/**` is never hand-edited; CI runs the `--check` form.
- The writer never deletes: a file it does not generate is reported (and fails `--check`), never removed.

## Gotchas
- The host starts with `UseContentRoot(AppContext.BaseDirectory)`: the API's `appsettings*.json` are copied into this
  project's output. A copied `appsettings.Development.local.json` is dropped from the configuration sources, so a
  switch set on one machine cannot change the docs; `DocsHostSettings` supplies the secrets instead.
- The host runs with migrations off, in-memory security stores, `IStartupTask`s removed and an unreachable connection
  string; anything that opens the database during start-up fails the tool (by design).
- Descriptions come from the XML documentation files next to each assembly (`GenerateDocumentationFile` is on in the
  SDK). A missing `<summary>` gives an empty cell; overloaded actions get no summary (doc ids are matched by name).
- An options class whose only bindable member is a get-only collection (for example `Identity:DevUsers`) is documented
  as an array at the section root, because that is how the module binds it.
- Health endpoints map plain request delegates, so their owner shows as `Platform` and their method as `ANY`.
