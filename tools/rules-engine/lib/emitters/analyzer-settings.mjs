// The codelab_* analyzer keys read by Codelab.Build.Analyzers (CLB1001-CLB1005, ADR-012). Shared by the module
// .editorconfig emitter and the repo-wide rules/codelab.globalconfig emitter, so both always carry the same keys.
// The key names mirror packages/dotnet/Codelab.Build.Analyzers/Options/OptionKeys.cs.
import { MAGIC_NUMBERS_RULE } from '../constants.mjs';

export const ANALYZER_SETTING_KEYS = Object.freeze({
  methodLines: 'codelab_method_lines',
  fileLines: 'codelab_file_lines',
  parameters: 'codelab_parameters',
  allowedNumbers: 'codelab_allowed_numbers',
  excludeTests: 'codelab_exclude_tests',
  magicNumbersEnforced: 'codelab_magic_numbers_enforced',
});

const LIST_SEPARATOR = ',';

/**
 * Key/value pairs for the analyzer. The magic-number keys come from the base.no-magic-values rule; a module that
 * exempts that rule (it is then missing from `required`) gets codelab_magic_numbers_enforced = false only.
 * @param {{ thresholds: object, required: object[] }} rules a compiled module or the compiled base
 * @returns {[string, string][]}
 */
export function analyzerSettings(rules) {
  const sizes = [
    [ANALYZER_SETTING_KEYS.methodLines, String(rules.thresholds.methodLines)],
    [ANALYZER_SETTING_KEYS.fileLines, String(rules.thresholds.fileLines)],
    [ANALYZER_SETTING_KEYS.parameters, String(rules.thresholds.parameters)],
  ];
  const magicNumbers = rules.required.find((rule) => rule.id === MAGIC_NUMBERS_RULE);
  if (!magicNumbers) {
    return [...sizes, [ANALYZER_SETTING_KEYS.magicNumbersEnforced, String(false)]];
  }
  const settings = magicNumbers.settings ?? {};
  return [
    ...sizes,
    [ANALYZER_SETTING_KEYS.allowedNumbers, (settings.allowedNumbers ?? []).join(LIST_SEPARATOR)],
    [ANALYZER_SETTING_KEYS.excludeTests, String(settings.excludeTests === true)],
    [ANALYZER_SETTING_KEYS.magicNumbersEnforced, String(true)],
  ];
}

/** `key = value` lines, in the order analyzerSettings returns them. */
export function analyzerSettingLines(rules) {
  return analyzerSettings(rules).map(([key, value]) => `${key} = ${value}`);
}
