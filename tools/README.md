# tools/

## Purpose

Repo tooling. This page covers `build.ps1`, the single build entry point for CI and humans. Each tool folder
(`extract/`, `rules-engine/`, `docs-gate/`, `ci/`, ...) has its own README.

## Use

```powershell
# Full CI contract (default when no switch is given)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Target CI

# Pack Codelab.Build.Sdk 0.1.0-ci.<n> -> feed\nuget and @codelab/ng-tools 0.0.1-ci.<n> -> feed\npm
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build.ps1 -Pack

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
| 6 | dotnet | `<dotnet> build codelab-starter.slnx -warnaserror` then `<dotnet> test codelab-starter.slnx` |
| 7 | docs gate | `<dotnet> run --project tools/docs-gate -- check --root . --base <base ref>` (base ref from `tools/ci/base-ref.mjs`) |
| 8 | conflict markers | `node tools/ci/conflict-markers.mjs --root .` |
| 9 | content rules | `node tools/rules-engine/check-content.mjs --root .`: every forbidden/required rule of `rules/rules.compiled.json` over every tracked file (absolute state; the hooks' rules, enforced in CI) |
| 10 | scanner tests | `node --test "tools/extract/tests/**/*.test.mjs" "tools/ci/tests/**/*.test.mjs"` |

After the steps, the platform-owned paths the branch changes against the base ref are listed (owner review before a
local merge, while no host enforces CODEOWNERS).

`-Pack` also pins the new SDK version in `Directory.Build.props` (`CodelabBuildSdkVersion`) and then builds and
tests the solution with `-p:UseCodelabPackageSource=false` and an empty, temporary `NUGET_PACKAGES`: the ADR-002
feed mode a fork uses.

Environment:

| Variable | Effect |
|----------|--------|
| `CODELAB_DOTNET` | dotnet to use. Otherwise `E:\codelab-tools\dotnet10\dotnet.exe` if present, else `dotnet` on PATH. |
| `CODELAB_CI_NUMBER` | the `<n>` in `-ci.<n>` for `-Pack`. Otherwise one above the highest found in `feed\`. |
| `CODELAB_BASE_REF` | base ref for the docs gate's module-changed check (same as `-BaseRef`). Default `origin/main`, else `main`. An unresolvable ref FAILS the docs gate; with no commit at all the gate compares the working tree only. |
| `CODELAB_PR_DESCRIPTION` | PR text scanned (with the last commit message) for the `Docs-Not-Needed: <reason>` trailer. |
| `CODELAB_DOCS_NOT_NEEDED` | set BY build.ps1 to the trailer's reason before the docs gate runs; the docs gate reads it. |
| `CODELAB_RELEASE`, `CODELAB_COMMIT` | release stamping, read by Codelab.Build.Sdk (not by build.ps1). |

## Extend

- New CI step: add one entry to `$steps` in `Invoke-CiTarget` with a function that returns `$true`/`$false`, guard
  its entry point with `Test-EntryPoints`, and add the row above. Steps must exit non-zero on failure.
- New package to pack: add an `Invoke-Pack<Name>` function and call it from `Invoke-PackTarget` with the shared
  ci number, so every package from one run carries the same `ci.<n>`.

## Rules

- All CI logic lives here; a host pipeline (GitHub Actions later) only calls this script.
- A step whose entry point does not exist FAILS with `MISSING: <path>`; nothing is skipped silently.
- Node tests are `*.test.mjs` files; a test folder with none fails the step.
- `-Pack` never edits package source versions: the .nupkg version comes from `-p:PackageVersion`, the npm version
  is stamped on a staged copy in `%TEMP%`. The only file it edits is the `CodelabBuildSdkVersion` pin in
  `Directory.Build.props`, so the feed-mode build uses what was just packed.
- The docs gate never runs without a base ref once the repo has a commit: a check against HEAD cannot see a committed
  module change (DOCS005).

## Gotchas

- Windows PowerShell 5.1 compatible: no `&&`, `?:` or `??`.
- `node --test <folder>` does not work on Node 24 (it runs the folder as a script); the script passes a glob.
- The local NuGet feed folder must exist even when empty (restore fails on a missing local source); `feed\nuget\.gitkeep`
  keeps it in git.
- A version already in `feed\nuget` cannot be pushed again; the ci number auto-increments to avoid that.
