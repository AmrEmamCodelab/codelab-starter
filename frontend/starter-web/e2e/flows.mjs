#!/usr/bin/env node
// Scripted phase-5 user flows: runs the selected flows in the installed Microsoft Edge against a running app and
// writes flows-report.json, flows-report.md and one screenshot per step to --out-dir. See e2e/README.md (Flows).
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { runActivation } from './flows/activation.mjs';
import { runContactFaq } from './flows/contact-faq.mjs';
import { runNotificationBell } from './flows/notification-bell.mjs';
import { runSettingsJobs } from './flows/settings-jobs.mjs';
import { FLOW_USAGE, parseFlowArgs, resolveCredentials } from './lib/flow-args.mjs';
import { FLOW_REPORT_JSON, FLOW_REPORT_MARKDOWN, FlowExitCodes, FlowNames } from './lib/flow-constants.mjs';
import { buildFlowReport, renderFlowMarkdown, summarize } from './lib/flow-log.mjs';
import { FlowRecorder, launchFlowBrowser, loadChromium, MissingFlowRuntimeError } from './lib/flow-runtime.mjs';

const FLOW_RUNNERS = Object.freeze({
  [FlowNames.SettingsJobs]: runSettingsJobs,
  [FlowNames.NotificationBell]: runNotificationBell,
  [FlowNames.Activation]: runActivation,
  [FlowNames.ContactFaq]: runContactFaq,
});

async function appReachable(baseUrl) {
  try {
    return (await fetch(baseUrl)).ok;
  } catch {
    return false;
  }
}

async function main(argv) {
  const parsed = parseFlowArgs(argv);
  if (parsed.help) {
    console.log(FLOW_USAGE);
    return FlowExitCodes.Ok;
  }
  if (parsed.error) {
    console.error(`${parsed.error}\n\n${FLOW_USAGE}`);
    return FlowExitCodes.Usage;
  }
  const { options } = parsed;
  const credentials = resolveCredentials(process.env, () => readFileSync(options.secretsFile, 'utf8'));
  if (credentials.error) {
    console.error(credentials.error);
    return FlowExitCodes.Environment;
  }
  if (!(await appReachable(options.baseUrl))) {
    console.error(`The app is not reachable at ${options.baseUrl}: start it with run-local.ps1.`);
    return FlowExitCodes.Environment;
  }
  let browser;
  try {
    browser = await launchFlowBrowser(await loadChromium(), options.headed);
  } catch (error) {
    console.error(error instanceof MissingFlowRuntimeError ? error.message : `Microsoft Edge did not start: ${error.message}`);
    return FlowExitCodes.Environment;
  }
  mkdirSync(options.outDir, { recursive: true });
  const startedAt = new Date().toISOString();
  const browserVersion = browser.version();
  const results = [];
  try {
    for (const name of options.flows) {
      const recorder = new FlowRecorder({ browser, name, baseUrl: options.baseUrl, outDir: options.outDir, secrets: [credentials.password] });
      try {
        await FLOW_RUNNERS[name]({ recorder, credentials, options });
      } catch (error) {
        recorder.fail('Flow set-up', error);
      } finally {
        await recorder.close();
      }
      results.push({ name, steps: recorder.steps });
      console.log(`${name}: ${recorder.failed ? 'FAILED' : 'passed'} (${recorder.steps.length} steps)`);
    }
  } finally {
    await browser.close();
  }
  const report = buildFlowReport({ summary: summarize(results), baseUrl: options.baseUrl, startedAt, finishedAt: new Date().toISOString(), browserVersion });
  writeFileSync(path.join(options.outDir, FLOW_REPORT_JSON), `${JSON.stringify(report, null, 2)}\n`);
  writeFileSync(path.join(options.outDir, FLOW_REPORT_MARKDOWN), renderFlowMarkdown(report));
  console.log(`Report: ${path.join(options.outDir, FLOW_REPORT_MARKDOWN)}`);
  return report.passed ? FlowExitCodes.Ok : FlowExitCodes.Failed;
}

process.exitCode = await main(process.argv.slice(2));
