// The starter app's ESLint flat config: the @codelab/ng-tools 0.1 boundary preset plus the HTTP boundary.
// eslint.config.mjs uses it for `npm run lint`; tools/lint-fixtures.spec.ts uses it on lint-fixtures/.
//
// What it enforces:
// - element boundaries (preset): features -> core, layouts, patterns, shared; shared -> core only; core -> nothing;
//   external packages such as @codelab/ng-dga are not policed by boundaries;
// - no third-party UI library (preset no-restricted-imports, base.fe-ui-library);
// - codelab/wire-id-cast (preset);
// - HttpClient and the HTTP backends only inside core/api; fetch and XMLHttpRequest only inside core/api and core/http
//   (the runtime-config loader runs before HttpClient exists). Same folders as base.fe-http-via-api-service;
// - no inline styles (inline-styles.mjs): component styles, style bindings in an inline template or host, NgStyle;
// - no magic numbers outside tests (base.no-magic-values; allowed numbers from rules/eslint-boundaries.compiled.json)
//   and every enum member an explicit integer (base.enums-as-int, base.enum-explicit-values); see magic-values.mjs;
// - no inline config: `/* eslint-disable */` and friends are ignored (LINTER_OPTIONS), so no comment silences a gate.

import { RESTRICTED_UI_LIBRARY_PATTERNS, UI_LIBRARY_MESSAGE, createCodelabEslintConfig } from '@codelab/ng-tools/eslint';
import { magicValueConfigs } from './magic-values.mjs';
import { inlineStyleConfigs } from './inline-styles.mjs';

/** The Angular app folder, relative to the ESLint root. */
export const APP_ROOT = 'src/app';

/** The only folder (under the app root) where HttpClient may be imported: ApiService lives there. */
export const HTTP_FOLDER = 'core/api';

/** Folders (under the app root) where the browser HTTP globals may be used (the base rule's excludes). */
export const HTTP_GLOBAL_FOLDERS = Object.freeze([HTTP_FOLDER, 'core/http']);

/** WireId aliases imported from other files (codelab/wire-id-cast); none in the starter yet. */
export const WIRE_ID_ALIASES = Object.freeze([]);

const ERROR = 'error';

/**
 * Inline `eslint-disable` / `eslint` / `global` comments are ignored, so no file can switch off a gate it fails
 * (the frontend twin of the C# inline-suppression ban). A stale directive is an error too.
 */
export const LINTER_OPTIONS = Object.freeze({ noInlineConfig: true, reportUnusedDisableDirectives: ERROR });

const TYPESCRIPT_FILES = '**/*.ts';
const SPEC_FILES = '**/*.spec.ts';
const ANGULAR_HTTP_MODULE = '@angular/common/http';

/** The HTTP primitives that bypass ApiService. Providers such as provideHttpClient stay allowed. */
export const RESTRICTED_HTTP_IMPORTS = Object.freeze(['HttpClient', 'HttpBackend', 'HttpHandler', 'HttpXhrBackend', 'FetchBackend']);

/** Browser HTTP globals that bypass ApiService. */
export const RESTRICTED_HTTP_GLOBALS = Object.freeze(['fetch', 'XMLHttpRequest']);

/** Shown next to a refused HTTP use; starts with the base rule id like the hook and CI messages. */
export const HTTP_MESSAGE =
  '[base.fe-http-via-api-service] Feature code must not call HTTP directly. Inject ApiService (or the generated API client) from core/api; it owns the base URL, auth and signing headers.';

/** Paths no lint run looks at: generated outputs, the lint fixtures and build output (relative to the ESLint root). */
export const IGNORED_PATHS = Object.freeze([
  `${APP_ROOT}/core/api/generated/**`,
  `${APP_ROOT}/core/permissions/permission-keys.generated.ts`,
  'lint-fixtures/**',
  'dist/**',
  '.angular/**',
  'coverage/**',
  'node_modules/**',
]);

/**
 * HTTP primitives outside their folders, as two config objects. ESLint replaces (does not merge) a rule's options in
 * a later config object, so the import object repeats the preset's UI-library patterns next to the HTTP paths. Spec
 * files keep the preset's UI ban only (tests may build HTTP fakes).
 */
export function httpBoundaryConfigs(appRoot = APP_ROOT) {
  return [
    {
      name: 'starter/http-client-via-api-service',
      files: [TYPESCRIPT_FILES],
      ignores: [`${appRoot}/${HTTP_FOLDER}/**`, SPEC_FILES],
      rules: {
        'no-restricted-imports': [ERROR, {
          paths: [{ name: ANGULAR_HTTP_MODULE, importNames: [...RESTRICTED_HTTP_IMPORTS], message: HTTP_MESSAGE }],
          patterns: RESTRICTED_UI_LIBRARY_PATTERNS.map((regex) => ({ regex, message: UI_LIBRARY_MESSAGE })),
        }],
      },
    },
    {
      name: 'starter/http-globals-via-api-service',
      files: [TYPESCRIPT_FILES],
      ignores: [...HTTP_GLOBAL_FOLDERS.map((folder) => `${appRoot}/${folder}/**`), SPEC_FILES],
      rules: {
        'no-restricted-globals': [ERROR, ...RESTRICTED_HTTP_GLOBALS.map((name) => ({ name, message: HTTP_MESSAGE }))],
      },
    },
  ];
}

/**
 * The full config. `rootDir` is the folder ESLint runs in (frontend/starter-web, or lint-fixtures in the spec); the
 * boundary preset matches element folders against full paths from it.
 */
export function createStarterWebEslintConfig({ rootDir, appRoot = APP_ROOT }) {
  return [
    { name: 'starter/ignores', ignores: [...IGNORED_PATHS] },
    { name: 'starter/no-inline-config', linterOptions: { ...LINTER_OPTIONS } },
    ...createCodelabEslintConfig({ rootDir, appRoot, wireIdAliases: [...WIRE_ID_ALIASES] }),
    ...httpBoundaryConfigs(appRoot),
    ...inlineStyleConfigs(),
    ...magicValueConfigs({ rootDir }),
  ];
}
