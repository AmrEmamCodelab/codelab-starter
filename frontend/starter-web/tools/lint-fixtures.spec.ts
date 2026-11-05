// @vitest-environment node
//
// Dogfoods the app's ESLint config (tools/lint/starter-eslint-config.mjs) on lint-fixtures/: each `*.ts.fixture`
// case is linted as the `.ts` file it stands for, next to real `.ts` import targets. Cases are `.fixture` files so
// the repository's content rules and the normal lint run never see the deliberate violations.
import { readFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { ESLint, type Linter } from 'eslint';
import { beforeAll, describe, expect, it } from 'vitest';

const TOOLS_DIR = dirname(fileURLToPath(import.meta.url));
const FIXTURES_ROOT = resolve(TOOLS_DIR, '..', 'lint-fixtures');
const APP_DIR = join(FIXTURES_ROOT, 'src', 'app');
const CONFIG_MODULE = pathToFileURL(join(TOOLS_DIR, 'lint', 'starter-eslint-config.mjs')).href;
const CASE_SUFFIX = '.fixture';

const RuleIds = {
  boundaries: 'boundaries/dependencies',
  restrictedImports: 'no-restricted-imports',
  restrictedGlobals: 'no-restricted-globals',
  inlineStyles: 'starter-styles/no-inline-styles',
  magicNumbers: 'starter/no-magic-numbers',
  enumIntValues: 'starter/enum-int-values',
} as const;

const HTTP_RULE_ID = 'base.fe-http-via-api-service';
const UI_LIBRARY_HINT = '@codelab/ng-dga';
const STYLE_RULE_IDS = ['base.fe-design-tokens', 'base.fe-logical-css'] as const;
/** inline-styles: NgStyle import, the inline template's style attribute, the styles property. */
const INLINE_STYLES_FINDINGS = 3;
/** inline-style-bindings: the template literal (one finding), the host [style.color] key and the host style key. */
const INLINE_STYLE_BINDINGS_FINDINGS = 3;

const Cases = {
  httpClientInFeature: 'features/orders/http-client-in-feature.component.ts.fixture',
  fetchInFeature: 'features/orders/fetch-in-feature.component.ts.fixture',
  featureImportsShared: 'features/orders/feature-imports-shared.component.ts.fixture',
  sharedImportsFeature: 'shared/status-label/shared-imports-feature.ts.fixture',
  thirdPartyUi: 'features/orders/third-party-ui.component.ts.fixture',
  inlineStyles: 'features/orders/inline-styles.component.ts.fixture',
  inlineStyleBindings: 'features/orders/inline-style-bindings.component.ts.fixture',
  styledByScss: 'features/orders/styled-by-scss.component.ts.fixture',
} as const;

const RealFiles = [
  'core/api/api.service.ts',
  'core/http/config-loader.ts',
  'shared/status-label/status-label.ts',
  'features/orders/orders-page.ts',
] as const;

/** A feature component that tries to switch every gate off with inline config comments (linted as text). */
const SUPPRESSED_PATH = 'features/orders/suppressed.component.ts';
const SUPPRESSION_COMMENTS = ['/* eslint-disable */', '/* eslint starter/no-magic-numbers: "off" */'] as const;
/** ESLint's severity for an error (2); warnings such as "inline config has no effect" are 1. */
const ERROR_SEVERITY = 2;
const SUPPRESSED_BODY = [
  "import { Component } from '@angular/core';",
  "@Component({ selector: 'app-suppressed', template: '<p>x</p>', styles: ['p { color: red; }'] })",
  'export class SuppressedComponent {',
  '  delay(x: number): number {',
  '    return x * 5000;',
  '  }',
  '}',
  "export enum Shade { Dark = 'dark' }",
  '',
].join('\n');
let eslint: ESLint;

beforeAll(async () => {
  const { createStarterWebEslintConfig } = await import(CONFIG_MODULE);
  eslint = new ESLint({
    cwd: FIXTURES_ROOT,
    overrideConfigFile: true,
    overrideConfig: createStarterWebEslintConfig({ rootDir: FIXTURES_ROOT }),
  });
});

async function lintFile(filePath: string, text?: string): Promise<Linter.LintMessage[]> {
  const code = text ?? (await readFile(filePath, 'utf8'));
  const [result] = await eslint.lintText(code, { filePath });
  const fatal = result.messages.filter((message) => message.fatal === true);
  expect(fatal).toEqual([]);
  return result.messages;
}

/** Lints a `.ts.fixture` case as the `.ts` file it stands for. */
async function lintCase(relativeCase: string): Promise<Linter.LintMessage[]> {
  const casePath = join(APP_DIR, relativeCase);
  return lintFile(casePath.slice(0, -CASE_SUFFIX.length), await readFile(casePath, 'utf8'));
}

const ruleIds = (messages: Linter.LintMessage[]) => messages.map((message) => message.ruleId);

describe('the starter ESLint config on lint-fixtures', () => {
  it('refuses HttpClient in a feature component with the base rule id', async () => {
    const messages = await lintCase(Cases.httpClientInFeature);
    const http = messages.filter((message) => message.ruleId === RuleIds.restrictedImports);
    expect(http).toHaveLength(1);
    expect(http[0].message).toContain(HTTP_RULE_ID);
  });

  it('refuses fetch in a feature component', async () => {
    const messages = await lintCase(Cases.fetchInFeature);
    expect(ruleIds(messages)).toEqual([RuleIds.restrictedGlobals]);
    expect(messages[0].message).toContain(HTTP_RULE_ID);
  });

  it('allows a feature importing shared, core and @codelab/ng-dga', async () => {
    expect(await lintCase(Cases.featureImportsShared)).toEqual([]);
  });

  it('refuses shared importing a feature', async () => {
    expect(ruleIds(await lintCase(Cases.sharedImportsFeature))).toContain(RuleIds.boundaries);
  });

  it('refuses a third-party UI library import', async () => {
    const messages = await lintCase(Cases.thirdPartyUi);
    const ui = messages.filter((message) => message.ruleId === RuleIds.restrictedImports);
    expect(ui).toHaveLength(1);
    expect(ui[0].message).toContain(UI_LIBRARY_HINT);
  });

  it.each([
    [Cases.inlineStyles, INLINE_STYLES_FINDINGS],
    [Cases.inlineStyleBindings, INLINE_STYLE_BINDINGS_FINDINGS],
  ])('refuses inline styles in %s with the design-token and logical-CSS rule ids', async (relativeCase, count) => {
    const messages = await lintCase(relativeCase);
    expect(ruleIds(messages)).toEqual(Array(count).fill(RuleIds.inlineStyles));
    for (const message of messages) {
      for (const ruleId of STYLE_RULE_IDS) expect(message.message).toContain(ruleId);
    }
  });

  it('allows styleUrl, classes and an attribute that merely ends in style', async () => {
    expect(await lintCase(Cases.styledByScss)).toEqual([]);
  });

  it.each(RealFiles)('passes the clean fixture %s (HTTP is allowed in core/api and core/http)', async (relativePath) => {
    expect(await lintFile(join(APP_DIR, relativePath))).toEqual([]);
  });

  it.each(SUPPRESSION_COMMENTS)('ignores the inline config comment %s, so every gate still fires', async (comment) => {
    const source = `\n${SUPPRESSED_BODY}`;
    const errors = (await lintFile(join(APP_DIR, SUPPRESSED_PATH), source)).filter((message) => message.severity === ERROR_SEVERITY);
    expect(new Set(ruleIds(errors))).toEqual(new Set([RuleIds.inlineStyles, RuleIds.magicNumbers, RuleIds.enumIntValues]));
  });

  it('ignores generated outputs', async () => {
    const generated = join(APP_DIR, 'core', 'permissions', 'permission-keys.generated.ts');
    expect(await eslint.isPathIgnored(generated)).toBe(true);
  });
});
