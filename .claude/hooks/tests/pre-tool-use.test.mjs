// End-to-end PreToolUse tests: fixture JSON on stdin -> pre-tool-use.mjs -> JSON decision on stdout.
import { after, before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {
  contextOf,
  createFixtureProject,
  decisionOf,
  loadInput,
  reasonOf,
  removeProject,
  runHookScript,
  toolInput,
  writeFiles,
} from './helpers/fixture-project.mjs';

const SCRIPT = 'pre-tool-use.mjs';
const DENY = 'deny';

describe('pre-tool-use: exit criteria', () => {
  let root;
  before(() => { root = createFixtureProject(); });
  after(() => removeProject(root));

  it('refuses a Write adding DateTime.Now to a .cs file, with the rule id and fix', () => {
    const { status, output } = runHookScript(SCRIPT, loadInput('write-datetime-now', root), { projectRoot: root });
    assert.equal(status, 0);
    assert.equal(decisionOf(output), DENY);
    assert.match(reasonOf(output), /\[base\.time-provider\]/);
    assert.match(reasonOf(output), /Fix: Inject TimeProvider/);
  });

  it('refuses an Edit whose RESULTING file violates a whole-file rule (new_string alone is clean)', () => {
    const input = loadInput('edit-whole-file-violation', root);
    assert.doesNotMatch(input.tool_input.new_string, /namespace/, 'fixture: the edit text alone must not match the rule');
    const { output } = runHookScript(SCRIPT, input, { projectRoot: root });
    assert.equal(decisionOf(output), DENY);
    assert.match(reasonOf(output), /\[servicerequests\.api-no-dbcontext\]/);
    assert.match(reasonOf(output), /Fix: Call an Application use case/);
  });

  it('refuses a Bash heredoc writing the same violation into backend/', () => {
    const { output } = runHookScript(SCRIPT, loadInput('bash-heredoc-violation', root), { projectRoot: root });
    assert.equal(decisionOf(output), DENY);
    assert.match(reasonOf(output), /\[hooks\.no-shell-source-writes\]/);
    assert.match(reasonOf(output), /\[base\.time-provider\]/);
  });

  it('refuses a new file in a new module folder without module.rules.yaml and names the new-module skill', () => {
    const { output } = runHookScript(SCRIPT, loadInput('new-module-without-rules', root), { projectRoot: root });
    assert.equal(decisionOf(output), DENY);
    assert.match(reasonOf(output), /\[hooks\.module-without-rules\]/);
    assert.match(reasonOf(output), /new-module/);
  });

  it('allows a clean edit and injects the module docs once per session', () => {
    const input = loadInput('clean-edit', root);
    const first = runHookScript(SCRIPT, input, { projectRoot: root });
    assert.equal(decisionOf(first.output), null);
    assert.match(contextOf(first.output), /ServiceRequests: the sample request module/);
    assert.match(contextOf(first.output), /MODULE\.md/);
    const second = runHookScript(SCRIPT, input, { projectRoot: root });
    assert.equal(second.output, null, 'the same module is not injected twice in one session');
  });
});

describe('pre-tool-use: new module flow', () => {
  let root;
  before(() => { root = createFixtureProject(); });
  after(() => removeProject(root));

  const newModuleFile = (projectRoot, relativePath, content) => toolInput({
    projectRoot, toolName: 'Write', toolInputFields: { file_path: `${projectRoot}/${relativePath}`, content },
  });

  it('regression: refuses a page in a new frontend feature area without module.rules.yaml and names new-screen', () => {
    const page = 'frontend/starter-web/src/app/features/invoices/list/list-page.component.ts';
    const { output } = runHookScript(SCRIPT, newModuleFile(root, page, 'export class ListPageComponent {}\n'), { projectRoot: root });
    assert.equal(decisionOf(output), DENY);
    assert.match(reasonOf(output), /\[hooks\.module-without-rules\]/);
    assert.match(reasonOf(output), /new-screen/);
    assert.match(reasonOf(output), /features\/dashboard\//);
    assert.match(reasonOf(output), /tools\/rules-engine\/compile\.mjs/);
    assert.doesNotMatch(reasonOf(output), /new-module/);
  });

  it('allows a page in a new frontend feature area once its module.rules.yaml exists', () => {
    writeFiles(root, { 'frontend/starter-web/src/app/features/receipts/module.rules.yaml': 'module: Receipts\n' });
    const page = 'frontend/starter-web/src/app/features/receipts/list/list-page.component.ts';
    const { output } = runHookScript(SCRIPT, newModuleFile(root, page, 'export class ListPageComponent {}\n'), { projectRoot: root });
    assert.doesNotMatch(reasonOf(output) ?? '', /hooks\.module-without-rules/);
  });

  it('allows writing module.rules.yaml and MODULE.md into a new module folder', () => {
    for (const name of ['module.rules.yaml', 'MODULE.md']) {
      const { output } = runHookScript(SCRIPT, newModuleFile(root, `backend/src/Modules/Invoices/${name}`, 'x\n'), { projectRoot: root });
      assert.equal(decisionOf(output), null, name);
    }
  });

  it('allows code in a new module once its rules exist, applies base rules and injects its MODULE.md', () => {
    writeFiles(root, {
      'backend/src/Modules/Invoices/module.rules.yaml': 'module: Invoices\n',
      'backend/src/Modules/Invoices/MODULE.md': '# Invoices\n\n## Purpose\nInvoice records.\n',
    });
    const clean = runHookScript(SCRIPT, newModuleFile(root, 'backend/src/Modules/Invoices/Domain/Invoice.cs', 'class Invoice {}\n'), { projectRoot: root });
    assert.equal(decisionOf(clean.output), null);
    assert.match(contextOf(clean.output), /Invoice records\./);
    assert.match(contextOf(clean.output), /not in rules\/rules\.compiled\.json yet/);

    const dirty = runHookScript(SCRIPT, newModuleFile(root, 'backend/src/Modules/Invoices/Domain/Due.cs', 'var d = DateTime.UtcNow;\n'), { projectRoot: root });
    assert.equal(decisionOf(dirty.output), DENY);
    assert.match(reasonOf(dirty.output), /\[base\.time-provider\]/);
  });
});

describe('pre-tool-use: path guards and ratchet', () => {
  let root;
  before(() => { root = createFixtureProject(); });
  after(() => removeProject(root));

  const write = (relativePath, content = 'x\n') => toolInput({
    projectRoot: root, toolName: 'Write', toolInputFields: { file_path: `${root}/${relativePath}`, content },
  });

  it('refuses rules/** and packages/** without CODELAB_PLATFORM_OWNER=1 and allows them with it', () => {
    for (const relativePath of ['rules/base.rules.yaml', 'packages/dotnet/X/X.cs']) {
      const refused = runHookScript(SCRIPT, write(relativePath), { projectRoot: root });
      assert.equal(decisionOf(refused.output), DENY, relativePath);
      assert.match(reasonOf(refused.output), /\[hooks\.platform-owner\]/);
      const allowed = runHookScript(SCRIPT, write(relativePath), { projectRoot: root, env: { CODELAB_PLATFORM_OWNER: '1' } });
      assert.equal(decisionOf(allowed.output), null, relativePath);
    }
  });

  it('regression: refuses every guardrail switch the platform owns (CODEOWNERS paths, build script, scrub lists, MSBuild)', () => {
    for (const relativePath of [
      'deploy/Install.ps1', '.github/CODEOWNERS', 'tools/build.ps1', 'tools/extract/modhs-terms.txt',
      'tools/extract/allowlist.manifest.yaml', '.claude/hooks/lib/constants.mjs', '.claude/settings.local.json',
      'Directory.Build.props', 'backend/src/Modules/ServiceRequests/Directory.Build.props', 'global.json',
      'docs/adr/ADR-001.md', 'provenance/SOURCE.md',
      // Lint/analyzer configs and nested MSBuild/feed settings switch guardrails off too.
      'frontend/starter-web/eslint.config.mjs', 'frontend/starter-web/tools/lint/starter-eslint-config.mjs',
      'frontend/starter-web/stylelint.config.mjs', '.editorconfig', 'backend/Directory.Build.targets',
      'backend/nuget.config', 'tools/extract/scan.mjs', 'docs/standards/SKILL.md',
    ]) {
      const { output } = runHookScript(SCRIPT, write(relativePath), { projectRoot: root });
      assert.equal(decisionOf(output), DENY, relativePath);
      assert.match(reasonOf(output), /\[hooks\.platform-owner\]/, relativePath);
    }
  });

  it('regression: refuses a module.rules.yaml that grants itself an exemption, but not one without exemptions', () => {
    const rulesFile = 'backend/src/Modules/ServiceRequests/module.rules.yaml';
    const selfExempt = 'module: ServiceRequests\nexemptions:\n  - rule: base.time-provider\n    reason: we need the wall clock\n';
    const refused = runHookScript(SCRIPT, write(rulesFile, selfExempt), { projectRoot: root });
    assert.equal(decisionOf(refused.output), DENY);
    assert.match(reasonOf(refused.output), /exemptions/);
    const owner = runHookScript(SCRIPT, write(rulesFile, selfExempt), { projectRoot: root, env: { CODELAB_PLATFORM_OWNER: '1' } });
    assert.equal(decisionOf(owner.output), null);
    const plain = runHookScript(SCRIPT, write(rulesFile, 'module: ServiceRequests\nmayDependOn: []\n'), { projectRoot: root });
    assert.equal(decisionOf(plain.output), null);
  });

  it('regression: refuses analyzer opt-outs (banned APIs off, NoWarn/pragma/editorconfig RS0030)', () => {
    for (const [relativePath, content] of [
      ['backend/src/Starter.Api/Starter.Api.csproj', '<Project><PropertyGroup><CodelabEnableBannedApis>false</CodelabEnableBannedApis></PropertyGroup></Project>'],
      ['backend/src/Starter.Api/Starter.Api.csproj', '<Project><PropertyGroup><NoWarn>$(NoWarn);RS0030</NoWarn></PropertyGroup></Project>'],
      ['backend/src/Starter.Api/Clock.cs', '#pragma warning disable RS0030\nclass Clock { }\n'],
      ['backend/src/Starter.Api/.editorconfig', '[*.cs]\ndotnet_diagnostic.RS0030.severity = none\n'],
    ]) {
      // .editorconfig is platform-owned too; the owner unlock proves the opt-out rule still refuses it.
      const { output } = runHookScript(SCRIPT, write(relativePath, content), { projectRoot: root, env: { CODELAB_PLATFORM_OWNER: '1' } });
      assert.equal(decisionOf(output), DENY, content);
      assert.match(reasonOf(output), /\[base\.no-analyzer-opt-out\]/, content);
    }
  });

  it('refuses generated files even for the platform owner', () => {
    for (const relativePath of ['rules/rules.compiled.json', 'backend/src/Starter.Api/BannedSymbols.generated.txt', 'docs/generated/permissions.md', 'feed/npm/x.tgz']) {
      const { output } = runHookScript(SCRIPT, write(relativePath), { projectRoot: root, env: { CODELAB_PLATFORM_OWNER: '1' } });
      assert.equal(decisionOf(output), DENY, relativePath);
      assert.match(reasonOf(output), /\[hooks\.generated-file\]/);
    }
  });

  it('refuses edits to a file marked as generated in its header', () => {
    writeFiles(root, { 'backend/src/Starter.Api/Generated.cs': '// <auto-generated />\nclass G {}\n' });
    const input = toolInput({ projectRoot: root, toolName: 'Edit', toolInputFields: { file_path: `${root}/backend/src/Starter.Api/Generated.cs`, old_string: 'class G', new_string: 'class H' } });
    assert.equal(decisionOf(runHookScript(SCRIPT, input, { projectRoot: root }).output), DENY);
  });

  it('refuses an Edit that removes a required pattern from the resulting file', () => {
    const input = toolInput({
      projectRoot: root, toolName: 'Edit',
      toolInputFields: { file_path: `${root}/backend/src/Modules/ServiceRequests/Api/ServiceRequestsController.cs`, old_string: '[HasPermission(Permissions.ServiceRequests.View)]\n', new_string: '' },
    });
    const { output } = runHookScript(SCRIPT, input, { projectRoot: root });
    assert.equal(decisionOf(output), DENY);
    assert.match(reasonOf(output), /\[servicerequests\.controller-permission\]/);
  });

  it('allows an unrelated edit to a file that already broke a rule (ratchet), but not a second violation', () => {
    writeFiles(root, { 'backend/src/Starter.Api/Legacy.cs': 'class Legacy { object a = DateTime.Now; int b = 1; }\n' });
    const edit = (newString) => toolInput({
      projectRoot: root, toolName: 'Edit',
      toolInputFields: { file_path: `${root}/backend/src/Starter.Api/Legacy.cs`, old_string: 'int b = 1;', new_string: newString },
    });
    assert.equal(decisionOf(runHookScript(SCRIPT, edit('int b = 2;'), { projectRoot: root }).output), null);
    assert.equal(decisionOf(runHookScript(SCRIPT, edit('object c = DateTime.Now;'), { projectRoot: root }).output), DENY);
  });

  it('checks an Edit with an empty old_string that creates a new file like a Write (regression)', () => {
    const input = toolInput({
      projectRoot: root, toolName: 'Edit',
      toolInputFields: { file_path: `${root}/backend/src/Starter.Api/Created.cs`, old_string: '', new_string: 'class C { object N = DateTime.Now; }' },
    });
    const { output } = runHookScript(SCRIPT, input, { projectRoot: root });
    assert.equal(decisionOf(output), DENY);
    assert.match(reasonOf(output), /\[base\.time-provider\]/);
  });

  it('honours module exemptions: a compiled module without the inherited rule may use the clock', () => {
    writeFiles(root, { 'backend/src/Modules/Clocks/module.rules.yaml': 'module: Clocks\n' });
    const { output } = runHookScript(SCRIPT, write('backend/src/Modules/Clocks/SystemClock.cs', 'class SystemClock { object Now => DateTime.UtcNow; }\n'), { projectRoot: root });
    assert.equal(decisionOf(output), null);
  });

  it('ignores files outside the project and tools it does not handle', () => {
    const outside = toolInput({ projectRoot: root, toolName: 'Write', toolInputFields: { file_path: `${root}/../elsewhere/A.cs`, content: 'DateTime.Now' } });
    assert.equal(runHookScript(SCRIPT, outside, { projectRoot: root }).output, null);
    const read = toolInput({ projectRoot: root, toolName: 'Read', toolInputFields: { file_path: `${root}/x.cs` } });
    assert.equal(runHookScript(SCRIPT, read, { projectRoot: root }).output, null);
  });
});

describe('pre-tool-use: shell writes', () => {
  let root;
  before(() => { root = createFixtureProject(); });
  after(() => removeProject(root));

  const shell = (command, toolName = 'Bash') => runHookScript(SCRIPT, toolInput({ projectRoot: root, toolName, toolInputFields: { command } }), { projectRoot: root });

  it('refuses sed -i, tee and redirects into source paths', () => {
    for (const command of [
      "sed -i 's/a/b/' backend/src/Modules/ServiceRequests/Application/SubmitRequest.cs",
      'echo hi | tee docs/notes.md',
      'echo "class A {}" >> backend/src/A.cs',
    ]) {
      const { output } = shell(command);
      assert.equal(decisionOf(output), DENY, command);
      assert.match(reasonOf(output), /\[hooks\.no-shell-source-writes\]/);
    }
  });

  it('refuses PowerShell Set-Content, Out-File and Add-Content into source paths', () => {
    for (const command of [
      "Set-Content -Path backend/src/A.cs -Value 'class A {}'",
      "'x' | Out-File -FilePath docs/a.md",
      'Add-Content backend\\src\\A.cs "more"',
    ]) {
      assert.equal(decisionOf(shell(command, 'PowerShell').output), DENY, command);
    }
  });

  it('allows reads, tests, fd duplication, null devices, temp files and gitignored output', () => {
    for (const command of [
      'npm test 2>&1',
      'dotnet build > /dev/null',
      'node -e "console.log(1 > 0)"',
      'echo x > bin/out.txt',
      'git status --porcelain > "$TMPDIR/status.txt"',
      'Get-Content backend/src/A.cs | Out-Null',
      'cp tools/A.cs "$TMPDIR/a.cs"',
      'git checkout -b feature/x',
    ]) {
      assert.equal(decisionOf(shell(command).output), null, command);
    }
  });

  it('regression: refuses the shell write forms that used to bypass the hook (POSIX)', () => {
    for (const command of [
      "cd node_modules && echo 'DateTime.Now' > ../tools/A.cs",
      'cp /tmp/x tools/A.cs',
      'mv /tmp/x rules/base.rules.yaml',
      'install -m 644 /tmp/x tools/A.cs',
      'dd if=/tmp/x of=tools/A.cs',
      'curl -o tools/A.cs https://example.test/x',
      'git checkout stash@{0} -- rules/base.rules.yaml',
      "awk -i inplace '{print}' tools/A.cs",
      'echo x > "$(pwd)/tools/A.cs"',
      'cmd /c "echo x > tools\\A.cs"',
      'eval "echo x > tools/A.cs"',
      'env bash -c "echo x > tools/A.cs"',
      'bash -lc "echo x > tools/A.cs"',
      'rm -f rules/base.rules.yaml',
      'cp /tmp/x .',
    ]) {
      assert.equal(decisionOf(shell(command).output), DENY, command);
    }
  });

  it('regression: refuses the PowerShell write forms that used to bypass the hook', () => {
    const encoded = Buffer.from('Set-Content tools/A.cs x', 'utf16le').toString('base64');
    for (const command of [
      'sc tools/A.cs x',
      'ac tools/A.cs x',
      "'x' | Tee-Object -FilePath tools/A.cs",
      'Copy-Item C:/temp/x tools/A.cs',
      'Move-Item x rules\\base.rules.yaml',
      'New-Item -Path tools/A.cs -Value x',
      'Set-Content -Path:tools/A.cs -Value x',
      'Set-Cont`ent tools/A.cs x',
      '1 | % { Set-Content tools/A.cs x }',
      'iex "Set-Content tools/A.cs x"',
      'Set-Content "$PWD/tools/A.cs" x',
      "[IO.File]::Copy('C:/temp/x', 'tools/A.cs')",
      'Invoke-WebRequest https://example.test/x -OutFile tools/A.cs',
      'Expand-Archive x.zip -DestinationPath rules',
      'Rename-Item rules/base.rules.yaml b.yaml',
      'Remove-Item rules/base.rules.yaml',
      `powershell -EncodedCommand ${encoded}`,
    ]) {
      assert.equal(decisionOf(shell(command, 'PowerShell').output), DENY, command);
    }
  });

  it('regression: asks (fails closed) when a command writes files whose path cannot be resolved', () => {
    for (const command of [
      `python -c "open('tools/A.cs','w').write('x')"`,
      `node -e "require('fs').writeFileSync('rules/base.rules.yaml','x')"`,
      `perl -e 'open(F,">tools/A.cs")'`,
      'f=tools/A.cs; echo x > "$f"',
      'git apply fix.patch',
      'patch -p1 < fix.patch',
      "[IO.File]::WriteAllText($p, 'x')",
      '[IO.StreamWriter]::new($p)',
      'ls | xargs rm',
    ]) {
      const { output } = shell(command);
      assert.equal(decisionOf(output), 'ask', command);
      assert.match(reasonOf(output), /\[hooks\.unresolved-shell-write\]/, command);
    }
  });
});

describe('pre-tool-use: missing compiled rules', () => {
  let root;
  before(() => { root = createFixtureProject({ withCompiledRules: false }); });
  after(() => removeProject(root));

  it('allows content with a warning when rules/rules.compiled.json is missing, but still guards paths', () => {
    const write = (relativePath, content) => toolInput({ projectRoot: root, toolName: 'Write', toolInputFields: { file_path: `${root}/${relativePath}`, content } });
    const content = runHookScript(SCRIPT, write('backend/src/Modules/ServiceRequests/Application/A.cs', 'var n = DateTime.Now;'), { projectRoot: root });
    assert.equal(decisionOf(content.output), null);
    assert.match(content.output.systemMessage, /rules\.compiled\.json is missing/);
    const guarded = runHookScript(SCRIPT, write('rules/rules.compiled.json', '{}'), { projectRoot: root });
    assert.equal(decisionOf(guarded.output), DENY);
  });
});

describe('pre-tool-use: evaluation budget', () => {
  let root;
  before(() => {
    root = createFixtureProject();
    const compiled = JSON.parse(fs.readFileSync(path.join(root, 'rules/rules.compiled.json'), 'utf8'));
    compiled.base.forbidden.push({ id: 'base.slow', pattern: '^(\\w+\\s?)*$', appliesTo: ['**/*.cs'], fix: 'none' });
    writeFiles(root, { 'rules/rules.compiled.json': JSON.stringify(compiled) });
  });
  after(() => removeProject(root));

  it('regression: denies (instead of failing open at the hook timeout) when a rule regex backtracks catastrophically', () => {
    const content = `${'a'.repeat(40)}!\nclass C { object A() => DateTime.Now; }\n`;
    const input = toolInput({ projectRoot: root, toolName: 'Write', toolInputFields: { file_path: `${root}/backend/src/Starter.Api/Slow.cs`, content } });
    const started = Date.now();
    const { output } = runHookScript(SCRIPT, input, { projectRoot: root, env: { CODELAB_HOOK_BUDGET_MS: '500' } });
    assert.equal(decisionOf(output), DENY);
    assert.match(reasonOf(output), /\[hooks\.rule-evaluation-timeout\]/);
    assert.ok(Date.now() - started < 20_000, 'the hook returns soon after the budget');
  });
});

describe('pre-tool-use: robustness', () => {
  it('never blocks on a crash: malformed stdin yields a warning and exit 0', async () => {
    const { spawnSync } = await import('node:child_process');
    const path = await import('node:path');
    const { HOOKS_DIR } = await import('./helpers/fixture-project.mjs');
    const result = spawnSync(process.execPath, [path.join(HOOKS_DIR, SCRIPT)], { input: '{not json', encoding: 'utf8' });
    assert.equal(result.status, 0);
    assert.match(JSON.parse(result.stdout).systemMessage, /hook failed/);
  });
});
