// rules/codelab.globalconfig: the base clean-code thresholds for EVERY project, including projects without a
// module.rules.yaml (tools/, tests/). Codelab.Build.Sdk passes it as a GlobalAnalyzerConfigFiles item; a module's
// generated .editorconfig overrides it key by key (Codelab.Build.Analyzers reads the tree options first).
import { GENERATED_MARKER, NEWLINE, PATHS, REGENERATE_COMMAND } from '../constants.mjs';
import { analyzerSettingLines } from './analyzer-settings.mjs';

const GLOBAL_MARKER = 'is_global = true';

export function emitGlobalconfig(compiledBase) {
  return [
    `# ${GENERATED_MARKER} from ${PATHS.baseRules}. Do not edit; change the base rules and run: ${REGENERATE_COMMAND}`,
    '# Read by Codelab.Build.Sdk for every project; a module\'s generated .editorconfig overrides these values.',
    GLOBAL_MARKER,
    '',
    ...analyzerSettingLines(compiledBase),
  ].join(NEWLINE) + NEWLINE;
}
