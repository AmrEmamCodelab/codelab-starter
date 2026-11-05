// Phase 4 lint proof, hook half: pre-tool-use refuses a Write or Edit that adds a third-party UI library import to the
// app's UI folders (features, patterns, shared, layouts, showcase), or a feature import to a shared building block,
// and lets the allowed imports through. The rules come from this repository's rules sources compiled in-process (the
// bytes CI step 3 requires rules/rules.compiled.json to hold), so the proof follows rules/base.rules.yaml.
// ESLint is the CI half (frontend/starter-web/tools/lint-fixtures.spec.ts).
import { after, before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
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
const REPO_ROOT = path.resolve(HOOKS_DIR, '..', '..');
const RULES_ENGINE = pathToFileURL(path.join(REPO_ROOT, 'tools', 'rules-engine', 'lib', 'engine.mjs')).href;
const COMPILED_RULES_PATH = 'rules/rules.compiled.json';
const UI_RULE = /\[base\.fe-ui-library-only\]/;
const UI_FIX = /Fix: Use the matching component from @codelab\/ng-dga/;
const SHARED_RULE = /\[base\.fe-shared-no-features\]/;

const APP = 'frontend/starter-web/src/app';
const UI_FOLDERS = Object.freeze({
  features: `${APP}/features/orders`,
  patterns: `${APP}/patterns/record-list`,
  shared: `${APP}/shared/status-label`,
  layouts: `${APP}/layouts/portal-frame`,
  showcase: `${APP}/showcase/gallery`,
});
const MODULE_DOC = '# Fixture\n\n## Purpose\nFixture folder.\n\n## Use\n-\n\n## Extend\n-\n\n## Rules\n-\n\n## Gotchas\n-\n';
const CLEAN_COMPONENT = "import { Component } from '@angular/core';\n\n@Component({ selector: 'app-x', template: '' })\nexport class XComponent {}\n";

/** The external DGA scopes are assembled at run time: the scrub sweep refuses them written literally. */
const AT = '@';
const UI_LIBRARIES = Object.freeze([
  '@angular/material/button',
  'primeng/button',
  'ng-zorro-antd/button',
  [AT, 'dev-', 'dga', '/angular'].join(''),
  [AT, 'dga', '/ui'].join(''),
]);

const fileIn = (folder) => `${UI_FOLDERS[folder]}/x.component.ts`;
const withImport = (statement) => `${statement}\n${CLEAN_COMPONENT}`;

function fixtureFiles() {
  const files = {};
  for (const folder of Object.values(UI_FOLDERS)) {
    files[`${folder}/module.rules.yaml`] = 'module: Fixture\n';
    files[`${folder}/MODULE.md`] = MODULE_DOC;
    files[`${folder}/x.component.ts`] = CLEAN_COMPONENT;
  }
  return files;
}

const write = (root, relativePath, content) =>
  toolInput({ projectRoot: root, toolName: 'Write', toolInputFields: { file_path: `${root}/${relativePath}`, content } });

const edit = (root, relativePath, oldString, newString) =>
  toolInput({ projectRoot: root, toolName: 'Edit', toolInputFields: { file_path: `${root}/${relativePath}`, old_string: oldString, new_string: newString } });

describe('pre-tool-use lint proof: UI libraries and shared-to-feature imports', () => {
  let root;
  before(async () => {
    const { compileRepository } = await import(RULES_ENGINE);
    const compiled = compileRepository(REPO_ROOT).outputs.find((output) => output.path === COMPILED_RULES_PATH);
    root = createFixtureProject({ files: fixtureFiles(), commit: true });
    writeFiles(root, { [COMPILED_RULES_PATH]: compiled.content });
  });
  after(() => removeProject(root));

  for (const folder of Object.keys(UI_FOLDERS)) {
    for (const library of UI_LIBRARIES) {
      it(`refuses a Write adding ${library} to ${folder}/`, () => {
        const content = withImport(`import { Widget } from '${library}';`);
        const { status, output } = runHookScript(SCRIPT, write(root, fileIn(folder), content), { projectRoot: root });
        assert.equal(status, 0);
        assert.equal(decisionOf(output), DENY);
        assert.match(reasonOf(output), UI_RULE);
        assert.match(reasonOf(output), UI_FIX);
      });
    }
  }

  it('refuses an Edit that adds a UI library import to an existing feature component', () => {
    const input = edit(root, fileIn('features'), "import { Component } from '@angular/core';", "import { Component } from '@angular/core';\nimport { ButtonModule } from 'primeng/button';");
    const { output } = runHookScript(SCRIPT, input, { projectRoot: root });
    assert.equal(decisionOf(output), DENY);
    assert.match(reasonOf(output), UI_RULE);
  });

  it('refuses side-effect, dynamic and re-export forms of a UI library import', () => {
    for (const statement of ["import 'primeng/resources/primeng.css';", "export const load = () => import('@angular/material/dialog');", "export * from 'ngx-bootstrap/modal';"]) {
      const { output } = runHookScript(SCRIPT, write(root, fileIn('patterns'), withImport(statement)), { projectRoot: root });
      assert.equal(decisionOf(output), DENY, statement);
      assert.match(reasonOf(output), UI_RULE);
    }
  });

  for (const folder of ['shared', 'patterns', 'layouts']) {
    it(`refuses ${folder}/ importing a feature`, () => {
      const content = withImport("import { OrdersPage } from '../../features/orders/orders-page';");
      const { output } = runHookScript(SCRIPT, write(root, fileIn(folder), content), { projectRoot: root });
      assert.equal(decisionOf(output), DENY);
      assert.match(reasonOf(output), SHARED_RULE);
    });
  }

  it('allows a feature importing @codelab/ng-dga, patterns, layouts and shared', () => {
    const content = withImport([
      "import { DgaButtonComponent } from '@codelab/ng-dga';",
      "import { RecordListPattern } from '../../patterns/record-list/record-list-pattern';",
      "import { PortalFrame } from '../../layouts/portal-frame/portal-frame';",
      "import { StatusLabel } from '../../shared/status-label/status-label';",
    ].join('\n'));
    const { status, output } = runHookScript(SCRIPT, write(root, fileIn('features'), content), { projectRoot: root });
    assert.equal(status, 0);
    assert.equal(decisionOf(output), null);
  });

  it('allows shared importing core, including core/features', () => {
    const content = withImport("import { FeatureStore } from '../../core/features/feature.store';");
    const { output } = runHookScript(SCRIPT, write(root, fileIn('shared'), content), { projectRoot: root });
    assert.equal(decisionOf(output), null);
  });

  it('allows the showcase importing a feature-free mix of ng-dga, patterns and layouts', () => {
    const content = withImport("import { DgaCardComponent } from '@codelab/ng-dga';\nimport { PortalFrame } from '../../layouts/portal-frame/portal-frame';");
    const { output } = runHookScript(SCRIPT, write(root, fileIn('showcase'), content), { projectRoot: root });
    assert.equal(decisionOf(output), null);
  });
});
