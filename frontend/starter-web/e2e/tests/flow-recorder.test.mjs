// @vitest-environment node
import { mkdtempSync, rmSync, utimesSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { BROWSER_LOCALES } from '../lib/constants.mjs';
import { FLOW_VIEWPORT, StepStatus } from '../lib/flow-constants.mjs';
import { FlowRecorder, readMails } from '../lib/flow-runtime.mjs';

const folders = [];
function tempFolder() {
  const folder = mkdtempSync(path.join(os.tmpdir(), 'flow-recorder-'));
  folders.push(folder);
  return folder;
}

afterEach(() => {
  while (folders.length > 0) rmSync(folders.pop(), { recursive: true, force: true });
});

function fakePage() {
  const shots = [];
  return { shots, isClosed: () => false, screenshot: async ({ path: file }) => shots.push(path.basename(file)) };
}

function recorder(secrets = ['s3cret']) {
  return new FlowRecorder({ browser: null, name: 'demo', baseUrl: 'http://localhost:4280/', outDir: tempFolder(), secrets });
}

describe('FlowRecorder', () => {
  it('records passed steps with their detail and a numbered screenshot', async () => {
    const flow = recorder();
    const page = fakePage();
    await flow.step('one', page, async () => 'done');
    await flow.step('two', null, async () => undefined);
    expect(flow.steps).toEqual([
      { title: 'one', status: StepStatus.Passed, detail: 'done', screenshot: 'demo-01.png' },
      { title: 'two', status: StepStatus.Passed, detail: '', screenshot: null },
    ]);
    expect(page.shots).toEqual(['demo-01.png']);
  });

  it('fails a step with the first error line, redacts secrets and skips the steps after it', async () => {
    const flow = recorder();
    await flow.step('bad', null, async () => {
      throw new Error('fill("s3cret") timed out\nstack line');
    });
    let ran = false;
    await flow.step('later', null, async () => {
      ran = true;
    });
    expect(ran).toBe(false);
    expect(flow.failed).toBe(true);
    expect(flow.steps.map((step) => [step.status, step.detail])).toEqual([
      [StepStatus.Failed, 'fill("***") timed out'],
      [StepStatus.Skipped, 'an earlier step failed'],
    ]);
  });

  it('runs a needed clean-up after a failure and keeps the flow failed', async () => {
    const flow = recorder();
    await flow.step('bad', null, async () => {
      throw new Error('boom');
    });
    let cleaned = false;
    await flow.cleanup('undo', null, true, async () => {
      cleaned = true;
      return 'undone';
    });
    await flow.cleanup('nothing', null, false, async () => 'never');
    expect(cleaned).toBe(true);
    expect(flow.failed).toBe(true);
    expect(flow.steps.slice(1).map((step) => [step.title, step.status])).toEqual([
      ['undo', StepStatus.Passed],
      ['nothing', StepStatus.Skipped],
    ]);
  });

  it('logs an error raised outside a step as a failed step', () => {
    const flow = recorder();
    flow.fail('Flow set-up', new Error('context refused s3cret'));
    expect(flow.steps).toEqual([{ title: 'Flow set-up', status: StepStatus.Failed, detail: 'context refused ***', screenshot: null }]);
    expect(flow.failed).toBe(true);
  });

  it('opens a context at the desktop viewport by default and at a given viewport on request', async () => {
    const options = [];
    const browser = {
      newContext: async (contextOptions) => {
        options.push(contextOptions);
        return { addInitScript: async () => undefined, newPage: async () => ({ setDefaultTimeout: () => undefined }), close: async () => undefined };
      },
    };
    const flow = new FlowRecorder({ browser, name: 'demo', baseUrl: 'http://localhost:4280/', outDir: tempFolder(), secrets: [] });
    await flow.openPage('ar');
    await flow.openPage('en', { name: '390', width: 390, height: 844 });
    expect(options.map((entry) => entry.viewport)).toEqual([{ ...FLOW_VIEWPORT }, { width: 390, height: 844 }]);
    expect(options.map((entry) => entry.locale)).toEqual([BROWSER_LOCALES.ar, BROWSER_LOCALES.en]);
  });

  it('builds app URLs below the base URL', () => {
    expect(recorder().url('/admin/jobs')).toBe('http://localhost:4280/admin/jobs');
  });
});

describe('readMails', () => {
  it('reads every .eml with its modified time and treats a missing folder as empty', () => {
    const folder = tempFolder();
    writeFileSync(path.join(folder, 'a.eml'), 'To: a@b.test\r\nSubject: Hi\r\n\r\nbody');
    writeFileSync(path.join(folder, 'ignored.txt'), 'x');
    utimesSync(path.join(folder, 'a.eml'), new Date('2026-11-06T10:00:00Z'), new Date('2026-11-06T10:00:00Z'));
    const mails = readMails(folder);
    expect(mails).toEqual([{ mtimeMs: Date.parse('2026-11-06T10:00:00Z'), mail: { to: 'a@b.test', subject: 'Hi', body: 'body' } }]);
    expect(readMails(path.join(folder, 'missing'))).toEqual([]);
  });
});
