// @vitest-environment node
//
// Runs under Vitest (`npm test`, tools/**/*.test.mjs) and under `node --test tools/permission-gates.test.mjs`: the
// runner's describe/it come from whichever started the file, and the assertions are node:assert in both.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DEFAULT_FEATURES_DIR, ExitCodes, RECIPE, parseArguments, run } from './permission-gates.mjs';

const { afterEach, beforeEach, describe, it } = process.env.VITEST ? await import('vitest') : await import('node:test');

const APP_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

function capture() {
  const sink = { logs: [], errors: [] };
  return { sink, io: { log: (line) => sink.logs.push(line), error: (line) => sink.errors.push(line) } };
}

describe('parseArguments', () => {
  it('defaults to the features folder and accepts --dir', () => {
    assert.deepEqual(parseArguments([], 'C:/app'), { dir: path.resolve('C:/app', DEFAULT_FEATURES_DIR) });
    assert.deepEqual(parseArguments(['--dir', 'other'], 'C:/app'), { dir: path.resolve('C:/app', 'other') });
  });

  it('refuses anything else', () => {
    assert.match(parseArguments(['--folder', 'x']).error, /Usage/);
    assert.match(parseArguments(['--dir']).error, /Usage/);
  });
});

describe('run', () => {
  let dir;
  const write = (relative, text) => {
    const file = path.join(dir, relative);
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, text);
  };

  beforeEach(() => {
    dir = fs.mkdtempSync(path.join(os.tmpdir(), 'permission-gates-'));
  });

  afterEach(() => {
    fs.rmSync(dir, { recursive: true, force: true });
  });

  it('passes gated and public buttons', () => {
    write('orders/a.component.html', '<dga-button *hasPermission="PermissionKeys.OrdersManage">x</dga-button>');
    write('orders/b.component.ts', "@Component({ template: '<button type=\"button\" data-public-action (click)=\"retry()\">x</button>' })");
    const { sink, io } = capture();
    assert.equal(run(['--dir', dir], { appDir: APP_DIR, ...io }), ExitCodes.ok);
    assert.match(sink.logs[0], /2 feature files, every action button gated/);
  });

  it('fails an ungated button with file:line and the recipe', () => {
    write('orders/a.component.html', '<p></p>\n<dga-button>x</dga-button>');
    const { sink, io } = capture();
    assert.equal(run(['--dir', dir], { appDir: APP_DIR, ...io }), ExitCodes.failure);
    assert.match(sink.errors[0], /orders\/a\.component\.html:2:1 <dga-button>/);
    assert.ok(sink.errors[0].includes(RECIPE));
  });

  it('exits 2 for a missing folder or bad arguments', () => {
    assert.equal(run(['--dir', path.join(dir, 'missing')], { appDir: APP_DIR, ...capture().io }), ExitCodes.usage);
    assert.equal(run(['--bad'], { appDir: APP_DIR, ...capture().io }), ExitCodes.usage);
  });
});
