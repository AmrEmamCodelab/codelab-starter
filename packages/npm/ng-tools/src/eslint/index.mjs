// The Codelab ESLint flat-config preset for an Angular app.
//
//   // eslint.config.mjs (in frontend/starter-web)
//   import { createCodelabEslintConfig } from '@codelab/ng-tools/eslint';
//   export default createCodelabEslintConfig({ appRoot: 'src/app' });
//
// It returns flat-config objects for: TypeScript parsing, the element-type boundaries (eslint-plugin-boundaries) plus
// the feature-to-feature policies from rules/eslint-boundaries.compiled.json when it is found, the third-party UI
// library ban (no-restricted-imports) and the codelab/wire-id-cast rule.

import tsParser from '@typescript-eslint/parser';
import boundaries from 'eslint-plugin-boundaries';
import { basePolicies, elementDescriptors } from './element-types.mjs';
import { featurePolicies, findCompiledRules, loadCompiledRules } from './compiled-rules.mjs';
import { PLUGIN_NAMESPACE, codelabPlugin } from './plugin.mjs';
import { restrictedUiImports } from './ui-libraries.mjs';

const TYPESCRIPT_FILES = '**/*.ts';
const RESOLVED_EXTENSIONS = Object.freeze(['.ts', '.mts', '.mjs', '.js', '.json']);
const ERROR = 'error';
const DEFAULT_APP_ROOT = 'src/app';

/**
 * @typedef {object} CodelabEslintOptions
 * @property {string} [rootDir] the ESLint root path element patterns are relative to (default: the working directory)
 * @property {string} [appRoot] the Angular app folder holding core/layouts/patterns/shared/features/showcase, relative
 *   to rootDir (default `src/app`)
 * @property {string | false} [compiledRules] path of rules/eslint-boundaries.compiled.json; default: found by walking
 *   up from rootDir; `false` to use the default policies only
 * @property {string[]} [wireIdAliases] WireId alias names imported from other files (e.g. `RecordId`)
 * @property {object} [resolver] the `import/resolver` setting (default: node resolution with TypeScript extensions;
 *   use a TypeScript resolver when the app imports through tsconfig paths)
 */

/** The full preset. @param {CodelabEslintOptions} [options] */
export function createCodelabEslintConfig(options = {}) {
  const rootDir = options.rootDir ?? process.cwd();
  const appRoot = options.appRoot ?? DEFAULT_APP_ROOT;
  return [
    {
      name: 'codelab/typescript',
      files: [TYPESCRIPT_FILES],
      languageOptions: { parser: tsParser, sourceType: 'module' },
    },
    boundaryConfig({ rootDir, appRoot, compiledRules: options.compiledRules, resolver: options.resolver }),
    {
      name: 'codelab/ui-libraries',
      files: [TYPESCRIPT_FILES],
      rules: { 'no-restricted-imports': [ERROR, restrictedUiImports()] },
    },
    {
      name: 'codelab/wire-id',
      files: [TYPESCRIPT_FILES],
      plugins: { [PLUGIN_NAMESPACE]: codelabPlugin },
      rules: { [`${PLUGIN_NAMESPACE}/wire-id-cast`]: [ERROR, { aliases: options.wireIdAliases ?? [] }] },
    },
  ];
}

/** Every policy: the element-type defaults, then the compiled feature-to-feature policies (the last match wins). */
export function boundaryPolicies({ rootDir, compiledRules }) {
  if (compiledRules === false) return basePolicies();
  const path = compiledRules ?? findCompiledRules(rootDir);
  return path === null ? basePolicies() : [...basePolicies(), ...featurePolicies(loadCompiledRules(path))];
}

/** The boundaries config object alone, for apps that compose their own preset. */
export function boundaryConfig({ rootDir, appRoot, compiledRules, resolver }) {
  return {
    name: 'codelab/boundaries',
    files: [TYPESCRIPT_FILES],
    plugins: { boundaries },
    settings: {
      'boundaries/root-path': rootDir,
      'boundaries/elements': elementDescriptors(appRoot),
      'import/resolver': resolver ?? { node: { extensions: [...RESOLVED_EXTENSIONS] } },
    },
    rules: {
      'boundaries/dependencies': [ERROR, { default: 'disallow', policies: boundaryPolicies({ rootDir, compiledRules }) }],
    },
  };
}

export { ElementTypes, ALLOWED_DEPENDENCIES } from './element-types.mjs';
export { RESTRICTED_UI_LIBRARY_PATTERNS, UI_LIBRARY_MESSAGE } from './ui-libraries.mjs';
export { codelabPlugin } from './plugin.mjs';
