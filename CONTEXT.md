# CONTEXT.md

Status: **phase 4** (design system, page patterns and the public portal UI). The backend kernel and host (`Starter.Api`), three modules (Identity: basic, Active Directory and SSO login, refresh, logout, lockout and activation; Users: users, organizations and the Excel import; Access: the permission catalog, roles, virtual roles, direct grants, org scope and the live per-user resolve), the `Codelab.Identity` 0.5 package, the Angular 22 app `frontend/starter-web` (login, staff shell, dashboard, the admin screens for users, organizations, import and access, 403 and 404 pages, the public portal pages of module 15 on a mocked contact API, the portal and requester shells, the footer and the notifications menu, Arabic-first), all 8 page patterns, the component showcase at `/showcase` (dev and staging), the owned component library `@codelab/ng-dga` (31 catalogued components, project branding, the generated catalog and tokens docs), the design kit in `docs/design/`, the visual and accessibility harness (`npm run visual`), the frontend lint chain (RTL, icon subset, permission gates), generated docs and client, the scaffold generators, the fork model (`init.ps1`, `tools/remove-module.ps1`, `tools/upstream-merge.ps1`) and the local runner exist. The versioned security core from phase 1 is consumed from source in the starter and from the feed in a fork. This file grows with every phase. Sections marked *planned* describe the target from the platform team's build plan (kept outside the repository; its decisions are in [docs/adr/](docs/adr/README.md)).

## Business context

Codelab delivers request portals for Saudi ministries and government entities. A citizen, resident or organization (the **requester**) submits a request, internal **staff** review it through a workflow, and **admins** manage users, permissions and reference data. Every portal is Arabic-first (RTL) with English, follows the DGA (Digital Government Authority) look, and runs on premises: Windows Server with IIS, SQL Server, and a backend zone with no internet egress.

`codelab-starter` gives each new project that system on day one: three login modes (basic, Active Directory, SSO), users and organizations, roles and permissions, a generic records and workflow engine, notifications, attachments, public portal pages, logs and an ops console. A project forks it, runs `init.ps1` (name, branding, login modes, optional modules, fresh keys), and reshapes the sample `ServiceRequests` module into its own request types.

## Glossary

| Term | Meaning | Code name |
| --- | --- | --- |
| Starter | This repository, the upstream of every project fork. | `codelab-starter`, `Starter.*` |
| Fork | A project repository created from the starter and fully renamed by `init.ps1` (ADR-003). | `codelab.starter.json` (`renameMap`, `starterBase`) |
| Platform owner | The person who approves rules, packages, hooks and deploy changes (Amr Emam). | `.github/CODEOWNERS` |
| Package | Versioned, security-critical code consumed by version (ADR-000, ADR-002). | `Codelab.*`, `@codelab/*` |
| Module | One business or platform area: one backend project plus one frontend feature folder (ADR-004). | `Starter.Modules.<Name>`, `IModule` |
| Kernel | The shared backend projects every module builds on: ports, results, validation, modularity, persistence, hosting. | `Starter.{Domain,Application,Infrastructure,Api}` |
| Use case | One Application class a controller calls; it returns an `Outcome<T>` (a value or a `Refusal`). | `<Verb><Noun>`, `<Name>UseCase` |
| Rules file | A module's machine-readable guardrails, inherited from the base rules. | `module.rules.yaml`, `rules/base.rules.yaml` |
| Permission key | A dotted, typed key (`dashboard.view`). The token carries none; every request resolves them live. | `<Name>PermissionKeys`, `PermissionKeys` (TypeScript, generated) |
| Break-glass account | The seeded administrator that holds every permission; its password comes only from local secrets or the environment. | `Identity:BreakGlass` |
| Record | A business item that moves through a workflow; the sample record type is a service request (planned, P7). | `Record`, `ServiceRequest` |
| Transition | An allowed move from one status to another, with its permission and reason requirement (planned, P7). | `TransitionRule` |
| Lookup | Bilingual reference data seeded by migrations, referenced through ID constants (planned, P5). | `LookupEntity`, `<Name>Ids` |
| Refusal | An expected business "no" returned with an error code, not thrown; mapped to problem+json. | `Refusal`, `<Name>ErrorCodes`, `OutcomeResults` |
| PublicId | An opaque, encrypted identifier used on the wire instead of a database ID. | `PublicId<T>`, `WireId<T>` (TypeScript) |
| Reference code | The short code shown to a user on an error, linking to the log entry. | `referenceCode` in problem+json |
| Feed | Where packages are published: a local folder now, GitHub Packages later. | `feed/nuget`, `feed/npm` |

## Repo map (what exists after phase 4)

| Path | What it is | Owner doc |
| --- | --- | --- |
| `CLAUDE.md`, `CONTEXT.md` | Agent entry point and this map. | this file |
| `codelab-starter.slnx` | The single solution: `/packages/`, `/backend/`, `/tests/` (the P0 spike), `/tools/`. | [tools/README.md](tools/README.md) |
| `codelab.starter.json` | Fork manifest: product prefix, `requiredModules` (Identity, Access), `removedModules`, `renameMap`, `starterBase` and the strict-JSON `prefixTargets`. | [tools/fork/README.md](tools/fork/README.md) |
| `init.ps1` | Fork bootstrap: `-Minimal -ProductPrefix -RemoveModule [-FeedDir -RenameMap -SkipSecrets -SkipNpmLock -AllowMissingTargets]`. It pins `Codelab.*` and `@codelab/*` to the feed, drops `packages/` and writes fresh secrets. | [tools/fork/README.md](tools/fork/README.md) |
| `tools/remove-module.ps1`, `tools/upstream-merge.ps1` | Remove a module from a fork; merge the upstream starter with rename replay (`-Upstream`, `-Continue`). | [tools/fork/README.md](tools/fork/README.md) |
| `tools/fork/` | The fork-model library and `Test-ForkModel.ps1`, the real-repo fork test. | [tools/fork/README.md](tools/fork/README.md) |
| `global.json`, `.nvmrc` | .NET SDK 10.0.403 and Node 24 pins (ADR-005). | [ADR-005](docs/adr/ADR-005-node-pin.md) |
| `Directory.Build.props/.targets`, `Directory.Packages.props` | Imports Codelab.Build.Sdk from source or feed (`UseCodelabPackageSource`); central package versions, including the feed-mode `Codelab.*` pins. | [ADR-002](docs/adr/ADR-002-package-consumption.md) |
| `nuget.config`, `.npmrc`, `package.json` | Package sources (`Codelab.*` only from the feed); npm workspaces `packages/npm/*` and `frontend/starter-web`. | [ADR-002](docs/adr/ADR-002-package-consumption.md) |
| `backend/Directory.Build.props/.targets` | `CodelabPackageReference` (a project reference from source, a feed package in a fork) and module discovery: every `*.Modules.*` reference of the host becomes a `ModuleAssemblyAttribute`. | [backend/src/Modules/README.md](backend/src/Modules/README.md) |
| `backend/src/Starter.Domain/` | Kernel domain: domain exceptions, `AppLanguage`. | [MODULE.md](backend/src/Starter.Domain/MODULE.md) |
| `backend/src/Starter.Application/` | Ports only (no EF, no ASP.NET): `Outcome<T>` and `Refusal`, `IValidator<T>`, `IUnitOfWork`, `ICurrentUserContext`, `IUserDirectory`, `IPermissionCatalogSource`, `IUserPermissionGrants`, `ICurrentLanguage`, auditing. | [MODULE.md](backend/src/Starter.Application/MODULE.md) |
| `backend/src/Starter.Infrastructure/` | Modularity (`IModule`, `ModuleCatalog`), `ModuleDbContext`, startup tasks (`ReferenceData`, then `Accounts`), `[HasPermission]` and `[AuthenticatedUser]`, the pipeline, the features endpoint, `OutcomeResults` (problem+json). | [MODULE.md](backend/src/Starter.Infrastructure/MODULE.md) |
| `backend/src/Starter.Api/` | The host: `Program.cs` (at most 150 lines, asserted by a test), OpenAPI v1, `appsettings*.json`, the launch profile on :5180. | [MODULE.md](backend/src/Starter.Api/MODULE.md) |
| `backend/src/Modules/<Name>/` | One project per module (`Starter.Modules.<Name>`, folders `Domain/`, `Application/`, `Infrastructure/`, `Api/`), with `module.rules.yaml`, `MODULE.md` and the generated `CLAUDE.md`, `BannedSymbols.generated.txt` and `.editorconfig`. The ONLY module list is the ProjectReference list of `Starter.Api.csproj` (plus the slnx). | [backend/src/Modules/README.md](backend/src/Modules/README.md) |
| `backend/tests/Modules/Starter.Modules.<Name>.Tests/` | Module unit tests (fakes, `FakeTimeProvider`, no database). | the module's `MODULE.md` |
| `backend/tests/Starter.Application.Tests`, `Starter.Api.Tests` | Kernel tests without a database (Api.Tests uses a TestServer host with probe modules). | [MODULE.md](backend/src/Starter.Infrastructure/MODULE.md) |
| `backend/tests/Starter.Architecture.Tests` | Module boundaries from `rules/rules.compiled.json`, layers, thin controllers, int enums and the `Program.cs` size; modules are discovered. | [README.md](backend/tests/Starter.Architecture.Tests/README.md) |
| `backend/tests/Starter.Contract.Tests` | The endpoint contract over the real host without a database: a permission or an allowlist entry (`EndpointAllowlist.cs`), PublicIds, validators, problem+json. | [README.md](backend/tests/Starter.Contract.Tests/README.md) |
| `backend/tests/Starter.Integration.Tests` | The real host on a throwaway `StarterIt_<guid>` LocalDB database: login, refresh, logout then 401, 403 for the dev user, seeding per environment. | [README.md](backend/tests/Starter.Integration.Tests/README.md) |
| `backend/tools/Starter.DocsGen` (+ `.Tests`) | Starts the host in process (no SQL, no secrets) and writes `docs/generated`; `--check` fails on drift. | [MODULE.md](backend/tools/Starter.DocsGen/MODULE.md) |
| `backend/tools/Starter.DevIdp` (+ `.Tests`) | Dev-only OpenID Connect issuer (code + PKCE S256, RS256 id_token, JWKS, userinfo, end-session) on :5190 and in process for tests (`DevIdpInProcessServer`); refuses any environment but Development, never deployed. | [MODULE.md](backend/tools/Starter.DevIdp/MODULE.md) |
| `backend/src/Modules/{Identity,Access,Users}/` | Module 1 identity and login (basic, AD, SSO via `Auth:Modes`; lockout, activation, auth event trail), module 3 roles and permissions (roles, virtual roles, direct grants, org scope, live resolve) and module 2 users, organizations and the Excel import. | the module's `MODULE.md` |
| `docs/generated/` | DERIVED: `openapi.v1.json`, `permissions.json/.md`, `endpoints.md`, `error-codes.md`, `config-keys.md`. Never hand-edited. | [README.md](docs/generated/README.md) |
| `frontend/starter-web/` | Angular 22 app (standalone, signals, Vitest, an npm workspace): `src/app/core` (ApiService, auth, permissions, i18n, session, branding), `layouts/` (WebLayouts: staff shell, portal shell, requester shell, footer, notifications menu and feed, error dialog), `features/{auth,dashboard,admin,errors,portal}` (admin: users, organizations, import, and access: roles list, role editor, user grants, see `features/admin/access/README.md`; portal: the public pages under `/portal`), `shared/` (WebShared: the `*hasPermission` directive, file picker, geo select), `patterns/` (WebPatterns: the 8 page patterns) and `showcase/` (WebShowcase: the `/showcase` gallery, behind the `showcase` flag of `config.json`). | [MODULE.md](frontend/starter-web/MODULE.md) |
| `frontend/starter-web/tools/` | ESLint and Stylelint config, `i18n-parity.mjs`, `no-inline-styles.mjs`, `permission-gates.mjs`, `rtl-lint.mjs`, `icons-subset.mjs`, `api-gen` (the generated client and `permission-keys.generated.ts`), the lint-fixture spec. | [README.md](frontend/starter-web/tools/README.md) |
| `frontend/starter-web/e2e/` | The visual and accessibility harness (`npm run visual`): screenshots in `ar` and `en` at 1440 and 390 px against baselines or a design target, plus axe. Baselines and evidence live in `docs/evidence/P4/`. | [README.md](frontend/starter-web/e2e/README.md) |
| `docs/design/` | The design kit: the generated component catalog and tokens, the 8 page patterns and a page per pattern. | [README.md](docs/design/README.md) |
| `packages/npm/ng-dga/` | `@codelab/ng-dga` 0.1.0: 31 standalone OnPush components (one `<selector>.catalog.json` each), `--dga-*` tokens, project branding (`provideDgaBranding`), the IBM Plex fonts and the owned icon font (ADR-009). | [MODULE.md](packages/npm/ng-dga/MODULE.md) |
| `packages/dotnet/Codelab.Build.Sdk/`, `Codelab.Build.Analyzers/` | MSBuild SDK (analyzers, banned APIs, release stamping) and the clean-code analyzers CLB1001 to CLB1005 (ADR-012). | the folder's README or `MODULE.md` |
| `packages/dotnet/Codelab.Identity/` | Codelab.Identity 0.5: Active Directory over LDAPS, generic OIDC code + PKCE, deployment guards, login matching. | [MODULE.md](packages/dotnet/Codelab.Identity/MODULE.md) |
| `packages/dotnet/Codelab.{Security,Observability,Errors,Integration,Testing}/` | The versioned core: signing, PublicIds, replay and sid-revocation stores; Serilog, correlation and health; problem+json; the outbound engine; architecture and contract checks. | `MODULE.md` in each folder |
| `packages/npm/ng-security/`, `packages/npm/ng-tools/` | `@codelab/ng-security` (signing interceptors, `WireId<T>`, the problem+json parser) and `@codelab/ng-tools` (ESLint and Stylelint presets, and the i18n, RTL, icon-subset and permission-gate checks). | `MODULE.md` in each folder |
| `packages/vectors/`, `packages/samples/` | Golden vectors over TEST-ONLY fixture keys; the feed-only package integration test (CI step 13). | `README.md` in each folder |
| `packages/versions.json`, `packages/CHANGELOG.md` | The one version source of every package, and the package changelog. | [tools/README.md](tools/README.md) |
| `feed/` | Local package feed filled by `tools/build.ps1 -Pack` (contents gitignored). | [feed/README.md](feed/README.md) |
| `tools/build.ps1` | The one build entry for CI and humans: the CI steps, `-Pack`, `-Pack -Release`. | [tools/README.md](tools/README.md) |
| `tools/scaffold/` | `new-module.mjs`, `new-endpoint.mjs`, `new-permission.mjs`: deterministic generators, and the only sanctioned editor of the slnx and the host's module list. | [README.md](tools/scaffold/README.md) |
| `tools/devseed/` | `New-LocalSecrets.ps1` writes the gitignored `backend/src/Starter.Api/appsettings.Development.local.json` with fresh random values (`-Force` replaces it). | [MODULE.md](backend/src/Starter.Api/MODULE.md) |
| `run-local.ps1`, `tools/run-local/` | The one-command local stack: API :5180, web :4280, Seq :5341 when available; `-Stop` stops only what it started. | [README.md](tools/run-local/README.md) |
| `docs/recipes/` | Step-by-step recipes (add an endpoint, add a permission). | [README.md](docs/recipes/README.md) |
| `tools/extract/` | Allowlist extraction from the harvest copy, scrub sweep, secret scan, provenance (`generate-source.mjs`, `check-origins.mjs`). | `README.md` in the folder |
| `tools/rules-engine/`, `rules/` | Rules schema, base rules, the compiler to `rules/rules.compiled.json`, `rules/codelab.globalconfig` and the per-module files, the content check. | `README.md` in the folder |
| `tools/docs-gate/`, `tools/ci/`, `tools/vectors/` | The MODULE.md and dead-reference gate; host-neutral CI checks and the pack planner; the C# against TS vector check. | `README.md` in each folder |
| `deploy/handover/` | The offline handover kit and its proof (ADR-008). | `README.md` in the folder |
| `.claude/hooks/`, `.claude/skills/`, `.claude/agents/` | Hooks that refuse rule violations early (ADR-006); skills (`coding-standards`, `do-work`, `write-a-skill`, `new-module`, `new-endpoint`, `new-permission`, `new-screen`); the review and commit-gate agents, and the design agents (`dga-design-capture`, `screen-builder`, `dga-reviewer`, `i18n-keeper`, `visual-verifier`). | [CLAUDE.md](CLAUDE.md) routing table |
| `docs/standards/`, `docs/adr/` | Coding standards, error handling, examples; architecture decisions. | [docs/standards/SKILL.md](docs/standards/SKILL.md), [docs/adr/README.md](docs/adr/README.md) |
| `provenance/` | Harvest source commit, tree hash and per-file origin map (`origins/*.json`; `SOURCE.md` is generated). | `SOURCE.md` in the folder |
| `.github/` | `CODEOWNERS` and the PR template, ready for the GitHub move (ADR-007). | [ADR-007](docs/adr/ADR-007-repo-host-ci.md) |

## Planned layout (later phases)

| Path | Phase | What it will hold |
| --- | --- | --- |
| `services/{attachment-service,integration-gateway,ops-console}` | P6, P8 | Separately released deployables. |
| `deploy/` | P6, P8, P10 | IIS kits, security headers, SQL scripts for the DBA, Seq; the handover kit is finished in P10. |
| `docs/runbooks/` | P6 onward | Runbooks. |

## Module index

| # | Module | Code | Phase |
| --- | --- | --- | --- |
| 1 | Identity and login: basic login; the single session issuer (JWT with `sub`, `sid` and `sver`, a rotated refresh token); logout revokes the sid; idle timeout; break-glass and Development-only dev accounts; `IUserDirectory`; Active Directory and SSO via `Auth:Modes`, lockout, activation links, the auth event trail; `IAccountAdministration` for the Users module. | [Starter.Modules.Identity](backend/src/Modules/Identity/MODULE.md), schema `identity` | P2, P3 |
| 2 | Users and organizations: organizations (internal or external), the user admin endpoints over `IAccountAdministration`, the Excel user import (preview, then commit), activation links. | [Starter.Modules.Users](backend/src/Modules/Users/MODULE.md), schema `users` | P3, P5 |
| 3 | Roles and permissions: the catalog in code (`AccessPermissionKeys`), the `[HasPermission]` handler, the live per-user resolve, direct grants, the catalog synchronizer; roles, virtual roles, assignments and grants at a `PermissionScope` (global or own organization), `IOrganizationScope`, the self-lockout guard. | [Starter.Modules.Access](backend/src/Modules/Access/MODULE.md), schema `access` | P2, P3 |
| 4 | Lookups (bilingual, seeded by migrations) | planned | P5 |
| 5 | Audit and record history (personal data masked) | planned | P5 |
| 6 | Settings and background jobs | planned | P5 |
| 7 | Requests and records (base record, numbering, draft and submit wizard, cancel and resubmit) | planned | P7 |
| 8 | Workflow engine (startup-validated transition table, allowed actions, reasons, timeline) | planned | P7 |
| 9 | Assignment and queues | planned | P7 |
| 10 | Deadlines and SLA timers | planned | P7 |
| 11 | Detail and oversight views | planned | P7 |
| 12 | Notifications (bell, email, SMS, bilingual templates, outbox) | planned | P5, P8 |
| 13 | Attachments (service, typed links, upload UI) | planned | P6 |
| 14 | Integrations | deferred (gateway extension point in P8) | - |
| 15 | Public portal pages and contact us: the UI is delivered (landing, service guide, FAQ, contact form on a mocked API, privacy and terms under `/portal`); the contact-us backend and settings-driven content are planned | [WebPortal](frontend/starter-web/src/app/features/portal/MODULE.md) (UI); backend planned | P4, P5 |
| 16 | Logs, error reference codes, ops console | packages since P1; console planned | P1, P8 |
| sample | Service requests (Draft → Submitted → Under Review → Approved/Rejected), deleted or renamed per project | planned | P7 |

Frontend features: [auth](frontend/starter-web/src/app/features/auth/MODULE.md) (login), [dashboard](frontend/starter-web/src/app/features/dashboard/MODULE.md), [admin](frontend/starter-web/src/app/features/admin/MODULE.md) (users, organizations, import and access: roles, role editor, user grants), [errors](frontend/starter-web/src/app/features/errors/MODULE.md) (403, 404) and [portal](frontend/starter-web/src/app/features/portal/MODULE.md) (WebPortal: the public pages). The shells are in [layouts](frontend/starter-web/src/app/layouts/MODULE.md), the core in [core](frontend/starter-web/src/app/core/MODULE.md), the shared pieces in [shared](frontend/starter-web/src/app/shared/MODULE.md), the 8 page patterns in [patterns](frontend/starter-web/src/app/patterns/MODULE.md) and the component gallery in [showcase](frontend/starter-web/src/app/showcase/MODULE.md) (WebShowcase).

## Commands

Run from the repo root. Put `E:\codelab-tools\node24` first on PATH in any shell that runs node, npm, npx or ng.

| Task | Command |
| --- | --- |
| Install Node tooling | `npm install` (repo root) |
| Local secrets (once; `-Force` replaces them) | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\devseed\New-LocalSecrets.ps1` |
| Run the local stack | `powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1` (`-Stop`, `-NoWeb`, `-NoSeq`) |
| API only | `E:\codelab-tools\dotnet10\dotnet.exe run --project backend\src\Starter.Api` (http://localhost:5180) |
| Dev IdP for local SSO | `E:\codelab-tools\dotnet10\dotnet.exe run --project backend/tools/Starter.DevIdp` (http://localhost:5190) |
| Web only | `npm start --workspace frontend/starter-web` (http://localhost:4280) |
| Full CI | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI` |
| Build and test .NET | `E:\codelab-tools\dotnet10\dotnet.exe build codelab-starter.slnx -warnaserror` then `... test codelab-starter.slnx` |
| Frontend test, lint, build | `npm test --workspace frontend/starter-web`, `npm run lint --workspace frontend/starter-web`, `npm run build --workspace frontend/starter-web` |
| Visual and accessibility check (app on :4280) | `npm run visual --workspace frontend/starter-web -- --base-url http://localhost:4280 --routes e2e/routes/p4-public.json --baseline-dir ../../docs/evidence/P4/baselines` ([e2e/README.md](frontend/starter-web/e2e/README.md)) |
| Component catalog and tokens docs | `npm run catalog:generate -w @codelab/ng-dga`, `npm run tokens:generate -w @codelab/ng-dga` |
| Generated docs (`--check` for drift) | `E:\codelab-tools\dotnet10\dotnet.exe run --project backend/tools/Starter.DocsGen -- --out docs/generated` |
| Generated client (`api:gen:check` for drift) | `npm run api:gen --workspace frontend/starter-web` (after the docs) |
| New module, endpoint, permission | `node tools/scaffold/new-module.mjs --name <Pascal>`; `node tools/scaffold/new-endpoint.mjs --module <Name> --name <Pascal> --method <GET, POST, PUT or DELETE> --route <segment>`; `node tools/scaffold/new-permission.mjs --module <Name> --action <kebab>` |
| Recompile rules | `node tools/rules-engine/compile.mjs` (`--check` for the drift check) |
| Docs gate | `E:\codelab-tools\dotnet10\dotnet.exe run --project tools/docs-gate -- check --root .` |
| Provenance | `node tools/extract/check-origins.mjs --root .`; `node tools/extract/generate-source.mjs` (needs the harvest copy) |
| Fork-model test | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\fork\Test-ForkModel.ps1 -RemoveModule Demo` |
| Pack prereleases to the local feed | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Pack` |
| Release (platform owner) | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI -Pack -Release` |
| Golden vectors, C# against TS | `node tools/vectors/verify-vectors.mjs --prefix CODELABTEST` (and `--prefix ALTPFX`) |

The .NET 10 SDK is not on PATH on this machine; see [CLAUDE.md](CLAUDE.md#build).

## Local ports and database

| What | Where |
| --- | --- |
| API | http://localhost:5180 (launch profile Development; the Development CORS policy allows the web origin) |
| Web | http://localhost:4280 (`public/config.json` points at the API); the public portal at `/portal`, the showcase at `/showcase` when `config.json` has `"showcase": true` |
| Seq (optional) | http://localhost:5341 |
| Dev database | LocalDB `(localdb)\MSSQLLocalDB`, database `StarterDev`; migrations run on start in Development only |
| Integration tests | `StarterIt_<guid>` on LocalDB, created and dropped by each fixture; `CODELAB_TEST_SQL` overrides the server |

LocalDB note: when `sqllocaldb info MSSQLLocalDB` says Running but connections fail with named-pipe error 121 or 40, stop and start the instance from a normal (unsandboxed) shell: `sqllocaldb stop MSSQLLocalDB -k`, then `sqllocaldb start MSSQLLocalDB`. This disconnects every LocalDB user, including `StarterDev`.

## Use-case tracing

Trace a use case end to end. For example, the login and the permission sync after it:

1. **Route**: `frontend/starter-web/src/app/app.routes.ts` (`RoutePaths.Login`) → `features/auth/login-page/login-page.component.ts` → `features/auth/sign-in.service.ts`.
2. **Core service and client**: `core/auth/auth.service.ts` (`login`) → `core/api/clients/auth.client.ts` → `ApiService.post(ApiPaths.Login)` in `core/api/api.service.ts`, the only `HttpClient` user. The wire shapes are generated into `core/api/generated/openapi.ts` from `docs/generated/openapi.v1.json`; the phase 2 clients still use the hand-written `core/api/clients/api.models.ts`.
3. **Endpoint**: `POST /api/auth/login` → `AuthController` in `Starter.Modules.Identity.Api` (`backend/src/Modules/Identity/Api/AuthController.cs`), `[AllowAnonymous]` and listed in `EndpointAllowlist.cs`.
4. **Use case**: `LoginUseCase` in `Application/SignInUseCases.cs`, validated by `LoginCommandValidator`, → `SessionIssuer` → the ports implemented by `Infrastructure/Persistence/Repositories.cs` and `Infrastructure/Security/JwtAccessTokenIssuer.cs`.
5. **Permissions**: `core/permissions/permission-sync.service.ts` → `MeClient.permissions()` → `GET /api/me/permissions` → `MyPermissionsController` in `backend/src/Modules/Access/Api/AccessControllers.cs` (`[AuthenticatedUser]`) → `GetMyPermissions` in `Application/UseCases.cs` → `IUserPermissionResolver` → `IUserDirectory`, implemented by Identity.
6. **Gated page**: `/` → `permissionGuard()` with the generated `PermissionKeys.DashboardView` → `GET /api/dashboard` → `DashboardController` (`[HasPermission(AccessPermissionKeys.DashboardView)]`) → `GetDashboard`.

The permission, endpoint, error-code and config-key lists are generated into `docs/generated/`. Each module's `MODULE.md` names its entry points.

## Ports between modules

| Port (kernel, `Starter.Application` or `Starter.Infrastructure`) | Implemented by | Used by |
| --- | --- | --- |
| `IUserDirectory` (`FindBySubjectAsync` returns a `DirectoryUser`) | Identity | Access (the resolver and the handler) |
| `IUserPermissionGrants` (`EnsureGrantedAsync`) | Access | Identity (Development dev-user seeding) |
| `IPermissionCatalogSource` | every module with permissions (Access, Users) | Access `PermissionCatalog`, the union of all sources |
| `IAccountAdministration` | Identity | Users (the user admin endpoints and the import) |
| `IOrganizationScope` | Access (`CurrentUserOrganizationScope`) | no module yet; for modules that filter records by the caller's organization (P7) |
| `ICurrentUserContext` (`IsAuthenticated`, `SubjectId`, `SessionId`) | the kernel (`HttpCurrentUserContext`) | every module |
| `IStartupTask` (`ReferenceData` = 1, then `Accounts` = 2) | Access (catalog sync), Identity (account seeding) | the kernel `StartupTaskRunner` |

Identity, Access and Users never reference each other (`mustNotDependOn` in each `module.rules.yaml`); they meet only through these ports.
