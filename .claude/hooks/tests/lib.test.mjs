// Unit tests for the pure helpers: glob matching, edit simulation, rule evaluation, compiled-rule normalization
// and shell-write detection.
import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { matchesGlob } from '../lib/glob.mjs';
import { simulateEdit } from '../lib/edit-simulation.mjs';
import { findIntroducedViolations } from '../lib/evaluate.mjs';
import { findModuleForPath, normalizeCompiledRules, rulesForModule } from '../lib/compiled-rules.mjs';
import { detectShellWrites, tokenize } from '../lib/shell-writes.mjs';
import { parsePorcelain, planChecks } from '../lib/stop-gate.mjs';
import { formatRoutingContext, matchRoutes } from '../lib/routing.mjs';

describe('glob', () => {
  const cases = [
    ['backend/src/A.cs', '**/*.cs', true],
    ['A.cs', '**/*.cs', true],
    ['a/b/C.cs', '*.cs', true],
    ['a/b/C.ts', '*.{cs,ts}', true],
    ['rules/x/y.yaml', 'rules/**', true],
    ['rulesx/y.yaml', 'rules/**', false],
    ['docs/generated/a.md', 'docs/generated/**', true],
    ['x/Foo.Tests/A.cs', '**/*.Tests/**', true],
    ['BACKEND/A.CS', 'backend/*.cs', true],
    ['backend/a/A.cs', 'backend/*.cs', false],
  ];
  for (const [file, glob, expected] of cases) {
    it(`${glob} ${expected ? 'matches' : 'does not match'} ${file}`, () => assert.equal(matchesGlob(file, glob), expected));
  }
});

describe('simulateEdit', () => {
  it('replaces the first occurrence and keeps $ sequences literal', () => {
    assert.equal(simulateEdit({ original: 'a a', oldString: 'a', newString: '$&$1' }), '$&$1 a');
  });
  it('replaces every occurrence with replace_all', () => {
    assert.equal(simulateEdit({ original: 'a a', oldString: 'a', newString: 'b', replaceAll: true }), 'b b');
  });
  it('matches LF old_string against a CRLF file', () => {
    assert.equal(simulateEdit({ original: 'x\r\ny\r\n', oldString: 'x\ny', newString: 'z' }), 'z\n');
  });
  it('returns null when old_string is missing (the Edit tool fails by itself)', () => {
    assert.equal(simulateEdit({ original: 'abc', oldString: 'zzz', newString: 'q' }), null);
  });
});

describe('compiled rules', () => {
  const clockRule = { id: 'base.clock', regex: 'DateTime\\.Now', appliesTo: '*.cs', fixMessage: 'Use TimeProvider.', flags: 'gi' };
  const compiled = normalizeCompiledRules({
    base: {
      forbidden: [clockRule],
      required: [{ id: 'base.prose-only', statement: 'Enforced by tests, no pattern.' }],
    },
    modules: [
      { module: 'Orders', root: 'backend/src/Modules/Orders', description: 'Orders module.', forbidden: [clockRule], required: [{ id: 'orders.perm', pattern: 'HasPermission', files: ['**/*Controller.cs'] }] },
      { name: 'Legacy', path: 'backend/src/Modules/Legacy', forbidden: [], excludes: [] },
    ],
  });

  it('accepts the engine shape and the tolerated aliases', () => {
    assert.equal(compiled.baseRules[0].pattern, 'DateTime\\.Now');
    assert.deepEqual(compiled.baseRules[0].files, ['*.cs']);
    assert.equal(compiled.baseRules[0].fix, 'Use TimeProvider.');
    assert.equal(compiled.baseRules[0].flags, 'i', 'g/y are controlled by the evaluator');
    assert.equal(compiled.modules[0].name, 'Orders');
    assert.equal(compiled.modules[0].path, 'backend/src/Modules/Orders');
    assert.equal(compiled.modules[0].summary, 'Orders module.');
    assert.equal(compiled.modules[1].name, 'Legacy');
  });

  it('skips prose-only rules that have no pattern', () => {
    assert.deepEqual(compiled.baseRules.map((rule) => rule.id), ['base.clock']);
  });

  it('falls back to the default module roots and generated globs', () => {
    assert.ok(compiled.moduleRoots.some((root) => root.path === 'backend/src/Modules'));
    assert.ok(compiled.generatedGlobs.includes('rules/*.compiled.json'));
  });

  it('uses a compiled module\'s effective rules, and base rules for new modules and non-module files', () => {
    const module = findModuleForPath('backend/src/Modules/Orders/Api/OrdersController.cs', compiled);
    assert.equal(module.name, 'Orders');
    assert.deepEqual(rulesForModule(compiled, module).map((rule) => rule.id), ['base.clock', 'orders.perm']);
    const exempt = findModuleForPath('backend/src/Modules/Legacy/A.cs', compiled);
    assert.deepEqual(rulesForModule(compiled, exempt), [], 'an exempted base rule is not re-added');
    const fresh = findModuleForPath('backend/src/Modules/Billing/A.cs', compiled);
    assert.equal(fresh.name, 'Billing');
    assert.equal(fresh.compiled, null);
    assert.deepEqual(rulesForModule(compiled, fresh).map((rule) => rule.id), ['base.clock']);
    assert.equal(findModuleForPath('backend/src/Modules/A.cs', compiled), null);
    assert.equal(findModuleForPath('backend/src/Starter.Api/Program.cs', compiled), null);
  });

  it('reports only violations the change introduces (ratchet)', () => {
    const rules = rulesForModule(compiled, findModuleForPath('backend/src/Modules/Orders/Api/OrdersController.cs', compiled));
    const relativePath = 'backend/src/Modules/Orders/Api/OrdersController.cs';
    const ids = (before, after) => findIntroducedViolations({ rules, relativePath, before, after }).map((rule) => rule.id);
    assert.deepEqual(ids(null, 'HasPermission DateTime.Now'), ['base.clock']);
    assert.deepEqual(ids(null, 'class A {}'), ['orders.perm'], 'a new controller must carry the required pattern');
    assert.deepEqual(ids('class A {}', 'class B {}'), [], 'a file that already lacked it is not made worse');
    assert.deepEqual(ids('HasPermission', 'class B {}'), ['orders.perm']);
    assert.deepEqual(ids('HasPermission DateTime.Now', 'HasPermission DateTime.Now x'), []);
  });
});

describe('detectShellWrites', () => {
  const targets = (command) => detectShellWrites(command).map((write) => write.target);

  it('finds redirect, append, tee, sed -i, perl -i targets', () => {
    assert.deepEqual(targets('echo a > a.txt'), ['a.txt']);
    assert.deepEqual(targets('echo a >> "dir with space/b.txt"'), ['dir with space/b.txt']);
    assert.deepEqual(targets('make | tee -a build.log out.txt'), ['build.log', 'out.txt']);
    assert.deepEqual(targets("sed -i.bak -e 's/a/b/' x.cs y.cs"), ['x.cs', 'y.cs']);
    assert.deepEqual(targets("sed -n 's/a/b/p' x.cs"), []);
    assert.deepEqual(targets("perl -pi -e 's/a/b/' z.cs"), ['z.cs']);
    assert.deepEqual(targets('cmd 1>out.txt 2>err.txt'), ['out.txt', 'err.txt']);
    assert.deepEqual(targets('cmd &> all.txt'), ['all.txt']);
  });

  it('ignores fd duplication, input redirects, here-strings and quoted >', () => {
    assert.deepEqual(targets('cmd 2>&1 >&2 < in.txt'), []);
    assert.deepEqual(targets('grep "a > b" file'), []);
    assert.deepEqual(targets("cat <<< 'x > y'"), []);
    assert.deepEqual(targets("node -e 'const f = (a) => a > 1'"), []);
  });

  it('attaches the heredoc body as content', () => {
    const writes = detectShellWrites("cat <<'EOF' > out.cs\nline one\nline > two\nEOF\necho done");
    assert.equal(writes.length, 1);
    assert.equal(writes[0].target, 'out.cs');
    assert.equal(writes[0].content, 'line one\nline > two');
  });

  it('handles <<- heredocs and redirect-before-heredoc order', () => {
    const writes = detectShellWrites('cat > a.cs <<-END\n\tbody\n\tEND');
    assert.equal(writes[0].target, 'a.cs');
    assert.equal(writes[0].content, '\tbody');
  });

  it('marks variable, substitution and glob targets as unresolved (null)', () => {
    assert.deepEqual(targets('echo a > $OUT'), [null]);
    assert.deepEqual(targets('echo a > "$(mktemp)"'), [null]);
    assert.deepEqual(targets('echo a > ~/x'), [null]);
  });

  it('finds PowerShell cmdlet and [IO.File] targets, with -Value content', () => {
    const writes = detectShellWrites("Set-Content -Path src/A.cs -Value 'DateTime.Now' -Encoding utf8");
    assert.equal(writes[0].target, 'src/A.cs');
    assert.equal(writes[0].content, 'DateTime.Now');
    assert.deepEqual(targets("'x' | Out-File -Encoding utf8 out.md"), ['out.md']);
    assert.deepEqual(targets('Add-Content -LiteralPath a.md -Value x'), ['a.md']);
    assert.deepEqual(targets("[System.IO.File]::WriteAllText('src/B.cs', 'x')"), ['src/B.cs']);
    assert.deepEqual(targets('Get-Content a.md | Out-Null'), []);
  });

  it('looks inside bash -c and powershell -Command scripts', () => {
    assert.deepEqual(targets("bash -c 'echo a > inner.txt'"), ['inner.txt']);
    assert.deepEqual(targets('powershell -NoProfile -Command "Set-Content x.md hi"'), ['x.md']);
  });

  it('splits on ; && || | and newlines', () => {
    assert.deepEqual(targets('a && echo x > one; echo y > two || echo z | tee three\necho w > four'), ['one', 'two', 'three', 'four']);
    assert.equal(tokenize('a&&b').filter((token) => token.type === 'operator').length, 1);
  });
});

describe('stop gate planning', () => {
  it('parses porcelain -z output including renames and deletions', () => {
    const parsed = parsePorcelain(' M a.cs\0?? dir/b.md\0R  new.cs\0old.cs\0 D gone.cs\0');
    assert.deepEqual(parsed, [
      { path: 'a.cs', deleted: false },
      { path: 'dir/b.md', deleted: false },
      { path: 'new.cs', deleted: false },
      { path: 'gone.cs', deleted: true },
    ]);
  });

  it('runs the docs gate for module/doc changes and the rules check for rules changes', () => {
    const rules = normalizeCompiledRules(null);
    const plan = (paths) => planChecks({ changed: paths.map((path) => ({ path, deleted: false })), rules });
    assert.equal(plan(['backend/src/Modules/Orders/A.cs']).runDocsGate, true);
    assert.equal(plan(['docs/standards/x.md']).runDocsGate, true);
    assert.equal(plan(['tools/build.ps1']).runDocsGate, false);
    assert.equal(plan(['backend/src/Modules/Orders/module.rules.yaml']).runRulesCheck, true);
    assert.equal(plan(['rules/base.rules.yaml']).runRulesCheck, true);
    assert.equal(plan(['backend/src/Modules/Orders/A.cs']).runRulesCheck, false);
  });
});

describe('routing', () => {
  const routes = [
    { intent: 'a new page', skill: 'new-screen', keywords: ['page', 'screen'] },
    { intent: 'a deadline', skill: 'deadline', keywords: ['sla'] },
  ];
  it('matches keywords as whole words, case-insensitively', () => {
    assert.deepEqual(matchRoutes(routes, 'Add a PAGE for invoices').map((route) => route.skill), ['new-screen']);
    assert.deepEqual(matchRoutes(routes, 'paged list'), []);
  });
  it('formats the table with the likely match and flags skills that do not exist', () => {
    const projectRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'codelab-routing-'));
    try {
      fs.mkdirSync(path.join(projectRoot, '.claude/skills/new-screen'), { recursive: true });
      fs.writeFileSync(path.join(projectRoot, '.claude/skills/new-screen/SKILL.md'), '# new-screen\n');
      const text = formatRoutingContext({ routes, prompt: 'needs an SLA', projectRoot });
      assert.match(text, /a new page -> skill new-screen\n/);
      assert.match(text, /a deadline -> skill deadline \(skill not built yet\)/);
      assert.match(text, /Likely match for this request: deadline\./);
      assert.equal(formatRoutingContext({ routes: [], prompt: 'x', projectRoot }), null);
    } finally {
      fs.rmSync(projectRoot, { recursive: true, force: true });
    }
  });
});
