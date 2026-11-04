// Module-level .editorconfig (NOT root): analyzer severities and the clean-code analyzer thresholds (codelab_* keys)
// for the module's C# files. It layers on top of the repo-root .editorconfig and overrides the repo-wide base values
// in rules/codelab.globalconfig, so a module's thresholds override reaches the analyzer.
import { GENERATED_MARKER, NEWLINE, REGENERATE_COMMAND } from '../constants.mjs';
import { analyzerSettingLines } from './analyzer-settings.mjs';

const CSHARP_SECTION = '[*.cs]';

export function emitEditorconfig(module) {
  const severities = Object.keys(module.analyzers)
    .sort()
    .map((id) => `dotnet_diagnostic.${id}.severity = ${module.analyzers[id]}`);
  return [
    `# ${GENERATED_MARKER} from ${module.rulesFile} (+ rules/base.rules.yaml). Do not edit.`,
    `# Change the analyzers or thresholds of the rules file and run: ${REGENERATE_COMMAND}`,
    '',
    CSHARP_SECTION,
    ...severities,
    '',
    '# Codelab.Build.Analyzers thresholds (CLB1001-CLB1005, ADR-012)',
    ...analyzerSettingLines(module),
  ].join(NEWLINE) + NEWLINE;
}
