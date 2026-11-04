# @codelab/ng-tools

## Purpose

The Codelab Angular tooling shipped to every project. Version 0.1 carries these tools (RTL lint, permission-gate lint,
icon subsetting and the i18n key check follow in later versions):

| Tool | What it enforces or does | Entry point |
| --- | --- | --- |
| ESLint boundary preset | Element types `core`, `layouts`, `patterns`, `shared`, `features`, `showcase` (one folder each under the app root) with a default-deny policy: features reach core, layouts, patterns and shared; shared reaches core only; layouts and patterns reach core and shared; showcase reaches everything but features; core reaches nothing. Feature-to-feature imports follow the modules' `mayDependOn` / `mustNotDependOn` from `rules/eslint-boundaries.compiled.json` when it is found. | `@codelab/ng-tools/eslint` |
| UI library ban | `no-restricted-imports` refuses third-party UI kits (Angular Material, PrimeNG, NG-ZORRO, ngx-bootstrap, ng-bootstrap, the external DGA packages and others). UI comes from `@codelab/ng-dga` and the page patterns (decision D5). | part of the ESLint preset |
| `codelab/wire-id-cast` | Refuses `as WireId<...>`, `<WireId<...>>value` and casts to a `WireId` alias outside the decoder: wire ids are built only by `tryAsId` / `asId` / `toIdSet` from `@codelab/ng-security`. | part of the ESLint preset; `codelabPlugin` |
| Stylelint preset | Tokens only: colours and spacing are `var(--token)` (or `calc()` of tokens, or a neutral keyword); no hex, named or functional colours. Logical CSS only (`stylelint-use-logical`). Token files (`**/tokens/**`, `_tokens*.scss`) are exempt. | `@codelab/ng-tools/stylelint` |
| `codelab-vectors-sync` | Copies the golden vectors (`packages/vectors`) into a consumer, or checks that the consumer's copy has not drifted. | bin; `@codelab/ng-tools/vectors` |

## Use

```js
// eslint.config.mjs (in the Angular app folder)
import { createCodelabEslintConfig } from '@codelab/ng-tools/eslint';

export default [
  ...createCodelabEslintConfig({
    appRoot: 'src/app',               // folder holding core/, layouts/, patterns/, shared/, features/, showcase/
    wireIdAliases: ['RecordId'],      // WireId aliases imported from other files
  }),
];
```

```json
// .stylelintrc.json
{ "extends": ["@codelab/ng-tools/stylelint"] }
```

```powershell
# Copy the vectors into a consumer, then check them in CI (exit 1 on any drift)
npx codelab-vectors-sync --from ..\..\packages\vectors --to src\testing\vectors
npx codelab-vectors-sync --from ..\..\packages\vectors --to src\testing\vectors --check
```

Options of `createCodelabEslintConfig`: `rootDir` (default the working directory), `appRoot` (default `src/app`),
`compiledRules` (a path, or `false` for the element-type policies only; by default found by walking up from
`rootDir`), `wireIdAliases`, and `resolver` (the `import/resolver` setting; the default resolves relative imports with
TypeScript extensions, so pass a TypeScript resolver when the app imports through tsconfig paths).

`codelab-vectors-sync` exit codes: `0` in sync (or synced), `1` drift found by `--check`, `2` usage error. `--prune`
deletes files that exist only in the target; without it they are kept and reported.

Inside the starter the package is linked by npm workspaces. Scripts: `npm run build -w @codelab/ng-tools` copies
`src/` to `dist/` (Node built-ins only, so it also runs as `prepack` on the staged copy `tools/build.ps1 -Pack`
packs), and `npm test -w @codelab/ng-tools` runs the Vitest suite. Elsewhere, until a hosted registry exists:
`npm install ./feed/npm/codelab-ng-tools-<version>.tgz`.

## Extend

- A new ESLint rule goes in `src/eslint/rules/`, is registered in `src/eslint/plugin.mjs` and switched on in the
  preset, with a `RuleTester` test in `tests/`.
- New element types or allow lists change `ALLOWED_DEPENDENCIES` in `src/eslint/element-types.mjs` together with the
  base rule they implement (`rules/base.rules.yaml`); the boundary test covers every pair automatically.
- Module boundaries come from `rules/eslint-boundaries.compiled.json`, which the rules engine generates. Lint code
  reads that data and never parses YAML. A new `schemaVersion` needs a reader change here first.
- New tools are plain ES modules (`.mjs`) under `src/` with tests under `tests/`, exported from `src/index.mjs` or a
  sub-path in `package.json` `exports`.

## Rules

- Platform-owner review for any change (`packages/` in CODEOWNERS).
- Dependency-light: Node built-ins first; every dependency is pinned exactly (`.npmrc save-exact`), and the lint
  engines (`eslint`, `stylelint`, `typescript`) are peer dependencies.
- No external DGA npm package dependency, ever (decision D5; the scrub sweep fails on one).
- Test fixtures must pass the repository's content rules themselves: CSS that breaks a rule is written inline in the
  test, never as a `.css` or `.scss` fixture file.

## Gotchas

- `npm pack` names the tarball `codelab-ng-tools-<version>.tgz` (scope folded into the name).
- `engine-strict` is on: Node older than the `.nvmrc` pin refuses to install.
- Element patterns are matched on the full path from `rootDir` (`partialMatch: false`). Run ESLint from the folder
  `rootDir` names, or pass `rootDir` explicitly; otherwise no file is inside an element and nothing is checked.
- `wire-id-cast` is syntactic. It sees `WireId` itself, aliases declared in the same file and the names listed in
  `wireIdAliases`; an alias imported from elsewhere and not listed is not recognised.
- The external DGA scopes are refused by a regular expression, and tests assemble those package names at runtime:
  the scrub sweep rejects them when written literally anywhere in the repository.
- ESLint size and magic-number rules for TypeScript (ADR-012) are not in 0.1; they arrive with the phase 2 frontend.
