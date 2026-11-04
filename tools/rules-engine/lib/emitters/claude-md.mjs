// Nested CLAUDE.md per module: the module's effective rules in prose, auto-loaded by Claude Code for files in the
// module folder. MODULE.md stays the human-written guide; this file is generated from the rules.
import { GENERATED_MARKER, MODULE_FILES, NEWLINE, REGENERATE_COMMAND } from '../constants.mjs';

const BACKTICK = '`';
const NONE = '_none_';
const SECTION_SPACER = '';

/** Inline code span that survives backticks inside the text. */
export function codeSpan(text) {
  const longestRun = Math.max(0, ...(text.match(/`+/g) ?? []).map((run) => run.length));
  const fence = BACKTICK.repeat(longestRun + 1);
  const pad = text.startsWith(BACKTICK) || text.endsWith(BACKTICK) ? ' ' : '';
  return `${fence}${pad}${text}${pad}${fence}`;
}

const list = (items) => (items.length > 0 ? items.map((item) => `- ${item}`) : [`- ${NONE}`]);
const names = (items) => (items.length > 0 ? items.map(codeSpan).join(', ') : NONE);

function header(module) {
  return [
    `<!-- ${GENERATED_MARKER} from ${module.rulesFile} (+ rules/base.rules.yaml). Do not edit; change the rules file and run \`${REGENERATE_COMMAND}\`. -->`,
    `# ${module.module} module: rules for AI edits`,
    SECTION_SPACER,
    `Read [${MODULE_FILES.moduleDoc}](${MODULE_FILES.moduleDoc}) first. The rules below are enforced by CI (\`tools/build.ps1 -Target CI\`); the Claude hooks refuse violating edits early. Owners: ${module.owners.join(', ')}.`,
  ];
}

function dependencies(module) {
  return [
    '## Dependencies',
    `- May depend on: ${names(module.mayDependOn)}`,
    `- Must not depend on: ${names(module.mustNotDependOn)}`,
  ];
}

function required(module) {
  const lines = module.required.map((rule) => {
    const pending = rule.availableFrom ? `, automated from ${rule.availableFrom}` : '';
    return `${codeSpan(rule.id)}: ${rule.statement} (enforced by ${rule.enforcedBy.join(', ')}${pending})`;
  });
  return ['## Required', ...list(lines)];
}

function forbidden(module) {
  const lines = module.forbidden.map((rule) =>
    `${codeSpan(rule.id)} (${rule.severity}) in ${rule.appliesTo.map(codeSpan).join(', ')}: ${rule.description} Fix: ${rule.fix}`);
  return ['## Forbidden', ...list(lines)];
}

function bannedApis(module) {
  const lines = module.bannedSymbols.map((entry) => `${codeSpan(entry.symbol)}: ${entry.fix} (${codeSpan(entry.id)}, build error RS0030)`);
  return ['## Banned APIs', 'Plus the global list in Codelab.Build.Sdk (DateTime.Now and friends).', ...list(lines)];
}

function thresholds(module) {
  const lines = Object.entries(module.thresholds).map(([key, value]) => `${key}: ${value}`);
  return ['## Thresholds', ...list(lines)];
}

function recipesAndExemptions(module) {
  const exemptions = module.exemptions.map((exemption) => `${codeSpan(exemption.rule)}: ${exemption.reason}`);
  return [
    '## Recipes',
    ...list(module.recipes.map(codeSpan)),
    SECTION_SPACER,
    '## Exemptions from base rules',
    ...list(exemptions),
  ];
}

export function emitClaudeMd(module) {
  const sections = [header, dependencies, required, forbidden, bannedApis, thresholds, recipesAndExemptions];
  return sections.map((section) => section(module).join(NEWLINE)).join(NEWLINE + NEWLINE) + NEWLINE;
}
