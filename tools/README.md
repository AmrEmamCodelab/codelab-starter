# tools/

## Purpose

Repo tooling. This page covers `build.ps1`, the single build entry point for CI and humans. Each tool folder
(`extract/`, `rules-engine/`, `docs-gate/`, `ci/`, ...) has its own README.

| Folder | What it holds |
|--------|---------------|
| `extract/`, `rules-engine/`, `docs-gate/`, `ci/`, `vectors/` | Harvest and scanners, the rules compiler, the docs gate, host-neutral CI checks, the vector check (own READMEs) |
| `scaffold/` | `new-module`, `new-endpoint` and `new-permission` generators ([README](scaffold/README.md)) |
| `fork/`, `remove-module.ps1`, `upstream-merge.ps1` | The fork model and the fork-model test ([README](fork/README.md)) |
| `run-local/` (+ the root `run-local.ps1`) | Local stack start and stop ([README](run-local/README.md)) |
| `devseed/` | `New-LocalSecrets.ps1`, the gitignored local secrets file |
| `../backend/tools/Starter.DocsGen` | Generated docs (`docs/generated`) and the `--check` drift gate ([MODULE.md](../backend/tools/Starter.DocsGen/MODULE.md)) |

## Use

```powershell
# Full CI contract (default when no switch is given)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI

# Pack every package in packages/versions.json as <version>-ci.<n> (one <n> per run) -> feed\nuget, feed\npm
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Pack

# Release: pack the exact versions.json versions; refused when any is already in the feed
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Pack -Release

# Check a pack or release plan without packing (optionally against another feed folder)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Pack -Release -DryRun [-FeedDir <folder>]

# CI, then pack only when CI is green; stop at the first failing step; fixed prerelease number
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI -Pack -FailFast -CiNumber 42
```

`-Target CI` runs, in order (paths relative to the repo root):

| # | Step | Command |
|---|------|---------|
| 1 | scrub sweep | `node tools/extract/scrub-sweep.mjs --root .` |
| 2 | secret scan | `node tools/extract/secret-scan.mjs --root .` + `gitleaks detect --no-git --source . --redact` when gitleaks is on PATH or at `tools\bin\gitleaks.exe` |
| 3 | rules check | `node tools/rules-engine/compile.mjs --check` |
| 4 | rules tests | `node --test "tools/rules-engine/tests/**/*.test.mjs"` |
| 5 | hook tests | `node --test ".claude/hooks/tests/**/*.test.mjs"` |
| 6 | dotnet | `<dotnet> build codelab-starter.slnx -warnaserror` then `<dotnet> test codelab-starter.slnx --no-build --logger trx --results-directory <temp>`. FAILS when the TRX results show 0 executed tests of `Starter.Integration.Tests` (the integration tier needs LocalDB `(localdb)\MSSQLLocalDB`, or `CODELAB_TEST_SQL`) |
| 7 | docs gate | `<dotnet> run --project tools/docs-gate -- check --root . --base <base ref>` (base ref from `tools/ci/base-ref.mjs`) |
| 8 | conflict markers | `node tools/ci/conflict-markers.mjs --root .` |
| 9 | content rules | `node tools/rules-engine/check-content.mjs --root .`: every forbidden/required rule of `rules/rules.compiled.json` over every tracked file (absolute state; the hooks' rules, enforced in CI) |
| 10 | scanner tests | `node --test` over `tools/extract/tests`, `tools/ci/tests`, `tools/vectors/tests`, `deploy/handover/tests`, `tools/fork/tests`, `tools/scaffold/tests` (with the slow dogfood test; never set `CODELAB_SKIP_DOGFOOD` in CI), `tools/devseed/tests` and `tools/run-local` (`**/*.test.mjs`; the handover, fork and devseed tests need Windows PowerShell) |
| 11 | npm build+test | `npm ci` (only when `node_modules` is missing), then `npm run build --workspaces`, `node tools/ci/lint-packages.mjs --root .` and `npm test --workspaces` (every workspace: the `packages/npm/*` packages and `frontend/starter-web`, whose build is `ng build`) |
| 12 | vectors verify | `node tools/vectors/verify-vectors.mjs --prefix CODELABTEST`, then `--prefix ALTPFX`: C# and TS produce the same canonical bytes and signatures for the pinned prefix and for a second one |
| 13 | package integration test | `packages/samples` (see its README), consumed like a fork: feed only. It refuses a Codelab `ProjectReference` in a sample and a sample in the solution, then runs `<dotnet> build` and `test` of `Package.Sample.Tests` with an empty temporary `NUGET_PACKAGES`, then `npm ci` and `npm test` of `ng-security-consumer`. The versions come from `packages/samples/Codelab.Versions.props` and the consumer's `package.json`. When one of them is not in `feed\`, as on a fresh clone, it first packs a ci prerelease (as `-Pack` does) and tests against that. |
| 14 | handover offline | `deploy/handover/New-HandoverKit.ps1` builds a kit for `Package.Sample.Tests` and `ng-security-consumer` at the versions step 13 tested, then `Test-HandoverKit.ps1` restores and builds both from the kit alone (empty package folders, HTTP blocked through an unroutable proxy). A simulation: the VM run with the network adapter disabled stays an owner step (ADR-008). |
| 15 | provenance origins | `node tools/extract/check-origins.mjs --root .`: every `provenance/origins/*.json` entry is `rewritten` with a target that exists, or `staged` with a `note` or `deferred` field |
| 16 | generated drift | `<dotnet> run --project backend/tools/Starter.DocsGen -- --out docs/generated --check`, then `npm run api:gen:check --workspace frontend/starter-web` (needs no SQL and no secrets file) |
| 17 | frontend lint | `npm run lint --workspace frontend/starter-web`: ESLint (module boundaries, HTTP only in `core/api`), Stylelint (tokens, logical CSS) and `tools/i18n-parity.mjs`. Step 11 lints `packages/npm` only |
| 18 | fork-model test | `powershell -NoProfile -ExecutionPolicy Bypass -File tools\fork\Test-ForkModel.ps1 -RemoveModule Demo` (ADR-003): scaffold Demo upstream, fork, `init.ps1 -Minimal -RemoveModule Demo`, upstream merge, then build and test the fork from the feed, all in `%TEMP%` |

After the steps, the platform-owned paths the branch changes against the base ref are listed (owner review before a
local merge, while no host enforces CODEOWNERS).

### Pack and release

`-Pack` packs every package listed in `packages/versions.json`:

| Package | Kind | Packed with |
|---------|------|-------------|
| Codelab.Build.Sdk (Codelab.Build.Analyzers ships inside it) | NuGet | `dotnet pack -c Release -p:PackageVersion=<v> -p:CodelabRelease=<v>`, then `dotnet nuget push --source feed\nuget` |
| Codelab.Security, .Observability, .Errors, .Integration, .Testing | NuGet | the same |
| @codelab/ng-security, @codelab/ng-tools | npm | `npm run build --workspace <name>`, then `npm pack --ignore-scripts` on a `%TEMP%` copy whose `package.json` carries `<v>`; the tarball is copied to `feed\npm` |

One run, in order:

1. `node tools/ci/package-versions.mjs plan` builds the plan. Every package gets `<version>-ci.<n>` with the same
   `<n>`, or the exact version with `-Release`. The plan is REFUSED when a planned file is already in the feed or a
   package source is missing.
2. Every package is packed into one `%TEMP%` staging folder.
3. The Codelab dependencies inside each `.nupkg` are checked against the plan. For example, Codelab.Errors must
   depend on the Codelab.Observability version of this run.
4. Only then is anything pushed to `feed\`.
5. The SDK version is pinned in `Directory.Build.props` (`CodelabBuildSdkVersion`).
6. The solution is built and tested with `-p:UseCodelabPackageSource=false` and an empty, temporary
   `NUGET_PACKAGES`: the ADR-002 feed mode a fork uses.

`-Pack -Release` refuses, before packing anything, when any `versions.json` version is already in `feed\nuget` or
`feed\npm`. `-Pack -DryRun` stops after the plan: it shows what would be packed and whether a release would be
refused, and changes nothing. `-FeedDir <folder>` checks against another feed folder, which must hold `nuget\` and
`npm\`. It is accepted only with `-DryRun`, because the feed-mode build restores through `nuget.config`, which names
`feed\nuget`.

### Versioning

The scheme is explicit-version-file semver. `packages/versions.json` maps each package id to its next release
version, a plain `x.y.z`. It is the only version source. The tests in `tools/ci/tests` fail when a Codelab package
project or an npm workspace package is missing from it.

- **Prereleases:** `-Pack` publishes `<x.y.z>-ci.<n>`. `<n>` is `-CiNumber`, else `CODELAB_CI_NUMBER`, else one above
  the highest `ci.<n>` in `feed\`.
- **Releases:**
  1. Set the version in `versions.json`.
  2. In `packages/CHANGELOG.md`, rename the package's `Unreleased` section to `[<version>] - <date>`.
  3. Move the `PublicAPI.Unshipped.txt` entries into `PublicAPI.Shipped.txt`.
  4. Run `-Target CI -Pack -Release`: the release is packed only when all 18 steps pass, so step 13 has proven the
     sample against the latest prerelease first.
  5. Point the sample at the release (`packages/samples/Codelab.Versions.props`, the consumer's `package.json`, then
     `npm install` there to refresh its lock) and run `-Target CI` again: step 13 now proves the released packages.

  A released version is never packed again. A release skips a package that is already in the feed at its
  `versions.json` version and has no `Unreleased` changelog section (released and unchanged), so one changed package
  can ship without bumping the others; a package in the feed that still has an `Unreleased` section is refused.
- **CI never writes to the feed:** when a sample pin is missing (a fresh clone, or a version not yet released),
  `-Target CI` step 13 packs a ci prerelease into a temporary feed, tests the sample against it and removes it.
  `feed\` and the `Directory.Build.props` SDK pin change only through `-Pack`.
- **Assemblies:** each assembly is stamped with its package version through `CodelabRelease` (Codelab.Build.Sdk
  release stamping). The InformationalVersion is `<version>+<short sha>`.
- **Source manifests:** they are never edited. The npm `package.json` versions in `packages/npm/*` are ignored for
  packing. The Codelab.Build.Sdk csproj keeps `CodelabBuildSdkBaseVersion` only as the fallback for a bare
  `dotnet pack`, and a test keeps it equal to `versions.json`.

**Why not MinVer or git-tag versions.** Agents never tag, and the orchestrator commits only after a phase.

- A tag-based version would therefore be `0.0.0-alpha.0.<height>` for every package all through the build-out.
- Tag-based versioning cannot give packages in one repo different versions (Integration `0.1.0` next to Security
  `0.5.0`) without one tag prefix per package.

An explicit file can be reviewed in a diff, works in a repo with no commits, and gives one `ci.<n>` across NuGet and
npm. A `<package>/v<version>` git tag may be added after a release, but nothing reads it.

Environment:

| Variable | Effect |
|----------|--------|
| `CODELAB_DOTNET` | dotnet to use. Otherwise `E:\codelab-tools\dotnet10\dotnet.exe` if present, else `dotnet` on PATH. |
| `CODELAB_NODE_DIR` | folder holding `node.exe` (and `npm.cmd`), put first on PATH for the run. Otherwise `E:\codelab-tools\node24` if present (ADR-005), else `node` on PATH. |
| `CODELAB_CI_NUMBER` | the `<n>` in `-ci.<n>` for `-Pack`. Otherwise one above the highest found in `feed\`. |
| `CODELAB_BASE_REF` | base ref for the docs gate's module-changed check (same as `-BaseRef`). Default `origin/main`, else `main`. An unresolvable ref FAILS the docs gate; with no commit at all the gate compares the working tree only. |
| `CODELAB_PR_DESCRIPTION` | PR text scanned (with the last commit message) for the `Docs-Not-Needed: <reason>` trailer. |
| `CODELAB_DOCS_NOT_NEEDED` | set BY build.ps1 to the trailer's reason before the docs gate runs; the docs gate reads it. |
| `CODELAB_RELEASE`, `CODELAB_COMMIT` | release stamping, read by Codelab.Build.Sdk. `-Pack` sets `CodelabRelease` to each package's version itself. |
| `CODELAB_TEST_SQL` | SQL Server for the Codelab.Security store tests and Starter.Integration.Tests in step 6 (one value for both): a server name such as `(localdb)\P2Proof`, or a connection string (any value containing `=`). Default `(localdb)\MSSQLLocalDB`. The tests create and drop their own uniquely named databases. |

## Extend

- New CI step: add one entry to `$steps` in `Invoke-CiTarget` with a function that returns `$true`/`$false`, guard
  its entry point with `Test-EntryPoints`, and add the row above. Steps must exit non-zero on failure.
- New package to pack: add its id and next version to `packages/versions.json` and a section to
  `packages/CHANGELOG.md`. NuGet packages live at `packages/dotnet/<Id>/<Id>.csproj`. npm packages live at
  `packages/npm/<name>` and need a `build` script that writes `dist`. Nothing in `build.ps1` changes.
- New project: add it to `codelab-starter.slnx` so step 6 builds and tests it.

## Rules

- All CI logic lives here; a host pipeline (GitHub Actions later) only calls this script.
- A step whose entry point does not exist FAILS with `MISSING: <path>`; nothing is skipped silently.
- Node tests are `*.test.mjs` files; a test folder with none fails the step.
- `-Pack` never edits package source versions: the .nupkg version comes from `-p:PackageVersion`, the npm version
  is stamped on a staged copy in `%TEMP%`. The only file it edits is the `CodelabBuildSdkVersion` pin in
  `Directory.Build.props`, so the feed-mode build uses what was just packed.
- A version in the feed is immutable. `-Release` is refused rather than overwriting it, and so is a ci number that is
  already in the feed.
- A release is a platform-owner decision. An agent runs `-Pack -Release` only when the phase plan assigns the release
  to its task.
- The docs gate never runs without a base ref once the repo has a commit: a check against HEAD cannot see a committed
  module change (DOCS005).

## Gotchas

- Windows PowerShell 5.1 compatible: no `&&`, `?:` or `??`.
- `node --test <folder>` does not work on Node 24 (it runs the folder as a script); the script passes a glob.
- The local NuGet feed folder must exist even when empty (restore fails on a missing local source); `feed\nuget\.gitkeep`
  keeps it in git.
- A version already in `feed\nuget` cannot be pushed again; the ci number auto-increments to avoid that.
- A ProjectReference between two packages (Codelab.Errors to Codelab.Observability) is packed as a dependency on the
  version of the current run. That is only right while both packages share a version. If the versions ever differ,
  the dependency check stops the pack: pack the dependency first and switch the reference to a PackageReference.
- Step 11 builds `dist` for every npm package, because step 12 and the npm pack import `@codelab/ng-security/core`
  from `dist`.
- Step 6 runs the Codelab.Security SQL store tests, which need a reachable SQL Server (`CODELAB_TEST_SQL`), and the
  starter integration tests, which create and drop only their own `StarterIt_<guid>` database on LocalDB.
- Step 18 needs every `packages/versions.json` version in `feed\` (or it packs a ci prerelease in a scratch copy) and
  takes several minutes.
- Step 13 restores the sample's whole closure into an empty folder, so it downloads its third-party packages from
  nuget.org on every run, and `npm ci` the consumer. When it packs a prerelease (pins missing from the feed), it also
  rewrites the `CodelabBuildSdkVersion` pin in `Directory.Build.props`, exactly like `-Pack`.
- The samples are not in `codelab-starter.slnx`, so step 6 never builds them from source; only step 13 builds them.
