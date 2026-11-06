// @vitest-environment node
import { describe, expect, it } from 'vitest';
import { StepStatus } from '../lib/flow-constants.mjs';
import { buildFlowReport, flowStatus, redact, renderFlowMarkdown, screenshotName, summarize } from '../lib/flow-log.mjs';

const passed = (title) => ({ title, status: StepStatus.Passed, detail: 'ok', screenshot: null });
const failed = (title) => ({ title, status: StepStatus.Failed, detail: 'boom | pipe\nnext line', screenshot: 'f-02.png' });

describe('screenshotName', () => {
  it('pads the step number', () => {
    expect(screenshotName('activation', 3)).toBe('activation-03.png');
    expect(screenshotName('activation', 12)).toBe('activation-12.png');
  });
});

describe('flowStatus and summarize', () => {
  it('fails a flow with any failed step and the run with any failed flow', () => {
    expect(flowStatus([passed('a'), { ...passed('b'), status: StepStatus.Skipped }])).toBe(StepStatus.Passed);
    expect(flowStatus([passed('a'), failed('b')])).toBe(StepStatus.Failed);
    const summary = summarize([{ name: 'one', steps: [passed('a')] }, { name: 'two', steps: [failed('b')] }]);
    expect(summary.passed).toBe(false);
    expect(summary.flows.map((flow) => flow.status)).toEqual([StepStatus.Passed, StepStatus.Failed]);
  });

  it('does not pass an empty run', () => {
    expect(summarize([]).passed).toBe(false);
  });
});

describe('redact', () => {
  it('replaces every occurrence of every secret and ignores empty ones', () => {
    expect(redact('pw=abc then abc', ['abc', '', null])).toBe('pw=*** then ***');
  });
});

describe('renderFlowMarkdown', () => {
  it('renders the verdict, a flow table and escaped step rows with screenshot links', () => {
    const summary = summarize([{ name: 'settings-jobs', steps: [passed('Sign in'), failed('Save')] }]);
    const report = buildFlowReport({ summary, baseUrl: 'http://localhost:4280', startedAt: 's', finishedAt: 'f', browserVersion: '140' });
    const markdown = renderFlowMarkdown(report);
    expect(report.kind).toBe('codelab-flows');
    expect(markdown).toContain('- Verdict: **FAIL**');
    expect(markdown).toContain('| settings-jobs | failed | 2 |');
    expect(markdown).toContain('| 2 | Save | failed | boom \\| pipe next line | [f-02.png](screenshots/f-02.png) |');
  });
});
