// The hook decision log (CODELAB_HOOK_LOG): one JSON line per PreToolUse / Stop decision, best-effort. The pipeline
// proof reads it to show a refused edit that the agent then corrected.
import { afterEach, beforeEach, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {
  createFixtureProject,
  removeProject,
  runHookScript,
  toolInput,
  writeFiles,
} from './helpers/fixture-project.mjs';
import {
  appendDecisionLog,
  buildLogEntry,
  classifyDecision,
  loggedPaths,
  recordDecision,
  ruleIdsIn,
  skillIn,
} from '../lib/decision-log.mjs';
import { denyOutput, askOutput, blockStopOutput } from '../lib/hook-io.mjs';
import { EnvVars, HookNames, LoggedDecisions } from '../lib/constants.mjs';

const FIXED_NOW = new Date(Date.UTC(2026, 10, 7, 9, 30, 0));
const LOG_KEYS = ['ts', 'hook', 'tool', 'path', 'paths', 'decision', 'ruleIds', 'skill'];
const MODULE_FILE = 'backend/src/Modules/ServiceRequests/Application/Clock.cs';
const NEW_MODULE_FILE = 'backend/src/Modules/Permits/Domain/Permit.cs';

function readLog(logPath) {
  return fs.readFileSync(logPath, 'utf8').trim().split('\n').map((line) => JSON.parse(line));
}

describe('decision-log: classification and parsing', () => {
  it('classifies deny, block, ask, warnings and silence', () => {
    assert.equal(classifyDecision(denyOutput('[base.x] no')), LoggedDecisions.Deny);
    assert.equal(classifyDecision(blockStopOutput('fix it')), LoggedDecisions.Deny);
    assert.equal(classifyDecision(askOutput('[hooks.unresolved-shell-write] maybe')), LoggedDecisions.Warn);
    assert.equal(classifyDecision({ systemMessage: 'rules missing' }), LoggedDecisions.Warn);
    assert.equal(classifyDecision({ hookSpecificOutput: { hookEventName: 'PreToolUse', additionalContext: 'docs' } }), LoggedDecisions.Allow);
    assert.equal(classifyDecision(null), LoggedDecisions.Allow);
  });

  it('reads every distinct rule id, ignoring attribute brackets in fix text', () => {
    const text = '- [base.time-provider] x Fix: [HasPermission(Keys.View)] or [AllowAnonymous]\n- [base.time-provider]\n[hooks.module-without-rules] y';
    assert.deepEqual(ruleIdsIn(text), ['base.time-provider', 'hooks.module-without-rules']);
    assert.deepEqual(ruleIdsIn(''), []);
  });

  it('reads the first skill named as "Use the `<skill>` skill"', () => {
    assert.equal(skillIn('Fix: Use the `new-endpoint` skill (node tools/scaffold/new-endpoint.mjs); or Use the `new-screen` skill'), 'new-endpoint');
    assert.equal(skillIn('Fix: Inject TimeProvider.'), null);
  });

  it('builds the entry with every contract key, a repo-relative path and no rule ids on allow', () => {
    const projectRoot = path.resolve(os.tmpdir(), 'codelab-log-root');
    const input = { tool_name: 'Write', tool_input: { file_path: path.join(projectRoot, 'backend', 'A.cs') } };
    const denied = buildLogEntry({
      hook: HookNames.PreToolUse, input, projectRoot, now: FIXED_NOW,
      output: denyOutput('[base.endpoint-has-permission] open. Fix: Use the `new-endpoint` skill.'),
    });
    assert.deepEqual(Object.keys(denied), LOG_KEYS);
    assert.deepEqual(denied, {
      ts: '2026-11-07T09:30:00.000Z', hook: 'pre-tool-use', tool: 'Write', path: 'backend/A.cs', paths: ['backend/A.cs'],
      decision: 'deny', ruleIds: ['base.endpoint-has-permission'], skill: 'new-endpoint',
    });
    const allowed = buildLogEntry({ hook: HookNames.StopGate, input: {}, output: null, projectRoot, now: FIXED_NOW });
    assert.deepEqual(allowed, {
      ts: '2026-11-07T09:30:00.000Z', hook: 'on-stop-gate', tool: null, path: null, paths: [], decision: 'allow', ruleIds: [], skill: null,
    });
  });

  it('logs the repo files a shell command writes or deletes, so a shell refusal can be paired', () => {
    const projectRoot = path.resolve(os.tmpdir(), 'codelab-log-root');
    const bash = (command) => ({ tool_name: 'Bash', cwd: projectRoot, tool_input: { command } });
    assert.deepEqual(loggedPaths(bash('rm backend/A.cs docs/b.md backend/A.cs'), projectRoot), ['backend/A.cs', 'docs/b.md']);
    assert.deepEqual(loggedPaths(bash('dotnet build > /dev/null'), projectRoot), []);
    assert.deepEqual(loggedPaths(bash(`echo x > ${path.join(os.tmpdir(), 'elsewhere.txt')}`), projectRoot), []);
    assert.deepEqual(loggedPaths(bash('echo x > "$TMPDIR/a.txt"'), projectRoot), []);
    const removeItem = { tool_name: 'PowerShell', cwd: path.join(projectRoot, 'backend'), tool_input: { command: 'Remove-Item -Path src/A.cs' } };
    assert.deepEqual(loggedPaths(removeItem, projectRoot), ['backend/src/A.cs']);

    const denied = buildLogEntry({
      hook: HookNames.PreToolUse, input: bash('rm backend/A.cs'), projectRoot, now: FIXED_NOW,
      output: denyOutput('[hooks.no-shell-source-writes] rm remove deletes the source path backend/A.cs.'),
    });
    assert.deepEqual(Object.keys(denied), LOG_KEYS);
    assert.equal(denied.path, 'backend/A.cs');
    assert.deepEqual(denied.paths, ['backend/A.cs']);
    assert.deepEqual(denied.ruleIds, ['hooks.no-shell-source-writes']);
  });
});

describe('decision-log: writing', () => {
  let directory;
  beforeEach(() => { directory = fs.mkdtempSync(path.join(os.tmpdir(), 'codelab-hook-log-')); });
  afterEach(() => fs.rmSync(directory, { recursive: true, force: true }));

  it('does nothing when CODELAB_HOOK_LOG is unset', () => {
    assert.equal(appendDecisionLog({ entry: { a: 1 }, env: {} }), false);
    assert.equal(recordDecision({ hook: HookNames.PreToolUse, input: {}, output: null, env: {} }), false);
  });

  it('appends one JSON line per decision and creates the folder', () => {
    const logPath = path.join(directory, 'nested', 'hooks.jsonl');
    const env = { [EnvVars.HookLog]: logPath };
    assert.equal(appendDecisionLog({ entry: { n: 1 }, env }), true);
    assert.equal(appendDecisionLog({ entry: { n: 2 }, env }), true);
    assert.deepEqual(readLog(logPath), [{ n: 1 }, { n: 2 }]);
  });

  it('is best-effort: an unwritable target returns false instead of throwing', () => {
    const env = { [EnvVars.HookLog]: directory };
    assert.equal(appendDecisionLog({ entry: { n: 1 }, env }), false);
  });
});

describe('decision-log: the hooks write it', () => {
  let root;
  let logPath;
  beforeEach(() => {
    root = createFixtureProject({ commit: true });
    logPath = path.join(root, '.hook-tmp', 'decisions.jsonl');
  });
  afterEach(() => removeProject(root));

  const write = (relativePath, content) => toolInput({
    projectRoot: root, toolName: 'Write', toolInputFields: { file_path: `${root}/${relativePath}`, content },
  });
  const run = (script, input) => runHookScript(script, input, { projectRoot: root, env: { [EnvVars.HookLog]: logPath } });

  it('pre-tool-use logs a refused edit, then the corrected edit as allow (hook proof shape)', () => {
    const refused = run('pre-tool-use.mjs', write(MODULE_FILE, 'class Clock { object t = DateTime.Now; }\n'));
    assert.equal(refused.output.hookSpecificOutput.permissionDecision, 'deny');
    run('pre-tool-use.mjs', write(MODULE_FILE, 'class Clock { object t = time.GetUtcNow(); }\n'));
    const [first, second] = readLog(logPath);
    assert.deepEqual({ ...first, ts: undefined }, {
      ts: undefined, hook: 'pre-tool-use', tool: 'Write', path: MODULE_FILE, paths: [MODULE_FILE], decision: 'deny', ruleIds: ['base.time-provider'], skill: null,
    });
    assert.ok(!Number.isNaN(Date.parse(first.ts)));
    assert.equal(second.decision, 'allow');
    assert.equal(second.path, MODULE_FILE);
  });

  it('pre-tool-use logs a refused shell delete with its path (not null)', () => {
    const shell = toolInput({ projectRoot: root, toolName: 'Bash', toolInputFields: { command: `rm ${MODULE_FILE}` } });
    const refused = run('pre-tool-use.mjs', shell);
    assert.equal(refused.output.hookSpecificOutput.permissionDecision, 'deny');
    const [entry] = readLog(logPath);
    assert.equal(entry.tool, 'Bash');
    assert.equal(entry.path, MODULE_FILE);
    assert.deepEqual(entry.paths, [MODULE_FILE]);
    assert.deepEqual(entry.ruleIds, ['hooks.no-shell-source-writes']);
  });

  it('pre-tool-use logs the skill a module-folder refusal names', () => {
    run('pre-tool-use.mjs', write(NEW_MODULE_FILE, 'public sealed class Permit { }\n'));
    const [entry] = readLog(logPath);
    assert.equal(entry.decision, 'deny');
    assert.deepEqual(entry.ruleIds, ['hooks.module-without-rules']);
    assert.equal(entry.skill, 'new-module');
  });

  it('the stop gate logs its block with the rule ids', () => {
    writeFiles(root, { 'backend/src/Modules/ServiceRequests/Application/Late.cs': 'class Late { object t = DateTime.UtcNow; }\n' });
    const { output } = run('on-stop-gate.mjs', { session_id: 's', cwd: root, hook_event_name: 'Stop', stop_hook_active: false });
    assert.equal(output.decision, 'block');
    const [entry] = readLog(logPath);
    assert.equal(entry.hook, 'on-stop-gate');
    assert.equal(entry.decision, 'deny');
    assert.deepEqual(entry.ruleIds, ['base.time-provider']);
  });

  it('writes nothing when CODELAB_HOOK_LOG is unset', () => {
    runHookScript('pre-tool-use.mjs', write(MODULE_FILE, 'class Clock { }\n'), { projectRoot: root });
    assert.equal(fs.existsSync(logPath), false);
  });
});
