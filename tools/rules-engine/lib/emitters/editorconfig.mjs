// Module-level .editorconfig (NOT root): analyzer severities for the module's C# files. It layers on top of the
// repo-root .editorconfig, so only the module's severities live here.
import { GENERATED_MARKER, NEWLINE, REGENERATE_COMMAND } from '../constants.mjs';

const CSHARP_SECTION = '[*.cs]';

export function emitEditorconfig(module) {
  const severities = Object.keys(module.analyzers)
    .sort()
    .map((id) => `dotnet_diagnostic.${id}.severity = ${module.analyzers[id]}`);
  return [
    `# ${GENERATED_MARKER} from ${module.rulesFile} (+ rules/base.rules.yaml). Do not edit.`,
    `# Change the analyzers section of the rules file and run: ${REGENERATE_COMMAND}`,
    '',
    CSHARP_SECTION,
    ...severities,
  ].join(NEWLINE) + NEWLINE;
}
