import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { checkSpec } from '../lib/check-spec.mjs';
import { MAX_HAND_EDITS, PRE_APPROVED_BY, REQUIRED_SECTIONS, RuleId, Section, Severity, SpecStatus } from '../lib/constants.mjs';
import {
  APPROVED_FRONT_MATTER,
  buildSpec,
  endpointsTable,
  handEditTable,
  ruleIds,
  screensTable,
  transitionsTable,
} from './helpers/spec-builder.mjs';

describe('checkSpec: a valid spec', () => {
  it('has no findings, reports the status and zero hand edits', () => {
    const result = checkSpec(buildSpec());
    assert.deepEqual(result.findings, []);
    assert.equal(result.ok, true);
    assert.equal(result.status, SpecStatus.draft);
    assert.equal(result.handEditCount, 0);
  });

  it('accepts CRLF line endings', () => {
    const result = checkSpec(buildSpec().replaceAll('\n', '\r\n'));
    assert.deepEqual(result.findings, []);
  });

  it('passes the approval gate when approved with who and when', () => {
    const result = checkSpec(buildSpec({ frontMatter: APPROVED_FRONT_MATTER }), { requireApproved: true });
    assert.deepEqual(result.findings, []);
    assert.equal(result.status, SpecStatus.approved);
  });

  it('accepts the headless pre-approval record only inside a pipeline-proof run', () => {
    const frontMatter = { ...APPROVED_FRONT_MATTER, approvedBy: PRE_APPROVED_BY };
    assert.equal(checkSpec(buildSpec({ frontMatter }), { requireApproved: true, allowPreApproval: true }).ok, true);
    assert.equal(checkSpec(buildSpec({ frontMatter }), { requireApproved: true }).ok, false);
  });
});

describe('checkSpec: sections', () => {
  for (const name of REQUIRED_SECTIONS) {
    it(`reports the missing section "${name}"`, () => {
      const result = checkSpec(buildSpec({ sections: { [name]: undefined } }));
      assert.equal(result.ok, false);
      const missing = result.findings.filter((item) => item.ruleId === RuleId.sectionMissing);
      assert.equal(missing.length, 1);
      assert.match(missing[0].message, new RegExp(name));
    });
  }

  it('reports a section out of order', () => {
    const order = [...REQUIRED_SECTIONS];
    [order[0], order[1]] = [order[1], order[0]];
    assert.ok(ruleIds(checkSpec(buildSpec({ order }))).includes(RuleId.sectionOrder));
  });

  it('reports a duplicated section', () => {
    const spec = `${buildSpec()}\n## ${Section.docs}\n\nAgain.\n`;
    assert.ok(ruleIds(checkSpec(spec)).includes(RuleId.sectionDuplicate));
  });

  it('reports an empty section, and an HTML comment alone is empty', () => {
    const result = checkSpec(buildSpec({ sections: { [Section.tests]: '<!-- write the tests\n     here -->' } }));
    assert.deepEqual(ruleIds(result), [RuleId.sectionEmpty]);
  });

  it('warns about an unknown section without failing', () => {
    const result = checkSpec(`${buildSpec()}\n## Notes\n\nExtra.\n`);
    assert.deepEqual(ruleIds(result, Severity.warning), [RuleId.sectionUnknown]);
    assert.equal(result.ok, true);
  });

  it('ignores headings inside fenced code blocks', () => {
    const scenario = 'Example:\n\n```md\n## Inputs\n```';
    assert.deepEqual(checkSpec(buildSpec({ sections: { [Section.scenario]: scenario } })).findings, []);
  });
});

describe('checkSpec: tables', () => {
  it('reports a missing table', () => {
    const result = checkSpec(buildSpec({ sections: { [Section.endpoints]: 'Some endpoints.' } }));
    assert.deepEqual(ruleIds(result), [RuleId.tableMissing]);
  });

  it('reports wrong columns', () => {
    const bad = '| Method | Path | Permission |\n| --- | --- | --- |\n| GET | /api/x | x.view |';
    assert.deepEqual(ruleIds(checkSpec(buildSpec({ sections: { [Section.endpoints]: bad } }))), [RuleId.tableColumns]);
  });

  it('accepts column names in any letter case', () => {
    const lower = transitionsTable().replace('Reason required', 'reason Required');
    assert.deepEqual(checkSpec(buildSpec({ sections: { [Section.transitions]: lower } })).findings, []);
  });

  it('reports a row with the wrong number of cells', () => {
    const bad = transitionsTable([['Draft', 'submit', 'Submitted', 'leave.create']]);
    assert.ok(ruleIds(checkSpec(buildSpec({ sections: { [Section.transitions]: bad } }))).includes(RuleId.tableCellCount));
  });

  it('reports an empty cell', () => {
    const bad = transitionsTable([['Draft', 'submit', 'Submitted', '', 'no']]);
    assert.deepEqual(ruleIds(checkSpec(buildSpec({ sections: { [Section.transitions]: bad } }))), [RuleId.tableCellEmpty]);
  });

  it('reports a "Reason required" value other than yes or no', () => {
    const bad = transitionsTable([['Draft', 'submit', 'Submitted', 'leave.create', 'maybe']]);
    assert.deepEqual(ruleIds(checkSpec(buildSpec({ sections: { [Section.transitions]: bad } }))), [RuleId.tableValue]);
  });

  it('reports an unknown HTTP method and a route without a leading slash', () => {
    const bad = endpointsTable([['FETCH', 'api/leave', 'leave.create', 'none', '200']]);
    const result = checkSpec(buildSpec({ sections: { [Section.endpoints]: bad } }));
    assert.deepEqual(ruleIds(result), [RuleId.tableValue, RuleId.tableValue]);
  });

  it('accepts routes in inline code', () => {
    const table = endpointsTable([['GET', '`/api/leave/{id}`', 'leave.view', 'none', 'detail']]);
    assert.deepEqual(checkSpec(buildSpec({ sections: { [Section.endpoints]: table } })).findings, []);
  });

  it('reports an unknown page pattern', () => {
    const bad = screensTable([['/leave/new', 'custom-form', 'leave.create', 'none']]);
    assert.deepEqual(ruleIds(checkSpec(buildSpec({ sections: { [Section.screens]: bad } }))), [RuleId.tableValue]);
  });

  it('keeps an escaped pipe inside a cell', () => {
    const table = endpointsTable([['GET', '/api/leave', 'leave.view', 'none', 'a \\| b']]);
    assert.deepEqual(checkSpec(buildSpec({ sections: { [Section.endpoints]: table } })).findings, []);
  });
});

describe('checkSpec: front matter and approval', () => {
  it('reports a missing front matter', () => {
    assert.ok(ruleIds(checkSpec(buildSpec({ frontMatter: null }))).includes(RuleId.frontMatterMissing));
  });

  it('reports each missing key', () => {
    const result = checkSpec(buildSpec({ frontMatter: { approvedOn: undefined, inputs: undefined } }));
    assert.deepEqual(ruleIds(result), [RuleId.frontMatterKeyMissing, RuleId.frontMatterKeyMissing]);
  });

  it('reports an unparseable front-matter line', () => {
    const spec = buildSpec().replace('status: draft', 'status: draft\nnot a key value line');
    assert.ok(ruleIds(checkSpec(spec)).includes(RuleId.frontMatterSyntax));
  });

  it('warns about an unknown key', () => {
    assert.deepEqual(ruleIds(checkSpec(buildSpec({ frontMatter: { owner: 'x' } })), Severity.warning), [RuleId.frontMatterUnknownKey]);
  });

  it('reports a feature that is not a kebab-case slug', () => {
    assert.deepEqual(ruleIds(checkSpec(buildSpec({ frontMatter: { feature: 'Leave Request' } }))), [RuleId.featureSlug]);
  });

  it('reports empty inputs, in block and flow form', () => {
    assert.deepEqual(ruleIds(checkSpec(buildSpec({ frontMatter: { inputs: [] } }))), [RuleId.inputsInvalid]);
    const flow = buildSpec().replace(/inputs:\n {2}- .*\n/, 'inputs: []\n');
    assert.deepEqual(ruleIds(checkSpec(flow)), [RuleId.inputsInvalid]);
  });

  it('accepts a flow list of inputs', () => {
    const flow = buildSpec().replace(/inputs:\n {2}- .*\n/, 'inputs: [docs/brd.md, "the scenario"]\n');
    assert.deepEqual(checkSpec(flow).findings, []);
  });

  it('reports an unknown status', () => {
    const result = checkSpec(buildSpec({ frontMatter: { status: 'done' } }));
    assert.deepEqual(ruleIds(result), [RuleId.statusInvalid]);
    assert.equal(result.status, null);
  });

  it('refuses an unapproved spec with requireApproved', () => {
    const result = checkSpec(buildSpec(), { requireApproved: true });
    assert.deepEqual(ruleIds(result), [RuleId.approvalRequired]);
    assert.equal(result.ok, false);
  });

  it('reports an approved spec without who or when', () => {
    const result = checkSpec(buildSpec({ frontMatter: { ...APPROVED_FRONT_MATTER, approvedBy: '' } }));
    assert.deepEqual(ruleIds(result), [RuleId.approvalIncomplete]);
  });

  it('refuses an approver that is an agent, a tool or the proof approver outside a proof run', () => {
    const approvedAs = (approvedBy, options = {}) =>
      ruleIds(checkSpec(buildSpec({ frontMatter: { ...APPROVED_FRONT_MATTER, approvedBy } }), { requireApproved: true, ...options }));
    assert.deepEqual(approvedAs('spec-writer agent'), [RuleId.approvalNotHuman]);
    assert.deepEqual(approvedAs('Spec-Writer', { agentNames: ['spec-writer'] }), [RuleId.approvalNotHuman]);
    assert.deepEqual(approvedAs('the pipeline'), [RuleId.approvalNotHuman]);
    assert.deepEqual(approvedAs(PRE_APPROVED_BY), [RuleId.approvalPreApproved]);
    assert.deepEqual(approvedAs(PRE_APPROVED_BY, { allowPreApproval: true }), []);
    assert.deepEqual(approvedAs('Sara Al-Harbi (platform owner)', { agentNames: ['spec-writer', 'commit-gate'] }), []);
    assert.deepEqual(approvedAs('Aisha Botros'), [], 'whole words only: names that contain "ai" or "bot" are people');
  });

  it('sees through separators, look-alike digits and role-only approvers (early warning, not the gate)', () => {
    const agentNames = ['spec-writer', 'commit-gate', 'i18n-keeper'];
    const approvedAs = (approvedBy) =>
      ruleIds(checkSpec(buildSpec({ frontMatter: { ...APPROVED_FRONT_MATTER, approvedBy } }), { requireApproved: true, agentNames }));
    for (const automated of ['Spec Writer', 'spec_writer', 'SpecWriter', 'Commit Gate', 'i18n keeper', 'C1aude', 'B0t', 'P1peline', 'Platform Owner', 'The Reviewer', 'n/a']) {
      assert.deepEqual(approvedAs(automated), [RuleId.approvalNotHuman], automated);
    }
    for (const person of ['Sara Al-Harbi (platform owner)', 'Robin Keeper', 'Aisha Botros', 'سارة الحربي', 'Lina Gates']) {
      assert.deepEqual(approvedAs(person), [], person);
    }
  });

  it('reports an approvedOn that is not a real date', () => {
    const result = checkSpec(buildSpec({ frontMatter: { ...APPROVED_FRONT_MATTER, approvedOn: '2026-02-30' } }));
    assert.deepEqual(ruleIds(result), [RuleId.approvedOnInvalid]);
  });
});

describe('checkSpec: skill plan', () => {
  it('reports an unknown skill', () => {
    const plan = '1. new-module - Leave\n2. new-widget - something';
    const result = checkSpec(buildSpec({ sections: { [Section.skillPlan]: plan } }));
    assert.ok(ruleIds(result).includes(RuleId.skillPlanUnknownSkill));
    assert.equal(result.ok, false);
  });

  it('accepts skill names in backticks or bold, and the deadline recipe', () => {
    const plan = '1. `new-module` Leave\n2. **new-permission** keys\n3. new-transition flow\n4. deadline-recipe 3 days\n5. new-endpoint x\n6. new-screen y';
    const deadlines = '| Status | Duration |\n| --- | --- |\n| Submitted | 3 days |';
    assert.deepEqual(checkSpec(buildSpec({ sections: { [Section.skillPlan]: plan, [Section.deadlines]: deadlines } })).findings, []);
  });

  it('reports a plan that is not an ordered list', () => {
    assert.deepEqual(ruleIds(checkSpec(buildSpec({ sections: { [Section.skillPlan]: '- new-module' } }))), [RuleId.skillPlanEmpty]);
  });

  it('reports new-module after another skill', () => {
    const plan = '1. new-permission - keys\n2. new-module - Leave\n3. new-transition - x\n4. new-endpoint - y\n5. new-screen - z';
    assert.deepEqual(ruleIds(checkSpec(buildSpec({ sections: { [Section.skillPlan]: plan } }))), [RuleId.skillPlanModuleFirst]);
  });

  it('warns when declared work has no matching skill', () => {
    const plan = '1. new-module - Leave';
    const result = checkSpec(buildSpec({ sections: { [Section.skillPlan]: plan, [Section.deadlines]: '3 days on Submitted.' } }));
    assert.equal(result.ok, true);
    assert.deepEqual(ruleIds(result, Severity.warning), Array(4).fill(RuleId.skillPlanCoverage));
  });
});

describe('checkSpec: hand-edit log and review log', () => {
  it('counts the hand edits', () => {
    const result = checkSpec(buildSpec({ sections: { [Section.handEditLog]: handEditTable(3) } }));
    assert.equal(result.handEditCount, 3);
    assert.deepEqual(result.findings, []);
  });

  it(`warns above ${MAX_HAND_EDITS} hand edits but stays ok`, () => {
    const result = checkSpec(buildSpec({ sections: { [Section.handEditLog]: handEditTable(MAX_HAND_EDITS + 1) } }));
    assert.equal(result.handEditCount, MAX_HAND_EDITS + 1);
    assert.deepEqual(ruleIds(result), [RuleId.handEditsTooMany]);
    assert.equal(result.findings[0].severity, Severity.warning);
    assert.equal(result.ok, true);
  });

  it(`does not warn at exactly ${MAX_HAND_EDITS}`, () => {
    assert.deepEqual(checkSpec(buildSpec({ sections: { [Section.handEditLog]: handEditTable(MAX_HAND_EDITS) } })).findings, []);
  });

  it('reports a hand-edit number that is not a number', () => {
    const table = handEditTable(1).replace('| 1 |', '| one |');
    assert.deepEqual(ruleIds(checkSpec(buildSpec({ sections: { [Section.handEditLog]: table } }))), [RuleId.tableValue]);
  });

  it('requires the iteration count in the review log', () => {
    const result = checkSpec(buildSpec({ sections: { [Section.reviewLog]: 'Round 1: two findings fixed.' } }));
    assert.deepEqual(ruleIds(result), [RuleId.reviewIterationsMissing]);
  });

  it('reads a bold or bulleted iteration line', () => {
    const log = '- standards-reviewer: 1 finding, fixed.\n- **Iterations:** 2';
    assert.deepEqual(checkSpec(buildSpec({ sections: { [Section.reviewLog]: log } })).findings, []);
  });
});
