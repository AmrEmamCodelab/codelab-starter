// ESLint boundary data (rules/eslint-boundaries.compiled.json): read by @codelab/ng-tools' ESLint config.
// Data only; the lint logic lives in ng-tools.
import { GENERATED_MARKER, JSON_INDENT, NEWLINE, SCHEMA_VERSION } from '../constants.mjs';

const MAGIC_NUMBERS_RULE = 'base.no-magic-values';

function magicNumberSettings(compiledBase) {
  const rule = compiledBase.required.find((candidate) => candidate.id === MAGIC_NUMBERS_RULE);
  return { ignore: rule?.settings?.allowedNumbers ?? [] };
}

export function emitEslintBoundaries(compiled) {
  const data = {
    $comment: `${GENERATED_MARKER} from rules/*.rules.yaml. Do not edit.`,
    schemaVersion: SCHEMA_VERSION,
    noMagicNumbers: magicNumberSettings(compiled.base),
    modules: compiled.modules.map((module) => ({
      module: module.module,
      kind: module.kind,
      path: module.path,
      mayDependOn: module.mayDependOn,
      mustNotDependOn: module.mustNotDependOn,
    })),
  };
  return JSON.stringify(data, null, JSON_INDENT) + NEWLINE;
}
