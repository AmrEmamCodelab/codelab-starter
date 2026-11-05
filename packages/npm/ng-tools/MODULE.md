# @codelab/ng-tools

## Purpose

The Codelab Angular tooling shipped to every project. Version 0.5 carries these tools (0.5 added the RTL lint, icon
subsetting, the i18n check and one `codelab-ng-tools` command for every file check):

| Tool | What it enforces or does | Entry point |
| --- | --- | --- |
| ESLint boundary preset | Element types `core`, `layouts`, `patterns`, `shared`, `features`, `showcase` (one folder each under the app root) with a default-deny policy: features reach core, layouts, patterns and shared; shared reaches core only; layouts and patterns reach core and shared; showcase reaches everything but features; core reaches nothing. Feature-to-feature imports follow the modules' `mayDependOn` / `mustNotDependOn` from `rules/eslint-boundaries.compiled.json` when it is found. | `@codelab/ng-tools/eslint` |
| UI library ban | `no-restricted-imports` refuses third-party UI kits (Angular Material, PrimeNG, NG-ZORRO, ngx-bootstrap, ng-bootstrap, the external DGA packages and others). UI comes from `@codelab/ng-dga` and the page patterns (decision D5). | part of the ESLint preset |
| `codelab/wire-id-cast` | Refuses `as WireId<...>`, `<WireId<...>>value` and casts to a `WireId` alias outside the decoder: wire ids are built only by `tryAsId` / `asId` / `toIdSet` from `@codelab/ng-security`. | part of the ESLint preset; `codelabPlugin` |
| Stylelint preset | Tokens only: colours and spacing are `var(--token)` (or `calc()` of tokens, or a neutral keyword); no hex, named or functional colours. Logical CSS only (`stylelint-use-logical`). Token files (`**/tokens/**`, `_tokens*.scss`) are exempt. | `@codelab/ng-tools/stylelint` |
| Route-table permission gate | `assertRoutesGated(routes, options)` / `findUngatedRoutes`: every route that renders a page (`component` or `loadComponent`) carries a permission key in its route data on itself or an ancestor, or its full path is public; redirects are exempt; lazily loaded child arrays are passed in, and a `loadChildren` route without one fails; a page's effective (innermost) key must be enforced by a permission guard in `canActivate` or `canMatch` on the route that declares the key, or in `canActivateChild` on an ancestor of that route, recognized by the `markPermissionGuard` tag or `options.isPermissionGuard` (`missing-guard`); optional `knownPermissions` refuses retired or mistyped keys (base rule `base.fe-routes-gated`). For a Vitest spec in the app. | `@codelab/ng-tools/routes` |
| Template permission gate | Every action element in the `.html` templates and the inline templates of the `.ts` files under a folder (a `<dga-button>`; a `<button>` with any event binding or that submits: `type="submit"`, or no type inside a form binding `(submit)`/`(ngSubmit)`; a `role="button"` element with an event binding; any element binding `(click)` or `(confirm)`, also as `on-click`/`on-confirm`) carries `*hasPermission` (on itself or an ancestor, or `[hasPermission]` on an `<ng-template>` wrapper) or `data-public-action`; a string-literal key on the directive is refused (`base.fe-buttons-gated`). A small tokenizer, no Angular compiler. | `codelab-ng-tools permission-gates`; `@codelab/ng-tools/permission-gates` |
| RTL lint | Templates (`.html` and inline `template:`) and TypeScript never name a physical side (base rule `base.fe-tokens-logical-css`; Stylelint covers `.css`/`.scss`): no `[style.margin-left]`-style bindings (also host `'[style.left]'` and `el.style.marginLeft`), no physical side in style text (`style="padding-right:.."`, `[ngStyle]`, `setStyle(el, 'left', ..)`, a CSS string), no `text-align`/`float`/`clear` to `left`/`right`, no hard-coded `dir="ltr"`/`dir="rtl"` (or `setAttribute('dir', 'rtl')`) outside the direction files (default `index.html`) or an element marked `data-fixed-direction`, no physically named icon (`arrow-left`), no `flipInRtl="false"` on an icon that mirrors, and with `--icons` every directional icon in the map (`*-start`, `*-end`, `logout`, `login`) has `mirrorInRtl`. | `codelab-ng-tools rtl-lint`; root export `checkRtl` |
| Icon subsetting | Scans folders for the icons in use (every quoted token equal to a known icon name, so tables of names count; `*.generated.ts` skipped), fails on a literal icon name the map does not know, and writes the subset code point map and, through the generator contract, a subset WOFF2 with the same code points, so only the used icons ship and the font that blocks the first paint stays small. | `codelab-ng-tools icons-subset`; root export `collectIconUsage`, `subsetOutputs` |
| i18n check | The translation folder agrees (same keys in every language, `ar` and `en` present, every leaf non-empty text), every Arabic text has an Arabic letter (placeholders, web and e-mail addresses ignored; `--allow-latin` keys exempt), and every key the code uses literally exists in every language: `'a.b' \| t` in templates, `<x>Key="a.b"` inputs, `translate('a.b')` / `instant('a.b')`, and every key-shaped literal of a `*translation-keys.ts` catalogue (base rule `base.fe-no-raw-text`). | `codelab-ng-tools i18n-check`; root export `checkI18n` |
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

```ts
// src/app/route-gates.spec.ts (Vitest)
import { assertRoutesGated } from '@codelab/ng-tools/routes';

it('gates every page route', () => {
  assertRoutesGated(routes, {
    permissionDataKey: RouteDataKeys.Permission,   // the data key permissionGuard() reads
    publicPaths: PUBLIC_ROUTE_PATHS,               // full paths, no leading slash: 'login', '**', ''
    lazyChildren: { admin: ADMIN_ROUTES },         // each loadChildren array, by the full path of its mount
    knownPermissions: Object.values(PermissionKeys),
  });
});
```

```ts
// The app's permission guard tags what it returns, so the route gate can see it (no ng-tools import needed at runtime)
const PERMISSION_GUARD_MARKER = Symbol.for('codelab.ng-tools.permission-guard'); // = PERMISSION_GUARD_MARKER
export function permissionGuard(key?: string): CanActivateFn {
  const guard: CanActivateFn = async (route, state) => { /* ... */ };
  return Object.defineProperty(guard, PERMISSION_GUARD_MARKER, { value: true });
}
// or pass `isPermissionGuard: (guard) => ...` in the route-gate options
```

```powershell
# Every subcommand exits 0 clean, 1 a finding, 2 usage; `--fix` (where offered) replaces the fix text.
# Every action button under the features folder gated
npx codelab-ng-tools permission-gates --dir src/app/features [--fix "See docs/recipes/add-permission.md."]
# No physical direction in templates and TypeScript; the icon map adds the icon-mirroring rules
npx codelab-ng-tools rtl-lint src --icons node_modules/@codelab/ng-dga/assets/icons/codelab-icons.json [--direction-file index.html]...
# The icons in use (fails on unknown names); --out writes <stem>.json and, with the generator, <stem>.woff2; --check compares
npx codelab-ng-tools icons-subset --dir src/app --dir <ng-dga component sources> --map <codelab-icons.json> [--keep close,menu]
  [--out <folder> [--stem codelab-icons] [--generator <ng-dga>/scripts/lib/icon-font.mjs --svg-dir <ng-dga>/icons/svg] [--check]]
# Translation parity, Arabic in ar, and every key the code uses
npx codelab-ng-tools i18n-check --dir src/assets/i18n --src src/app [--allow-latin layout.language-switcher.en] [--pipe t]
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

- Another permission-gate element or marker: `GateMarkers` and `isActionButton` in `src/permission-gates/gate-rule.mjs`,
  with pass and fail cases in `tests/permission-gates.test.mjs`. Another route-table rule: `src/routes/route-gates.mjs`
  (a new `RouteFindingKinds` value and its message), with its `.d.mts` types and a case in `tests/route-gates.test.mjs`.
- Another `codelab-ng-tools` subcommand: a folder under `src/` with its rules, a `cli.mjs` exporting `run<Name>(argv, io)`
  (arguments through `src/shared/arguments.mjs`, exit codes `ExitCodes`), an `index.mjs`, a value in `Subcommands` and a
  branch in `main` (`src/cli/main.mjs`), root exports in `src/index.mjs`, and a test file under `tests/`.
- Another RTL rule: a `RtlFindingKinds` value, its detection in `src/rtl/rtl-rules.mjs` (template attributes in
  `findRtlTemplateFindings`, code in `findRtlScriptFindings`) and its text in `FINDING_TEXT` (`src/rtl/check.mjs`),
  with pass and fail cases in `tests/rtl-lint.test.mjs`. Another place an icon name is given: `src/icons/icon-inputs.mjs`
  (both the subset and the RTL lint read it).
- Another way code uses a translation key: `src/i18n/key-usage.mjs`, with cases in `tests/i18n-check.test.mjs`.
- Shared scanning helpers live in `src/shared/` (file listing and globs, the TypeScript lexer, attribute literals);
  the template tokenizer and inline-template finder stay in `src/permission-gates/` and are reused from there.

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

- The 0.5 file checks (RTL lint, icon subset, i18n check) are lexical like the template gate: they read comments,
  strings and templates correctly but resolve nothing. A key or icon name built at run time (`'chevron-' + side`,
  `` `errors.${code}` ``, a ternary before `| t`) is not seen: write icon names whole and list built keys in a
  `*translation-keys.ts` catalogue. Positions inside an inline template are source lines but template-relative columns.
- Icon subsetting must scan every library that renders icons itself (the ng-dga component sources, `src/lib`), not
  only the app, or the library's chevrons and close buttons disappear from the subset. Scanning a bundled library
  build keeps every icon, because the bundle carries the full glyph table. The subset keeps the full font's code
  points, so it can replace the full font file one for one; swapping it into the app build is the app's step.
- The generator contract is the only coupling to `@codelab/ng-dga`'s build: `buildIconFont(manifest, readSvg)` from
  its `scripts/lib/icon-font.mjs`, loaded by path at run time (never a dependency). A consumer without the ng-dga
  source tree writes the subset map only.
- The RTL lint flags `left:`/`right:` only inside string literals and style attributes, so a TypeScript object such
  as `{ left: node, right: node }` passes; camelCase side names (`marginLeft`, `paddingRight`) are flagged anywhere
  in code, kebab-case ones (`margin-left`) anywhere in code or strings.
- `data-fixed-direction` exempts one element from the direction and icon rules only; physical styles are refused on
  it as well. A whole file is exempt from the direction rule only through `--direction-file`.

- `assertRoutesGated` never calls `loadComponent` or `loadChildren`. A lazy child array is checked only when it is
  passed in `lazyChildren` under the full path of its mount (slashes at the ends are ignored); otherwise the mount is
  reported as `unresolved-lazy-children`.
- A public path exempts only the outermost route with that full path: with `''` public (the shell), a pathless
  child of the shell still needs its own key. Public status is never inherited by children.
- The template check is lexical: it reads comments, `{{ }}`, block parameters (`@if (...)`, `@let ...;`), quoted
  attribute values and script/style bodies correctly, but it does not resolve components. A button inside a child
  component is checked in that component's template; a gate on the child's host element in the parent does not
  count.
- Route data alone enforces nothing, and a guard is tracked per key: `permissionGuard()` checks the data of the route
  it runs for, so a page's effective (innermost) key counts as enforced only when the route that declares it runs a
  permission guard in `canActivate` or `canMatch`, or a strict ancestor of that route runs one in `canActivateChild`.
  Otherwise the page is reported as `missing-guard`. Two shapes this catches: a child that declares its own key under
  a parent whose `canActivate` guard checks the parent's key (`/admin` guarded by `access.admin.view`, `/admin/secret`
  declaring `users.manage` with no guard of its own), and a page whose only guard is its own `canActivateChild`
  (Angular never runs it for the route itself). A key-less child inherits its ancestor's key and that key's guard.
  Any function counts as a permission guard only when it carries the
  `Symbol.for('codelab.ng-tools.permission-guard')` property (`markPermissionGuard`) or `options.isPermissionGuard`
  accepts it; a guard given as a string or class token is never recognized by the default.
- A `(confirm)` output (the editor-dialog pattern's save) is an action: gate the dialog host (or an ancestor) with
  `*hasPermission`, or mark it `data-public-action`. Key events count only on a `<button>` or a `role="button"`
  element, so `<input (keyup.enter)>` is not an action. A `<button>` with a bound `[type]` is not taken for a submit
  button.
- `template: NAME` is scanned when `const NAME = '...'` (or `let`/`var`, a string or template literal) is declared in
  the same file; a template imported from another file is not scanned.
- `[hasPermission]` counts only on `<ng-template>` (the structural directive needs a template); on any other element
  it is not a gate. Inline templates are found as the string literal of a `template:` property; specs (`*.spec.ts`),
  `*.test.ts`, `*.testing.ts` and `*.d.ts` are skipped.

- `npm pack` names the tarball `codelab-ng-tools-<version>.tgz` (scope folded into the name).
- `engine-strict` is on: Node older than the `.nvmrc` pin refuses to install.
- Element patterns are matched on the full path from `rootDir` (`partialMatch: false`). Run ESLint from the folder
  `rootDir` names, or pass `rootDir` explicitly; otherwise no file is inside an element and nothing is checked.
- `wire-id-cast` is syntactic. It sees `WireId` itself, aliases declared in the same file and the names listed in
  `wireIdAliases`; an alias imported from elsewhere and not listed is not recognised.
- The external DGA scopes are refused by a regular expression, and tests assemble those package names at runtime:
  the scrub sweep rejects them when written literally anywhere in the repository.
- ESLint size and magic-number rules for TypeScript (ADR-012) are not in 0.1; they arrive with the phase 2 frontend.
