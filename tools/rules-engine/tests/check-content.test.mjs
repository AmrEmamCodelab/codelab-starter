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

  test('fails when rules.compiled.json is missing', () => {
    const empty = createWorkspace();
    try {
      assert.equal(runScript(empty).status, EXIT_VIOLATIONS);
    } finally {
      removeWorkspace(empty);
    }
  });
});
