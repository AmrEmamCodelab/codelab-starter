// CI content check (build.ps1 step 9): the hooks' forbidden/required rules in absolute state over the whole tree.
import { test, describe, beforeEach, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { checkContent, EXIT_CLEAN, EXIT_VIOLATIONS } from '../check-content.mjs';
import { compileRepository, writeOutputs } from '../lib/engine.mjs';
import { ENGINE_DIR, createWorkspace, removeWorkspace, writeWorkspaceFile } from './helpers/workspace.mjs';

const SCRIPT = path.join(ENGINE_DIR, 'check-content.mjs');
const DEMO_FILE = 'backend/src/Modules/Demo/Application/Bad.cs';

function runScript(root) {
  return spawnSync(process.execPath, [SCRIPT, '--root', root], { encoding: 'utf8' });
}

describe('check-content', () => {
  let root;
  beforeEach(() => {
    root = createWorkspace();
    writeOutputs(root, compileRepository(root).outputs);
  });
  afterEach(() => removeWorkspace(root));

  test('a clean tree passes', () => {
    writeWorkspaceFile(root, 'backend/src/Starter.Api/Clean.cs', 'class Clean { }\n');
    assert.equal(runScript(root).status, EXIT_CLEAN);
  });

  test('regression: violations a hook bypass lets through fail CI (empty catch, role string, DateTime.Now)', () => {
    writeWorkspaceFile(root, DEMO_FILE, [
      'class Bad {',
      '  void A() { try { } catch { } }',
      '  bool B(System.Security.Claims.ClaimsPrincipal u) => u.IsInRole("Admin");',
      '  object C() => System.DateTime.Now;',
      '}',
    ].join('\n'));
    const ids = checkContent(root).violations.map((item) => item.rule.id).sort();
    assert.deepEqual(ids, ['base.no-empty-catch', 'base.no-string-role-check', 'base.time-provider']);
    const result = runScript(root);
    assert.equal(result.status, EXIT_VIOLATIONS);
    assert.match(result.stderr, /Bad\.cs: \[base\.time-provider\]/);
  });

  test('warning-severity rules are reported but do not fail', () => {
    writeWorkspaceFile(root, 'backend/src/Starter.Api/Old.cs', 'class Old {\n  // return x;\n}\n');
    const result = runScript(root);
    assert.equal(result.status, EXIT_CLEAN);
    assert.match(result.stderr, /warning .*base\.no-commented-out-code/);
  });

  test('regression: a suppression of any analyzer rule, inline or in a nested .editorconfig or csproj, fails CI', () => {
    writeWorkspaceFile(root, 'backend/src/Modules/Demo/Application/A.cs', [
      '#pragma warning disable CA1822, CA2000',
      '[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000")]',
      'sealed class A { }',
    ].join('\n'));
    writeWorkspaceFile(root, 'backend/src/Modules/Demo/Application/.editorconfig', '[*.cs]\ndotnet_diagnostic.CA2000.severity = none\n');
    writeWorkspaceFile(root, 'backend/src/Modules/Demo/Starter.Modules.Demo.csproj', '<Project><PropertyGroup><NoWarn>$(NoWarn);CA1822;CA2000</NoWarn></PropertyGroup></Project>\n');
    const found = checkContent(root).violations.map((item) => `${item.path}:${item.rule.id}`).sort();
    assert.deepEqual(found, [
      'backend/src/Modules/Demo/Application/.editorconfig:base.no-hand-written-editorconfig',
      'backend/src/Modules/Demo/Application/.editorconfig:base.no-inline-suppression',
      'backend/src/Modules/Demo/Application/A.cs:base.no-inline-suppression',
      'backend/src/Modules/Demo/Starter.Modules.Demo.csproj:base.no-inline-suppression',
    ]);
    assert.equal(runScript(root).status, EXIT_VIOLATIONS);
  });

  test('regression: a spaced pragma, an aliased SuppressMessage, a ruleset and an extra analyzer config fail CI', () => {
    writeWorkspaceFile(root, 'backend/src/Modules/Demo/Application/B.cs', [
      'using SM = System.Diagnostics.CodeAnalysis.SuppressMessageAttribute;',
      '# pragma warning disable CA1822, CA2000',
      'sealed class B { }',
    ].join('\n'));
    writeWorkspaceFile(root, 'backend/src/Modules/Demo/Starter.Modules.Demo.csproj', [
      '<Project><PropertyGroup><CodeAnalysisRuleSet>off.ruleset</CodeAnalysisRuleSet></PropertyGroup>',
      '<ItemGroup><GlobalAnalyzerConfigFiles Include="analysis.cfg" /></ItemGroup></Project>',
    ].join('\n'));
    writeWorkspaceFile(root, 'backend/src/Modules/Demo/off.ruleset', '<RuleSet Name="off"><Rules AnalyzerId="X" RuleNamespace="X"><Rule Id="CA2000" Action="None" /></Rules></RuleSet>\n');
    const found = checkContent(root).violations.map((item) => `${item.path}:${item.rule.id}`).sort();
    assert.deepEqual(found, [
      'backend/src/Modules/Demo/Application/B.cs:base.no-inline-suppression',
      'backend/src/Modules/Demo/Starter.Modules.Demo.csproj:base.no-analyzer-config-redirect',
      'backend/src/Modules/Demo/off.ruleset:base.no-ruleset-file',
    ]);
    assert.equal(runScript(root).status, EXIT_VIOLATIONS);
  });

  test('regression: an eslint, stylelint or TypeScript suppression comment in the frontend fails CI', () => {
    const feature = 'frontend/starter-web/src/app/features/dashboard';
    writeWorkspaceFile(root, `${feature}/a.ts`, '/* eslint-disable */\nexport const a = 1;\n');
    writeWorkspaceFile(root, `${feature}/b.ts`, '// @ts-ignore\nexport const b = 1;\n');
    writeWorkspaceFile(root, `${feature}/c.scss`, '/* stylelint-disable */\n');
    const found = checkContent(root).violations.filter((item) => item.rule.id === 'base.no-lint-suppression').map((item) => item.path).sort();
    assert.deepEqual(found, [`${feature}/a.ts`, `${feature}/b.ts`, `${feature}/c.scss`]);
    assert.equal(runScript(root).status, EXIT_VIOLATIONS);
  });

  test('a clean frontend file and a spec using a suppression comment do not trip base.no-lint-suppression', () => {
    const feature = 'frontend/starter-web/src/app/features/dashboard';
    writeWorkspaceFile(root, `${feature}/clean.ts`, 'export const clean = true;\n');
    writeWorkspaceFile(root, `${feature}/clean.spec.ts`, '// @ts-ignore\nexport const s = true;\n');
    const found = checkContent(root).violations.filter((item) => item.rule.id === 'base.no-lint-suppression');
    assert.deepEqual(found, []);
  });

  test('regression: raw text in an .html template or an inline component template fails CI', () => {
    const feature = 'frontend/starter-web/src/app/features/dashboard';
    writeWorkspaceFile(root, `${feature}/x.component.html`, '<section><h1>Order list</h1><button>Delete all</button></section>\n');
    writeWorkspaceFile(root, `${feature}/y.component.ts`, "@Component({ selector: 'app-y', template: '<h1>Delete all</h1>' })\nexport class Y {}\n");
    const ids = checkContent(root).violations.map((item) => item.rule.id).sort();
    assert.deepEqual(ids, ['base.fe-raw-text-in-inline-template', 'base.fe-raw-text-in-template']);
    assert.equal(runScript(root).status, EXIT_VIOLATIONS);
  });

  test('regression: a UI kit pulled in by a stylesheet, package.json or angular.json fails CI with no TS import', () => {
    const web = 'frontend/starter-web';
    writeWorkspaceFile(root, `${web}/src/app/shared/zz-probe.component.scss`, [
      "@use '@angular/material' as mat;",
      "@import 'primeng/resources/primeng.min.css';",
      "@import 'bootstrap/scss/bootstrap';",
    ].join('\n'));
    writeWorkspaceFile(root, `${web}/package.json`, '{ "dependencies": { "bootstrap": "^5.3.3", "bootstrap-icons": "^1.11.3" } }\n');
    writeWorkspaceFile(root, `${web}/angular.json`, '{ "styles": ["node_modules/primeng/resources/primeng.min.css", "src/styles.scss"] }\n');
    const found = checkContent(root).violations.map((item) => `${item.path}:${item.rule.id}`).sort();
    assert.deepEqual(found, [
      `${web}/angular.json:base.fe-ui-library-styles-and-deps`,
      `${web}/package.json:base.fe-ui-library-styles-and-deps`,
      `${web}/src/app/shared/zz-probe.component.scss:base.fe-ui-library-styles-and-deps`,
    ]);
    assert.equal(runScript(root).status, EXIT_VIOLATIONS);
  });

  test('regression: a UI kit pulled in by Sass meta.load-css, an npm alias or a git/tarball source fails CI', () => {
    const web = 'frontend/starter-web';
    writeWorkspaceFile(root, `${web}/src/app/shared/a.component.scss`, "@use 'sass:meta';\n@include meta.load-css('bootstrap/scss/bootstrap');\n");
    writeWorkspaceFile(root, `${web}/src/app/shared/b.component.scss`, "@use 'sass:meta' as m;\n.x { @include m.load-css(\"primeng/resources/primeng.min.css\"); }\n");
    writeWorkspaceFile(root, `${web}/package.json`, '{ "dependencies": { "ui-kit": "npm:bootstrap@5.3.3" } }\n');
    writeWorkspaceFile(root, `${web}/projects/p1/package.json`, '{ "dependencies": { "mat": "npm:@angular/material@20.0.0" } }\n');
    writeWorkspaceFile(root, `${web}/projects/p2/package.json`, '{ "dependencies": { "kit": "github:twbs/bootstrap#v5.3.3" } }\n');
    writeWorkspaceFile(root, `${web}/projects/p3/package.json`, '{ "dependencies": { "kit": "git+https://github.com/twbs/bootstrap.git" } }\n');
    writeWorkspaceFile(root, `${web}/projects/p4/package.json`, '{ "dependencies": { "kit": "https://registry.npmjs.org/primeng/-/primeng-20.0.0.tgz" } }\n');
    const found = checkContent(root).violations.map((item) => `${item.path}:${item.rule.id}`).sort();
    assert.deepEqual(found, [
      `${web}/package.json:base.fe-ui-library-styles-and-deps`,
      `${web}/projects/p1/package.json:base.fe-ui-library-styles-and-deps`,
      `${web}/projects/p2/package.json:base.fe-ui-library-styles-and-deps`,
      `${web}/projects/p3/package.json:base.fe-ui-library-styles-and-deps`,
      `${web}/projects/p4/package.json:base.fe-ui-library-styles-and-deps`,
      `${web}/src/app/shared/a.component.scss:base.fe-ui-library-styles-and-deps`,
      `${web}/src/app/shared/b.component.scss:base.fe-ui-library-styles-and-deps`,
    ]);
    assert.equal(runScript(root).status, EXIT_VIOLATIONS);
  });

  test('regression: a stylesheet imported from a remote URL (CDN) fails CI whatever it names', () => {
    const shared = 'frontend/starter-web/src/app/shared';
    writeWorkspaceFile(root, `${shared}/a.component.css`, '@import url("https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css");\n');
    writeWorkspaceFile(root, `${shared}/b.component.scss`, "@import 'https://cdnjs.cloudflare.com/ajax/libs/twitter-bootstrap/5.3.3/css/bootstrap.min.css';\n");
    writeWorkspaceFile(root, `${shared}/c.component.css`, '@import url(//unpkg.com/some-kit/dist/kit.css);\n');
    const found = checkContent(root).violations
      .filter((item) => item.rule.id === 'base.fe-no-remote-stylesheet')
      .map((item) => item.path)
      .sort();
    assert.deepEqual(found, [`${shared}/a.component.css`, `${shared}/b.component.scss`, `${shared}/c.component.css`]);
    assert.equal(runScript(root).status, EXIT_VIOLATIONS);
  });

  test('local stylesheets, local load-css and non-kit aliases stay allowed', () => {
    const web = 'frontend/starter-web';
    writeWorkspaceFile(root, `${web}/src/app/shared/ok.component.scss`, "@use 'sass:meta';\n@use '../tokens' as t;\n@include meta.load-css('theme/dark');\n.x { background: url('/assets/bg.png'); }\n");
    writeWorkspaceFile(root, `${web}/package.json`, '{ "dependencies": { "icons": "npm:bootstrap-icons@1.11.3", "cdk": "npm:@angular/cdk@22.0.0" }, "scripts": { "build": "ng build" } }\n');
    writeWorkspaceFile(root, `${web}/angular.json`, '{ "styles": ["node_modules/bootstrap-icons/font/bootstrap-icons.css", "src/styles.scss"] }\n');
    const found = checkContent(root).violations.map((item) => item.rule.id);
    assert.deepEqual(found, []);
  });

  test('bootstrap-icons, @angular/cdk and @codelab/ng-dga in styles and dependencies stay allowed', () => {
    const web = 'frontend/starter-web';
    writeWorkspaceFile(root, `${web}/src/styles.scss`, "@use '@codelab/ng-dga/styles' as dga;\n@import 'bootstrap-icons/font/bootstrap-icons.css';\n");
    writeWorkspaceFile(root, `${web}/package.json`, '{ "dependencies": { "@angular/cdk": "^22.0.0", "@codelab/ng-dga": "0.1.0", "bootstrap-icons": "^1.11.3" } }\n');
    const found = checkContent(root).violations.map((item) => item.rule.id);
    assert.deepEqual(found, []);
  });

  test('fails when rules.compiled.json is missing', () => {
    const empty = createWorkspace();
    try {
      assert.equal(runScript(empty).status, EXIT_VIOLATIONS);
    } finally {
      removeWorkspace(empty);
    }
  });
});
