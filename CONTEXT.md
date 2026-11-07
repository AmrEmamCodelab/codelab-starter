# CONTEXT.md

Status: **v1.0.0** (phase 10). Every phase P0 to P10 has landed: the versioned `Codelab.*` / `@codelab/*` packages at 1.0.0, the backend kernel and modules 1 to 13, 15 and 16 with the service-requests sample, the Angular 22 app with `@codelab/ng-dga` and the 8 page patterns, the attachment service, integration gateway and Ops Console, the IIS deploy and handover kits, the AI kit (`/build-feature`), and the fork tooling (`init.ps1`, `tools/upstream-merge.ps1`). What ships and what is still open: [CHANGELOG.md](CHANGELOG.md) and [docs/release-notes/v1.0.0.md](docs/release-notes/v1.0.0.md). Decisions are in [docs/adr/](docs/adr/README.md).

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
| Record | A business item that moves through a workflow; the sample record type is a service request. | `Record`, `ServiceRequest` |
| Transition | An allowed move from one status to another, with its permission and reason requirement. | `TransitionRule` |
| Lookup | Bilingual reference data seeded by migrations, referenced through ID constants. | `LookupEntity`, `<Name>Ids` |
| Refusal | An expected business "no" returned with an error code, not thrown; mapped to problem+json. | `Refusal`, `<Name>ErrorCodes`, `OutcomeResults` |
| PublicId | An opaque, encrypted identifier used on the wire instead of a database ID. | `PublicId<T>`, `WireId<T>` (TypeScript) |
| Reference code | The short code shown to a user on an error, linking to the log entry. | `referenceCode` in problem+json |
| Feed | Where packages are published: a local folder now, GitHub Packages later. | `feed/nuget`, `feed/npm` |

## Repo map (what exists after phase 10, v1.0.0)

| Path | What it is | Owner doc |
| --- | --- | --- |
| `CLAUDE.md`, `CONTEXT.md` | Agent entry point and this map. | this file |
| `codelab-starter.slnx` | The single solution: `/packages/`, `/backend/`, `/tests/` (the P0 spike), `/tools/`. | [tools/README.md](tools/README.md) |
| `codelab.starter.json` | Fork manifest: product prefix, `requiredModules` (Identity, Access), `removedModules`, `renameMap`, `starterBase` and the strict-JSON `prefixTargets`. | [tools/fork/README.md](tools/fork/README.md) |
| `init.ps1` | Fork bootstrap (ADR-003), interactive or `-ConfigFile <json>` (prefix, names en/ar, colour, logo, login modes, optional modules attachments/portal/sms/ops, database); `-WhatIf`, `-SkipDatabase`, `-SkipSecrets`, legacy `-Minimal`. Renames `Starter.*` to the prefix, removes unticked modules and `packages/`, pins `Codelab.*`/`@codelab/*` to the feed, writes fresh keys, creates the DB and the break-glass admin, and writes `codelab.starter.json`. | [tools/fork/README.md](tools/fork/README.md) |
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
| `frontend/starter-web/` | Angular 22 app (standalone, signals, Vitest, an npm workspace): `src/app/core` (ApiService, auth, permissions, i18n, session, branding), `layouts/` (WebLayouts: staff shell, portal shell, requester shell, footer, notifications menu and feed, error dialog), `features/{auth,dashboard,admin,errors,portal,notifications,attachments}` (admin: users, organizations, import, and access: roles list, role editor, user grants, see `features/admin/access/README.md`; portal: the public pages under `/portal`), `shared/` (WebShared: the `*hasPermission` directive, file picker, geo select), `patterns/` (WebPatterns: the 8 page patterns) and `showcase/` (WebShowcase: the `/showcase` gallery, behind the `showcase` flag of `config.json`). | [MODULE.md](frontend/starter-web/MODULE.md) |
| `frontend/starter-web/tools/` | ESLint and Stylelint config, `i18n-parity.mjs`, `no-inline-styles.mjs`, `permission-gates.mjs`, `rtl-lint.mjs`, `icons-subset.mjs`, `api-gen` (the generated client and `permission-keys.generated.ts`), the lint-fixture spec. | [README.md](frontend/starter-web/tools/README.md) |
| `frontend/starter-web/e2e/` | The visual and accessibility harness (`npm run visual`): screenshots in `ar` and `en` at 1440 and 390 px against baselines or a design target, plus axe. Baselines and evidence live in `docs/evidence/P4/`. | [README.md](frontend/starter-web/e2e/README.md) |
| `docs/design/` | The design kit: the generated component catalog and tokens, the 8 page patterns and a page per pattern. | [README.md](docs/design/README.md) |
| `packages/npm/ng-dga/` | `@codelab/ng-dga` 1.0.0: 31 standalone OnPush components (one `<selector>.catalog.json` each), `--dga-*` tokens, project branding (`provideDgaBranding`), the IBM Plex fonts and the owned icon font (ADR-009). | [MODULE.md](packages/npm/ng-dga/MODULE.md) |
| `packages/dotnet/Codelab.Build.Sdk/`, `Codelab.Build.Analyzers/` | MSBuild SDK (analyzers, banned APIs, release stamping) and the clean-code analyzers CLB1001 to CLB1005 (ADR-012). | the folder's README or `MODULE.md` |
| `packages/dotnet/Codelab.Identity/` | Codelab.Identity 1.0: Active Directory over LDAPS, generic OIDC code + PKCE, deployment guards, login matching. | [MODULE.md](packages/dotnet/Codelab.Identity/MODULE.md) |
| `packages/dotnet/Codelab.{Security,Observability,Errors,Integration,Testing}/` | The versioned core: signing, PublicIds, replay and sid-revocation stores; Serilog, correlation and health; problem+json; the outbound engine; architecture and contract checks. | `MODULE.md` in each folder |
| `packages/npm/ng-security/`, `packages/npm/ng-tools/` | `@codelab/ng-security` (signing interceptors, `WireId<T>`, the problem+json parser) and `@codelab/ng-tools` (ESLint and Stylelint presets, and the i18n, RTL, icon-subset and permission-gate checks). | `MODULE.md` in each folder |
| `packages/vectors/`, `packages/samples/` | Golden vectors over TEST-ONLY fixture keys; the feed-only package integration test (CI step 13). | `README.md` in each folder |
| `packages/versions.json`, `packages/CHANGELOG.md` | The one version source of every package (all at 1.0.0), and the package changelog. | [tools/README.md](tools/README.md) |
| `CHANGELOG.md`, `docs/release-notes/` | Starter changelog per release, and the release notes (what ships, open items, needs-human). | [v1.0.0](docs/release-notes/v1.0.0.md) |
| `feed/` | Local package feed filled by `tools/build.ps1 -Pack` (contents gitignored). | [feed/README.md](feed/README.md) |
| `tools/build.ps1` | The one build entry for CI and humans: the CI steps, `-Pack`, `-Pack -Release`. | [tools/README.md](tools/README.md) |
| `tools/scaffold/` | `new-module.mjs`, `new-endpoint.mjs`, `new-permission.mjs`, `new-lookup.mjs`, `new-transition.mjs`: deterministic generators (every one takes `--dry-run`), and the only sanctioned editor of the slnx and the host's module list. | [README.md](tools/scaffold/README.md) |
| `tools/devseed/` | `New-LocalSecrets.ps1` writes the gitignored local secrets of the API (`backend/src/Starter.Api/appsettings.Development.local.json`) and of the Attachment service (`services/attachment-service/src/Codelab.Attachments.Api/appsettings.Development.local.json`) with fresh random values: one shared ticket and receipt ring and an ES256 S2S key pair. An existing file only gets its missing keys; `-Force` regenerates both. | [README.md](tools/devseed/README.md) |
| `run-local.ps1`, `tools/run-local/` | The one-command local stack: API :5180, then the Attachment service :5185, then web :4280, plus Seq :5341 when available. `-NoWeb`, `-NoSeq`, `-NoAttachments`; `-Stop` stops only what it started. | [README.md](tools/run-local/README.md) |
| `docs/recipes/` | Step-by-step recipes (add an endpoint, add a permission). | [README.md](docs/recipes/README.md) |
| `docs/features/` | Feature specs written by the build-feature pipeline (one per feature, human-approved before any build); format in TEMPLATE.md, worked example examples/site-visit-permit.md. | [README.md](docs/features/README.md) |
| `plans/` | Tracked PRDs (`<slug>-prd.md`) and phased plans (`<slug>.md`). | [README.md](plans/README.md) |
| `tools/feature-spec/` | Feature-spec validator: `node tools/feature-spec/check-spec.mjs <spec.md> [--require-approved] [--json]` (exit 0 ok, 1 findings, 2 usage); library checkSpec(text). Tests: `node --test "tools/feature-spec/tests/**/*.test.mjs"`. | [MODULE.md](tools/feature-spec/MODULE.md) |
| `tools/facts-drift/` | Facts-drift gate: `node tools/facts-drift/check.mjs --root .` (every path, command, switch, type, skill and agent named in agents, skills and docs resolves; no stale-term or source-product text). | [MODULE.md](tools/facts-drift/MODULE.md) |
| `tools/ai-evals/` | Kit evals: plain-language fixtures per scaffold skill and input type run through Claude Code headless in throwaway %TEMP% workspaces and graded; pass rate >= 90% over >= 10 runs. Dry run: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/ai-evals/Invoke-KitEval.ps1 -Fixture all -DryRun`. `lib/HeadlessClaude.psm1` is the headless contract the pipeline proofs import. | [MODULE.md](tools/ai-evals/MODULE.md) |
| `tools/pipeline-proofs/` | Pipeline-proof harness: `Invoke-PipelineProof.ps1 -Proof <proof> -EvidenceDir <dir> [-DryRun] [-PreApproveSpec]` (proof: complaint, permit or feedback); inputs under inputs/<proof>/. | [MODULE.md](tools/pipeline-proofs/MODULE.md) |
| `.claude/skills/reduce-comments/` | Comments-only differ: `node .claude/skills/reduce-comments/scripts/comment-diff.mjs verify <baseline> <edited>` (`count <file>`). | [MODULE.md](.claude/skills/reduce-comments/scripts/MODULE.md) |
| `tools/extract/` | Allowlist extraction from the harvest copy, scrub sweep, secret scan, provenance (`generate-source.mjs`, `check-origins.mjs`). | `README.md` in the folder |
| `tools/rules-engine/`, `rules/` | Rules schema, base rules, the compiler to `rules/rules.compiled.json`, `rules/codelab.globalconfig` and the per-module files, the content check. | `README.md` in the folder |
| `tools/docs-gate/`, `tools/ci/`, `tools/vectors/` | The MODULE.md and dead-reference gate; host-neutral CI checks and the pack planner; the C# against TS vector check. | `README.md` in each folder |
| `deploy/handover/` | The offline handover kit and its proof (ADR-008). | `README.md` in the folder |
| `deploy/kits/` | IIS deploy kits: `Install-Common.psm1` (prerequisites, backup before change, settings merge that keeps server values and adds only new keys, payload mirror that never touches server settings/logs, idempotent IIS app pool + site/child-app helper with a deterministic -WhatIf plan) and the kits built on it (`Install-Attachment.ps1` in P6; Api/Web/Gateway/Ops in P8). Pester 3.4 tests in `tests/`. | [MODULE.md](deploy/kits/MODULE.md) |
| `services/attachment-service/` | The attachment service deployable (P6): Codelab.Attachments.Core (file-share store, streaming upload pipeline) and Codelab.Attachments.Api (http://localhost:5185; POST /api/uploads with a one-time upload ticket, S2S-only GET/DELETE /api/files/{fileId}, /health/live and /health/ready); its own Codelab.Attachments.slnx. | [MODULE.md](services/attachment-service/MODULE.md) |
| `services/integration-gateway/` | The DMZ relay (P8): SSO token/JWKS and SMS for an API without egress, ES256 S2S. | [MODULE.md](services/integration-gateway/MODULE.md) |
| `services/ops-console/` | The Windows-only support tool (P8): BFF + ops-web, reference-code lookup over Seq, jobs, certificates. | [MODULE.md](services/ops-console/MODULE.md) |
| `deploy/sql/`, `tools/sql-kit/`, `tools/kit/` | DBA scripts and the SQL kit; `tools/kit/New-DeployKit.ps1` builds `publish\deploy-<sha>\` (also `tools\build.ps1 -Kit`). | [MODULE.md](tools/kit/MODULE.md) |
| `docs/runbooks/` | Runbooks: key rotation, IdP certificate, CSP enforce, Seq, gateway, upstream merge, handover. | [README.md](docs/runbooks/README.md) |
| `.claude/hooks/`, `.claude/skills/`, `.claude/agents/` | Hooks that refuse rule violations early (ADR-006); skills (`coding-standards`, `do-work`, `write-a-skill`, `new-module`, `new-endpoint`, `new-permission`, `new-screen`); the 16 agents of the build-feature pipeline (spec-writer, workflow-designer, api-designer, backend-builder, screen-builder, test-writer, usecase-tester, visual-verifier, dga-design-capture, dga-reviewer, i18n-keeper, module-reviewer, standards-reviewer, security-reviewer, bug-verifier, commit-gate), mapped in [docs/ai-kit/README.md](docs/ai-kit/README.md); every agent reads [docs/ai-kit/knowledge-wiring.md](docs/ai-kit/knowledge-wiring.md). Set `CODELAB_HOOK_LOG=<file>` to have the PreToolUse hook and the Stop gate append one JSON line per decision (see .claude/hooks/README.md). | [CLAUDE.md](CLAUDE.md) routing table |
| `docs/standards/`, `docs/adr/` | Coding standards, error handling, examples; architecture decisions. | [docs/standards/SKILL.md](docs/standards/SKILL.md), [docs/adr/README.md](docs/adr/README.md) |
| `provenance/` | Harvest source commit, tree hash and per-file origin map (`origins/*.json`; `SOURCE.md` is generated). | `SOURCE.md` in the folder |
| `.github/` | `CODEOWNERS` and the PR template, ready for the GitHub move (ADR-007). | [ADR-007](docs/adr/ADR-007-repo-host-ci.md) |

## Module index

| # | Module | Code | Phase |
| --- | --- | --- | --- |
| 1 | Identity and login: basic login; the single session issuer (JWT with `sub`, `sid` and `sver`, a rotated refresh token); logout revokes the sid; idle timeout; break-glass and Development-only dev accounts; `IUserDirectory`; Active Directory and SSO via `Auth:Modes`, lockout, activation links, the auth event trail; `IAccountAdministration` for the Users module. | [Starter.Modules.Identity](backend/src/Modules/Identity/MODULE.md), schema `identity` | P2, P3 |
| 2 | Users and organizations: organizations (internal or external), the user admin endpoints over `IAccountAdministration`, the Excel user import (preview, then commit), activation: e-mailed by the outbox (`account-activation`, a fresh sealed ticket at drain time through INotificationService.SendImmediateEmailAsync) to an account with an e-mail address, otherwise a link for the administrator. | [Starter.Modules.Users](backend/src/Modules/Users/MODULE.md), schema `users` | P3, P5 |
| 3 | Roles and permissions: the catalog in code (`AccessPermissionKeys`), the `[HasPermission]` handler, the live per-user resolve, direct grants, the catalog synchronizer; roles, virtual roles, assignments and grants at a `PermissionScope` (global or own organization), `IOrganizationScope`, the self-lockout guard. | [Starter.Modules.Access](backend/src/Modules/Access/MODULE.md), schema `access` | P2, P3 |
| 4 | Lookups: bilingual reference data in one generic pair of tables (`lookups.LookupTypes`, `lookups.LookupItems`), seeded only by migrations (`tools/scaffold/new-lookup.mjs`), ids from `Starter.Domain.Lookups.<Name>Ids` constants; cached `GET /api/lookups/{type}` for signed-in users; admin view and activate/deactivate (`lookups.view`, `lookups.manage`); seeded type `contact-topics`. | [Starter.Modules.Lookups](backend/src/Modules/Lookups/MODULE.md), schema `lookups` | P5 |
| 5 | Audit and record history: the kernel `AuditSaveChangesInterceptor` writes every module save to `audit.AuditEntries` (personal data masked via `[PersonalData]`, secrets left out, SYSTEM actor for jobs); `RecordHistoryEntry` + `HasRecordHistory` (FK Restrict); the viewer `GET api/admin/audit` (`audit.view`); the DBA append-only script `deploy/sql/audit/`. | [Starter.Modules.Audit](backend/src/Modules/Audit/MODULE.md), schema `audit` (kernel) | P5 |
| 6 | Settings and background jobs: runtime settings (6a) in the database with a single-row version stamp, read through the kernel `IRuntimeSettings` from a node-wide snapshot (stamp checked once per request scope, 30 s TTL as the upper bound across nodes), admin list and update at `api/admin/settings` (`settings.view`, `settings.manage`); background jobs (6b): the catalog of every IJob registered with AddStarterJob, run history with actor SYSTEM, manual run now, recurring schedules on Hangfire (schema hangfire, no dashboard, Jobs:Enabled per node), built-in outbox-drain (every 5 min) and security-replay-purge (daily) | [Starter.Modules.Settings](backend/src/Modules/Settings/MODULE.md), [Starter.Modules.Jobs](backend/src/Modules/Jobs/MODULE.md), schemas `settings`, `jobs`, `hangfire` | P5 |
| 7 | Requests and records: the records kernel, base record, numbering, draft and submit, cancel and resubmit. | [Starter.Modules.Records](backend/src/Modules/Records/MODULE.md) | P7 |
| 8 | Workflow engine: the startup-validated transition table, allowed actions, reasons, timeline (kernel `Starter.Infrastructure/Workflow`, `/new-transition`). | [Starter.Infrastructure](backend/src/Starter.Infrastructure/MODULE.md) | P7 |
| 9 | Assignment and queues. | [Starter.Modules.Assignments](backend/src/Modules/Assignments/MODULE.md) | P7 |
| 10 | Deadlines and SLA timers (escalation). | [Starter.Modules.Deadlines](backend/src/Modules/Deadlines/MODULE.md) | P7 |
| 11 | Detail and oversight views (shared records UI, staff detail, admin oversight). | [Starter.Modules.Records](backend/src/Modules/Records/MODULE.md) | P7 |
| 12 | Notifications: bilingual templates seeded by migration, the bell items and feed API (GET /api/notifications, unread-count, mark read), the admin test send (notifications.test), e-mail and SMS through the outbox (SMTP to the internal relay or .eml file drop; SMS file sink, gateway sender in P8), INotificationService / INotificationRecipients kernel ports; the UI part (bell source and /notifications page) is [WebNotifications](frontend/starter-web/src/app/features/notifications/MODULE.md). | [Starter.Modules.Notifications](backend/src/Modules/Notifications/MODULE.md), schema `notifications` | P5, P8 |
| 13 | Attachments: the attachment type catalog (`ServiceAttachment`, ids from `Starter.Domain.Attachments.AttachmentTypeIds`, seeded by migration) and typed links of files to any record (`RecordAttachment`, soft remove); sealed upload tickets for direct browser upload to the attachment service, receipt-verified links (scope = record, type, user), S2S download and outbox delete (`attachments.delete-file`); `attachments.view`, `attachments.upload`, `attachments.remove`; the required-attachments interlock; Development demo record type `attachments-demo`. The UI part is the shared `app-record-attachments` widget ([MODULE.md](frontend/starter-web/src/app/shared/record-attachments/MODULE.md)) and the development-only `/attachments/demo` page ([WebAttachments](frontend/starter-web/src/app/features/attachments/MODULE.md)). | [Starter.Modules.Attachments](backend/src/Modules/Attachments/MODULE.md), schema `attachments`; `services/attachment-service` | P6 |
| 14 | Integrations | deferred (gateway extension point in P8) | - |
| 15 | Public portal pages and contact us: the UI (landing, service guide, FAQ, contact, privacy and terms under /portal) reads its content from the API and the contact form shows the CU-YYYY-NNN receipt; the backend stores contact messages under a CU-YYYY-NNN number (anonymous, rate-limited, announced to Portal:Contact:InboxEmail via the contact-received template) and serves the bilingual FAQ, service guide, privacy and terms content (seeded by migration, cached public read, admin replace with portal.content.view / portal.content.manage) | [Starter.Modules.Portal](backend/src/Modules/Portal/MODULE.md), schema `portal`; [WebPortal](frontend/starter-web/src/app/features/portal/MODULE.md) (UI) | P4, P5 |
| 16 | Logs, error reference codes, Ops Console. | [Starter.Modules.Ops](backend/src/Modules/Ops/MODULE.md); `services/ops-console` | P1, P8 |
| sample | Service requests (Draft → Submitted → Under Review → Approved/Rejected), deleted or renamed per project. | [Starter.Modules.ServiceRequests](backend/src/Modules/ServiceRequests/MODULE.md) | P7 |

Frontend features: [auth](frontend/starter-web/src/app/features/auth/MODULE.md) (login), [dashboard](frontend/starter-web/src/app/features/dashboard/MODULE.md), [admin](frontend/starter-web/src/app/features/admin/MODULE.md) (users, organizations, import and access: roles, role editor, user grants), [errors](frontend/starter-web/src/app/features/errors/MODULE.md) (403, 404) and [portal](frontend/starter-web/src/app/features/portal/MODULE.md) (WebPortal: the public pages). The shells are in [layouts](frontend/starter-web/src/app/layouts/MODULE.md), the core in [core](frontend/starter-web/src/app/core/MODULE.md), the shared pieces in [shared](frontend/starter-web/src/app/shared/MODULE.md), the 8 page patterns in [patterns](frontend/starter-web/src/app/patterns/MODULE.md) and the component gallery in [showcase](frontend/starter-web/src/app/showcase/MODULE.md) (WebShowcase).

## Commands

Run from the repo root. Put `E:\codelab-tools\node24` first on PATH in any shell that runs node, npm, npx or ng.

| Task | Command |
| --- | --- |
| Install Node tooling | `npm install` (repo root) |
| Fork bootstrap (once per project) | `powershell -NoProfile -ExecutionPolicy Bypass -File init.ps1` (interactive) or `... init.ps1 -ConfigFile <init.json>`; `-WhatIf` plans only |
| Merge a new starter release into a fork | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\upstream-merge.ps1 -Upstream <remote>` (`-Continue` after resolving conflicts); [runbook](docs/runbooks/README.md) |
| Offline handover kit | `powershell -NoProfile -ExecutionPolicy Bypass -File deploy\handover\New-HandoverKit.ps1 -OutputPath <dir>`, proof: `deploy\handover\Test-HandoverKit.ps1` ([README.md](deploy/handover/README.md)) |
| Local secrets (API + Attachment service; adds missing keys, `-Force` regenerates both) | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\devseed\New-LocalSecrets.ps1` |
| Run the local stack | `powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1` (`-Stop`, `-NoWeb`, `-NoSeq`, `-NoAttachments`); `-Slot <1-9>` starts a reviewer stack next to a running default stack (see [README.md](tools/run-local/README.md) "Slots") |
| API only | `E:\codelab-tools\dotnet10\dotnet.exe run --project backend\src\Starter.Api` (http://localhost:5180) |
| Attachment service only | `E:\codelab-tools\dotnet10\dotnet.exe run --project services\attachment-service\src\Codelab.Attachments.Api` (http://localhost:5185, health /health/ready); test: `E:\codelab-tools\dotnet10\dotnet.exe test services\attachment-service\Codelab.Attachments.slnx` |
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
| Attachment service | http://localhost:5185 (Development; health `/health/live`, `/health/ready`; CORS allows the web origin; files under `%LOCALAPPDATA%\codelab-starter\attachments`) |
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
| `IAttachableRecordAccess` (`RecordType`, `CanViewAsync`, `CanChangeAsync`) | the module that owns each record type (Attachments only for the Development `attachments-demo` type) | Attachments (every list, ticket, link, download and remove) |
| `IRequiredAttachments` (`FindMissingTypeKeysAsync`; refuse with `AttachmentErrorCodes.RequiredMissing`, 409) | Attachments (`RequiredAttachmentsCheck`) | modules that submit a record (Records, P7) |

Identity, Access and Users never reference each other (`mustNotDependOn` in each `module.rules.yaml`); they meet only through these ports.
