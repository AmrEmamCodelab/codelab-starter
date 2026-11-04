# tools/ci

## Purpose

Small, host-neutral CI checks and helpers that `tools/build.ps1` calls:

- `conflict-markers.mjs` (CI step 8) fails when any text file in the repo contains a git merge-conflict marker.
  That covers source, config, scripts and docs. The docs gate's `DOCS004` covers only Markdown and module rules.
- `base-ref.mjs` resolves the ref a branch is compared with (`--base`, else `CODELAB_BASE_REF`, else `origin/main`
  or `main`). `build.ps1` passes it to the docs gate (`--base`) so DOCS005 sees committed module changes, and uses it
  for the protected-path report. It prints `NONE` for a repository without commits and exits 1 when a requested or
  default ref does not exist: CI never silently compares HEAD with itself.
- `package-versions.mjs` plans `build.ps1 -Pack` from `packages/versions.json`:
  - It gives every package `<version>-ci.<n>`, or the exact version with `--release`.
  - It works out each package's source and feed file name.
  - It refuses a plan whose source is missing, and a ci plan whose version is already in the feed.
  - A release skips a package whose exact version is already in the feed and whose `packages/CHANGELOG.md` section
    has no `[Unreleased]` entry (released and unchanged, `skip: true`). It refuses one that is in the feed but has an
    `[Unreleased]` entry (bump it), an `[Unreleased]` version that differs from `versions.json`, and a release that
    would pack nothing.
  - It reports the next free ci number and stamps the version into a staged npm `package.json`.

  Its tests also check the repository contract: every Codelab package project and npm workspace package is in
  `versions.json`, `build.ps1` has CI steps 11 and 12, and CI step 13 packs into a temporary feed (never `feed\`,
  never the `Directory.Build.props` pin).
- `lint-packages.mjs` (CI step 11) runs ESLint over the `@codelab/*` package sources (`packages/npm/*/src`,
  `scripts`): `no-magic-numbers` with the allowed numbers from `rules/eslint-boundaries.compiled.json`
  (`base.no-magic-values`; test files excluded) and `no-restricted-imports` for every module a package's
  `module.rules.yaml` lists in `mustNotDependOn`, by package name or relative path (`base.module-boundaries`).

## Use

```powershell
node tools/ci/conflict-markers.mjs --root .        # exit 0 clean, 1 markers found, 2 usage error
node tools/ci/base-ref.mjs --root . [--base <ref>] # prints the base ref (or NONE); exit 1 when it cannot resolve
node tools/ci/package-versions.mjs plan --root . (--ci <n> | --release) [--feed <dir>]  # JSON plan; exit 1 refused
node tools/ci/package-versions.mjs next-ci --root . [--feed <dir>]                       # next free ci number
node tools/ci/package-versions.mjs stamp --manifest <package.json> --version <semver>    # staged copies only
node --test "tools/ci/tests/**/*.test.mjs"         # unit tests (CI step 10)
```

Output is one `<file>:<line>: merge-conflict marker '<marker>'` line per finding, followed by `PASSED` or `FAILED`.

## Extend

- **New check:** add a `<name>.mjs` that exports its pure logic plus a `main(argv)` that returns an exit code, and
  add a `tests/<name>.test.mjs`. Wire it as a step in `tools/build.ps1` and document it in `tools/README.md`.
- **Skipped folders:** `EXCLUDED_DIRECTORIES` in `conflict-markers.mjs`. Keep it equal to `ExcludedDirectoryNames`
  in `tools/docs-gate/src/Repository/RepositoryLayout.cs`.

## Rules

- Use only Node built-ins (`node:fs`, `node:child_process`, `node:test`). These checks must run before `npm install`.
- Exit codes are fixed: 0 pass, 1 findings, 2 usage or tool error.
- The marker rules here and in `MergeMarkerScanner.cs` must stay identical. Change both and both test suites together.

## Gotchas

- In a git work tree only tracked files and untracked files that are not ignored are scanned, so `.gitignore`
  applies. Outside git, every file is scanned except the skipped folders.
- Binary files (a NUL byte in the first 8000 bytes) and files over 10 MiB are skipped.
- A separator line of seven `=` is reported only inside an open conflict, because it is also a Markdown heading
  underline.
- Never write a literal marker at the start of a line in any file, including docs and tests. Build markers with
  `'<'.repeat(7)`.
