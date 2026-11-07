# Starter.Infrastructure

## Purpose
The kernel Infrastructure project (ADR-004): the ASP.NET Core and EF Core aware plumbing that every module and the
host use. It never names a module.

| Folder | What it provides |
| --- | --- |
| `Modularity` | `IModule`, `ModuleCatalog` (discovery from the host's `ModuleAssemblyAttribute` list), `ModuleNaming` (names from assembly names), `ModuleRegistration` (`AddStarterModules`: every `AddModule`, then `AddControllers()` with the kernel and module assemblies as application parts; `MapStarterModules`: controllers, then every `UseModule`) |
| `Persistence` | `ModuleDbContext` (own schema, own `__EFMigrationsHistory`, `IUnitOfWork`), `AddModuleDbContext<T>(schema)` on `ConnectionStrings:Starter`, `DatabaseOptions` (`Database:ApplyMigrationsOnStartup`, Development only), the Codelab.Security SQL stores pointed at the same database; every context gets the audit interceptor; the model build refuses a cascading record history FK |
| `Startup` | `IStartupTask`, `StartupPhase` (ReferenceData = 1, Accounts = 2), `StartupTaskRunner` (migrations when enabled, then tasks in phase order; a failure stops the host) |
| `Authorization` | `[HasPermission(key)]`, `[AuthenticatedUser]`, `IEndpointAccessDeclaration`, the fallback policy (authenticated, `AuthenticatedOrUnroutedRequirement`), the `NoCredentials` default scheme used until a module registers one |
| `Features` | `IFeatureFlagSource`, `FeatureFlag`, `Features:*` switches (validated on start), `IFeatureGate`, `[RequiresFeature]` (removes the action from MVC while off), the anonymous `GET /api/features` controller (flags only) |
| `Users`, `Localization` | HttpCurrentUserContext (sub, sid), HttpAuditContextAccessor, SystemActorScope (ISystemActorScope on an async-local flag: inside a scope the current user reads as anonymous and the audit context is the system actor with the current correlation id, even with an HTTP context in reach), HttpCurrentLanguage (Accept-Language, Arabic default) |
| `Jobs` | JobRegistration: AddStarterJob<TJob>() (scoped IJob, idempotent, plus the system actor scope) and AddStarterSystemActor(); the jobs module (Starter.Modules.Jobs) owns the catalog, schedules and run history |
| `Results`, `Errors` | `OutcomeResults` (Outcome to 200/204 or problem+json), `StarterFailures` (domain exceptions to 404/403/422) |
| `Messaging` | OutboxDbContext (schema messaging, table OutboxMessages: Id, Type, PayloadJson, Status int, CreatedAt, NotBefore, Attempt, LockedUntil, SentAt, LastError), migrations InitialCreate + DeliveryTracking and OutboxMigrationModel, EfOutbox (IOutbox), the drain: OutboxDrainRunner (IOutboxDrainRunner), OutboxDispatcher (routing to the one IOutboxHandler per type, DI scope per message), SqlOutboxStore (IOutboxStore: atomic READPAST/UPDLOCK claim with a 5-minute lease), OutboxDrainSchedule (lease, back-off 1/5/15/60 min, MaxBatchesPerPass 20), OutboxErrorCodes, OutboxOptions (Messaging:Outbox: Enabled default true, BatchSize 50, 1-500), AddStarterMessaging and AddOutboxHandler<T> |
| `Numbering` | NumberingDbContext (schema numbering, no tables; InitialCreate creates the schema), SqlNumberSequences (one SQL SEQUENCE per prefix and year, e.g. numbering.CU_2026, created on first use, NO CACHE), RequestNumberAllocator (IRequestNumberAllocator; year in Numbering:TimeZoneId, default Arab Standard Time, validated on start), AddStarterNumbering |
| `Auditing` | `AuditSaveChangesInterceptor` (scoped; attached to every module context by `AddModuleDbContext`; captures added/modified/deleted rows, masks `[PersonalData]` with `PiiMask`, leaves out `[NotAudited]`, secrets and binary, name conventions `AuditNamingConventions` as safety net; actor = subject id or SYSTEM, correlation id, `TimeProvider`), `SqlAuditTrailWriter` (same connection and transaction; opens one when the caller has none), `AuditDbContext` (schema `audit`, table `AuditEntries`, reshaped `InitialCreate` + `AuditMigrationModel`), `EfAuditTrailQueries`, `RecordHistoryMapping` (`HasRecordHistory<TRecord, THistory>()` = FK OnDelete(Restrict) + (RecordId, At) index; `EnsureRestrict` run by `ModuleDbContext` refuses a cascading history FK), `AddStarterAuditing` (called by `AddStarterPersistence`) |
| `Workflow` | `WorkflowRegistration`: `AddRecordWorkflow<TDefinition>()` (the record type's `IRecordWorkflowDefinition` as a singleton, the `IRecordWorkflowCatalogs` registry, the scoped open generic `IRecordWorkflow<TRecord>` facade, idempotent per definition) and `AddRecordWorkflowKernel()` (registry, facade and check without a definition; called by Records, Assignments and Deadlines); `WorkflowCatalogStartupCheck` (an `IHostedLifecycleService` that validates every catalog against the union of the permission catalogs in `StartingAsync`, before any hosted service, and throws `WorkflowCatalogInvalidException` listing every `CatalogViolation`; no database) |
| `Hosting` | `ApiRoutes.Prefix` (`api`), `AddStarterKernel`, `UseStarterPipeline` (runs `CorsOriginBootCheck` first, so a bad `Codelab:Security:Cors:AllowedOrigins` stops the boot), `MapStarterEndpoints` (controllers, module extras, signing key, CSP report sink, health) |
| `Security` | The security edge: `CspReportEndpoint.MapCspReportSink` (anonymous `POST /api/security/csp-reports`, route `SecurityEdgeRoutes.CspReports`; `application/csp-report` and `application/reports+json` only, otherwise 415; body capped at `CspReportContract.MaxBodyBytes` = 16 KiB, otherwise 413, checked for declared and chunked bodies through `BoundedBody`; 400 when the body is not a report; Codelab client rate limit; exempt from request signing; 204), `CspReportNormalizer` (both formats become `CspViolation`; at most 20 per batch, JSON depth 8, non-`csp-violation` reports skipped), `CspReportUrls` (URLs keep scheme://host[:port]/path only; `data:`/`blob:` keep the scheme; fields clipped to 512 characters, control characters dropped), `CspViolationLog` (one Warning per violation, EventId 1301 `CspViolation`, properties `CspDirective`, `CspDisposition`, `CspDocumentUri`, `CspBlockedUri`, `CspSourceFile`, `CspLineNumber`, `CspColumnNumber`, `CspStatusCode`, `CspReportFormat`), `CorsOriginBootCheck` (Codelab.Security CORS rules plus user info and duplicate origins; throws `OptionsValidationException` listing every problem) |
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
app.UseStarterPipeline();               // CORS origin check (throws), CORS, routing, authentication, user log context, Codelab.Security, authorization
app.MapStarterEndpoints(modules);       // controllers, module extras, signing-key endpoint, CSP report sink, health
```
### Security edge (folder `Security`)

**CSP report sink.** `MapStarterEndpoints` calls `CspReportEndpoint.MapCspReportSink`, which maps
`POST /api/security/csp-reports` (`SecurityEdgeRoutes.CspReports`, built from `ApiRoutes.Prefix`).

| Aspect | Contract |
| --- | --- |
| Access | Anonymous (`CspReportEndpoint.AnonymousReason`), exempt from request signing (`SignatureExemption.WholeRequest`). |
| Formats | `CspReportContract.FormatOf`: `application/csp-report` (`CspReportFormat.ReportUri` = 1) and `application/reports+json` (`CspReportFormat.ReportingApi` = 2), parameters such as `charset` ignored. Anything else: 415. |
| Size | `CspReportContract.MaxBodyBytes` (16 KiB): a declared `Content-Length` over it is 413 before reading; a chunked body is read by `BoundedBody.ReadAsync` at most one byte past the cap, then 413. |
| Rate | `RequireRateLimiting(ClientRateLimitOptions.PolicyName)` (`codelab-client`): per client, `Codelab:Security:RateLimiting:PermitLimit` per `WindowSeconds` (package defaults 100 per 60 s); over it: 429. |
| Normalizer | `CspReportNormalizer.TryNormalize`: JSON depth at most 8; a Reporting API batch keeps only `csp-violation` reports, at most `MaxViolationsPerRequest` (20); not JSON or no envelope: 400. Each report becomes a `CspViolation` (directive lowercase or `unknown`, `CspDisposition` Unspecified 0 / Enforce 1 / Report 2, URLs redacted by `CspReportUrls.Redact`, fields clipped to `MaxFieldLength` 512). |
| Log event | `CspViolationLog.Write`: one Warning per violation, category `Starter.Security.CspReports`, EventId 1301 `CspViolation`, properties `CspDirective`, `CspDisposition`, `CspDocumentUri`, `CspBlockedUri`, `CspSourceFile`, `CspLineNumber`, `CspColumnNumber`, `CspStatusCode`, `CspReportFormat`. |
| Answer | 204 with no body; nothing is stored. Problems use the one problem+json envelope (`FrameworkStatusCodes.Describe`). |

**CORS boot check.** `UseStarterPipeline` calls `CorsOriginBootCheck.EnsureValid(app)` before `UseCors()`. It reads
`IOptions<CodelabCorsOptions>` (section `Codelab:Security:Cors`) and throws `OptionsValidationException` listing
every problem of `AllowedOrigins`, so the host never starts with a bad list:

- a wildcard `*` with `AllowCredentials` true (always), or at all outside Development;
- a non-https origin outside Development;
- anything but a bare `scheme://host[:port]` (a path, query, trailing slash, or the default port spelled out);
- an origin with user info (`https://user@host`);
- the same origin twice, compared as the browser sends it (scheme and host case-insensitive).

The first three are Codelab.Security's own rules (`CorsOriginValidator`), whose options validator may throw first;
the last two are the starter's. The other keys of the section (`AllowedHeaders`, `ExposedHeaders`,
`PreflightMaxAgeSeconds`) are not checked here. `CorsOriginBootCheck.Validate(options, isDevelopment)` is the pure
form. Tests: `backend/tests/Starter.Api.Tests/SecurityEdge` (`CorsOriginBootCheckTests`, `CspReportContractTests`,
`CspReportNormalizerTests`, `CspReportSinkHttpTests`, `CspReportUrlsTests`, `CspViolationLogTests`).

A module calls `services.AddModuleDbContext<RecordsDbContext>(RecordsDbContext.SchemaName)`, registers its
`IPermissionCatalogSource`, `IFeatureFlagSource` and `IStartupTask`s in `Add<Name>Module`, and guards controllers with
`[HasPermission(RecordsPermissionKeys.View)]`. See `backend/src/Modules/README.md`.

## Extend
- **Add an outbox handler:** see Starter.Application MODULE.md (Extend); register with services.AddOutboxHandler<THandler>(). The drain itself is run by the jobs module (outbox-drain, every 5 minutes) through IOutboxDrainRunner (scoped: resolve it in a scope).
- **Register a record type's workflow:** in the owning module, `services.AddRecordWorkflow<MyWorkflowDefinition>()`
  (recipe `docs/recipes/add-workflow-type.md`); the start check then refuses a broken catalog or a permission key no
  module declares. Tests: `backend/tests/Starter.Api.Tests/Workflow`.
- **New kernel service:** register it in `AddStarterKernel` (or a new `AddStarterXxx` called from it), with options
  that carry `public const string SectionName` and a validator registered with `ValidateOnStart`.
- **New pipeline step:** add it to `UseStarterPipeline` in the position Codelab.Security documents; never in
  `Program.cs`.
- **New kernel endpoint:** a controller here under `ApiRoutes.Prefix`, `[AllowAnonymous]` or `[AuthenticatedUser]`
  only with an entry in `backend/tests/Starter.Contract.Tests/EndpointAllowlist.cs`.
- **Point a CSP at the sink:** the SPA document's `Content-Security-Policy-Report-Only` carries `report-uri /api/security/csp-reports; report-to csp-endpoint` and the response carries `Reporting-Endpoints: csp-endpoint="/api/security/csp-reports"`. Keep both spellings: Chromium uses only report-to, and only over https; Firefox and Safari use report-uri. Seq query: `EventId.Name = 'CspViolation'`, grouped by `CspDirective` and `CspBlockedUri`. Runbook: `docs/runbooks/csp-enforce.md`.
- Frozen for the modules (contract §3): add members, never rename, remove or change a signature.

## Rules
- Every module save is audited in the same transaction; a failure to write the trail fails the save. A module context must not enable a retrying execution strategy (the audit writer opens its own transaction when the caller has none).
- Never reference a module (`starterinfrastructure.no-module-reference`); derive every name from assembly names.
- `AddStarterKernel` runs before `AddCodelabSecurity`: hosted services start in registration order, so migrations
  create the database before the Codelab.Security stores ensure their tables.
- `Database:ApplyMigrationsOnStartup` refuses the start outside Development; production schema comes from DBA scripts.
- Every request without a declaration is authenticated-only (fallback policy); every permission check is live.
- Time comes from `TimeProvider`; tests: `backend/tests/Starter.Api.Tests` (no database).

## Gotchas
- **The outbox saves on its own, and delivery is at least once.** EfOutbox saves its own context (not the caller's transaction): enqueue after the caller's save. A drain claims a message for 5 minutes (Processing + LockedUntil); a drain that dies leaves it to be reclaimed after the lease, so a handler can see the same MessageId twice. Attempts are counted on claim; after 8 the row is Failed (LastError holds only a code: OUTBOX_NO_HANDLER, OUTBOX_LEASE_EXPIRED or the classified failure code). Failed rows are never retried automatically. Messaging:Outbox:Enabled=false stops delivery, not writing.
- **Numbering needs DDL rights at runtime.** Sequences are created on first use, so the application login needs CREATE SEQUENCE on the database and ALTER on schema numbering (DBA script). Numbers are not transactional: a rolled-back caller leaves a gap.
- **The audit schema is one more migrated context** (`audit.__EFMigrationsHistory`) and must exist before any module write; production applies its migration and then `deploy/sql/audit/Grant-AuditAppendOnly.sql` (DENY UPDATE, DELETE).
- **Masking happens at write time**: adding `[PersonalData]` later does not re-mask old rows.
- **Background work is SYSTEM only inside a system scope.** Outside a request the audit actor is already the system actor; a system scope also covers work that still has a request's HTTP context in reach (an AsyncLocal leak into Task.Run). The jobs module opens one around every run.
  the caller's module transaction (phase 5 adds that and the drain). The `messaging` schema is one more migrated
  context on start, with its own `__EFMigrationsHistory`, and one more DBA script in production.
- **A messaging migration** follows the module migration rules: add one with dotnet-ef, then reshape it (named types
  and ids, no generated marker, no `*.Designer.cs`), freeze `OutboxMigrationModel` and keep
  `backend/tests/Starter.Api.Tests/Messaging` (`HasPendingModelChanges`) green.
- **Unmatched routes are 404, not 401.** The fallback requirement passes a request that matched no endpoint, so the
  status-code page answers 404 problem+json; every real endpoint stays closed to anonymous callers.
- **`[RequiresFeature]` removes the action at startup**: a switched-off endpoint is a plain 404, and changing a
  switch needs a restart. A configured `Features:<name>` that no module declares refuses the start.
- **The `NoCredentials` scheme** is the default only while no module set one; the Identity module's bearer scheme
  replaces it. Without it a host with no Identity module would answer 500 on every protected request.
- **Codelab.Security SQL stores** take `ConnectionStrings:Starter` when `Codelab:Security:SqlStore:ConnectionString`
  is empty; their tables come from the package's `EnsureSchemaOnStart` script (idempotent), not from a migration.
- **A broken workflow catalog stops the boot** before migrations run: `WorkflowCatalogInvalidException` lists every
  violation (and the permission keys come from every module, so a catalog naming a key of a removed module fails).
- `ModuleCatalog.Discover` loads assemblies by name; a module listed in the host project but missing from the output
  folder fails the start with a `FileNotFoundException`.
- **The CSP sink is anonymous by necessity.** A browser posts reports without the page's credentials, so an authorized sink receives nothing. It is rate limited per client and stores nothing. Never log a raw report field: everything goes through `CspReportNormalizer`, which drops query strings, fragments, user info, the referrer and the script sample.
- **An empty `AllowedOrigins` passes the boot check.** No CORS policy is built then, so every cross-origin browser call
  fails its preflight; the deploy kit (`deploy/kits/templates/api/README.md`) reports the missing key as a gap.
- **The sink's route is in two more places.** The front site's CSP (`report-uri`, `Reporting-Endpoints`) and the kit's
  `deploy/kits/templates/web/security-headers.psd1` (`ReportPath`) name `/api/security/csp-reports`; moving
  `SecurityEdgeRoutes.CspReports` without them silences every report (the parity test pins `ReportPath`).
- **CORS origins are checked in `UseStarterPipeline`, before the host starts.** An origin is exact `scheme://host[:port]`: no trailing slash, no path, no default port, no user info, each listed once, https outside Development. `*` is refused with credentials, and outside Development always. `Codelab.Security`'s own validator may throw first with the same rules.
