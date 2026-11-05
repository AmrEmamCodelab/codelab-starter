# Starter.Infrastructure

## Purpose
The kernel Infrastructure project (ADR-004): the ASP.NET Core and EF Core aware plumbing that every module and the
host use. It never names a module.

| Folder | What it provides |
| --- | --- |
| `Modularity` | `IModule`, `ModuleCatalog` (discovery from the host's `ModuleAssemblyAttribute` list), `ModuleNaming` (names from assembly names), `ModuleRegistration` (`AddStarterModules`: every `AddModule`, then `AddControllers()` with the kernel and module assemblies as application parts; `MapStarterModules`: controllers, then every `UseModule`) |
| `Persistence` | `ModuleDbContext` (own schema, own `__EFMigrationsHistory`, `IUnitOfWork`), `AddModuleDbContext<T>(schema)` on `ConnectionStrings:Starter`, `DatabaseOptions` (`Database:ApplyMigrationsOnStartup`, Development only), the Codelab.Security SQL stores pointed at the same database |
| `Startup` | `IStartupTask`, `StartupPhase` (ReferenceData = 1, Accounts = 2), `StartupTaskRunner` (migrations when enabled, then tasks in phase order; a failure stops the host) |
| `Authorization` | `[HasPermission(key)]`, `[AuthenticatedUser]`, `IEndpointAccessDeclaration`, the fallback policy (authenticated, `AuthenticatedOrUnroutedRequirement`), the `NoCredentials` default scheme used until a module registers one |
| `Features` | `IFeatureFlagSource`, `FeatureFlag`, `Features:*` switches (validated on start), `IFeatureGate`, `[RequiresFeature]` (removes the action from MVC while off), the anonymous `GET /api/features` controller (flags only) |
| `Users`, `Localization` | `HttpCurrentUserContext` (`sub`, `sid`), `HttpAuditContextAccessor`, `HttpCurrentLanguage` (`Accept-Language`, Arabic default) |
| `Results`, `Errors` | `OutcomeResults` (Outcome to 200/204 or problem+json), `StarterFailures` (domain exceptions to 404/403/422) |
| `Hosting` | `ApiRoutes.Prefix` (`api`), `AddStarterKernel`, `UseStarterPipeline`, `MapStarterEndpoints` |
| `Configuration` | `AddLocalSecretsFile` (`appsettings.Development.local.json`, Development only) |

## Use
The host (Starter.Api `Program.cs`) is the whole wiring:
```csharp
var modules = ModuleCatalog.Discover(typeof(Program).Assembly);
builder.AddLocalSecretsFile();
builder.AddCodelabObservability(ModuleNaming.ServiceNameOf(typeof(Program).Assembly));
builder.Services.AddStarterKernel(builder.Configuration);          // BEFORE AddCodelabSecurity
builder.Services.AddCodelabErrors(errors => errors.AddStarterFailures());
builder.AddCodelabSecurity();
builder.Services.AddStarterModules(modules, builder.Configuration);
...
app.UseCodelabObservability();
app.UseCodelabErrors();
app.UseStarterPipeline();               // CORS, routing, authentication, user log context, Codelab.Security, authorization
app.MapStarterEndpoints(modules);       // controllers, module extras, signing-key endpoint, health
```
A module calls `services.AddModuleDbContext<RecordsDbContext>(RecordsDbContext.SchemaName)`, registers its
`IPermissionCatalogSource`, `IFeatureFlagSource` and `IStartupTask`s in `Add<Name>Module`, and guards controllers with
`[HasPermission(RecordsPermissionKeys.View)]`. See `backend/src/Modules/README.md`.

## Extend
- **New kernel service:** register it in `AddStarterKernel` (or a new `AddStarterXxx` called from it), with options
  that carry `public const string SectionName` and a validator registered with `ValidateOnStart`.
- **New pipeline step:** add it to `UseStarterPipeline` in the position Codelab.Security documents; never in
  `Program.cs`.
- **New kernel endpoint:** a controller here under `ApiRoutes.Prefix`, `[AllowAnonymous]` or `[AuthenticatedUser]`
  only with an entry in `backend/tests/Starter.Contract.Tests/EndpointAllowlist.cs`.
- Frozen for the modules (contract §3): add members, never rename, remove or change a signature.

## Rules
- Never reference a module (`starterinfrastructure.no-module-reference`); derive every name from assembly names.
- `AddStarterKernel` runs before `AddCodelabSecurity`: hosted services start in registration order, so migrations
  create the database before the Codelab.Security stores ensure their tables.
- `Database:ApplyMigrationsOnStartup` refuses the start outside Development; production schema comes from DBA scripts.
- Every request without a declaration is authenticated-only (fallback policy); every permission check is live.
- Time comes from `TimeProvider`; tests: `backend/tests/Starter.Api.Tests` (no database).

## Gotchas
- **Unmatched routes are 404, not 401.** The fallback requirement passes a request that matched no endpoint, so the
  status-code page answers 404 problem+json; every real endpoint stays closed to anonymous callers.
- **`[RequiresFeature]` removes the action at startup**: a switched-off endpoint is a plain 404, and changing a
  switch needs a restart. A configured `Features:<name>` that no module declares refuses the start.
- **The `NoCredentials` scheme** is the default only while no module set one; the Identity module's bearer scheme
  replaces it. Without it a host with no Identity module would answer 500 on every protected request.
- **Codelab.Security SQL stores** take `ConnectionStrings:Starter` when `Codelab:Security:SqlStore:ConnectionString`
  is empty; their tables come from the package's `EnsureSchemaOnStart` script (idempotent), not from a migration.
- `ModuleCatalog.Discover` loads assemblies by name; a module listed in the host project but missing from the output
  folder fails the start with a `FileNotFoundException`.
