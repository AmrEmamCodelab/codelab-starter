// The docs and rules side every generator writes with its code (CLAUDE.md rule 12, docs move with code): notes in the
// module's MODULE.md, the recipe in its module.rules.yaml `recipes`, a required rule, a row in a Markdown table.
// Pure text edits; each is idempotent (a second run with the same input returns the text unchanged) and keeps the
// file's line endings.
import { CRLF, MODULE_DOC_MARKER, NEWLINE, RulesYaml } from './constants.mjs';

const USE_HEADING = /^#{1,6}\s+Use\s*$/im;
const NEXT_HEADING = /^#{1,6}\s+\S/m;
const TRAILING_SPACE = /\s*$/;
const TABLE_ROW_PREFIX = '|';

/** The line ending a text already uses ('\r\n' or '\n'). */
export function eolOf(text) {
  return text.includes(CRLF) ? CRLF : NEWLINE;
}

/**
 * Inserts `lines` (each once) into a MODULE.md: before `marker` when the file has it, else at the end of its "Use"
 * section, else at the end of the file.
 * @param {string} doc
 * @param {string[]} lines
 * @param {string} [marker]
 */
export function addUseNotes(doc, lines, marker = MODULE_DOC_MARKER) {
  const missing = lines.filter((line) => !doc.includes(line));
  if (missing.length === 0) return doc;
  const eol = eolOf(doc);
  const block = missing.join(eol) + eol;
  const at = doc.indexOf(marker);
  if (at >= 0) return doc.slice(0, at) + block + doc.slice(at);
  const use = USE_HEADING.exec(doc);
  if (!use) return `${doc.replace(TRAILING_SPACE, '')}${eol}${eol}${block}`;
  const afterUse = use.index + use[0].length;
  const next = NEXT_HEADING.exec(doc.slice(afterUse));
  if (!next) return `${doc.replace(TRAILING_SPACE, '')}${eol}${block}`;
  const sectionEnd = afterUse + next.index;
  const body = doc.slice(0, sectionEnd).replace(TRAILING_SPACE, '');
  return `${body}${eol}${block}${eol}${doc.slice(sectionEnd)}`;
}

/**
 * Adds `recipe` to the one-line `recipes: [...]` of a module.rules.yaml, or appends that line when the file has none.
 * @returns {{ text: string, added: boolean }}
 */
export function addRecipe(yaml, recipe) {
  const match = RulesYaml.recipesLine.exec(yaml);
  if (match) {
    const items = match[1].split(',').map((item) => item.trim()).filter(Boolean);
    if (items.includes(recipe)) return { text: yaml, added: false };
    const line = `${RulesYaml.recipesKey}: [${[...items, recipe].join(RulesYaml.listSeparator)}]`;
    return { text: yaml.slice(0, match.index) + line + yaml.slice(match.index + match[0].length), added: true };
  }
  const eol = eolOf(yaml);
  return { text: `${yaml.replace(TRAILING_SPACE, '')}${eol}${RulesYaml.recipesKey}: [${recipe}]${eol}`, added: true };
}

/** The ids of every `- id: x` item in a rules file. */
export function ruleIdsOf(yaml) {
  return [...yaml.matchAll(/^\s*-\s+id:\s*(\S+)\s*$/gm)].map((match) => match[1]);
}

/**
 * Appends a rule to the `required:` block of a module.rules.yaml (creating the block before `recipes:` when absent).
 * Skips it when a rule whose id ends with `.<idSuffix>` already exists (a hand-named rule counts).
 * @param {string} yaml
 * @param {{ id: string, idSuffix: string, statement: string, enforcedBy: string[] }} rule
 * @returns {{ text: string, added: boolean }}
 */
export function addRequiredRule(yaml, { id, idSuffix, statement, enforcedBy }) {
  if (ruleIdsOf(yaml).some((existing) => existing === id || existing.endsWith(`.${idSuffix}`))) return { text: yaml, added: false };
  const eol = eolOf(yaml);
  const item = [
    `${RulesYaml.itemIndent}- id: ${id}`,
    `${RulesYaml.fieldIndent}statement: ${JSON.stringify(statement)}`,
    `${RulesYaml.fieldIndent}enforcedBy: [${enforcedBy.join(RulesYaml.listSeparator)}]`,
  ].join(eol) + eol;
  const lines = yaml.split(eol);
  const start = lines.findIndex((line) => line.trimEnd() === `${RulesYaml.requiredKey}:`);
  if (start < 0) {
    const recipes = lines.findIndex((line) => line.startsWith(`${RulesYaml.recipesKey}:`));
    const block = `${RulesYaml.requiredKey}:${eol}${item}`;
    if (recipes < 0) return { text: `${yaml.replace(TRAILING_SPACE, '')}${eol}${block}`, added: true };
    return { text: [...lines.slice(0, recipes), block + lines.slice(recipes).join(eol)].join(eol), added: true };
  }
  let end = start + 1;
  while (end < lines.length && !RulesYaml.topLevelKey.test(lines[end])) end += 1;
  // Comments and blank lines directly above the next key belong to it, not to the required block.
  while (end - 1 > start && (lines[end - 1].trim() === '' || lines[end - 1].trimStart().startsWith('#'))) end -= 1;
  return { text: [...lines.slice(0, end), item + lines.slice(end).join(eol)].join(eol), added: true };
}

/**
 * Adds `row` after the last row of the Markdown table whose header line is `header`.
 * @returns {string | null} the new text (unchanged when the row is there), or null when the table is missing
 */
export function addTableRow(doc, header, row) {
  if (doc.includes(row)) return doc;
  const eol = eolOf(doc);
  const lines = doc.split(eol);
  const start = lines.findIndex((line) => line.trim() === header);
  if (start < 0) return null;
  let end = start + 1;
  while (end < lines.length && lines[end].trimStart().startsWith(TABLE_ROW_PREFIX)) end += 1;
  return [...lines.slice(0, end), row, ...lines.slice(end)].join(eol);
}

/**
 * Records the module's MODULE.md notes and its recipe in `changes`. A missing file is not an error (a hand-made module,
 * a test tree): it becomes a note telling what to add by hand.
 * @param {import('./workspace.mjs').ChangeSet} changes
 * @param {{ docPath: string, rulesPath: string, lines: string[], recipe?: string }} request
 * @returns {string[]} notes for the report
 */
export function documentModule(changes, { docPath, rulesPath, lines, recipe }) {
  const notes = [];
  const doc = changes.read(docPath);
  if (doc === null) notes.push(`no ${docPath}: add these lines to its Use section by hand: ${lines.join(' ')}`);
  else changes.write(docPath, addUseNotes(doc, lines));
  if (!recipe) return notes;
  const rules = changes.read(rulesPath);
  if (rules === null) notes.push(`no ${rulesPath}: list ${recipe} in its recipes by hand`);
  else changes.write(rulesPath, addRecipe(rules, recipe).text);
  return notes;
}
