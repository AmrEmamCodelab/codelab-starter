# Starter.Architecture.Tests

The architecture guardrails (ADR-004, ADR-011, ADR-012). Every check goes through the `Codelab.Testing` helpers and
reads `rules/rules.compiled.json`, so the rules have one source.

## What it checks

| Test | Rule |
| --- | --- |
| `ModuleBoundaryTests` | `base.module-boundaries`: `mayDependOn` / `mustNotDependOn` of every module and kernel project, by type dependency; every discovered module has compiled rules |
| `LayerTests` | ADR-004 layering by namespace (every type in `.Domain`, `.Application`, `.Infrastructure` or `.Api`; the `IModule` class may sit at the root); Domain innermost; `base.application-ports-only` (no EF Core, no DbContext, no ASP.NET Core or hosting type in Application signatures); `base.no-dbcontext-in-api`; kernel Domain and Application reference no hosting or persistence assembly |
| `ControllerTests` | `base.thin-controllers`: sealed, in `.Api`, constructor takes only Application use cases (never a port, repository or DbContext), every action names its HTTP method |
| `EnumTests` | `base.enums-as-int`: every enum under `backend/src` (parsed with Roslyn) has no non-int underlying type and an explicit value on every member; every compiled enum is `int` |
| `ProgramTests` | `ProgramCs_HasAtMost150Lines` |
| `PublishTests` | the host csproj sets `CopyToPublishDirectory="Never"` on `appsettings.*.local.json`, so a publish never carries the dev key rings |
| `DataBoundaryTests` | ADR-004 at the database: every string a module builds (literals, `const` concatenations and interpolations, folded with Roslyn across files) names no other owner's schema (`[schema].` or `FROM schema.`); owners come from every `public const string SchemaName`. Rules `base.no-scheduler-dashboard-mapping` and `base.no-static-scheduler-storage` over all of `backend/src`, at any dashboard path (Roslyn names, so comments and strings do not count) |

## Use

```powershell
E:\codelab-tools\dotnet10\dotnet.exe test backend/tests/Starter.Architecture.Tests
```

## Extend

- Modules are discovered from the host's `ModuleAssemblyAttribute`s (`ModuleCatalog.Discover(typeof(Program).Assembly)`),
  one theory row per module. Adding or removing a module needs no edit here.
- A new rule: add a check that returns `CheckResult`, with a passing and a failing fixture under `Fixtures/` (see
  `ControllerShape` and its fixtures), and prefer a `Codelab.Testing` helper when one exists.

## Gotchas

- A module's `module.rules.yaml` must list the kernel projects it uses in `mayDependOn` (`StarterDomain`,
  `StarterApplication`, `StarterInfrastructure`); kernel namespaces are not nested in module namespaces, so they are
  checked like any other module.
- A layer with no types makes the Codelab.Testing layer check vacuous, and a vacuous check fails. A scaffolded module
  must have at least one type in each layer it declares.
- The enum scan reads source, so it also covers enums the compiler never sees in the test's assembly set.
- The cross-schema scan has no exemption list. A module that needs another module's data calls that module's port
  (or a kernel port); a schema owner declares `public const string SchemaName` so the scan knows it.
- Test samples that would match a content rule are built at run time (see `DataBoundaryTests`), so the rule does not
  flag the test file itself.
