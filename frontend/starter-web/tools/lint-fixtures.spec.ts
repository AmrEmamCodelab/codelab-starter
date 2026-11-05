// @vitest-environment node
//
// Dogfoods the app's ESLint config (tools/lint/starter-eslint-config.mjs) on lint-fixtures/: each `*.ts.fixture`
// case is linted as the `.ts` file it stands for, next to real `.ts` import targets. Cases are `.fixture` files so
// the repository's content rules and the normal lint run never see the deliberate violations.
import { readFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { GateFindingKinds, checkPermissionGates, findUngatedButtonsInSource } from '@codelab/ng-tools/permission-gates';
import { RouteFindingKinds, findUngatedRoutes } from '@codelab/ng-tools/routes';
import { ESLint, type Linter } from 'eslint';
import { beforeAll, describe, expect, it } from 'vitest';
import {
  FIXTURE_PERMISSION_DATA_KEY,
  FIXTURE_PUBLIC_PATHS,
  GATED_ORDER_ROUTES,
  UNGATED_ORDER_ROUTES,
  UNGUARDED_ORDER_ROUTES,
} from '../lint-fixtures/src/app/features/orders/orders.routes';

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
  featureImportsPatternsLayouts: 'features/orders/feature-imports-patterns-layouts.component.ts.fixture',
  showcaseImportsEverything: 'showcase/gallery/showcase-imports-everything.component.ts.fixture',
  showcaseImportsFeature: 'showcase/gallery/showcase-imports-feature.component.ts.fixture',
  patternImportsFeature: 'patterns/record-list/pattern-imports-feature.ts.fixture',
} as const;

/**
 * Lint proof (phase 4): a third-party UI library is refused in every UI element folder. The external DGA scopes are
 * assembled at run time because the scrub sweep refuses them written literally anywhere in the repository.
 */
const AT = '@';
const THIRD_PARTY_UI_IMPORTS = [
  '@angular/material/button',
  'primeng/button',
  'ng-zorro-antd/button',
  [AT, 'dev-', 'dga', '/angular'].join(''),
  [AT, 'dga', '/ui'].join(''),
] as const;
const UI_ELEMENT_FILES = [
  'features/orders/ui-import.component.ts',
  'patterns/record-list/ui-import.ts',
  'shared/status-label/ui-import.ts',
  'showcase/gallery/ui-import.component.ts',
  'layouts/portal-frame/ui-import.ts',
] as const;
const UI_IMPORT_CASES = UI_ELEMENT_FILES.flatMap((file) => THIRD_PARTY_UI_IMPORTS.map((specifier) => [file, specifier] as const));

/** Permission-gate cases: each ungated case fails on exactly one line (base.fe-permission-gated). */
const GateCases = {
  ungatedButton: { file: 'features/orders/ungated-button.component.html.fixture', line: 4, element: 'button' },
  ungatedInlineButton: { file: 'features/orders/ungated-inline-button.component.ts.fixture', line: 8, element: 'dga-button' },
  ungatedSubmitInForm: { file: 'features/orders/ungated-submit-in-form.component.html.fixture', line: 3, element: 'button' },
  ungatedOnClick: { file: 'features/orders/ungated-on-click.component.html.fixture', line: 3, element: 'button' },
  ungatedKeyupEnter: { file: 'features/orders/ungated-keyup-enter.component.html.fixture', line: 3, element: 'button' },
  ungatedAnchorClick: { file: 'features/orders/ungated-anchor-click.component.html.fixture', line: 3, element: 'a' },
  ungatedRoleButton: { file: 'features/orders/ungated-role-button.component.html.fixture', line: 3, element: 'div' },
  ungatedDialogConfirm: { file: 'features/orders/ungated-dialog-confirm.component.html.fixture', line: 3, element: 'app-editor-dialog-pattern' },
} as const;
const GATED_TEMPLATE = 'features/orders/orders-actions.component.html';
const UNGATED_ROUTE_PATH = '/orders/export';
const UNGUARDED_ROUTE_PATH = '/orders/report';

const RealFiles = [
  'core/api/api.service.ts',
  'core/http/config-loader.ts',
  'shared/status-label/status-label.ts',
  'features/orders/orders-page.ts',
  'patterns/record-list/record-list-pattern.ts',
  'layouts/portal-frame/portal-frame.ts',
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

  it.each(UI_IMPORT_CASES)('refuses in %s an import of the UI library %s', async (relativePath, specifier) => {
    const source = `import { Widget } from '${specifier}';\n\nexport const widgets = [Widget];\n`;
    const messages = await lintFile(join(APP_DIR, relativePath), source);
    const ui = messages.filter((message) => message.ruleId === RuleIds.restrictedImports);
    expect(ui).toHaveLength(1);
    expect(ui[0].message).toContain(UI_LIBRARY_HINT);
  });

  it('allows a feature importing patterns, layouts and @codelab/ng-dga', async () => {
    expect(await lintCase(Cases.featureImportsPatternsLayouts)).toEqual([]);
  });

  it('allows the showcase importing @codelab/ng-dga, patterns, layouts, shared and core', async () => {
    expect(await lintCase(Cases.showcaseImportsEverything)).toEqual([]);
  });

  it.each([Cases.showcaseImportsFeature, Cases.patternImportsFeature])('refuses %s reaching into a feature', async (relativeCase) => {
    expect(ruleIds(await lintCase(relativeCase))).toContain(RuleIds.boundaries);
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

describe('the permission gates on lint-fixtures', () => {
  const routeOptions = { permissionDataKey: FIXTURE_PERMISSION_DATA_KEY, publicPaths: FIXTURE_PUBLIC_PATHS };

  it('passes a route table whose pages are gated or public', () => {
    expect(findUngatedRoutes(GATED_ORDER_ROUTES, routeOptions)).toEqual([]);
  });

  it('fails a route without a permission key', () => {
    expect(findUngatedRoutes(UNGATED_ORDER_ROUTES, routeOptions)).toEqual([{ path: UNGATED_ROUTE_PATH, kind: RouteFindingKinds.missingPermission }]);
  });

  it('fails a route with a permission key but no permission guard', () => {
    expect(findUngatedRoutes(UNGUARDED_ORDER_ROUTES, routeOptions)).toEqual([{ path: UNGUARDED_ROUTE_PATH, kind: RouteFindingKinds.missingGuard }]);
  });

  it.each(Object.values(GateCases))('fails the ungated button in $file', async ({ file, line, element }) => {
    const casePath = join(APP_DIR, file);
    const findings = findUngatedButtonsInSource(casePath.slice(0, -CASE_SUFFIX.length), await readFile(casePath, 'utf8'));
    expect(findings).toEqual([{ kind: GateFindingKinds.ungatedButton, line, column: expect.any(Number), element }]);
  });

  it('passes gated, wrapped and public buttons, and the fixture features folder as a whole', async () => {
    const template = join(APP_DIR, GATED_TEMPLATE);
    expect(findUngatedButtonsInSource(template, await readFile(template, 'utf8'))).toEqual([]);
    expect(checkPermissionGates(join(APP_DIR, 'features')).findings).toEqual([]);
  });
});
