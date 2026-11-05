// The app's half of base.no-magic-values and base.enums-as-int / base.enum-explicit-values, as ESLint config:
// - no-magic-numbers for non-test TypeScript, with the allowed numbers read from rules/eslint-boundaries.compiled.json
//   (the same data and options as tools/ci/lint-packages.mjs uses for the packages);
// - every enum member has an explicit value, and no member is string-valued (enums travel and are stored as ints):
//   `starter/enum-int-values`.
//
// The core no-magic-numbers rule does not know TypeScript: it would flag the explicit value of every enum member and
// every numeric literal type. `starter/no-magic-numbers` wraps it and drops exactly those two reports (the
// @typescript-eslint variant's ignoreEnums and ignoreNumericLiteralTypes), so no extra plugin is needed.

import { existsSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { builtinRules } from 'eslint/use-at-your-own-risk';

/** Where the rules engine writes the allowed numbers, relative to the repository root. */
export const COMPILED_BOUNDARIES_PATH = join('rules', 'eslint-boundaries.compiled.json');

/** The compiled schema version this reader understands. */
export const SUPPORTED_SCHEMA_VERSION = 1;

/** Test sources are not linted for magic numbers (base.no-magic-values excludeTests); same idea as lint-packages. */
export const MAGIC_NUMBER_TEST_PATTERNS = Object.freeze(['**/*.spec.ts', '**/*.testing.ts', '**/*.test.ts', '**/tests/**']);

/** The local plugin namespace and rule id. */
export const PLUGIN_NAME = 'starter';
export const MAGIC_NUMBERS_RULE = 'no-magic-numbers';
export const MAGIC_NUMBERS_RULE_ID = `${PLUGIN_NAME}/${MAGIC_NUMBERS_RULE}`;

/** AST parents whose numeric literal is not a magic number: an enum member's value and a numeric literal type. */
export const ALLOWED_LITERAL_PARENTS = Object.freeze(['TSEnumMember', 'TSLiteralType']);

/** Shown on an enum member without a value; starts with the base rule id like the hook and CI messages. */
export const ENUM_EXPLICIT_VALUE_MESSAGE =
  '[base.enum-explicit-values] Give every enum member an explicit integer value (A = 1); implicit values shift when members are reordered.';

/** Shown on a string-valued enum member. */
export const ENUM_INT_MESSAGE =
  '[base.enums-as-int] Enum members are integers, not strings (A = 1); use a constants object for string keys.';

/** The enum rule id (a plugin rule, not no-restricted-syntax: ESLint replaces a rule's options per config object, so a
 * second no-restricted-syntax block would silently switch off the inline-style selectors). */
export const ENUM_RULE = 'enum-int-values';
export const ENUM_RULE_ID = `${PLUGIN_NAME}/${ENUM_RULE}`;

/** Message ids of the enum rule. */
export const EnumMessageIds = Object.freeze({ explicitValue: 'explicitValue', intValue: 'intValue' });

const ERROR = 'error';
const TYPESCRIPT_FILES = '**/*.ts';
const coreMagicNumbers = builtinRules.get(MAGIC_NUMBERS_RULE);

/**
 * The compiled boundary data found by walking up from `startDirectory`.
 * @throws Error when there is none or its schema version is not supported (the gate must not silently switch off).
 */
export function loadCompiledBoundaries(startDirectory) {
  let current = resolve(startDirectory);
  for (;;) {
    const candidate = join(current, COMPILED_BOUNDARIES_PATH);
    if (existsSync(candidate)) {
      const data = JSON.parse(readFileSync(candidate, 'utf8'));
      if (data?.schemaVersion !== SUPPORTED_SCHEMA_VERSION) {
        throw new Error(`${candidate}: schemaVersion ${data?.schemaVersion} is not supported (expected ${SUPPORTED_SCHEMA_VERSION}).`);
      }
      return data;
    }
    const parent = dirname(current);
    if (parent === current) throw new Error(`${COMPILED_BOUNDARIES_PATH} was not found above ${startDirectory}.`);
    current = parent;
  }
}

/** The numbers base.no-magic-values allows, from the compiled data (empty when the rule lists none). */
export function allowedNumbers(compiled) {
  return [...(compiled?.noMagicNumbers?.ignore ?? [])];
}

/** True when a report from the core rule is about a literal TypeScript allows (enum value, numeric literal type). */
export function isAllowedLiteral(node) {
  let current = node?.parent;
  // A negative literal (-1) is reported on the UnaryExpression whose parent holds it.
  if (current?.type === 'UnaryExpression') current = current.parent;
  return current !== undefined && ALLOWED_LITERAL_PARENTS.includes(current.type);
}

/** The core no-magic-numbers rule with the TypeScript-only literals dropped. */
export const noMagicNumbersRule = {
  meta: { ...coreMagicNumbers.meta, docs: { ...coreMagicNumbers.meta.docs, url: undefined } },
  create(context) {
    const filtered = Object.create(context, {
      report: {
        value(descriptor) {
          if (!isAllowedLiteral(descriptor.node)) context.report(descriptor);
        },
      },
    });
    return coreMagicNumbers.create(filtered);
  },
};

/** True when an enum member's value is a string (a string literal or a template literal). */
export function isStringInitializer(initializer) {
  if (initializer?.type === 'TemplateLiteral') return true;
  return initializer?.type === 'Literal' && typeof initializer.value === 'string';
}

/** Every enum member has an explicit value, and no value is a string. */
export const enumIntValuesRule = {
  meta: {
    type: 'problem',
    docs: { description: 'Enum members carry an explicit integer value (base.enums-as-int, base.enum-explicit-values).' },
    schema: [],
    messages: { [EnumMessageIds.explicitValue]: ENUM_EXPLICIT_VALUE_MESSAGE, [EnumMessageIds.intValue]: ENUM_INT_MESSAGE },
  },
  create(context) {
    return {
      TSEnumMember(node) {
        if (node.initializer === undefined || node.initializer === null) {
          context.report({ node, messageId: EnumMessageIds.explicitValue });
        } else if (isStringInitializer(node.initializer)) {
          context.report({ node: node.initializer, messageId: EnumMessageIds.intValue });
        }
      },
    };
  },
};

/** The local plugin carrying both rules. */
export const starterPlugin = Object.freeze({
  rules: { [MAGIC_NUMBERS_RULE]: noMagicNumbersRule, [ENUM_RULE]: enumIntValuesRule },
});

/** The config objects: magic numbers for non-test TypeScript, explicit integer enum values everywhere. */
export function magicValueConfigs({ rootDir }) {
  const ignore = allowedNumbers(loadCompiledBoundaries(rootDir));
  return [
    {
      name: 'starter/no-magic-values',
      files: [TYPESCRIPT_FILES],
      ignores: [...MAGIC_NUMBER_TEST_PATTERNS],
      plugins: { [PLUGIN_NAME]: starterPlugin },
      rules: {
        [MAGIC_NUMBERS_RULE_ID]: [ERROR, { ignore, ignoreArrayIndexes: true, ignoreDefaultValues: true, enforceConst: true }],
      },
    },
    {
      name: 'starter/enums-as-int',
      files: [TYPESCRIPT_FILES],
      plugins: { [PLUGIN_NAME]: starterPlugin },
      rules: { [ENUM_RULE_ID]: ERROR },
    },
  ];
}
