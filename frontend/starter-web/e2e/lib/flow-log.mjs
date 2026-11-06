// The step log of e2e/flows.mjs and its JSON + Markdown report (pure: steps in, report text out).
import { StepStatus } from './flow-constants.mjs';

const MARKDOWN_PIPE = /\|/g;
const LINE_BREAKS = /\r?\n/g;

/** A file-safe screenshot name: <flow>-<two-digit step>.png. */
export function screenshotName(flow, stepNumber) {
  return `${flow}-${String(stepNumber).padStart(2, '0')}.png`;
}

/** A flow's verdict: failed when any step failed. */
export function flowStatus(steps) {
  return steps.some((step) => step.status === StepStatus.Failed) ? StepStatus.Failed : StepStatus.Passed;
}

/** The run summary: { passed, flows: [{ name, status, steps }] }. */
export function summarize(flowResults) {
  const flows = flowResults.map((flow) => ({ name: flow.name, status: flowStatus(flow.steps), steps: flow.steps }));
  return { passed: flows.length > 0 && flows.every((flow) => flow.status === StepStatus.Passed), flows };
}

/**
 * Removes every secret from a text before it reaches the log: a password typed in a step must never appear in an
 * error message that quotes the failing call.
 */
export function redact(text, secrets) {
  return secrets.filter((secret) => typeof secret === 'string' && secret.length > 0).reduce((current, secret) => current.split(secret).join('***'), String(text));
}

/** The JSON report object. */
export function buildFlowReport({ summary, baseUrl, startedAt, finishedAt, browserVersion }) {
  return { kind: 'codelab-flows', baseUrl, browserVersion, startedAt, finishedAt, passed: summary.passed, flows: summary.flows };
}

function cell(text) {
  return String(text ?? '').replace(LINE_BREAKS, ' ').replace(MARKDOWN_PIPE, '\\|');
}

/** The Markdown report: a verdict per flow, then each flow's step table with its screenshot. */
export function renderFlowMarkdown(report) {
  const lines = [
    '# Scripted user flows',
    '',
    `- App: ${report.baseUrl}`,
    `- Browser: ${report.browserVersion ?? 'unknown'}`,
    `- Started: ${report.startedAt} · finished: ${report.finishedAt}`,
    `- Verdict: **${report.passed ? 'PASS' : 'FAIL'}**`,
    '',
    '| Flow | Result | Steps |',
    '| --- | --- | --- |',
    ...report.flows.map((flow) => `| ${flow.name} | ${flow.status} | ${flow.steps.length} |`),
  ];
  for (const flow of report.flows) {
    lines.push('', `## ${flow.name}`, '', '| # | Step | Result | Detail | Screenshot |', '| --- | --- | --- | --- | --- |');
    flow.steps.forEach((step, index) => {
      const shot = step.screenshot ? `[${step.screenshot}](screenshots/${step.screenshot})` : '';
      lines.push(`| ${index + 1} | ${cell(step.title)} | ${step.status} | ${cell(step.detail)} | ${shot} |`);
    });
  }
  return `${lines.join('\n')}\n`;
}
