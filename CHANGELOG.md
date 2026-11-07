# Changelog

Starter releases. Package changes are in [packages/CHANGELOG.md](packages/CHANGELOG.md); the format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased] - 1.0.0 (not tagged: dated when the CI, preset-matrix, Playwright smoke and real-tree upstream-merge legs are green)

First release. Details, open items and the needs-human list: [docs/release-notes/v1.0.0.md](docs/release-notes/v1.0.0.md).

### Added

- **P0 foundation:** `tools/build.ps1 -Target CI`, rules engine, hooks, docs gate, scrub sweep and secret scan, provenance of the harvested files, `Codelab.Build.Sdk`.
- **P1 security core:** `Codelab.Security`, `Codelab.Observability`, `Codelab.Errors`, `Codelab.Integration`, `Codelab.Testing`, `@codelab/ng-security`, `@codelab/ng-tools`; golden signing vectors; clean-code analyzers CLB1001 to CLB1005; offline handover kit.
- **P2 walking skeleton:** backend kernel, Identity and Access modules, architecture and contract tests, generated docs, the Angular 22 app, scaffold generators, `run-local.ps1`, the fork model.
- **P3 identity:** basic, Active Directory and SSO (OIDC) login, users and organizations, roles, permissions and grants, user import; `Codelab.Identity`.
- **P4 design system:** `@codelab/ng-dga` components, tokens and branding, the page patterns, the public portal pages.
- **P5 platform modules:** lookups, audit and record history, runtime settings, background jobs, outbox and numbering, notifications, contact us.
- **P6 attachments:** the attachment service, typed record attachments and the required-attachments interlock.
- **P7 records:** workflow kernel, records, assignments, deadlines and SLA, the service-requests sample.
- **P8 production edge:** integration gateway (SSO and SMS without API egress), Ops module and Ops Console, security headers and CSP, IIS deploy kits and runbooks.
- **P9 AI kit:** the `/build-feature` pipeline and its agents, auto-routed skills, refusal hooks, kit evals, the facts-drift gate.
- **P10 release:** `init.ps1` (interactive or `-ConfigFile`), one `InitialCreate` migration per DbContext, final `run-local.ps1`, handover kit, `tools/upstream-merge.ps1`, CI presets; all packages at 1.0.0; ADR-013.
