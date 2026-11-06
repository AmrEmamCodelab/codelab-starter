# Starter.DocsGen

## Purpose
Generates `docs/generated/` from the code, so the API contract, the permission catalog, the endpoint list, the error
codes, the configuration keys, the status enums and the workflows can never drift from what the host actually runs. It starts the real `Starter.Api`
host in-process (`WebApplicationFactory<Program>` on TestServer, Development, no database), fetches
`/openapi/v1.json`, reads the permission catalog sources and the endpoint data source from the host's services, and
reads every `*ErrorCodes` class and every options class with a `SectionName` by reflection over the host, its modules
(discovered from `ModuleAssemblyAttribute`, like the host) and the Codelab packages they reference. Status enums
(`StatusScanner`: every public `int` enum whose name ends with `Status`) are read from the host's own assemblies only
(the kernel and the modules, `SolutionAssemblies.OwnAssemblies`), never from the packages. Workflows
(`WorkflowCollector`) are the `IRecordWorkflowDefinition`s the host registered (`AddRecordWorkflow<TDefinition>()`),
cross-checked against every concrete definition class in those own assemblies.

Outputs: `openapi.v1.json`, `permissions.json`, `permissions.md`, `endpoints.md`, `error-codes.md`, `config-keys.md`,
`statuses.md`, `README.md`, `workflows/README.md` and one `workflows/<record-type>.md` per workflow. `statuses.md`
lists each enum with its values, names and summaries, grouped by owner, then a "Workflow statuses" section: each
record type's statuses (value, key, initial, terminal, requester/staff/admin wording keys) with a link to its sheet.
A workflow sheet is the client sign-off document: a Mermaid `stateDiagram-v2` (every status as `key (value)`, `[*]`
into the initial status and out of every terminal one, terminal statuses in the `terminal` class, one arrow per rule
labelled `action (permission, reason none|optional|required)`), the status table and the transition table (side,
permission, owner only, reason and its field, label key, notification decision). `workflows/README.md` is the index
and is written with zero workflows too.
`openapi.v1.json` and `permissions.json` are the inputs of the frontend `npm run api:gen`.

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
- **A new workflow column or diagram detail:** the catalog is read as is (`WorkflowDoc.Catalog`); add the column in
  `WorkflowRenderer` (transitions) or `WorkflowText.StatusTable` (statuses, shared with `statuses.md`), the diagram
  detail in `MermaidStateDiagram`, then update the golden diagram and the row assertions in `WorkflowsTests.cs`.
- **A new required start-up setting:** when the host refuses to start without a new secret, add a throwaway value in
  `DocsHostSettings` (a constant key; never a module type).

## Rules
- Output is deterministic: ordinal sorting everywhere, LF only, one trailing newline, UTF-8 without BOM, no timestamp,
  no `servers` entry in the OpenAPI document (`starterdocsgen.deterministic`).
- DocsGen never references a module type (`starterdocsgen.no-module-namespace`), so `tools/remove-module.ps1` keeps it
  building; module keys it must set are plain constants in `DocsHostSettings`.
- `docs/generated/**` is never hand-edited; CI runs the `--check` form.
- The writer never deletes: a file it does not generate is reported (and fails `--check`), never removed. That holds
  in `workflows/` too: the sheet of a removed record type stays `unexpected:` until someone deletes it.
- Mermaid ids are `s<value>` (`sn<value>` for a negative one), never the status key, so any key is safe; labels drop
  `"`, `;`, `:`, `#` and line breaks (`MermaidStateDiagram.Label`).

## Gotchas
- `statuses.md` lists every public `*Status` int enum, including result enums that never reach the database (for
  example `SheetReadStatus`); a status that should not be listed needs another name. A non-`int` enum is skipped
  (and already breaks `base.enums-as-int`).
- The host starts with `UseContentRoot(AppContext.BaseDirectory)`: the API's `appsettings*.json` are copied into this
  project's output. A copied `appsettings.Development.local.json` is dropped from the configuration sources, so a
  switch set on one machine cannot change the docs; `DocsHostSettings` supplies the secrets instead.
- The host runs with migrations off, in-memory security stores, `IStartupTask`s removed and an unreachable connection
  string; anything that opens the database during start-up fails the tool (by design).
- Descriptions come from the XML documentation files next to each assembly (`GenerateDocumentationFile` is on in the
  SDK). A missing `<summary>` gives an empty cell; overloaded actions get no summary (doc ids are matched by name).
- An options class whose only bindable member is a get-only collection (for example `Identity:DevUsers`) is documented
  as an array at the section root, because that is how the module binds it.
- **Workflow collection fails the tool (exit 3)** when a definition class is declared but not registered, when a
  record type is not a kebab key (it is a file name) or when two definitions share one. An invalid catalog already
  stops the host (`WorkflowCatalogStartupCheck`), so DocsGen never documents a workflow the host would refuse.
- Workflow statuses are int constants of the record type's statuses class, not enums, so `StatusScanner` does not see
  them; they reach `statuses.md` only through the registered catalog.
- Run the tool from the repository's own output path: `WebApplicationFactory` locates the solution root from the
  build output, so `dotnet run` with an `--artifacts-path` outside the repository exits 3 ("Solution root could not
  be located"). The tests do not have this limit.
- Health endpoints map plain request delegates, so their owner shows as `Platform` and their method as `ANY`.
- Settings a module validates on start must be supplied by `DocsHostSettings` as plain keys (no module types). Phase 6: `Attachments:ServiceBaseUrl` (an unreachable `https://attachments.invalid`) and a throwaway `Codelab:Security:ServiceAssertions:Client` with a P-256 key generated per run; DocsGen never calls the service.
