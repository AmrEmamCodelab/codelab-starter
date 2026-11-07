// Finds skill and agent names referenced in prose ("the `new-module` skill", "agent `commit-gate`", a table's
// "Skill" column) and checks that each exists under .claude/.
import {
  AGENT_FILE_EXTENSION, AGENTS_DIRECTORY, PATH_SEPARATOR, ReferenceKinds, RuleIds, SKILL_FILE_NAME, SKILLS_DIRECTORY,
} from './constants.mjs';
import { columnHeaderAt } from './markdown.mjs';

const NAME = /^\/?([a-z][a-z0-9]*(?:-[a-z0-9]+)*)$/;
const KIND_AFTER = /^\s+(skill|skills|agent|agents|sub-?agents?)\b/i;
const KIND_BEFORE = /\b(skill|agent|sub-?agent)\s*$/i;
const CHAIN_CONNECTOR = /^\s*(?:,\s*(?:and\s+|or\s+)?|\s+and\s+|\s+or\s+|\s*\/\s*|\s*\+\s*)$/i;
const AGENT_WORD = /agent/i;

function kindOfWord(word) {
  return AGENT_WORD.test(word) ? ReferenceKinds.agent : ReferenceKinds.skill;
}

/**
 * Named references on one prose line.
 * @param {string} lineText
 * @param {{ content: string, start: number, end: number }[]} spans the line's code spans, in order
 * @param {{ cells: object[], headers: string[] } | undefined} tableRow the row's table columns, when it is a table row
 * @param {{ skillColumnHeaders: Set<string>, agentColumnHeaders: Set<string> }} referenceConfig
 * @returns {{ kind: string, name: string, column: number }[]}
 */
export function findNamedReferences(lineText, spans, tableRow, referenceConfig) {
  const kinds = new Array(spans.length).fill(null);
  // Chains: "`a`, `b` and `c` skills" - spans joined by connectors share the kind named after the last one.
  let chainStart = 0;
  for (let index = 0; index < spans.length; index += 1) {
    const next = spans[index + 1];
    const between = next ? lineText.slice(spans[index].end, next.start) : null;
    if (between !== null && CHAIN_CONNECTOR.test(between)) continue;
    const after = KIND_AFTER.exec(lineText.slice(spans[index].end));
    if (after !== null) {
      for (let member = chainStart; member <= index; member += 1) kinds[member] = kindOfWord(after[1]);
    }
    chainStart = index + 1;
  }
  spans.forEach((span, index) => {
    const before = KIND_BEFORE.exec(lineText.slice(0, span.start));
    if (kinds[index] === null && before !== null) kinds[index] = kindOfWord(before[1]);
    const header = columnHeaderAt(tableRow, span.start);
    if (kinds[index] === null && header !== null) {
      if (referenceConfig.skillColumnHeaders.has(header)) kinds[index] = ReferenceKinds.skill;
      else if (referenceConfig.agentColumnHeaders.has(header)) kinds[index] = ReferenceKinds.agent;
    }
  });
  const references = [];
  spans.forEach((span, index) => {
    const match = NAME.exec(span.content.trim());
    if (kinds[index] !== null && match !== null) {
      references.push({ kind: kinds[index], name: match[1], column: span.start + 1 });
    }
  });
  return references;
}

/** Repository path that must exist for a reference. */
export function referenceTarget(kind, name) {
  return kind === ReferenceKinds.agent
    ? `${AGENTS_DIRECTORY}${PATH_SEPARATOR}${name}${AGENT_FILE_EXTENSION}`
    : `${SKILLS_DIRECTORY}${PATH_SEPARATOR}${name}${PATH_SEPARATOR}${SKILL_FILE_NAME}`;
}

/**
 * @returns {{ ruleId: string, message: string } | null}
 */
export function checkNamedReference(index, reference, referenceConfig) {
  const external = reference.kind === ReferenceKinds.agent ? referenceConfig.externalAgents : referenceConfig.externalSkills;
  const target = referenceTarget(reference.kind, reference.name);
  if (index.fileSet.has(target) || external.has(reference.name)) return null;
  return {
    ruleId: reference.kind === ReferenceKinds.agent ? RuleIds.agentMissing : RuleIds.skillMissing,
    message: `${reference.kind} '${reference.name}' does not exist (expected ${target})`,
  };
}
