# CONTEXT.md

Status: **phase 0** (bootstrap, provenance and guardrails). No application code exists yet. This file grows with every phase. Sections marked *planned* describe the target from the platform team's build plan (kept outside the repository; its decisions are in [docs/adr/](docs/adr/README.md)).

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

## Repo map (what exists in phase 0)

| Path | What it is | Owner doc |
| --- | --- | --- |
| `CLAUDE.md`, `CONTEXT.md` | Agent entry point and this map. | this file |
| `codelab-starter.slnx` | The single solution. Every .NET project is added here. | [tools/README.md](tools/README.md) |
| `global.json`, `.nvmrc` | .NET SDK 10.0.403 and Node 24 pins (ADR-005). | [ADR-005](docs/adr/ADR-005-node-pin.md) |
| `Directory.Build.props/.targets`, `Directory.Packages.props` | Imports Codelab.Build.Sdk from source or feed (`UseCodelabPackageSource`), central package versions. | [ADR-002](docs/adr/ADR-002-package-consumption.md) |
| `nuget.config`, `.npmrc`, `package.json` | Package sources (`Codelab.*` only from the feed), npm workspaces for `packages/npm/*`. | [ADR-002](docs/adr/ADR-002-package-consumption.md) |
| `packages/dotnet/Codelab.Build.Sdk/` | MSBuild SDK: analyzers, banned APIs (global and per-project), PublicApiAnalyzers, package validation, release stamping. | `README.md` in the folder |
| `packages/npm/ng-tools/` | `@codelab/ng-tools` placeholder (lints and tooling arrive in phase 1). | `README.md` in the folder |
| `feed/` | Local package feed filled by `tools/build.ps1 -Pack` (contents gitignored). | `feed/README.md` |
| `tools/build.ps1` | The one build entry for CI and humans. | [tools/README.md](tools/README.md) |
| `tools/extract/` | Allowlist extraction from the harvest copy, scrub sweep, secret scan, term denylist. | `README.md` in the folder |
| `tools/rules-engine/`, `rules/` | Rules schema, base rules, compiler to `rules/rules.compiled.json`, emitters. | `README.md` in the folder |
| `tools/docs-gate/` | .NET tool: MODULE.md sections, dead references, module-changed-without-docs. | `README.md` in the folder |
| `tools/ci/` | Host-neutral CI checks (conflict markers). | `README.md` in the folder |
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
| `packages/dotnet/Codelab.{Security,Observability,Errors,Integration,Identity,Testing}` | P1, P3 | The versioned core. |
| `packages/npm/{ng-security,ng-dga}` | P1, P2 | Frontend security and the owned DGA component library. |
| `services/{attachment-service,integration-gateway,ops-console}` | P6, P8 | Separately released deployables. |
| `deploy/` | P6, P8 | IIS kits, security headers, SQL scripts for the DBA, Seq, handover kit. |
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
| Pack to the local feed | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Pack` |
| Build and test .NET | `E:\codelab-tools\dotnet10\dotnet.exe build codelab-starter.slnx -warnaserror` then `... test codelab-starter.slnx` |
| Recompile rules | `node tools/rules-engine/compile.mjs` (`--check` for the drift check) |
| Docs gate | `E:\codelab-tools\dotnet10\dotnet.exe run --project tools/docs-gate -- check --root .` |

The .NET 10 SDK is not on PATH on this machine; see [CLAUDE.md](CLAUDE.md#build).

## Use-case tracing (planned, phase 2 onward)

To trace a use case end to end: route in `frontend/starter-web/src/app/features/<module>/` → feature API client (wrapping the generated client through `ApiService`) → endpoint in `Starter.Modules.<Name>` (`.Api` namespace, `[HasPermission]`) → use case (`.Application`) → domain rule or transition (`.Domain`) → repository (`.Infrastructure`). The permission, endpoint, error-code and status lists are generated into `docs/generated/`. Each module's `MODULE.md` names its entry points.
