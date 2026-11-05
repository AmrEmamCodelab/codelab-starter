// Phase 2 dogfood: the REAL rules/rules.compiled.json of this repository, run through pre-tool-use.mjs against
// frontend feature files (build plan exit criterion "Rules dogfood"). The fixture project lives in the OS temp dir.
import { after, before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {
  HOOKS_DIR,
  createFixtureProject,
  decisionOf,
  reasonOf,
  removeProject,
  runHookScript,
  toolInput,
  writeFiles,
} from './helpers/fixture-project.mjs';

const SCRIPT = 'pre-tool-use.mjs';
const DENY = 'deny';
const REPO_COMPILED_RULES = path.resolve(HOOKS_DIR, '..', '..', 'rules', 'rules.compiled.json');
const HTTP_RULE = /\[base\.fe-http-via-api-service\]/;
const HTTP_FIX = /Fix: Inject ApiService \(or the generated API client\) from core\/api/;

const APP = 'frontend/starter-web/src/app';
const FEATURE = `${APP}/features/orders`;
const COMPONENT = `${FEATURE}/x.component.ts`;

const CLEAN_COMPONENT = [
  "import { Component } from '@angular/core';",
  '',
  "@Component({ selector: 'app-x', template: '' })",
  'export class XComponent {}',
  '',
].join('\n');

const HTTP_COMPONENT = [
  "import { Component, inject } from '@angular/core';",
  "import { HttpClient } from '@angular/common/http';",
  '',
  "@Component({ selector: 'app-x', template: '' })",
  'export class XComponent {',
  '  private readonly http = inject(HttpClient);',
  '}',
  '',
].join('\n');

const SHARED_IMPORT_COMPONENT = [
  "import { Component } from '@angular/core';",
  "import { StatusLabel } from '../../shared/status-label/status-label';",
  '',
  "@Component({ selector: 'app-x', template: '', imports: [StatusLabel] })",
  'export class XComponent {}',
  '',
].join('\n');

const FEATURE_FILES = Object.freeze({
  [`${FEATURE}/module.rules.yaml`]: 'module: Orders\n',
  [`${FEATURE}/MODULE.md`]: '# Orders\n\n## Purpose\nFixture feature.\n\n## Use\n-\n\n## Extend\n-\n\n## Rules\n-\n\n## Gotchas\n-\n',
  [COMPONENT]: CLEAN_COMPONENT,
  [`${APP}/shared/status-label/status-label.ts`]: 'export class StatusLabel {}\n',
});

const write = (root, relativePath, content) =>
  toolInput({ projectRoot: root, toolName: 'Write', toolInputFields: { file_path: `${root}/${relativePath}`, content } });

const edit = (root, relativePath, oldString, newString) =>
  toolInput({
    projectRoot: root,
    toolName: 'Edit',
    toolInputFields: { file_path: `${root}/${relativePath}`, old_string: oldString, new_string: newString },
  });

describe('pre-tool-use dogfood: frontend feature rules from the real compiled rules', () => {
  let root;
  before(() => {
    root = createFixtureProject({ files: FEATURE_FILES, commit: true });
    writeFiles(root, { 'rules/rules.compiled.json': fs.readFileSync(REPO_COMPILED_RULES, 'utf8') });
  });
  after(() => removeProject(root));

  it('refuses a Write adding HttpClient to a feature component with the base rule fix', () => {
    const { status, output } = runHookScript(SCRIPT, write(root, COMPONENT, HTTP_COMPONENT), { projectRoot: root });
    assert.equal(status, 0);
    assert.equal(decisionOf(output), DENY);
    assert.match(reasonOf(output), HTTP_RULE);
    assert.match(reasonOf(output), HTTP_FIX);
    assert.doesNotMatch(reasonOf(output), /hooks\.module-without-rules/);
  });

  it('refuses an Edit whose resulting feature component uses HttpClient', () => {
    const input = edit(root, COMPONENT, 'export class XComponent {}', 'export class XComponent {\n  constructor(private readonly http: HttpClient) {}\n}');
    const { output } = runHookScript(SCRIPT, input, { projectRoot: root });
    assert.equal(decisionOf(output), DENY);
    assert.match(reasonOf(output), HTTP_RULE);
    assert.match(reasonOf(output), HTTP_FIX);
  });

  it('refuses fetch in a new feature file', () => {
    const content = 'export async function load(url: string): Promise<Response> {\n  return fetch(url);\n}\n';
    const { output } = runHookScript(SCRIPT, write(root, `${FEATURE}/loader.ts`, content), { projectRoot: root });
    assert.equal(decisionOf(output), DENY);
    assert.match(reasonOf(output), HTTP_RULE);
  });

  it('allows a feature component importing shared/', () => {
    const { status, output } = runHookScript(SCRIPT, write(root, COMPONENT, SHARED_IMPORT_COMPONENT), { projectRoot: root });
    assert.equal(status, 0);
    assert.equal(decisionOf(output), null);
  });

  it('allows HttpClient inside core/api (ApiService)', () => {
    const { output } = runHookScript(SCRIPT, write(root, `${APP}/core/api/api.service.ts`, HTTP_COMPONENT), { projectRoot: root });
    assert.equal(decisionOf(output), null);
  });

  it('allows HttpClient in a feature spec file (base rule excludes *.spec.ts)', () => {
    const { output } = runHookScript(SCRIPT, write(root, `${FEATURE}/x.component.spec.ts`, HTTP_COMPONENT), { projectRoot: root });
    assert.equal(decisionOf(output), null);
  });
});
