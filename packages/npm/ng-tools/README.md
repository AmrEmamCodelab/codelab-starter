# @codelab/ng-tools

## Purpose

Home of the Codelab Angular tooling shipped to every project: the ESLint boundary config (data from
`rules/rules.compiled.json`), RTL lint, wire-id lint, permission-gate lint, icon subsetting, golden-vector sync and
the i18n key check.

**Phase 0: placeholder.** Version `0.0.1` contains only this README. It exists so the npm half of the package feed
(pack, store in `feed/npm`, install by a clean machine) is proven before real tooling lands in Phase 2.

## Use

- Inside the starter: linked by npm workspaces (`packages/npm/*`), no install step.
- Elsewhere, until a hosted registry exists: `npm install ./feed/npm/codelab-ng-tools-0.0.1-ci.<n>.tgz`.
- Packed by `tools/build.ps1 -Pack` as `0.0.1-ci.<n>` (the source `package.json` version is never edited by CI;
  the pack runs on a staged copy).

## Extend

- Add tools as ES modules with tests (`node --test`), list them in `files`, and bump `version`.
- Lint rules read compiled rules data; they never parse YAML.

## Rules

- Platform-owner review for any change (`packages/` in CODEOWNERS).
- Dependency-light: prefer Node built-ins; every dependency is pinned exactly (`.npmrc save-exact`).
- No external DGA npm package dependency, ever (decision D5; the scrub sweep fails on it).

## Gotchas

- `npm pack` names the tarball `codelab-ng-tools-<version>.tgz` (scope folded into the name).
- `engine-strict` is on: Node older than the `.nvmrc` pin refuses to install.
