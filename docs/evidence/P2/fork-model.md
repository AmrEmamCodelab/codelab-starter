# P2 fork-model proof

Exit criterion: `init.ps1 -Minimal` on a scratch clone, an upstream commit that edits 5 files, adds 1 file and touches
the removed module, `tools/upstream-merge.ps1`, then the fork builds and tests green, conflicts occur only in the
expected files, the fork has no `packages/` source and restores Codelab.* only from the feed.

**Result (2026-11-05): PASS.** CI step 18 ran the test on the real repo, unpatched, and it passed end to end (run 3
below). Runs 1 and 2 (2026-11-04, by p2-int-proof) are kept as history: there the merge model passed but the fork's
build and test step was RED on D1, D3, D4 and D6. Those defects, and D5 and D7, are fixed.

## Run 3: the real repo, through CI (2026-11-05)

`tools\build.ps1 -Target CI`, step 18, which runs
`tools\fork\Test-ForkModel.ps1 -RemoveModule Demo` with `CODELAB_TEST_SQL=(localdb)\P2Proof`. Result: **PASS**.

| Step | Output | Check |
| --- | --- | --- |
| Snapshot and scaffold | 1554 files; `new-module.mjs --name Demo` scaffolded the Demo module | PASS |
| Feed | the repo `feed\` lacked the current versions, so the test packed `-ci.1` into its own scratch feed: `Codelab.Build.Sdk 0.2.1-ci.1`, `Codelab.Security`, `Codelab.Observability` and `Codelab.Errors 0.5.1-ci.1`, `Codelab.Testing 0.5.0-ci.1`, `Codelab.Integration 0.1.0-ci.1`, `@codelab/ng-security 0.5.1-ci.1`, `@codelab/ng-tools 0.1.1-ci.1`, `@codelab/ng-dga 0.1.0-ci.1` | PASS |
| Pack feed-mode check | `UseCodelabPackageSource=false`, isolated `NUGET_PACKAGES`, with Demo in the slnx: every suite green, including Demo 5/5, Architecture 35/35 and Contract 29/29 | PASS (D5, D7 fixed) |
| init | "Removed packages/ and its 13 solution project(s)"; dropped the `packages/npm/*` workspaces; committed "init: minimal fork (prefix FORKTEST, removed Demo)" | PASS |
| Merge | applied docs/README.md, docs/adr/README.md, docs/fork-model-probe.md, docs/standards/SKILL.md, tools/README.md; dropped the two Demo files and packages/fork-model-probe.md (removed-area); **CONFLICT CONTEXT.md (content)** only; "Merged cleanly" | PASS |
| Fork `dotnet test` | Application 53, Api 85, Identity 88, Access 48, Architecture 29, Contract 29, Integration 26, DocsGen 94, DocsGate 76, ArchitectureSpike 2: all green | PASS (D3, D4 fixed) |
| Fork frontend | `npm ci` from the refreshed lock, `npm run build` "Application bundle generation complete", `npm test` 40 files, **327/327** | PASS (D1, D6 fixed) |

## Run 1: the real repo, as asked

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\fork\Test-ForkModel.ps1 -RemoveModule Demo
```

Exit 2 (setup). Summary of the output:

- Snapshot 1508 files; `new-module.mjs --name Demo` created the Demo module and test project and changed
  `codelab-starter.slnx` and `Starter.Api.csproj` (the rules compile was skipped: the snapshot has no `node_modules`).
- "The feed E:\codelab-starter\feed lacks the current versions": the repo feed has no `@codelab/ng-dga` 0.1.0, so the
  test packs a ci prerelease in a scratch copy (`tools\build.ps1 -Pack -CiNumber 1`).
- That pack failed: "Feed-mode build failed: the pinned Codelab.Build.Sdk does not build the solution", 9 x NU1102
  "Unable to find package Codelab.Security / Observability / Errors with version (>= 0.5.1) ... Nearest version:
  0.5.1-ci.1". The scaffolded Demo module puts backend projects in the slnx, and the pack step's feed-mode check
  restores Codelab.* at the release pins in `Directory.Packages.props` (0.5.1) instead of the versions it just packed.

Blockers: D5 (the backend projects are not registered in `codelab-starter.slnx`, so even a passing run would not build
the backend in the fork) and D7 (the pack step's feed-mode check uses the release pins, see below).

## Run 2: scratch starter with the backend registered, scratch prerelease feed

To prove the model without editing shared files, p2-int-proof ran it against scratch copies under the session's
temporary folder:

- **Feed:** a snapshot of the working tree (backend not in the slnx, so the pack check passes), `npm ci`, then
  `tools\build.ps1 -Pack -CiNumber 1`: exit 0, all package test suites green. The feed it wrote holds exactly
  `Codelab.Build.Sdk 0.2.1-ci.1`, `Codelab.Security 0.5.1-ci.1`, `Codelab.Observability 0.5.1-ci.1`,
  `Codelab.Errors 0.5.1-ci.1`, `Codelab.Testing 0.5.0-ci.1`, `Codelab.Integration 0.1.0-ci.1` and the tarballs
  `@codelab/ng-security 0.5.1-ci.1`, `@codelab/ng-tools 0.1.1-ci.1`, `@codelab/ng-dga 0.1.0-ci.1`. The repo `feed\` was
  not used or written.
- **Starter:** a second snapshot with the 15 backend projects added to the slnx under `/backend/` (the D5 request),
  committed as a scratch git repo.

```
$env:CODELAB_TEST_SQL='(localdb)\P2Proof'
powershell -NoProfile -ExecutionPolicy Bypass -File tools\fork\Test-ForkModel.ps1 -RemoveModule Demo `
  -StarterRoot <scratch starter> -FeedDir <scratch feed> -KeepScratch
```

Exit 1 with 3 failures. Output, summarized:

| Step | Output | Check |
| --- | --- | --- |
| Scaffold | `new-module.mjs --name Demo`: 13 files created, slnx and `Starter.Api.csproj` changed | PASS |
| init | `init.ps1 -Minimal -ProductPrefix FORKTEST -RemoveModule Demo -FeedDir <scratch feed>`; "Removed packages/ and its 13 solution project(s)"; pinned every Codelab.* and @codelab/* to its `-ci.1` version; `@codelab/*` became `file:../../feed/npm/*.tgz`; prefix set in `appsettings.json` and `config.json`; one commit "init: minimal fork (prefix FORKTEST, removed Demo)" | PASS |
| Upstream commit | edited CONTEXT.md, docs/README.md, tools/README.md, docs/adr/README.md, docs/standards/SKILL.md; added docs/fork-model-probe.md; touched backend/src/Modules/Demo/MODULE.md, backend/src/Modules/Demo/fork-model-probe.md and packages/fork-model-probe.md | as specified |
| Merge | applied docs/README.md, docs/adr/README.md, docs/fork-model-probe.md, docs/standards/SKILL.md, tools/README.md; dropped the two Demo files and packages/fork-model-probe.md (removed-area); **CONFLICT CONTEXT.md (content)**, the one file the fork had pre-edited | PASS: conflicts only in the expected file |
| `-Continue` | conflict resolved by union; rules compiled; hashes recomputed; "Merged cleanly" | PASS |
| No `packages/` | asserted by the test (not listed among failures) | PASS |
| Feed origin | every `project.assets.json` Codelab.* is a package with the fork feed as a restore source; every `.nupkg.metadata` source is the fork's `feed\nuget`; the lock's `@codelab/*` resolve to `file:feed/npm/*.tgz` | PASS |
| `dotnet build codelab-starter.slnx -p:UseCodelabPackageSource=false` (empty `NUGET_PACKAGES`) | succeeded | PASS |
| `dotnet test` (same) | Application 53/53, Api 85/85, Identity 78/78, Access 47/47, DocsGen 94/94, Integration 22/22, DocsGate 70/70, ArchitectureSpike 2/2; **Architecture 28/29, Contract 22/24** | RED (D4, D3) |
| `npm install`, `npm run build --workspace frontend/starter-web` | build exit 127: "The ../../node_modules/@codelab/ng-dga/assets asset path must be within the workspace root" | RED (D1) |
| `npm test --workspace frontend/starter-web` | "Failed to resolve import @codelab/ng-security / @codelab/ng-dga" | RED (D6) |

Follow-up in the kept scratch fork: `npm ci` replaced the dangling links, then `npm test --workspace
frontend/starter-web` passed **37 files, 279 tests**; `npm run build` still failed on D1. The scratch folders were
deleted afterwards.

## Defects specific to the fork model

**D6 (fork-model test harness, `tools/fork/lib/fork-model.mjs`, `buildFork`).** The test runs `npm ci` in the clone
before init, which links `node_modules/@codelab/*` to the `packages/npm/*` workspaces. init then deletes `packages/` and
refreshes only the lock, so the links dangle, and `npm install` answers "up to date" and leaves them. Patch: use a clean
install from the refreshed lock in `buildFork` (line 220):

```js
for (const args of [['ci', '--no-audit', '--no-fund'], ['run', 'build', '--workspace', frontend], ['test', '--workspace', frontend]]) {
```

(with the argument list moved into a `lib/constants.mjs` constant). A real fork that ran `npm ci` before `init.ps1` has
the same problem; `tools/fork/README.md` should say "run `npm ci` again after init".

**D7 (`tools/build.ps1 -Pack`, shared file, integrator).** Once backend projects that use `CodelabPackageReference` are in
the slnx, the pack step's feed-mode verification fails with NU1102, because it restores Codelab.* at the release pins
in `Directory.Packages.props` (for example 0.5.1) while the feed it just wrote holds only `<v>-ci.<n>`. Patch: when the
feed-mode check builds the solution, pass the packed versions as overrides (the way init pins them, the exact version
else the highest `-ci.<n>`), or run the check with `-p:UseCodelabPackageSource=true` for the starter's own backend
projects and keep the feed-mode check to the package consumers. Until then, `Test-ForkModel.ps1` on the real repo needs
`-FeedDir` pointing at a complete feed (with `@codelab/ng-dga` 0.1.0), or the repo feed must get ng-dga 0.1.0 from a
release pack.
