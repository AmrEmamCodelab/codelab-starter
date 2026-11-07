// Builds feature-spec Markdown for the tests: a valid spec by default, with any front-matter value or section body
// replaced or removed.
import { Section } from '../../lib/constants.mjs';

export const VALID_FRONT_MATTER = Object.freeze({
  feature: 'leave-request',
  status: 'draft',
  approvedBy: '',
  approvedOn: '',
  inputs: ['Scenario: staff request leave; the manager approves'],
});

export const APPROVED_FRONT_MATTER = Object.freeze({
  ...VALID_FRONT_MATTER,
  status: 'approved',
  approvedBy: 'Sara Al-Harbi',
  approvedOn: '2026-11-07',
});

const table = (header, rows) => [
  `| ${header.join(' | ')} |`,
  `| ${header.map(() => '---').join(' | ')} |`,
  ...rows.map((row) => `| ${row.join(' | ')} |`),
].join('\n');

export const TRANSITION_HEADER = Object.freeze(['From', 'Action', 'To', 'Permission', 'Reason required']);
export const ENDPOINT_HEADER = Object.freeze(['Method', 'Route', 'Permission', 'Request', 'Response']);
export const SCREEN_HEADER = Object.freeze(['Route', 'Page pattern', 'Permission', 'Target image']);
export const HAND_EDIT_HEADER = Object.freeze(['#', 'File', 'Reason']);

export function transitionsTable(rows = [['Draft', 'submit', 'Submitted', 'leave.create', 'no'], ['Submitted', 'reject', 'Rejected', 'leave.decide', 'yes']]) {
  return table(TRANSITION_HEADER, rows);
}

export function endpointsTable(rows = [['POST', '/api/leave', 'leave.create', 'none', '201 { id }']]) {
  return table(ENDPOINT_HEADER, rows);
}

export function screensTable(rows = [['/leave/new', 'wizard', 'leave.create', 'none']]) {
  return table(SCREEN_HEADER, rows);
}

export function handEditTable(count = 0) {
  const rows = Array.from({ length: count }, (_, index) => [String(index + 1), `backend/file${index}.cs`, 'generator gap']);
  return table(HAND_EDIT_HEADER, rows);
}

export function validSections() {
  return {
    [Section.scenario]: 'Staff request leave; the manager approves.',
    [Section.inputs]: '- The scenario sentence.',
    [Section.modules]: '- New module `Leave`.',
    [Section.transitions]: transitionsTable(),
    [Section.deadlines]: 'None.',
    [Section.permissions]: '- leave.create, leave.decide',
    [Section.endpoints]: endpointsTable(),
    [Section.screens]: screensTable(),
    [Section.ruleFiles]: '- The new module rules file.',
    [Section.tests]: '- Unit tests per use case.',
    [Section.docs]: '- The module MODULE.md.',
    [Section.skillPlan]: '1. new-module - Leave\n2. new-permission - create, decide\n3. new-transition - submit, reject\n4. new-endpoint - create\n5. new-screen - wizard',
    [Section.handEditLog]: handEditTable(),
    [Section.reviewLog]: 'Iterations: 0',
  };
}

function frontMatterText(values) {
  const lines = ['---'];
  for (const [key, value] of Object.entries(values)) {
    if (value === undefined) continue;
    if (Array.isArray(value)) {
      lines.push(`${key}:`);
      for (const item of value) lines.push(`  - "${item}"`);
    } else {
      lines.push(value === '' ? `${key}:` : `${key}: ${value}`);
    }
  }
  lines.push('---');
  return lines.join('\n');
}

/**
 * A spec. `frontMatter` entries override the valid ones (undefined removes a key); `sections` entries override the
 * valid bodies (undefined removes the section); `order` replaces the section order. `frontMatter: null` omits it.
 */
export function buildSpec({ frontMatter = {}, sections = {}, order = null } = {}) {
  const bodies = { ...validSections(), ...sections };
  const names = order ?? Object.keys(validSections());
  const parts = [];
  if (frontMatter !== null) parts.push(frontMatterText({ ...VALID_FRONT_MATTER, ...frontMatter }));
  parts.push('# Leave request');
  for (const name of names) {
    if (bodies[name] === undefined) continue;
    parts.push(`## ${name}\n\n${bodies[name]}`);
  }
  return `${parts.join('\n\n')}\n`;
}

/** The rule ids of the findings, optionally of one severity. */
export function ruleIds(result, severity = null) {
  return result.findings.filter((item) => severity === null || item.severity === severity).map((item) => item.ruleId);
}
