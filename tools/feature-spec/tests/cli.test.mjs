// The CLI contract (exit codes, --json) and the repo's own specs: the template and the worked example must pass.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { after, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import { agentNamesIn, checkSpec as exportedCheckSpec, parseArguments } from '../check-spec.mjs';
import { checkSpec } from '../lib/check-spec.mjs';
import { ExitCode, PRE_APPROVAL_ENABLED, PRE_APPROVAL_VARIABLE, PRE_APPROVED_BY, RuleId } from '../lib/constants.mjs';
import { APPROVED_FRONT_MATTER, buildSpec } from './helpers/spec-builder.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(HERE, '..', '..', '..');
const CLI = path.join(REPO_ROOT, 'tools', 'feature-spec', 'check-spec.mjs');
const EXAMPLE = path.join(REPO_ROOT, 'docs', 'features', 'examples', 'site-visit-permit.md');
const TEMPLATE = path.join(REPO_ROOT, 'docs', 'features', 'TEMPLATE.md');
const scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'feature-spec-'));

after(() => fs.rmSync(scratch, { recursive: true, force: true }));

function writeSpec(name, text) {
  const file = path.join(scratch, name);
  fs.writeFileSync(file, text);
  return file;
}

/** Runs the CLI without the pipeline-proof variable (as a developer shell or CI would), plus `extraEnv`. */
function runWith(extraEnv, ...args) {
  const env = { ...process.env, ...extraEnv };
  if (!(PRE_APPROVAL_VARIABLE in extraEnv)) delete env[PRE_APPROVAL_VARIABLE];
  return spawnSync(process.execPath, [CLI, ...args], { encoding: 'utf8', env });
}

function run(...args) {
  return runWith({}, ...args);
}

describe('parseArguments', () => {
  it('reads one file and the two flags in any order', () => {
    assert.deepEqual(parseArguments(['--json', 'a.md', '--require-approved']), { file: 'a.md', requireApproved: true, json: true });
    assert.deepEqual(parseArguments(['a.md']), { file: 'a.md', requireApproved: false, json: false });
  });

  it('refuses no file, two files and unknown flags', () => {
    assert.equal(parseArguments([]), null);
    assert.equal(parseArguments(['a.md', 'b.md']), null);
    assert.equal(parseArguments(['a.md', '--strict']), null);
  });
});

describe('check-spec CLI', () => {
  it('re-exports the library function', () => {
    assert.equal(exportedCheckSpec, checkSpec);
  });

  it('exits 0 on a valid spec', () => {
    assert.equal(run(writeSpec('valid.md', buildSpec())).status, ExitCode.ok);
  });

  it('exits 1 on findings and names the rule', () => {
    const result = run(writeSpec('draft.md', buildSpec()), '--require-approved');
    assert.equal(result.status, ExitCode.findings);
    assert.match(result.stderr, new RegExp(RuleId.approvalRequired.replace('.', '\\.')));
  });

  it('exits 2 on bad usage and on a missing file', () => {
    assert.equal(run().status, ExitCode.usage);
    assert.equal(run(path.join(scratch, 'missing.md')).status, ExitCode.usage);
  });

  it('prints the result as JSON with --json', () => {
    const file = writeSpec('approved.md', buildSpec({ frontMatter: APPROVED_FRONT_MATTER }));
    const result = run(file, '--json', '--require-approved');
    assert.equal(result.status, ExitCode.ok);
    const parsed = JSON.parse(result.stdout);
    assert.equal(parsed.file, file);
    assert.equal(parsed.status, 'approved');
    assert.equal(parsed.handEditCount, 0);
    assert.deepEqual(parsed.findings, []);
  });

  it('refuses an agent that approves its own spec (the spec-writer agent, an agent word)', () => {
    for (const approvedBy of ['spec-writer agent', 'spec-writer', 'Claude', 'build bot']) {
      const file = writeSpec('self-approved.md', buildSpec({ frontMatter: { ...APPROVED_FRONT_MATTER, approvedBy } }));
      const result = run(file, '--require-approved');
      assert.equal(result.status, ExitCode.findings, approvedBy);
      assert.match(result.stderr, new RegExp(RuleId.approvalNotHuman.replace('.', '\\.')), approvedBy);
    }
  });

  it('accepts the pipeline-proof approver only with the variable Invoke-PipelineProof.ps1 sets', () => {
    const file = writeSpec('proof.md', buildSpec({ frontMatter: { ...APPROVED_FRONT_MATTER, approvedBy: PRE_APPROVED_BY } }));
    const outside = run(file, '--require-approved');
    assert.equal(outside.status, ExitCode.findings);
    assert.match(outside.stderr, new RegExp(RuleId.approvalPreApproved.replace('.', '\\.')));
    assert.equal(runWith({ [PRE_APPROVAL_VARIABLE]: 'yes' }, file, '--require-approved').status, ExitCode.findings);
    assert.equal(runWith({ [PRE_APPROVAL_VARIABLE]: PRE_APPROVAL_ENABLED }, file, '--require-approved').status, ExitCode.ok);
  });

  it('reads the agent names of the repository', () => {
    const names = agentNamesIn(REPO_ROOT);
    assert.ok(names.includes('spec-writer'));
    assert.ok(names.includes('commit-gate'));
    assert.deepEqual(agentNamesIn(path.join(scratch, 'no-repo')), []);
  });
});

describe('the repo specs', () => {
  it('the worked example passes with no finding', () => {
    assert.deepEqual(checkSpec(fs.readFileSync(EXAMPLE, 'utf8')).findings, []);
  });

  it('the worked example is a draft, so the build gate refuses it', () => {
    const result = checkSpec(fs.readFileSync(EXAMPLE, 'utf8'), { requireApproved: true });
    assert.deepEqual(result.findings.map((item) => item.ruleId), [RuleId.approvalRequired]);
  });

  it('the template passes with no finding', () => {
    assert.deepEqual(checkSpec(fs.readFileSync(TEMPLATE, 'utf8')).findings, []);
  });
});
