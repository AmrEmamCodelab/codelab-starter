import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ESLint } from 'eslint';
import { describe, expect, it } from 'vitest';
import { ALLOWED_DEPENDENCIES, ElementTypes, boundaryPolicies, createCodelabEslintConfig } from '../src/eslint/index.mjs';

const APP_ROOT_DIR = fileURLToPath(new URL('./fixtures/boundary-app', import.meta.url));
const APP = 'src/app';
const BOUNDARY_RULE = 'boundaries/dependencies';

/** One file per element, and the import specifier that reaches it from a file in another element. */
const ELEMENT_FILES = {
  core: 'core/http/api.service.ts',
  layouts: 'layouts/shell/shell.ts',
  patterns: 'patterns/work-queue/work-queue.ts',
  shared: 'shared/file-upload/file-upload.ts',
  features: 'features/records/record-page.ts',
  showcase: 'showcase/catalog.ts',
};

function eslintFor(options = {}) {
  return new ESLint({
    cwd: APP_ROOT_DIR,
    overrideConfigFile: true,
    overrideConfig: createCodelabEslintConfig({ rootDir: APP_ROOT_DIR, appRoot: APP, ...options }),
  });
}

/** Lints `code` as if it were a new file at `relativePath` under the app; returns the boundary messages. */
async function boundaryMessages(relativePath, code, options) {
  const [result] = await eslintFor(options).lintText(code, { filePath: join(APP_ROOT_DIR, APP, relativePath) });
  const fatal = result.messages.filter((message) => message.fatal);
  expect(fatal).toEqual([]);
  return result.messages.filter((message) => message.ruleId === BOUNDARY_RULE);
}

/** A relative import, from a probe file two folders below the app root, to an element file. */
function importOf(target) {
  return `import * as target from '../../${ELEMENT_FILES[target].replace(/\.ts$/, '')}';\nexport const used = target;\n`;
}

const FROM_FILES = {
  core: 'core/probe/probe.ts',
  layouts: 'layouts/probe/probe.ts',
  patterns: 'patterns/probe/probe.ts',
  shared: 'shared/probe/probe.ts',
  features: 'features/orders/probe.ts',
  showcase: 'showcase/probe/probe.ts',
};

describe('the element-type boundaries', () => {
  const types = Object.values(ElementTypes);
  const cases = types.flatMap((from) => types.filter((to) => to !== from).map((to) => [from, to]));

  it.each(cases)('%s -> %s follows the allow list', async (from, to) => {
    const messages = await boundaryMessages(FROM_FILES[from], importOf(to));
    const allowed = ALLOWED_DEPENDENCIES[from].includes(to);

    expect(messages.length === 0).toBe(allowed);
  });

  it('pins the plan: features reach core, layouts, patterns and shared; shared reaches core only', () => {
    expect([...ALLOWED_DEPENDENCIES.features].sort()).toEqual(['core', 'layouts', 'patterns', 'shared']);
    expect([...ALLOWED_DEPENDENCIES.shared]).toEqual(['core']);
    expect([...ALLOWED_DEPENDENCIES.core]).toEqual([]);
  });

  it('allows imports inside one element', async () => {
    const code = "import { API_SERVICE } from '../http/api.service';\nexport const used = API_SERVICE;\n";
    expect(await boundaryMessages('core/probe/probe.ts', code)).toEqual([]);
  });
});

describe('feature-to-feature policies from the compiled rules', () => {
  const recordsFile = 'features/records/probe.ts';
  const toOrders = "import { ORDER_PAGE } from '../orders/order-page';\nexport const used = ORDER_PAGE;\n";
  const toReports = "import { REPORT_PAGE } from '../reports/report-page';\nexport const used = REPORT_PAGE;\n";

  it('allows a feature to import a feature its rules list in mayDependOn', async () => {
    expect(await boundaryMessages(recordsFile, toOrders)).toEqual([]);
  });

  it('refuses a feature it must not depend on, and one it does not list', async () => {
    expect(await boundaryMessages(recordsFile, toReports)).toHaveLength(1);
    const ordersToRecords = "import { RECORD_PAGE } from '../records/record-page';\nexport const used = RECORD_PAGE;\n";
    expect(await boundaryMessages('features/orders/probe.ts', ordersToRecords)).toHaveLength(1);
  });

  it('refuses every cross-feature import when the compiled rules are switched off', async () => {
    expect(await boundaryMessages(recordsFile, toOrders, { compiledRules: false })).toHaveLength(1);
  });

  it('finds the compiled rules by walking up from the root directory', () => {
    expect(boundaryPolicies({ rootDir: join(APP_ROOT_DIR, APP) }).length).toBeGreaterThan(boundaryPolicies({ rootDir: APP_ROOT_DIR, compiledRules: false }).length);
  });
});
