# starter-web tools

## Purpose

The frontend guardrails and generators of the starter web app (contract sections 8 and 9):

| Tool | What it does | Run |
| --- | --- | --- |
| `lint/starter-eslint-config.mjs` | The app's ESLint flat config (used by `eslint.config.mjs`): the `@codelab/ng-tools` 0.1 boundary preset (features reach core, layouts, patterns, shared; shared reaches core only; no third-party UI library; `codelab/wire-id-cast`) plus the HTTP boundary: `HttpClient` and the HTTP backends only in `core/api`, `fetch`/`XMLHttpRequest` only in `core/api` and `core/http` (base rule `base.fe-http-via-api-service`). | `npm run lint` |
| `../stylelint.config.mjs` | The `@codelab/ng-tools` Stylelint preset (design tokens only, logical CSS only) with the SCSS syntax for `.scss`. | `npm run lint` |
| `i18n-parity.mjs` | Every file in `src/assets/i18n` has the same nested keys; `ar` and `en` exist; every leaf is non-empty text. Exit 0 ok, 1 problem, 2 usage. | `npm run lint` |
| `lint/inline-styles.mjs` | No inline styles in .ts (ESLint rule `starter-styles/no-inline-styles`, part of the config above): the `@Component` `styles` property, `style=`/`[style.*]`/`[attr.style]`/`[ngStyle]` in an inline `template` or `host`, and the `NgStyle` import. Stylelint sees only .scss/.css, so inline styles would skip `base.fe-design-tokens` and `base.fe-logical-css`. | `npm run lint` |
| `lint/magic-values.mjs` | No magic numbers and integer enums in .ts (part of the config above). `starter/no-magic-numbers` is ESLint's `no-magic-numbers` with the allowed numbers from `rules/eslint-boundaries.compiled.json` (`base.no-magic-values`, same options as `tools/ci/lint-packages.mjs`), minus enum member values and numeric literal types; `*.spec.ts`, `*.testing.ts`, `*.test.ts` and `tests/` are exempt. `starter/enum-int-values` refuses an enum member without a value or with a string value (`base.enum-explicit-values`, `base.enums-as-int`). Cases in `../lint-fixtures/src/app/features/billing`, tests in `lint/magic-values.spec.ts`. | `npm run lint` |
| `no-inline-styles.mjs` | The same template check on every `src/**/*.html` (no ESLint template parser in the starter). Exit 0 ok, 1 problem, 2 usage. | `npm run lint` |
| `api-gen/generate.mjs` | `docs/generated/openapi.v1.json` → `src/app/core/api/generated/openapi.ts` (openapi-typescript, PublicId fields as `WireId<'domain'>` from `@codelab/ng-security`) and `docs/generated/permissions.json` → `src/app/core/permissions/permission-keys.generated.ts` (`PermissionKeys`, `PermissionKey`). `--check` exits 1 on drift. | `npm run api:gen`, `npm run api:gen:check` |
| `permission-gates.mjs` | Template permission gate (base.fe-buttons-gated): every `<dga-button>` and every `<button>` with `(click)` in `src/app/features/**/*.html` and inline templates carries `*hasPermission` (itself, an ancestor, or `[hasPermission]` on `<ng-template>`) or `data-public-action`; string-literal keys are refused. Runs @codelab/ng-tools `permission-gates`. Exit 0 ok, 1 finding, 2 usage. | `npm run lint` |
| `lint-fixtures.spec.ts` | Lints `../lint-fixtures` with the real ESLint config and asserts each guardrail fires (or stays quiet), and the route-table and template permission gates on the fixtures. | `npm test` |

## Use

```powershell
$env:PATH = "E:\codelab-tools\node24;$env:PATH"   # private Node on the build machine
cd frontend/starter-web
npm run lint                 # eslint . && stylelint && i18n parity && no inline styles
npm run api:gen              # after Starter.DocsGen wrote docs/generated
npm run api:gen:check        # CI: exit 1 when the generated files are stale
npx vitest run tools         # the tool tests and the lint-fixture spec
```

`generate.mjs` also takes `--openapi <file>` and `--permissions <file>` (relative to `frontend/starter-web`) to
generate from other inputs, for example the bootstrap fixtures in `api-gen/tests/fixtures`.

Permission key names are the PascalCase of every dotted and dashed word: `dashboard.view` → `DashboardView`,
`access.admin.view` → `AccessAdminView`. Two keys that map to one name fail the generator.

The WireId domain of a PublicId field is derived from where it sits, because the OpenAPI schema does not carry it:

1. an `x-codelab-id-domain` extension on the schema wins;
2. a parameter or field named `<x>Id` / `<x>Ids` → `<x>` in kebab case (`requestTypeId` → `request-type`);
3. a field named `id` (or a schema that is itself a PublicId) → the schema name without its role suffix
   (`Response`, `Request`, `Summary`, `Details`, `Dto`, `Model`, `Item`, `View`): `SessionUser.id` → `session-user`;
4. anything else → `WireId<string>`.

## Extend

- A new HTTP boundary or restricted import goes in `lint/starter-eslint-config.mjs` with a case in
  `../lint-fixtures` and an assertion in `lint-fixtures.spec.ts`. Element boundaries and the UI-library list belong
  to `@codelab/ng-tools` (platform owner), not here.
- A new generated output gets a renderer in `api-gen/lib/`, an entry in `buildOutputs` (`generate.mjs`), a path in
  `api-gen/lib/constants.mjs` and a test. Every output starts with the `@generated` header.
- Violating lint cases are `*.ts.fixture` files; clean import targets are `*.ts`.

## Rules

- Generated files are never hand-edited: change the backend, run Starter.DocsGen, then `npm run api:gen`. The
  `@generated` header makes the Claude hooks refuse hand edits.
- No magic values: paths, flags, patterns and exit codes live in the constants of each tool.
- Every tool has Vitest tests (`*.test.mjs` or `*.spec.ts` under `tools/`), each with `@vitest-environment node`.
- No external DGA package anywhere (scrub sweep); UI comes from `@codelab/ng-dga`.
- No inline ESLint config: the config sets `linterOptions.noInlineConfig`, so `/* eslint-disable */`,
  `// eslint-disable-next-line` and `/* eslint rule: "off" */` are ignored (ESLint warns that they have no effect) and
  every gate still fires. A real exception goes through the rules exemptions, with platform-owner review.

## Gotchas

- ESLint replaces (does not merge) a rule's options when a later config object sets the same rule, so a new check
  never adds a second `no-restricted-syntax` block: `lint/inline-styles.mjs` owns that rule, and the enum check is
  its own plugin rule (`starter/enum-int-values`).
- The core `no-magic-numbers` rule reports a number in an array literal even inside a `const` declaration: name
  each number (`const FIRST = 3;`) and build the array from the names.

- openapi-typescript 7 calls the `transform` hook twice for an object property (first with the property path,
  then with the parent's path); the transform keeps the first domain per schema object.
- `PUBLIC_ID_PATTERN` must equal Codelab.Security `PublicIdFormat.OpenApiPattern`: it is the only PublicId marker
  in the document (the transformer clears `format`).
- openapi-typescript 7.13 declares `typescript ^5` as a peer; the app pins TypeScript 6, so installs need the peer
  conflict accepted (see the phase 2 report).
- `.scss` needs `postcss-scss` installed (the Stylelint `customSyntax`).
- ESLint replaces rule options in later config objects, so the HTTP import restriction repeats the preset's UI-library
  patterns. Keep them in sync by importing `RESTRICTED_UI_LIBRARY_PATTERNS`, never by copying.
- The boundary preset warns that `mode` is deprecated in eslint-plugin-boundaries 7; harmless until
  `@codelab/ng-tools` drops it.
