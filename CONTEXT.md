# CONTEXT.md

Status: **phase 1** (core packages). The versioned security core is released to the local feed: `Codelab.Security`, `Codelab.Observability` and `Codelab.Errors` 0.5.1 (0.5.0 is known insecure), `Codelab.Testing` 0.5.0, `Codelab.Integration` 0.1.0, `Codelab.Build.Sdk` 0.2.1, `@codelab/ng-security` 0.5.1 and `@codelab/ng-tools` 0.1.1, proven by a feed-only sample (`packages/samples`). No application code exists yet (phase 2). This file grows with every phase. Sections marked *planned* describe the target from the platform team's build plan (kept outside the repository; its decisions are in [docs/adr/](docs/adr/README.md)).

## Business context

Codelab delivers request portals for Saudi ministries and government entities. A citizen, resident or organization (the **requester**) submits a request, internal **staff** review it through a workflow, and **admins** manage users, permissions and reference data. Every portal is Arabic-first (RTL) with English, follows the DGA (Digital Government Authority) look, and runs on premises: Windows Server with IIS, SQL Server, and a backend zone with no internet egress.

`codelab-starter` gives each new project that system on day one: three login modes (basic, Active Directory, SSO), users and organizations, roles and permissions, a generic records and workflow engine, notifications, attachments, public portal pages, logs and an ops console. A project forks it, runs `init.ps1` (name, branding, login modes, optional modules, fresh keys), and reshapes the sample `ServiceRequests` module into its own request types.

## Glossary

| Term | Meaning | Code name (target) |
| --- | --- | --- |
| Starter | This repository, the upstream of every project fork. | `codelab-starter`, `Starter.*` |
| Fork | A project repository created from the starter and fully renamed by `init.ps1` (ADR-003). | `codelab.starter.json` (`renameMap`, `starterBase`) |
| Platform owner | The person who approves rules, packages, hooks and deploy changes (Amr Emam). | `.github/CODEOWNERS` |
| Package | Versioned, security-critical code consumed by version (ADR-000, ADR-002). | `Codelab.*`, `@codelab/*` |
| Module | One business or platform area: one backend project plus one frontend feature folder (ADR-004). | `Starter.Modules.<Name>` |
| Rules file | A module's machine-readable guardrails, inherited from the base rules. | `module.rules.yaml`, `rules/base.rules.yaml` |
| Record | A business item that moves through a workflow; the sample record type is a service request. | `Record`, `ServiceRequest` |
| Transition | An allowed move from one status to another, with its permission and reason requirement. | `TransitionRule`, transition catalog |
| Lookup | Bilingual reference data seeded by migrations, referenced through ID constants. | `LookupEntity`, `<Name>Ids` |
| Refusal | An expected business "no" returned with an error code, not thrown. | `CodedRefusal` |
| PublicId | An opaque, encrypted identifier used on the wire instead of a database ID. | `PublicId<T>`, `WireId<T>` (TypeScript) |
| Reference code | The short code shown to a user on an error, linking to the log entry. | `referenceCode` in problem+json |
| Feed | Where packages are published: a local folder now, GitHub Packages later. | `feed/nuget`, `feed/npm` |

## Repo map (what exists after phase 1)

| Path | What it is | Owner doc |
| --- | --- | --- |
| `CLAUDE.md`, `CONTEXT.md` | Agent entry point and this map. | this file |
| `codelab-starter.slnx` | The single solution. Every .NET project is added here. | [tools/README.md](tools/README.md) |
| `global.json`, `.nvmrc` | .NET SDK 10.0.403 and Node 24 pins (ADR-005). | [ADR-005](docs/adr/ADR-005-node-pin.md) |
| `Directory.Build.props/.targets`, `Directory.Packages.props` | Imports Codelab.Build.Sdk from source or feed (`UseCodelabPackageSource`), central package versions. | [ADR-002](docs/adr/ADR-002-package-consumption.md) |
| `nuget.config`, `.npmrc`, `package.json` | Package sources (`Codelab.*` only from the feed), npm workspaces for `packages/npm/*`. | [ADR-002](docs/adr/ADR-002-package-consumption.md) |
| `packages/dotnet/Codelab.Build.Sdk/` | MSBuild SDK: analyzers, banned APIs (global and per-project, `Thread.Sleep` included), PublicApiAnalyzers, package validation, release stamping. | `README.md` in the folder |
| `packages/dotnet/Codelab.Build.Analyzers/` | The clean-code analyzers CLB1001 method lines, CLB1002 file lines, CLB1003 parameters, CLB1004 magic numbers, CLB1005 enum member values (ADR-012), shipped inside Codelab.Build.Sdk; thresholds from the module `.editorconfig`, then `rules/codelab.globalconfig`. | `MODULE.md` in the folder |
| `packages/dotnet/Codelab.Security/` | Request/response signing, PublicIds, replay guards (memory, SQL), security headers, CORS, body ceiling, rate limiting, key-ring guards with the published-material denylist, sealed tickets, ES256 service assertions, upload safety, session revocation. | `MODULE.md` in the folder |
| `packages/dotnet/Codelab.Observability/` | One Serilog bootstrap, correlation id and reference codes, PII masking, user hash, opt-in OTel and Seq, job failure filter, health endpoints. | `MODULE.md` in the folder |
| `packages/dotnet/Codelab.Errors/` | `GlobalExceptionHandler`, the ordered failure classifier table, one problem+json envelope, `CodedRefusal`. | `MODULE.md` in the folder |
| `packages/dotnet/Codelab.Integration/` | Outbound engine 0.1: typed clients, retry and timeout policy, failure classification, certificate validation always on. | `MODULE.md` in the folder |
| `packages/dotnet/Codelab.Testing/` | Architecture rules from `rules.compiled.json`, source sweeps, the PII canary sink, the golden-vector runner, endpoint contract checks. | `MODULE.md` in the folder |
| `packages/dotnet/*.Tests/` | One test project per package (in `codelab-starter.slnx`, run by CI step 6). | the package's `MODULE.md` |
| `packages/npm/ng-security/` | `@codelab/ng-security`: canonical framing, request signing and response verification interceptors, session keys, `WireId<T>`, the problem+json parser; `./core` has no Angular import. | `MODULE.md` in the folder |
| `packages/npm/ng-tools/` | `@codelab/ng-tools` 0.1: ESLint boundary preset, `codelab/wire-id-cast`, Stylelint preset, `codelab-vectors-sync`. | `MODULE.md` in the folder |
| `packages/vectors/` | Golden vectors (canonical, request and response signature, PublicId, session keys) over TEST-ONLY fixture keys (`fixture-keys/`); C# and TS both run them. | `README.md` in the folder |
| `packages/samples/` | The package integration test (CI step 13): `Package.Sample.Api` + `Package.Sample.Tests` and `ng-security-consumer`, restored from the feed only. Not in the solution. | `README.md` in the folder |
| `packages/versions.json`, `packages/CHANGELOG.md` | The one version source of every package, and the package changelog. | [tools/README.md](tools/README.md) (Versioning) |
| `feed/` | Local package feed filled by `tools/build.ps1 -Pack` (contents gitignored). | `feed/README.md` |
| `tools/build.ps1` | The one build entry for CI and humans: 14 CI steps, `-Pack`, `-Pack -Release`. | [tools/README.md](tools/README.md) |
| `tools/vectors/` | `verify-vectors.mjs` (C# and TS agree for a prefix) and `fixture-keys.mjs` (fixture keys and their denylist file). | `README.md` in the folder |
| `tools/extract/` | Allowlist extraction from the harvest copy, scrub sweep, secret scan, term denylist. | `README.md` in the folder |
| `tools/rules-engine/`, `rules/` | Rules schema, base rules, compiler to `rules/rules.compiled.json`, `rules/codelab.globalconfig` and the per-module files, emitters. | `README.md` in the folder |
| `tools/docs-gate/` | .NET tool: MODULE.md sections, dead references, module-changed-without-docs. | `README.md` in the folder |
| `tools/ci/` | Host-neutral CI checks (conflict markers, base ref) and the pack planner `package-versions.mjs`. | `README.md` in the folder |
| `deploy/handover/` | `New-HandoverKit.ps1` (the full offline dependency closure, ADR-008) and `Test-HandoverKit.ps1` (offline restore and build proof). | `README.md` in the folder |
| `.claude/hooks/`, `.claude/settings.json` | Claude Code hooks that refuse rule violations early (ADR-006). | `README.md` in the folder |
| `.claude/skills/` | Skills: `coding-standards`, `do-work`, `write-a-skill`, and the planned scaffold skills. | [CLAUDE.md](CLAUDE.md) routing table |
| `docs/standards/` | Coding standards, error handling, examples. | [docs/standards/SKILL.md](docs/standards/SKILL.md) |
| `docs/adr/` | Architecture decisions. | [docs/adr/README.md](docs/adr/README.md) |
| `provenance/` | Harvest source commit, tree hash and per-file origin map. | `SOURCE.md` in the folder |
| `.github/` | `CODEOWNERS` and the PR template, ready for the GitHub move (ADR-007). | [ADR-007](docs/adr/ADR-007-repo-host-ci.md) |

## Planned layout (later phases)

| Path | Phase | What it will hold |
| --- | --- | --- |
| `backend/src/Starter.{Domain,Application,Infrastructure,Api}` | P2 | Kernel: ports, use-case plumbing, EF, outbox, numbering, jobs, auditing, host (`Program.cs` at most 150 lines). |
| `backend/src/Modules/<Name>/` | P2 onward | One project per module, with `module.rules.yaml`, `MODULE.md` and a generated `CLAUDE.md`. |
| `backend/tests/` | P2 | Architecture, contract, application, API and integration tests. |
| `frontend/starter-web/` | P2 | Angular 22 app: `core`, `layouts`, `patterns`, `shared`, `features`, `showcase`. |
| `packages/dotnet/Codelab.Identity` | P3 | AD (LDAP) and OIDC SSO providers and guards. |
| `packages/npm/ng-dga` | P2 | The owned DGA component library. |
| `services/{attachment-service,integration-gateway,ops-console}` | P6, P8 | Separately released deployables. |
| `deploy/` | P6, P8, P10 | IIS kits, security headers, SQL scripts for the DBA, Seq; the handover kit (`deploy/handover`, v0 since P1) is finished in P10. |
| `docs/{recipes,runbooks,design,generated}` | P2 onward | How-to recipes, runbooks, design catalog, generated docs (drift-checked). |
| `init.ps1`, `run-local.ps1`, `tools/upstream-merge.ps1`, `tools/remove-module.ps1` | P2, P10 | Fork bootstrap, local run, upstream merge with rename replay. |

## Module index (planned)

| # | Module | Phase |
| --- | --- | --- |
| 1 | Identity and login (basic, AD, SSO; one session issuer; lockout; idle timeout; logout; break-glass admin) | P2, P3 |
| 2 | Users and organizations (internal and external users, organizations, Excel import) | P3, P5 |
| 3 | Roles and permissions (catalog in code, role tree editor, direct grants, live changes, org scope) | P2, P3 |
| 4 | Lookups (bilingual, seeded by migrations) | P5 |
| 5 | Audit and record history (personal data masked) | P5 |
| 6 | Settings and background jobs | P5 |
| 7 | Requests and records (base record, numbering, draft and submit wizard, cancel and resubmit) | P7 |
| 8 | Workflow engine (startup-validated transition table, allowed actions, reasons, timeline) | P7 |
| 9 | Assignment and queues | P7 |
| 10 | Deadlines and SLA timers | P7 |
| 11 | Detail and oversight views | P7 |
| 12 | Notifications (bell, email, SMS, bilingual templates, outbox) | P5, P8 |
| 13 | Attachments (service, typed links, upload UI) | P6 |
| 14 | Integrations | deferred (gateway extension point in P8) |
| 15 | Public portal pages and contact us | P4, P5 |
| 16 | Logs, error reference codes, ops console | P1, P8 |
| sample | Service requests (Draft → Submitted → Under Review → Approved/Rejected), deleted or renamed per project | P7 |

## Commands

| Task | Command |
| --- | --- |
| Install Node tooling | `npm install` (repo root) |
| Full CI | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI` |
| Pack prereleases to the local feed | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Pack` |
| Release (platform owner; all CI steps first, refused when a version is already in the feed) | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI -Pack -Release` |
| npm packages build and test (Vitest) | `npm run build --workspaces` then `npm test --workspaces` (repo root, Node 24 first on PATH) |
| Golden vectors, C# against TS | `node tools/vectors/verify-vectors.mjs --prefix CODELABTEST` (and a second prefix, e.g. `--prefix ALTPFX`) |
| Package integration test (feed only) | CI step 13; by hand see [packages/samples/README.md](packages/samples/README.md) |
| Offline handover kit | `powershell -NoProfile -ExecutionPolicy Bypass -File deploy\handover\New-HandoverKit.ps1 -OutputPath <kit> -Projects <csproj,...> -NpmProjects <folder,...>`, then `deploy\handover\Test-HandoverKit.ps1 -KitPath <kit>` |
| Build and test .NET | `E:\codelab-tools\dotnet10\dotnet.exe build codelab-starter.slnx -warnaserror` then `... test codelab-starter.slnx` |
| Recompile rules | `node tools/rules-engine/compile.mjs` (`--check` for the drift check) |
| Docs gate | `E:\codelab-tools\dotnet10\dotnet.exe run --project tools/docs-gate -- check --root .` |

The .NET 10 SDK is not on PATH on this machine; see [CLAUDE.md](CLAUDE.md#build).

## Use-case tracing (planned, phase 2 onward)

To trace a use case end to end: route in `frontend/starter-web/src/app/features/<module>/` → feature API client (wrapping the generated client through `ApiService`) → endpoint in `Starter.Modules.<Name>` (`.Api` namespace, `[HasPermission]`) → use case (`.Application`) → domain rule or transition (`.Domain`) → repository (`.Infrastructure`). The permission, endpoint, error-code and status lists are generated into `docs/generated/`. Each module's `MODULE.md` names its entry points.
