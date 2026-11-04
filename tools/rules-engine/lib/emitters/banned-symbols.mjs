// BannedSymbols.generated.txt: picked up by Codelab.Build.Sdk as an AdditionalFile; RS0030 turns each use into a
// build error. Format per line: <documentation id>;<message>. Comment lines start with //.
import { GENERATED_MARKER, NEWLINE, REGENERATE_COMMAND } from '../constants.mjs';

export function emitBannedSymbols(module) {
  const header = [
    `// ${GENERATED_MARKER} from ${module.rulesFile} (+ rules/base.rules.yaml). Do not edit.`,
    `// Change the rules file and run: ${REGENERATE_COMMAND}`,
    '// Global bans (DateTime.Now and friends) come from Codelab.Build.Sdk/Sdk/BannedSymbols.txt.',
  ];
  const lines = module.bannedSymbols.map((entry) => `${entry.symbol};${entry.fix} (rule ${entry.id})`);
  return [...header, ...lines].join(NEWLINE) + NEWLINE;
}
