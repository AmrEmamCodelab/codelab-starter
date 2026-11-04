// End-to-end tests for the Stop gate, UserPromptSubmit and SessionStart hooks.
import { afterEach, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { createFixtureProject, HOOKS_DIR, removeProject, runHookScript, writeFiles } from './helpers/fixture-project.mjs';
import { runCommand, runStopGate } from '../lib/stop-gate.mjs';

const STOP_SCRIPT = 'on-stop-gate.mjs';
const stopInput = (projectRoot, stopHookActive = false) => ({
  session_id: 'stop-test', cwd: projectRoot, hook_event_name: 'Stop', stop_hook_active: stopHookActive,
});

describe('on-stop-gate', () => {
  let root;
  afterEach(() => removeProject(root));

  it('stays silent when nothing changed', () => {
    root = createFixtureProject({ commit: true });
    assert.equal(runHookScript(STOP_SCRIPT, stopInput(root), { projectRoot: root }).output, null);
  });

  it('blocks the stop when a changed file introduces a rule violation, and names the rule', () => {
    root = createFixtureProject({ commit: true });
    writeFiles(root, { 'backend/src/Modules/ServiceRequests/Application/Late.cs': 'class Late { object t = DateTime.UtcNow; }\n' });
    const { status, output } = runHookScript(STOP_SCRIPT, stopInput(root), { projectRoot: root });
    assert.equal(status, 0);
    assert.equal(output.decision, 'block');
    assert.match(output.reason, /\[base\.time-provider\]/);
    assert.match(output.reason, /docs gate skipped/);
  });

  it('regression: still blocks a violation Claude already COMMITTED on a branch (diffs against the merge base with main)', () => {
    root = createFixtureProject({ commit: true });
    const git = (...args) => runCommand({ command: 'git', args: ['-c', 'user.name=t', '-c', 'user.email=t@example.invalid', ...args], cwd: root });
    git('branch', '-M', 'main');
    git('checkout', '-q', '-b', 'feature/clock');
    writeFiles(root, { 'tools/Clock2.cs': 'class Clock2 { object t = DateTime.Now; }\n' });
    git('add', '-A');
    git('commit', '-q', '-m', 'clock');
    const { output } = runHookScript(STOP_SCRIPT, stopInput(root), { projectRoot: root });
    assert.equal(output?.decision, 'block');
    assert.match(output.reason, /tools\/Clock2\.cs[\s\S]*\[base\.time-provider\]/);
  });

  it('only warns when stop_hook_active is set, to avoid a block loop', () => {
    root = createFixtureProject({ commit: true });
    writeFiles(root, { 'backend/src/Modules/ServiceRequests/Application/Late.cs': 'class Late { object t = DateTime.UtcNow; }\n' });
    const { output } = runHookScript(STOP_SCRIPT, stopInput(root, true), { projectRoot: root });
    assert.equal(output.decision, undefined);
    assert.match(output.systemMessage, /not blocking again/);
  });

  it('does not block on violations that were already committed (ratchet against HEAD)', () => {
    root = createFixtureProject({
      commit: true,
      files: { 'backend/src/Starter.Api/Legacy.cs': 'class Legacy { object t = DateTime.Now; }\n' },
    });
    writeFiles(root, { 'backend/src/Starter.Api/Legacy.cs': 'class Legacy { object t = DateTime.Now; int x; }\n' });
    assert.equal(runHookScript(STOP_SCRIPT, stopInput(root), { projectRoot: root }).output, null);
  });

  it('runs the docs gate and the rules drift check when they exist and reports their failures', () => {
    root = createFixtureProject({ commit: true });
    writeFiles(root, {
      'tools/docs-gate/DocsGate.csproj': '<Project />\n',
      'tools/rules-engine/compile.mjs': "console.error('drift: rules.compiled.json is stale'); process.exit(1);\n",
      'backend/src/Modules/ServiceRequests/module.rules.yaml': 'module: ServiceRequests\nchanged: true\n',
    });
    const calls = [];
    const fakeRunner = (request) => {
      calls.push(request);
      if (request.args[0] === 'run') return { status: 1, stdout: 'MODULE.md missing section Gotchas', stderr: '' };
      return runCommand(request);
    };
    const output = runStopGate({ input: stopInput(root), projectRoot: root, env: { ...process.env, CODELAB_DOTNET: 'fake-dotnet' }, runner: fakeRunner });
    assert.equal(output.decision, 'block');
    assert.match(output.reason, /Docs gate .* failed[\s\S]*missing section Gotchas/);
    assert.match(output.reason, /Rules check .* failed[\s\S]*rules\.compiled\.json is stale/);
    const docsGateCall = calls.find((call) => call.command === 'fake-dotnet');
    assert.deepEqual(docsGateCall.args, ['run', '--project', 'tools/docs-gate', '--', 'check', '--root', '.']);
  });
});

describe('user-prompt-submit', () => {
  let root;
  afterEach(() => removeProject(root));

  it('adds the routing table and the likely skill as additionalContext', () => {
    root = createFixtureProject();
    writeFiles(root, { '.claude/hooks/routing-table.json': fs.readFileSync(path.join(HOOKS_DIR, 'routing-table.json'), 'utf8') });
    const { status, output } = runHookScript('user-prompt-submit.mjs', {
      session_id: 's', cwd: root, hook_event_name: 'UserPromptSubmit', prompt: 'Add a new page that lists invoices',
    }, { projectRoot: root });
    assert.equal(status, 0);
    assert.equal(output.hookSpecificOutput.hookEventName, 'UserPromptSubmit');
    assert.match(output.hookSpecificOutput.additionalContext, /routing table/);
    assert.match(output.hookSpecificOutput.additionalContext, /Likely match for this request: new-screen/);
  });
});

describe('session-start', () => {
  let root;
  afterEach(() => removeProject(root));

  it('describes the guardrails and the compiled rules state', () => {
    root = createFixtureProject();
    const { output } = runHookScript('session-start.mjs', { session_id: 's', cwd: root, hook_event_name: 'SessionStart', source: 'startup' }, { projectRoot: root });
    assert.equal(output.hookSpecificOutput.hookEventName, 'SessionStart');
    assert.match(output.hookSpecificOutput.additionalContext, /CI .* is the authoritative gate/);
    assert.match(output.hookSpecificOutput.additionalContext, /modules: ServiceRequests/);
  });

  it('says when the compiled rules are missing', () => {
    root = createFixtureProject({ withCompiledRules: false });
    fs.rmSync(path.join(root, 'rules'), { recursive: true, force: true });
    const { output } = runHookScript('session-start.mjs', { session_id: 's', cwd: root, hook_event_name: 'SessionStart', source: 'startup' }, { projectRoot: root });
    assert.match(output.hookSpecificOutput.additionalContext, /does not exist yet/);
  });
});
