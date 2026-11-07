// The feature-spec checks: front matter, required sections in order, the four tables, the skill plan, the hand-edit
// log and the review log. Pure: text in, findings out (the CLI and other tools such as the pipeline-proof harness
// call checkSpec directly).
import {
  AUTOMATED_APPROVER,
  DIGIT_ONE,
  DIGIT_ONE_LOOKALIKES,
  EndpointColumn,
  FrontMatterKey,
  HAND_EDIT_NUMBER,
  HandEditColumn,
  HTTP_METHODS,
  INLINE_CODE_MARK,
  ISO_DATE,
  ITERATIONS_LINE,
  KEBAB_SLUG,
  LOOKALIKE_CHARACTERS,
  MAX_HAND_EDITS,
  NONE_CONTENT,
  PAGE_PATTERNS,
  PLAN_ITEM_SKILL,
  PLAN_SKILLS,
  PlanSkill,
  PRE_APPROVED_BY,
  REASON_REQUIRED_VALUES,
  REQUIRED_FRONT_MATTER_KEYS,
  REQUIRED_SECTIONS,
  ROUTE_PREFIX,
  ROLE_ONLY_WORDS,
  RuleId,
  ScreenColumn,
  Section,
  Severity,
  SpecStatus,
  TableColumns,
  TransitionColumn,
} from './constants.mjs';
import { contentLines, firstTable, orderedListItems, parseFrontMatter, parseSections, splitLines } from './markdown.mjs';

const FIRST_LINE = 1;
const MONTH_INDEX_OFFSET = 1;

function finding(findings, { ruleId, message, line = null, severity = Severity.error }) {
  findings.push({ ruleId, severity, message, line });
}

function isRealDate(text) {
  const match = ISO_DATE.exec(text);
  if (!match) return false;
  const [, year, month, day] = match.map(Number);
  const date = new Date(Date.UTC(year, month - MONTH_INDEX_OFFSET, day));
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - MONTH_INDEX_OFFSET && date.getUTCDate() === day;
}

function scalar(values, key) {
  const value = values.get(key);
  return typeof value === 'string' ? value : '';
}

function checkFrontMatter(frontMatter, findings, approval) {
  if (!frontMatter.found) {
    finding(findings, { ruleId: RuleId.frontMatterMissing, line: FIRST_LINE, message: 'The spec must start with a YAML front matter block between two --- lines.' });
    return null;
  }
  for (const error of frontMatter.errors) {
    finding(findings, { ruleId: RuleId.frontMatterSyntax, line: error.line, message: `Front matter line is not "key: value" or a list item: ${error.text.trim()}` });
  }
  const { values } = frontMatter;
  for (const key of REQUIRED_FRONT_MATTER_KEYS) {
    if (!values.has(key)) finding(findings, { ruleId: RuleId.frontMatterKeyMissing, line: FIRST_LINE, message: `Front matter key "${key}" is missing.` });
  }
  for (const key of values.keys()) {
    if (!REQUIRED_FRONT_MATTER_KEYS.includes(key)) {
      finding(findings, { ruleId: RuleId.frontMatterUnknownKey, line: FIRST_LINE, severity: Severity.warning, message: `Front matter key "${key}" is not part of the format.` });
    }
  }
  const feature = scalar(values, FrontMatterKey.feature);
  if (values.has(FrontMatterKey.feature) && !KEBAB_SLUG.test(feature)) {
    finding(findings, { ruleId: RuleId.featureSlug, line: FIRST_LINE, message: `"feature" must be a kebab-case slug (got "${feature}").` });
  }
  const inputs = values.get(FrontMatterKey.inputs);
  if (values.has(FrontMatterKey.inputs) && (!Array.isArray(inputs) || inputs.length === 0)) {
    finding(findings, { ruleId: RuleId.inputsInvalid, line: FIRST_LINE, message: '"inputs" must be a list with at least one entry (an input file or the plain-language scenario).' });
  }
  const status = scalar(values, FrontMatterKey.status);
  const knownStatus = Object.values(SpecStatus).includes(status);
  if (values.has(FrontMatterKey.status) && !knownStatus) {
    finding(findings, { ruleId: RuleId.statusInvalid, line: FIRST_LINE, message: `"status" must be ${Object.values(SpecStatus).join(' or ')} (got "${status}").` });
  }
  checkApproval(values, status, findings, approval);
  return knownStatus ? status : null;
}

/** Lower-cased readings of the approver with look-alike characters folded ("C1aude" -> "claude" and "ciaude"). */
function approverReadings(approvedBy) {
  const lowered = approvedBy.toLowerCase();
  const folded = [...lowered].map((character) => LOOKALIKE_CHARACTERS[character] ?? character).join('');
  return [lowered, ...DIGIT_ONE_LOOKALIKES.map((letter) => folded.split(DIGIT_ONE).join(letter))];
}

/** Letters and digits only, so "Spec Writer", "spec_writer" and "spec-writer" compare equal. */
function compact(text) {
  return text.toLowerCase().replace(/[^\p{L}\p{N}]+/gu, '');
}

function wordsOf(text) {
  return text.split(/[^\p{L}\p{N}]+/u).filter(Boolean);
}

/**
 * True when approvedBy names no person: an agent's name (separators and look-alikes ignored), a word such as agent,
 * bot or pipeline, or only role words. An early warning only; the approval gate itself is the PreToolUse hook rule and
 * CODEOWNERS on docs/features/, since no name heuristic can tell a person's name from a made-up one.
 */
function namesAutomatedApprover(approvedBy, agentNames) {
  const agents = agentNames.map(compact).filter(Boolean);
  return approverReadings(approvedBy).some((reading) => {
    const words = wordsOf(reading);
    const roleOnly = words.length > 0 && words.every((word) => ROLE_ONLY_WORDS.has(word));
    const squeezed = compact(reading);
    return AUTOMATED_APPROVER.test(reading) || roleOnly || agents.some((agent) => squeezed.includes(agent));
  });
}

function checkApprover(approvedBy, findings, { allowPreApproval, agentNames }) {
  if (approvedBy === '') return;
  if (approvedBy === PRE_APPROVED_BY) {
    if (!allowPreApproval) {
      finding(findings, { ruleId: RuleId.approvalPreApproved, line: FIRST_LINE, message: `"approvedBy: ${PRE_APPROVED_BY}" is accepted only inside a pipeline-proof run; a person approves a real spec and writes their own name.` });
    }
    return;
  }
  if (namesAutomatedApprover(approvedBy, agentNames)) {
    finding(findings, { ruleId: RuleId.approvalNotHuman, line: FIRST_LINE, message: `"approvedBy" must name the person who approved the spec, not an agent or a tool (got "${approvedBy}").` });
  }
}

function checkApproval(values, status, findings, { requireApproved, allowPreApproval, agentNames }) {
  const approvedBy = scalar(values, FrontMatterKey.approvedBy);
  checkApprover(approvedBy, findings, { allowPreApproval, agentNames });
  const approvedOn = scalar(values, FrontMatterKey.approvedOn);
  if (status === SpecStatus.approved && (approvedBy === '' || approvedOn === '')) {
    finding(findings, { ruleId: RuleId.approvalIncomplete, line: FIRST_LINE, message: 'An approved spec records who approved it (approvedBy) and when (approvedOn).' });
  }
  if (approvedOn !== '' && !isRealDate(approvedOn)) {
    finding(findings, { ruleId: RuleId.approvedOnInvalid, line: FIRST_LINE, message: `"approvedOn" must be a real YYYY-MM-DD date (got "${approvedOn}").` });
  }
  if (requireApproved && status !== SpecStatus.approved) {
    finding(findings, { ruleId: RuleId.approvalRequired, line: FIRST_LINE, message: `The spec is "${status || 'unset'}"; building needs status: approved with approvedBy and approvedOn (human approval gate).` });
  }
}

function checkSectionLayout(sections, findings) {
  const byName = new Map();
  let lastIndex = -1;
  for (const section of sections) {
    const index = REQUIRED_SECTIONS.indexOf(section.name);
    if (index < 0) {
      finding(findings, { ruleId: RuleId.sectionUnknown, line: section.line, severity: Severity.warning, message: `Section "${section.name}" is not part of the format.` });
      continue;
    }
    if (byName.has(section.name)) {
      finding(findings, { ruleId: RuleId.sectionDuplicate, line: section.line, message: `Section "${section.name}" appears more than once.` });
      continue;
    }
    byName.set(section.name, section);
    if (index < lastIndex) {
      finding(findings, { ruleId: RuleId.sectionOrder, line: section.line, message: `Section "${section.name}" is out of order; the order is: ${REQUIRED_SECTIONS.join(', ')}.` });
    }
    lastIndex = Math.max(lastIndex, index);
  }
  for (const name of REQUIRED_SECTIONS) {
    if (!byName.has(name)) finding(findings, { ruleId: RuleId.sectionMissing, line: null, message: `Required section "## ${name}" is missing.` });
  }
  for (const section of byName.values()) {
    if (contentLines(section).length === 0) {
      finding(findings, { ruleId: RuleId.sectionEmpty, line: section.line, message: `Section "${section.name}" is empty; write "None." when the feature needs nothing here.` });
    }
  }
  return byName;
}

function sameColumns(actual, expected) {
  return actual.length === expected.length && expected.every((column, index) => actual[index].toLowerCase() === column.toLowerCase());
}

/** Checks a section's table shape and every cell; returns its rows (empty when the table is missing or malformed). */
function checkTable(section, findings) {
  const expected = TableColumns[section.name];
  const table = firstTable(section);
  if (!table) {
    finding(findings, { ruleId: RuleId.tableMissing, line: section.line, message: `Section "${section.name}" needs a table with the columns: ${expected.join(' | ')}.` });
    return [];
  }
  if (!sameColumns(table.header, expected)) {
    finding(findings, { ruleId: RuleId.tableColumns, line: table.headerLine, message: `Table in "${section.name}" must have the columns ${expected.join(' | ')} (got ${table.header.join(' | ')}).` });
    return [];
  }
  const rows = [];
  for (const row of table.rows) {
    if (row.cells.length !== expected.length) {
      finding(findings, { ruleId: RuleId.tableCellCount, line: row.line, message: `Row in "${section.name}" has ${row.cells.length} cells; expected ${expected.length}.` });
      continue;
    }
    row.cells.forEach((cell, index) => {
      if (cell === '') finding(findings, { ruleId: RuleId.tableCellEmpty, line: row.line, message: `Column "${expected[index]}" is empty in "${section.name}".` });
    });
    rows.push(row);
  }
  return rows;
}

function invalidValue(findings, row, sectionName, message) {
  finding(findings, { ruleId: RuleId.tableValue, line: row.line, message: `${sectionName}: ${message}` });
}

function checkTransitionRows(rows, findings) {
  for (const row of rows) {
    const value = row.cells[TransitionColumn.reasonRequired].toLowerCase();
    if (!REASON_REQUIRED_VALUES.includes(value)) invalidValue(findings, row, Section.transitions, `"Reason required" must be ${REASON_REQUIRED_VALUES.join(' or ')} (got "${value}").`);
  }
}

function checkEndpointRows(rows, findings) {
  for (const row of rows) {
    const method = row.cells[EndpointColumn.method];
    const route = row.cells[EndpointColumn.route].replaceAll(INLINE_CODE_MARK, '');
    if (!HTTP_METHODS.includes(method)) invalidValue(findings, row, Section.endpoints, `Method must be one of ${HTTP_METHODS.join(', ')} (got "${method}").`);
    if (!route.startsWith(ROUTE_PREFIX)) invalidValue(findings, row, Section.endpoints, `Route must start with "${ROUTE_PREFIX}" (got "${route}").`);
  }
}

function checkScreenRows(rows, findings) {
  for (const row of rows) {
    const route = row.cells[ScreenColumn.route].replaceAll(INLINE_CODE_MARK, '');
    const pattern = row.cells[ScreenColumn.pagePattern].replaceAll(INLINE_CODE_MARK, '');
    if (!route.startsWith(ROUTE_PREFIX)) invalidValue(findings, row, Section.screens, `Route must start with "${ROUTE_PREFIX}" (got "${route}").`);
    if (!PAGE_PATTERNS.includes(pattern)) invalidValue(findings, row, Section.screens, `Page pattern must be one of ${PAGE_PATTERNS.join(', ')} (got "${pattern}").`);
  }
}

function checkHandEditRows(rows, findings) {
  for (const row of rows) {
    const number = row.cells[HandEditColumn.number];
    if (!HAND_EDIT_NUMBER.test(number)) invalidValue(findings, row, Section.handEditLog, `"#" must be a number (got "${number}").`);
  }
  if (rows.length > MAX_HAND_EDITS) {
    finding(findings, { ruleId: RuleId.handEditsTooMany, line: rows[MAX_HAND_EDITS].line, severity: Severity.warning, message: `${rows.length} hand edits after approval; the pipeline target is at most ${MAX_HAND_EDITS}.` });
  }
}

/** A section that declares nothing ("None." or "None: reason"). */
function declaresNothing(section) {
  const lines = contentLines(section);
  return lines.length === 1 && NONE_CONTENT.test(lines[0].text.trim());
}

/** Checks the skill plan; returns the planned skills, or null when there is no plan to compare with. */
function checkSkillPlan(section, findings) {
  const items = orderedListItems(section);
  if (items.length === 0) {
    finding(findings, { ruleId: RuleId.skillPlanEmpty, line: section.line, message: `"${Section.skillPlan}" must be an ordered list; each item starts with one of: ${PLAN_SKILLS.join(', ')}.` });
    return null;
  }
  const skills = [];
  for (const item of items) {
    const skill = PLAN_ITEM_SKILL.exec(item.text)?.[1] ?? '';
    if (!PLAN_SKILLS.includes(skill)) {
      finding(findings, { ruleId: RuleId.skillPlanUnknownSkill, line: item.line, message: `Skill plan item must start with one of ${PLAN_SKILLS.join(', ')} (got "${skill || item.text}").` });
      continue;
    }
    skills.push({ skill, line: item.line });
  }
  const firstOther = skills.findIndex((entry) => entry.skill !== PlanSkill.newModule);
  const lateModule = skills.find((entry, index) => entry.skill === PlanSkill.newModule && firstOther >= 0 && index > firstOther);
  if (lateModule) {
    finding(findings, { ruleId: RuleId.skillPlanModuleFirst, line: lateModule.line, message: 'new-module items come first: the other skills edit the module it creates.' });
  }
  return skills.map((entry) => entry.skill);
}

function checkCoverage({ plannedSkills, transitionRows, endpointRows, screenRows, deadlines, line, findings }) {
  const needs = [
    [transitionRows.length > 0, PlanSkill.newTransition, Section.transitions],
    [endpointRows.length > 0, PlanSkill.newEndpoint, Section.endpoints],
    [screenRows.length > 0, PlanSkill.newScreen, Section.screens],
    [deadlines !== undefined && !declaresNothing(deadlines), PlanSkill.deadlineRecipe, Section.deadlines],
  ];
  for (const [needed, skill, sectionName] of needs) {
    if (needed && !plannedSkills.includes(skill)) {
      finding(findings, { ruleId: RuleId.skillPlanCoverage, line, severity: Severity.warning, message: `"${sectionName}" declares work but the skill plan has no ${skill} item (fine only when everything listed already exists).` });
    }
  }
}

function checkReviewLog(section, findings) {
  if (!section.lines.some((entry) => ITERATIONS_LINE.test(entry.text))) {
    finding(findings, { ruleId: RuleId.reviewIterationsMissing, line: section.line, message: `"${Section.reviewLog}" must record the review iteration count as a line "Iterations: <n>" (0 before the first review).` });
  }
}

function checkBody(sections, findings) {
  const rowsOf = (name, checkRows) => {
    const section = sections.get(name);
    if (!section) return [];
    const rows = checkTable(section, findings);
    checkRows(rows, findings);
    return rows;
  };
  const transitionRows = rowsOf(Section.transitions, checkTransitionRows);
  const endpointRows = rowsOf(Section.endpoints, checkEndpointRows);
  const screenRows = rowsOf(Section.screens, checkScreenRows);
  const handEditRows = rowsOf(Section.handEditLog, checkHandEditRows);
  const plan = sections.get(Section.skillPlan);
  if (plan) {
    const plannedSkills = checkSkillPlan(plan, findings);
    if (plannedSkills) checkCoverage({ plannedSkills, transitionRows, endpointRows, screenRows, deadlines: sections.get(Section.deadlines), line: plan.line, findings });
  }
  const review = sections.get(Section.reviewLog);
  if (review) checkReviewLog(review, findings);
  return handEditRows.length;
}

/**
 * Validates a feature spec (docs/features/<slug>.md).
 * @param {string} text the spec's Markdown
 * @param {{ requireApproved?: boolean, allowPreApproval?: boolean, agentNames?: string[] }} [options]
 *   requireApproved: the build gate (status must be approved). allowPreApproval: accept the pipeline-proof approver
 *   (the CLI sets it from the environment Invoke-PipelineProof.ps1 gives its run). agentNames: names that are never an
 *   approver (the CLI passes the repository's agent names).
 * @returns {{ findings: { ruleId: string, severity: string, message: string, line: number|null }[],
 *             status: string|null, handEditCount: number, ok: boolean }}
 *   ok is true when there is no error-severity finding; status is null when missing or unknown.
 */
export function checkSpec(text, { requireApproved = false, allowPreApproval = false, agentNames = [] } = {}) {
  const findings = [];
  const lines = splitLines(text);
  const frontMatter = parseFrontMatter(lines);
  const status = checkFrontMatter(frontMatter, findings, { requireApproved, allowPreApproval, agentNames });
  const sections = checkSectionLayout(parseSections(lines, frontMatter.bodyStart), findings);
  const handEditCount = checkBody(sections, findings);
  const ok = !findings.some((item) => item.severity === Severity.error);
  return { findings, status, handEditCount, ok };
}
