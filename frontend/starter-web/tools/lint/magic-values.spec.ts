// @vitest-environment node
//
// base.no-magic-values and base.enums-as-int / base.enum-explicit-values in the app's ESLint config: the helpers of
// magic-values.mjs, then the real config (starter-eslint-config.mjs) on the lint-fixtures/.../features/billing cases.
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { ESLint, type Linter } from 'eslint';
import { afterEach, beforeAll, describe, expect, it } from 'vitest';
import {
  COMPILED_BOUNDARIES_PATH,
  ENUM_INT_MESSAGE,
  ENUM_EXPLICIT_VALUE_MESSAGE,
  ENUM_RULE_ID,
  MAGIC_NUMBERS_RULE_ID,
  SUPPORTED_SCHEMA_VERSION,
  allowedNumbers,
  isAllowedLiteral,
  isStringInitializer,
  loadCompiledBoundaries,
} from './magic-values.mjs';

const LINT_DIR = dirname(fileURLToPath(import.meta.url));
const FIXTURES_ROOT = resolve(LINT_DIR, '..', '..', 'lint-fixtures');
const BILLING_DIR = join(FIXTURES_ROOT, 'src', 'app', 'features', 'billing');
const CONFIG_MODULE = pathToFileURL(join(LINT_DIR, 'starter-eslint-config.mjs')).href;
const CASE_SUFFIX = '.fixture';
const UNSUPPORTED_SCHEMA_VERSION = SUPPORTED_SCHEMA_VERSION + 1;
const ALLOWED_FROM_RULES = [-1, 0, 1];

const Cases = {
  magicNumber: 'magic-number.ts.fixture',
  implicitEnum: 'implicit-enum.ts.fixture',
  stringEnum: 'string-enum.ts.fixture',
  specFile: 'invoice-math.spec.ts.fixture',
  testingFile: 'invoice-builder.testing.ts.fixture',
} as const;

const CLEAN_FILE = 'invoice-constants.ts';
/** implicit-enum: two members without a value. */
const IMPLICIT_ENUM_FINDINGS = 2;
/** string-enum: a string literal and a template literal. */
const STRING_ENUM_FINDINGS = 2;

let eslint: ESLint;
const tempDirs: string[] = [];

beforeAll(async () => {
  const { createStarterWebEslintConfig } = await import(CONFIG_MODULE);
  eslint = new ESLint({
    cwd: FIXTURES_ROOT,
    overrideConfigFile: true,
    overrideConfig: createStarterWebEslintConfig({ rootDir: FIXTURES_ROOT }),
  });
});

afterEach(() => {
  for (const dir of tempDirs.splice(0)) rmSync(dir, { recursive: true, force: true });
});

async function lintFile(filePath: string, text?: string): Promise<Linter.LintMessage[]> {
  const code = text ?? (await readFile(filePath, 'utf8'));
  const [result] = await eslint.lintText(code, { filePath });
  expect(result.messages.filter((message) => message.fatal === true)).toEqual([]);
  return result.messages;
}

async function lintCase(relativeCase: string): Promise<Linter.LintMessage[]> {
  const casePath = join(BILLING_DIR, relativeCase);
  return lintFile(casePath.slice(0, -CASE_SUFFIX.length), await readFile(casePath, 'utf8'));
}

const withRule = (messages: Linter.LintMessage[], ruleId: string) => messages.filter((message) => message.ruleId === ruleId);

function tempRepoWith(compiled: unknown): string {
  const root = mkdtempSync(join(tmpdir(), 'starter-magic-values-'));
  tempDirs.push(root);
  mkdirSync(join(root, dirname(COMPILED_BOUNDARIES_PATH)), { recursive: true });
  writeFileSync(join(root, COMPILED_BOUNDARIES_PATH), JSON.stringify(compiled));
  const nested = join(root, 'frontend', 'app');
  mkdirSync(nested, { recursive: true });
  return nested;
}

describe('magic-values helpers', () => {
  it('reads the allowed numbers from the repository rules', () => {
    expect(allowedNumbers(loadCompiledBoundaries(FIXTURES_ROOT))).toEqual(ALLOWED_FROM_RULES);
  });

  it('walks up to the compiled rules from a nested folder', () => {
    const nested = tempRepoWith({ schemaVersion: SUPPORTED_SCHEMA_VERSION, noMagicNumbers: { ignore: [0] }, modules: [] });
    expect(allowedNumbers(loadCompiledBoundaries(nested))).toEqual([0]);
  });

  it('allows no numbers when the rules list none', () => {
    expect(allowedNumbers({ schemaVersion: SUPPORTED_SCHEMA_VERSION })).toEqual([]);
  });

  it('refuses an unsupported schema version', () => {
    const nested = tempRepoWith({ schemaVersion: UNSUPPORTED_SCHEMA_VERSION });
    expect(() => loadCompiledBoundaries(nested)).toThrow(/schemaVersion/);
  });

  it('refuses a folder with no compiled rules above it', () => {
    const root = mkdtempSync(join(tmpdir(), 'starter-magic-values-none-'));
    tempDirs.push(root);
    expect(() => loadCompiledBoundaries(root)).toThrow(/was not found/);
  });

  it('allows a literal under an enum member or a literal type, also when negated', () => {
    const enumMember = { type: 'TSEnumMember' };
    expect(isAllowedLiteral({ parent: enumMember })).toBe(true);
    expect(isAllowedLiteral({ parent: { type: 'UnaryExpression', parent: enumMember } })).toBe(true);
    expect(isAllowedLiteral({ parent: { type: 'TSLiteralType' } })).toBe(true);
    expect(isAllowedLiteral({ parent: { type: 'BinaryExpression' } })).toBe(false);
    expect(isAllowedLiteral({})).toBe(false);
  });

  it('treats string and template literals as string initializers, numbers not', () => {
    expect(isStringInitializer({ type: 'Literal', value: 'a' })).toBe(true);
    expect(isStringInitializer({ type: 'TemplateLiteral' })).toBe(true);
    expect(isStringInitializer({ type: 'Literal', value: 1 })).toBe(false);
    expect(isStringInitializer({ type: 'UnaryExpression' })).toBe(false);
    expect(isStringInitializer(undefined)).toBe(false);
  });
});

describe('the starter ESLint config on magic values and enums', () => {
  it('refuses a bare number in feature code', async () => {
    const messages = withRule(await lintCase(Cases.magicNumber), MAGIC_NUMBERS_RULE_ID);
    expect(messages).toHaveLength(1);
    expect(messages[0].message).toContain('5000');
  });

  it('refuses enum members without an explicit value', async () => {
    const messages = withRule(await lintCase(Cases.implicitEnum), ENUM_RULE_ID);
    expect(messages).toHaveLength(IMPLICIT_ENUM_FINDINGS);
    expect(messages.every((message) => message.message === ENUM_EXPLICIT_VALUE_MESSAGE)).toBe(true);
  });

  it('refuses string-valued enum members', async () => {
    const messages = withRule(await lintCase(Cases.stringEnum), ENUM_RULE_ID);
    expect(messages).toHaveLength(STRING_ENUM_FINDINGS);
    expect(messages.every((message) => message.message === ENUM_INT_MESSAGE)).toBe(true);
  });

  it.each([Cases.specFile, Cases.testingFile])('lets the test file %s use bare numbers', async (relativeCase) => {
    expect(await lintCase(relativeCase)).toEqual([]);
  });

  it('passes explicit integer enums, literal types, const numbers, indexes and defaults', async () => {
    expect(await lintFile(join(BILLING_DIR, CLEAN_FILE))).toEqual([]);
  });
});
