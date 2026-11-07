// Phase 9 automatic routing: the REAL routing-table.json through user-prompt-submit.mjs. A plain business scenario
// (no slash command, no keyword) still lands on build-feature, the default route; the table stays in sync with the
// routing table in the root CLAUDE.md.
import { after, before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createFixtureProject, HOOKS_DIR, removeProject, runHookScript, writeFiles } from './helpers/fixture-project.mjs';
import { formatRoutingContext, loadRoutingTable, matchRoutes, pickRoutes } from '../lib/routing.mjs';
import { DefaultRouteSkill, RepoPaths } from '../lib/constants.mjs';

const REPO_ROOT = path.resolve(HOOKS_DIR, '..', '..');
const ROOT_CLAUDE_MD = path.join(REPO_ROOT, 'CLAUDE.md');
const BUILD_FEATURE = 'build-feature';
const READY = 'ready';
const ROUTING_SECTION = /## Routing: intent → skill\n([\s\S]*?)\n## /;
const TABLE_ROW = /^\|(.+)\|(.+)\|(.+)\|$/;
const BACKTICKED = /`([a-z0-9-]+)`/g;
const RECIPE_LINK = /\]\((docs\/recipes\/[^)]+\.md)\)/;
const HEADER_OR_RULE = /^\|\s*(?:When the work is|---)/;

const { routes, defaultRoute } = loadRoutingTable(REPO_ROOT);

function promptContext(root, prompt) {
  const { status, output } = runHookScript('user-prompt-submit.mjs', {
    session_id: 's', cwd: root, hook_event_name: 'UserPromptSubmit', prompt,
  }, { projectRoot: root });
  assert.equal(status, 0);
  return output.hookSpecificOutput.additionalContext;
}

/** [{ skills: [...backticked names in the Skill column], recipe, status }] for each row of the CLAUDE.md table. */
function claudeMdRows() {
  const section = fs.readFileSync(ROOT_CLAUDE_MD, 'utf8').replace(/\r\n/g, '\n').match(ROUTING_SECTION)?.[1] ?? '';
  return section.split('\n')
    .filter((line) => TABLE_ROW.test(line) && !HEADER_OR_RULE.test(line))
    .map((line) => {
      const [, , skillCell, statusCell] = line.match(TABLE_ROW);
      return {
        skills: [...skillCell.matchAll(BACKTICKED)].map((match) => match[1]),
        recipe: skillCell.match(RECIPE_LINK)?.[1] ?? null,
        status: statusCell.trim(),
      };
    });
}

describe('routing table: plain scenarios reach build-feature', () => {
  let root;
  before(() => {
    root = createFixtureProject();
    writeFiles(root, { [RepoPaths.RoutingTable]: fs.readFileSync(path.join(REPO_ROOT, RepoPaths.RoutingTable), 'utf8') });
  });
  after(() => removeProject(root));

  it('the table declares build-feature as its default route', () => {
    assert.equal(defaultRoute.skill, BUILD_FEATURE);
    assert.equal(DefaultRouteSkill, BUILD_FEATURE);
  });

  it('a vague scenario with no keyword gets the default route build-feature', () => {
    const context = promptContext(root, 'Farmers register their farms and an officer checks the papers before the season starts');
    assert.match(context, /routing table/);
    assert.match(context, /No keyword matched: default route build-feature/);
    assert.doesNotMatch(context, /Likely match/);
  });

  it('the phase 9 routing-proof scenario starts on build-feature first', () => {
    const context = promptContext(root, 'clients request a site-visit permit; the inspector approves; undecided after 5 days it escalates to the manager');
    assert.match(context, /Likely match for this request: build-feature\b/);
  });

  for (const prompt of [
    'we need citizens to file a complaint about a service',
    'here is the BRD for the new service',
    'implement the scenarios in this use-case workbook',
    'build the screen in this DGA screenshot',
    'the Figma frame for the feedback screen is ready',
    'add an approval with escalation to the manager',
  ]) {
    it(`routes "${prompt}" to build-feature first`, () => {
      assert.equal(matchRoutes(routes, prompt)[0]?.skill, BUILD_FEATURE);
    });
  }

  it('specific work still names the specific skill or recipe', () => {
    assert.deepEqual(matchRoutes(routes, 'add a dropdown lookup of document kinds').map((route) => route.skill), ['new-lookup']);
    assert.ok(matchRoutes(routes, 'requests become overdue after an SLA').some((route) => route.recipe === 'docs/recipes/add-deadline.md'));
    assert.ok(matchRoutes(routes, 'test UC016 end to end').some((route) => route.skill === 'usecase-testing'));
    assert.ok(matchRoutes(routes, 'turn this PRD into an implementation plan').some((route) => route.skill === 'prd-to-plan'));
    assert.ok(matchRoutes(routes, 'write the PRD and plan for exports').some((route) => route.skill === 'prd-and-plan'));
    assert.ok(matchRoutes(routes, 'this class has too many comments').some((route) => route.skill === 'reduce-comments'));
  });

  it('pickRoutes uses the default only when nothing matched', () => {
    assert.equal(pickRoutes({ routes, defaultRoute, prompt: 'just do something useful' }).usedDefault, true);
    assert.equal(pickRoutes({ routes, defaultRoute, prompt: 'add a page' }).usedDefault, false);
    assert.equal(pickRoutes({ routes, defaultRoute: null, prompt: 'just do something useful' }).usedDefault, false);
  });
});

describe('routing table: formatting', () => {
  it('flags a missing recipe file and a skill without SKILL.md', () => {
    const projectRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'codelab-routing-'));
    try {
      const table = [
        { intent: 'a deadline', recipe: 'docs/recipes/add-deadline.md', keywords: ['sla'] },
        { intent: 'a page', skill: 'new-screen', keywords: ['page'] },
      ];
      const text = formatRoutingContext({ routes: table, prompt: 'x', projectRoot, defaultRoute: { skill: BUILD_FEATURE } });
      assert.match(text, /a deadline -> recipe docs\/recipes\/add-deadline\.md \(recipe not written yet\)/);
      assert.match(text, /a page -> skill new-screen \(skill not built yet\)/);
      assert.match(text, /default route build-feature/);
    } finally {
      fs.rmSync(projectRoot, { recursive: true, force: true });
    }
  });

  it('a broken table yields no routes and the built-in default', () => {
    const projectRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'codelab-routing-'));
    try {
      fs.mkdirSync(path.join(projectRoot, path.dirname(RepoPaths.RoutingTable)), { recursive: true });
      fs.writeFileSync(path.join(projectRoot, RepoPaths.RoutingTable), '{ not json');
      assert.deepEqual(loadRoutingTable(projectRoot), { routes: [], defaultRoute: { skill: DefaultRouteSkill } });
    } finally {
      fs.rmSync(projectRoot, { recursive: true, force: true });
    }
  });

  it('a bad pattern never throws', () => {
    assert.deepEqual(matchRoutes([{ skill: 'x', keywords: [], patterns: ['('] }], 'anything'), []);
  });
});

describe('routing table: in sync with the root CLAUDE.md', () => {
  const rows = claudeMdRows();

  it('every CLAUDE.md row is ready', () => {
    assert.ok(rows.length > 0, 'the routing section of CLAUDE.md has rows');
    for (const row of rows) assert.equal(row.status, READY, JSON.stringify(row));
  });

  it('the JSON table and the CLAUDE.md table list the same skills and the same recipe', () => {
    const jsonSkills = new Set(routes.filter((route) => route.skill).map((route) => route.skill));
    const mdSkills = new Set(rows.map((row) => row.skills[0]).filter(Boolean));
    assert.deepEqual([...jsonSkills].sort(), [...mdSkills].sort());
    const jsonRecipes = routes.filter((route) => route.recipe).map((route) => route.recipe);
    const mdRecipes = rows.map((row) => row.recipe).filter(Boolean);
    assert.deepEqual(jsonRecipes.sort(), mdRecipes.sort());
  });

  it('every recipe the table points to exists', () => {
    for (const route of routes.filter((entry) => entry.recipe)) {
      assert.ok(fs.existsSync(path.join(REPO_ROOT, route.recipe)), route.recipe);
    }
  });
});
