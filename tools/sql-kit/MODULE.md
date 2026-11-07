# tools/sql-kit

## Purpose

Generates the DBA migration scripts of a release (build plan phase 8): one idempotent, DBA-runnable file per DbContext
of the API, in the order the API migrates. The DBA flow that consumes them is in
[deploy/sql/README.md](../../deploy/sql/README.md); the deploy kit (`tools/kit/New-DeployKit.ps1`) puts them in its
`sql\` folder.

| File | What it is |
| --- | --- |
| `New-DbaSqlScripts.ps1` | The generator: builds the host into a temp work folder, lists the contexts, runs `dotnet ef migrations script --idempotent` per context, reshapes, writes `NN-<Context>.sql` (UTF-8 BOM, CRLF) and `sql-scripts.json`. |
| `SqlKit.psm1` | Pure functions: `ConvertTo-DbaSqlScript` (the reshaper), `Split-EfIdempotentScript` (its parser), `ConvertFrom-EfContextList`, `Get-DbaContextOrder`, `Get-DbaScriptFileName`. |
| `tests/SqlKit.Tests.ps1` | Pester 3.4 tests over `tests/fixtures/ef-idempotent.sql` (a synthetic EF script) and `expected-dba.sql` (its exact reshaped text). No dotnet, no database. |

## Use

```powershell
# Generate into an empty folder (dotnet-ef 10: -EfToolPath, else $env:CODELAB_DOTNET_EF, else dotnet-ef on PATH)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\sql-kit\New-DbaSqlScripts.ps1 -OutputPath $env:TEMP\release-sql
# Tests
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-Pester tools/sql-kit/tests/SqlKit.Tests.ps1 -EnableExit"
```

The SDK is resolved like `tools/build.ps1` (`$env:CODELAB_DOTNET`, the private SDK path, then `dotnet` on PATH). The
build goes to a temp folder through the `ArtifactsPath` / `UseArtifactsOutput` MSBuild properties, so the source
tree's `bin`/`obj` (and a running local stack) are never touched. dotnet-ef is not a repo tool: install it outside the
repository (`dotnet tool install dotnet-ef --version <EF version> --tool-path <folder>`).

What the reshaper produces for each EF block:

```sql
IF NOT EXISTS (
    SELECT * FROM [lookups].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261106090000_InitialCreate'
)
BEGIN
    EXEC(N'CREATE TABLE [lookups].[LookupTypes] (
    [Key] nvarchar(64) NOT NULL, ...
);');
END;
GO
```

## Extend

- **A new DbContext** needs nothing here: the generator asks the host. A new *kernel* context (outside
  `Starter.Modules.*`) that must run before others goes into `$script:DefaultKernelOrder` in `SqlKit.psm1`.
- **A new EF script shape** (a top-level statement EF starts to emit): teach `Split-EfIdempotentScript` a new
  `DbaSegmentKind` member (next explicit integer), render it in `ConvertTo-DbaSqlScript`, add it to the fixture and
  regenerate `expected-dba.sql` only after reading the diff. The parser refuses anything it does not know, by design.
- **Another output file** (for example a combined script): build it from `ConvertTo-DbaSqlScript` results in
  `New-DbaSqlScripts.ps1`, and list it in `sql-scripts.json`.

## Rules

- Windows PowerShell 5.1, `Set-StrictMode -Version Latest`, `$ErrorActionPreference = 'Stop'`; no PowerShell 7 syntax.
- `SqlKit.psm1` stays pure (no process, file or network access) so every rule of the reshaper is unit tested.
- Enums are integers with explicit values; every literal is in a constants block.
- The reshaper never guesses: an unknown top-level statement, an unterminated block, unbalanced transactions or a
  sqlcmd variable token (`$(`) in a body is an error with the line number.
- Generated scripts are never committed or edited; fix the migration and regenerate.
- No hosts, logins or database names in the scripts, the fixtures or the tests.

## Gotchas

- **The host must build its services.** `dotnet ef` runs `Program.cs` up to `Build()` to find the contexts. The
  generator sets the Development environment for that (Production refuses to build without server secrets such as
  `Observability:UserIdHash:Key`); no database is opened, nothing is started. An empty context list prints the host's
  `warn:` lines.
- **Line endings.** The fixtures may be checked out with LF or CRLF; the tests compare with LF. The generated files are
  always CRLF.
- **EF's body indent is removed exactly.** EF indents every body line by four spaces, string literal lines included;
  the reshaper removes those four and nothing else, so multi-line literals keep the text the migration wrote.
- **Nested EXEC doubles twice.** EF already wraps some statements (seed inserts, views, default-constraint drops) in
  `EXEC(N'...')`; inside the reshaped `EXEC` their quotes are doubled again (`''''`). That is correct SQL.
- **Slow first run.** The Release build of the host takes about a minute; each context then takes a few seconds.
