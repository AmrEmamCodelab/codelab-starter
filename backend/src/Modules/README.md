# Backend modules: the canonical anatomy

Every backend module has the shape below (ADR-004). `tools/scaffold/new-module.mjs --name <Name>` copies it; a
module written by hand must match it, because the architecture and contract tests discover modules by these
conventions, and DocsGen reads them by reflection. `<Name>` is PascalCase (`Records`); `<kebab>` is its kebab-case
form (`records`).

## Layout

```
backend/src/Modules/<Name>/
  Starter.Modules.<Name>.csproj     one project; layers are namespaces (folders), not projects
  module.rules.yaml                 the module's rules (mayDependOn, forbidden, thresholds)
  MODULE.md                         Purpose, Use, Extend, Rules, Gotchas
  CLAUDE.md, BannedSymbols.generated.txt, .editorconfig, CodeMetricsConfig.txt   GENERATED (rules engine)
  <Name>Module.cs                   public sealed class <Name>Module : IModule
  Domain/                           Starter.Modules.<Name>.Domain: entities, value objects, invariants
  Application/                      Starter.Modules.<Name>.Application: use cases, commands, validators, ports
    <Name>PermissionKeys.cs
    <Name>ErrorCodes.cs
  Infrastructure/                   Starter.Modules.<Name>.Infrastructure: DbContext, configurations, repositories
    Migrations/                     one InitialCreate to start with (ADR-010)
  Api/                              Starter.Modules.<Name>.Api: thin controllers and request/response records
backend/tests/Modules/Starter.Modules.<Name>.Tests/
```

Dependencies point inward: `Api -> Application -> Domain`, and `Infrastructure -> Application -> Domain`. Application
references the kernel ports (`Starter.Application`) only, never EF Core or ASP.NET Core.

## The csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$(CodelabTargetFramework)</TargetFramework>
    <RootNamespace>Starter.Modules.<Name></RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
  </ItemGroup>
  <ItemGroup>
    <CodelabPackageReference Include="Codelab.Security" />   <!-- source or feed, see backend/Directory.Build.targets -->
    <CodelabPackageReference Include="Codelab.Errors" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Starter.Domain\Starter.Domain.csproj" />
    <ProjectReference Include="..\..\Starter.Application\Starter.Application.csproj" />
    <ProjectReference Include="..\..\Starter.Infrastructure\Starter.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

A module never references another module's project. It talks to another module through a port in
`Starter.Application` (for example `IUserDirectory`, implemented by Identity).

## `<Name>Module` and the Add/Use extensions

```csharp
public sealed class RecordsModule : IModule
{
    public string Name => ModuleNaming.NameOf(typeof(RecordsModule).Assembly);   // never a hand-built "Starter." string

    public void AddModule(IServiceCollection services, IConfiguration configuration) => services.AddRecordsModule(configuration);

    public void UseModule(IEndpointRouteBuilder endpoints) => endpoints.UseRecordsModule();
}

public static class RecordsModuleRegistration
{
    public static IServiceCollection AddRecordsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RecordsOptions>().Bind(configuration.GetSection(RecordsOptions.SectionName)).ValidateOnStart();
        services.AddModuleDbContext<RecordsDbContext>(RecordsDbContext.SchemaName);
        services.AddScoped<IRecordsUnitOfWork>(provider => provider.GetRequiredService<RecordsDbContext>());
        services.AddSingleton<IPermissionCatalogSource, RecordsPermissionCatalog>();
        services.AddScoped<IValidator<CreateRecordRequest>, CreateRecordValidator>();
        services.AddScoped<CreateRecord>();
        return services;
    }

    public static IEndpointRouteBuilder UseRecordsModule(this IEndpointRouteBuilder endpoints) => endpoints;  // extras only
}
```

- Exactly one public `IModule` per module assembly, with a parameterless constructor (`ModuleCatalog` refuses
  anything else, and two modules with the same name).
- Controllers in `Api/` are found automatically (the kernel adds every module assembly as an MVC application part).
  `UseModule` maps only what is not a controller.
- Startup work is an `IStartupTask` (`StartupPhase.ReferenceData` for code-owned reference data,
  `StartupPhase.Accounts` for seeded accounts); it must be idempotent.
- Feature flags are an `IFeatureFlagSource` with camelCase names; gate endpoints with `[RequiresFeature]`.

## Permission keys and the catalog source

```csharp
public static class RecordsPermissionKeys
{
    public const string View = "records.view";
    public const string Approve = "records.approve";
}

public sealed class RecordsPermissionCatalog : IPermissionCatalogSource
{
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(RecordsPermissionKeys.View, "View records."),
        new(RecordsPermissionKeys.Approve, "Approve submitted records."),
    ];
}
```
Keys match `PermissionKeyFormat.Pattern`. The Access module unions every source into the catalog at start
(`StartupPhase.ReferenceData`). `tools/scaffold/new-permission.mjs --module <Name> --action <kebab>` adds a key.

## Error codes

```csharp
public static class RecordsErrorCodes
{
    public const string RecordNotFound = "RECORDS_RECORD_NOT_FOUND";
    public const string AlreadyClosed = "RECORDS_ALREADY_CLOSED";
}
```
A use case returns `Refusal.NotFound(RecordsErrorCodes.RecordNotFound)`; the client renders `errors.<CODE>` in ar and
en. DocsGen lists every `<Name>ErrorCodes` constant in `docs/generated/error-codes.md`.

## Validators

Every request body an endpoint binds has an `IValidator<T>` (the contract tests check it):
```csharp
public sealed class CreateRecordValidator : IValidator<CreateRecordRequest>
{
    public ValidationErrors Validate(CreateRecordRequest input) =>
        new ValidationErrors().RequireText(FieldNames.Of(nameof(CreateRecordRequest.Title)), input.Title, RecordLimits.TitleMaxLength);
}
```
The use case calls it first and returns `Refusal.Invalid(errors)` when it fails.

## Endpoints

```csharp
[ApiController]
[Route(ApiRoutes.Prefix + "/records")]
public sealed class RecordsController(CreateRecord createRecord) : ControllerBase
{
    [HttpPost]
    [HasPermission(RecordsPermissionKeys.Create)]
    public async Task<ActionResult<RecordResponse>> Create(CreateRecordRequest request, CancellationToken cancellationToken) =>
        (await createRecord.HandleAsync(request, cancellationToken)).ToActionResult(RecordResponse.From);
}
```
Thin: bind, call one use case, map the `Outcome` with `OutcomeResults`. Ids on the wire are `PublicId<T>`. An endpoint
without a permission carries `[AuthenticatedUser]` or `[AllowAnonymous]` and is listed in
`backend/tests/Starter.Contract.Tests/EndpointAllowlist.cs`. `tools/scaffold/new-endpoint.mjs` writes one.

## DbContext and InitialCreate

```csharp
public sealed class RecordsDbContext(DbContextOptions<RecordsDbContext> options) : ModuleDbContext(options), IRecordsUnitOfWork
{
    public const string SchemaName = "records";

    protected override string Schema => SchemaName;

    public DbSet<Record> Records => Set<Record>();
}
```
One DbContext per module, in its own schema, with its migrations history in that schema. Entity configurations in the
context's namespace subtree are applied automatically. Create the first migration once:
```powershell
& E:\codelab-tools\dotnet10\dotnet.exe ef migrations add InitialCreate --project backend\src\Modules\Records --startup-project backend\src\Starter.Api --context RecordsDbContext --output-dir Infrastructure\Migrations
```
Migrations are generated files: never hand-edit them. They run on start only in Development
(`Database:ApplyMigrationsOnStartup`); every other environment uses the DBA scripts.

## Tests

`backend/tests/Modules/Starter.Modules.<Name>.Tests/Starter.Modules.<Name>.Tests.csproj` (xunit) references the module
project and holds unit tests for every use case, validator and domain rule, in the same change as the code.
Architecture and contract tests find the module on their own; integration tests live in
`backend/tests/Starter.Integration.Tests`.

## Host registration

The module joins the host through ONE line in `backend/src/Starter.Api/Starter.Api.csproj`:
```xml
<ProjectReference Include="..\Modules\Records\Starter.Modules.Records.csproj" />
```
plus its slnx entries under `/backend/`. The scaffolder writes both; `tools/remove-module.ps1 -Name Records` removes
both. `Program.cs` never changes.
