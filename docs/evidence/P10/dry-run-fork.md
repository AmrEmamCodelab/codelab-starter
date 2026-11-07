# P10 dry-run fork (stand-in for the 1-hour test)

Date 2026-11-07, run by the dry-run-fork agent on the build box (not a clean box, not an outside developer: the real 1-hour test stays needsHuman). Toolchain: `E:\codelab-tools\dotnet10\dotnet.exe`, Node 24.22.2 (`E:\codelab-tools\node24`). npm, NuGet and dotnet caches were warm, so the times below are lower bounds.

Source: `git clone E:\codelab-starter` (HEAD c8be969), with the current working tree (the parallel P10 tasks' uncommitted files, 2026-11-07 17:19) added as one snapshot commit `fa06379`. Everything ran in `%TEMP%\p10-dryrun`, which was deleted at the end.

Config (`acme.json`): prefix `ACME`, `Acme Services` / `خدمات أكمي`, `#1B7F5C`, a 64px SVG logo, loginModes `["basic"]`, attachments+portal kept, sms+ops removed. The fallback run kept sms (see G1). The database was `(localdb)\P2Proof` `P10DryAcme1` on fork 1 and skipped on fork 2.

## Steps and times

| # | Step | Command | Result | Time |
|---|---|---|---|---|
| 1 | Clone the starter + snapshot | `git clone`, copy the working-tree changes, commit | ok | 7 s |
| 2a | Init as specified (sms+ops removed) | `init.ps1 -ConfigFile acme.json -FeedDir E:\codelab-starter\feed -WhatIf` | **BLOCKED**, exit 1: sms needs markers in 10 files (G1) | 1 s |
| 2b | Init, sms kept, before `npm ci` | `init.ps1 -ConfigFile acme1.json -FeedDir …` | **exit 2** after Step 1: `Cannot find package 'ajv'` (rules compile). The tree was left renamed and uncommitted (G2) | 2 s |
| 2c | `npm ci` (root) | `npm ci` | ok | 10 s |
| 2d | Init, full | same as 2b | exit 0. 3 commits: rename (1730 paths, 1952 files), remove Ops + packages/ (pinned 1.0.0 from feed), manifest. Keys written. DB: 16 migration history tables, break-glass created, one-time password printed | 39 s |
| 3a | Backend build | `dotnet build codelab-acme.slnx -warnaserror --artifacts-path %TEMP%\p10-dry-run-fork` | **exit 1**: 44/53 projects built, 23 errors in 9 test projects (G3, G4) | 12 s |
| 3b | Web build right after init | `npm run build` (frontend/acme-web) | **exit 1**: `@codelab/ng-tools` not found; node_modules links point at the removed `packages/npm/*` (G5) | 1 s |
| 3c | `npm ci` again | `npm ci` | ok | 13 s |
| 3d | Web build | `npm run build` | ok, `dist/acme-web` | 9 s |
| 4a | Second fork: `npm ci` + init (DB skipped) | `init.ps1 -ConfigFile acme2.json …` | exit 0, 3 commits | 30 s |
| 4b | Init rerun on fork 2 | same | exit 0, all 5 steps `[skip]`, no new commit | <1 s |
| 4c | Keys differ | compare every string leaf ≥16 chars in the 3 local secrets files | 19 values. The 14 secrets all differ. The 5 identical values are not secrets (audiences, storage path, CORS origin) | - |
| 4d | Fixture-key check | `node tools/devseed/Test-NoFixtureKeys.mjs .` (both forks) | 3192 files, **0 fixture hits**, exit 0 | 2-3 s |
| 4e | Leftover grep | `git grep -w -E "Starter\|starter\|STARTER"` | Real leftovers: **0**. What remains is expected: the protected names `codelab.starter*`/`starterBase`/`starterVersion`, the `renameMap` `from` entries, the rule-id prefixes listed as `from` in the manifest, the rename engine's own boundary tests (`Restarter`, `Starters`). Paths: only `codelab.starter.json` and its schema | - |
| 5 | Upstream merge | new `Starter.Api/Hosting/StarterUpstreamProbe.cs` plus a one-line edit in `Modules/Ops/Api/OpsController.cs`, committed in starter-clone; in fork 1: `tools/upstream-merge.ps1 -Upstream <starter-clone> -Branch main -BuildCommand "& dotnet build backend\src\Acme.Api\Acme.Api.csproj -warnaserror …"` | 1st try exit 1: refused, dirty tree (G6). After committing: **exit 0, clean**. Applied `Acme.Api/Hosting/AcmeUpstreamProbe.cs` (namespace `Acme.Api.Hosting`, class `AcmeUpstreamProbe`). Dropped the Ops change (`removed-area`). Build 0/0. starterBase moved, hashes recomputed | 8 s |
| 6a | run-local slot 1 | `ConnectionStrings__Acme=<P2Proof P10DryAcme1>; run-local.ps1 -Slot 1` | 1st try exit 1: API crashed, SQL 4060 in `SqlSchemaStartup` on the new `P10DryAcme1_Slot1` (G7) | ~25 s |
| 6b | run-local slot 1, retry | same | exit 0. api, attachments, gateway, web and seq all healthy. Ops skipped (module off) | 20 s |
| 6c | Log in (Playwright MCP) | `http://localhost:4380` as breakglass with the printed password | Arabic default, RTL, login works, dashboard shows `مرحبًا، Administrator`, brand green applied. Screenshot: [dry-run-fork-login-ar.png](dry-run-fork-login-ar.png). Header brand gap: G8 | - |
| 6d | Stop | `run-local.ps1 -Stop -Slot 1` | stopped only the 4 slot PIDs. The owner's 5180/5185 kept running | - |

Machine time on the happy path, warm caches: about 3 minutes (npm ci + init + npm ci + web build + run-local). A person also has to work out G2, G5 and G7 without help.

## Gaps for the integrator (not fixed here)

- **G1 (module-removal):** unticking `sms` is refused (10 files lack `codelab:module Sms`). The P10 preset "core only" (no sms) cannot run yet.
- **G2 (init-flow):** init does not check for `node_modules` before it starts. After Step 1 renames the tree, the rules compile fails and leaves a half-renamed, uncommitted tree with exit 2. Add a preflight to the plan (BLOCKED "run npm ci first"), or run `npm ci` itself.
- **G3 (migration squash, in progress):** test projects still reference the deleted migrations (`OutboxMigrationIds.DeliveryTracking`, `AccessMigrationIds.RolesAndGrants`, `IdentityMigrationIds.AccountAdministration`, `AddServiceRequestAttachmentTypes`, `AddContactTopicsLookup`, `AddServiceRequestCategoriesLookup`, `AddServiceRequestTemplates`) and leave an IDE0005 in `SeedMigrationTests.cs`. The starter itself has this too; it is not caused by the rename.
- **G4 (module-removal):** removing ops leaves `TraceIdentifierMembers` dangling in `Contract.Tests/EndpointAllowlist.cs:55` (CS0103). This is the marker edit module-removal already listed, not applied yet.
- **G5 (init-flow):** after Step 2 (packages/ removed, feed tarballs pinned), the root `node_modules/@codelab/*` symlinks still point at `packages/npm/*`, so the web build fails until a second `npm ci`. Step 2 should run `npm ci`/`npm install` itself, not only `--package-lock-only`, or say so in the output.
- **G6 (web):** the first `npm run build` in a fresh fork rewrites the tracked `src/generated/icons/codelab-icons-subset.json` (the subset was committed out of date: chevron-up, download and others were removed). The tree is then dirty and upstream-merge refuses. Commit a current subset in the starter, or gitignore it.
- **G7 (init-keys-db / run-local / backend):**
  - Init Step 4 creates the DB named in `database.connectionString` but does not save that connection string. The fork still points at `(localdb)\MSSQLLocalDB;Database=AcmeDev`, and the dry run only worked through `ConnectionStrings__Acme`.
  - run-local on a missing slot DB crashes in `Codelab.Security` `SqlSchemaStartup` (SQL 4060). The DB was created by the migrations racing in parallel, so the retry passed. This is the startup-order bug init-keys-db already reported.
- **G8 (branding):** the shell header shows the generic `بوابة الخدمات` with a home icon, not the `appName` `خدمات أكمي` or `branding/logo.svg`. The browser tab title is `Acme`, not `Acme Services`. Check whether the shell is meant to read `config.json` branding.
- **G9 (init-rename, cosmetic):** the common noun "starter" in prose is renamed too. For example, `upstream-merge.ps1` help now reads "Merges acme (upstream) changes … a path/URL of the acme repository".
- **G10 (init-keys-db, hygiene):** Initialize-LocalDatabase builds into a fixed `%TEMP%\codelab-devseed-database` and leaves it there. Two forks on one box share that folder and `%LOCALAPPDATA%\codelab-acme\attachments`.
- **G11 (tooling):** Playwright MCP can write only under the agent session's root folder (outside this repo). The screenshot was taken there, copied here, and the 4 files the agent created there were deleted.

## Cleanup

- Dropped `P10DryAcme1` and `P10DryAcme1_Slot1` on `(localdb)\P2Proof` (0 left).
- Deleted `%TEMP%\p10-dryrun`, `%TEMP%\p10-dry-run-fork`, `%TEMP%\codelab-devseed-database`, `%TEMP%\codelab-acme` and `%LOCALAPPDATA%\codelab-acme`, all created by this run.
- Nothing was committed in `E:\codelab-starter`. The owner's app was not touched.
