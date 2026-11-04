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

  test('fails when rules.compiled.json is missing', () => {
    const empty = createWorkspace();
    try {
      assert.equal(runScript(empty).status, EXIT_VIOLATIONS);
    } finally {
      removeWorkspace(empty);
    }
  });
});
