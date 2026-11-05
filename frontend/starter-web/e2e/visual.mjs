#!/usr/bin/env node
// Visual + accessibility harness: screenshots of app routes in ar/en at each width, through the installed Microsoft
// Edge (playwright-core, channel msedge, no browser download), compared with stored baselines (default, 0.1 % of
// pixels) or with a design target image (--target, 5 %), with pixelmatch tolerance 0.1, [data-visual-mask] regions
// masked, and an axe-core scan per screenshot that fails on any serious or critical violation. Writes the
// screenshots, the diff images and visual-report.json + visual-report.md to --out-dir.
//
//   node e2e/visual.mjs --base-url http://localhost:4280 --routes e2e/routes/login.json \
//     --langs ar,en --widths 1440,390 --baseline-dir docs/evidence/P4/baselines [--update]
//   node e2e/visual.mjs --base-url http://localhost:4280 --routes '[{"name":"service-guide","path":"/portal/service-guide"}]' \
//     --target design/service-guide.{lang}.{width}.png
//
// Exit codes: 0 pass, 1 a screenshot or the accessibility scan failed, 2 usage, 3 environment (browser, packages,
// app unreachable). Run node e2e/visual.mjs --help for every option. Docs: e2e/README.md.
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { parseArgs, resolveLanguageStorageKey, USAGE } from './lib/args.mjs';
import { ACTUAL_DIR, DIFF_DIR, ExitCodes, Modes, PNG_EXTENSION, REPORT_JSON, REPORT_MARKDOWN } from './lib/constants.mjs';
import { judgeShot } from './lib/judge.mjs';
import { buildReport, formatRatio, renderMarkdown, ShotStatus } from './lib/report.mjs';
import { baselinePath, loadRoutes, planShots, targetPath } from './lib/routes.mjs';
import { captureShot, launchBrowser, loadRuntime, MissingRuntimeError, readPng, writePng } from './lib/runtime.mjs';

/** process.argv holds the node binary and the script path before the user arguments. */
const FIRST_USER_ARGUMENT = 2;
const JSON_INDENT = 2;

function referenceFile(options, shot) {
  return options.mode === Modes.Target ? targetPath(options.target, shot) : baselinePath(options.baselineDir, shot);
}

async function appReachable(baseUrl) {
  try {
    await fetch(baseUrl);
    return true;
  } catch {
    return false;
  }
}

export async function main(argv, { now = () => new Date(), log = console.log, error = console.error } = {}) {
  const parsed = parseArgs(argv);
  if (parsed.help) {
    log(USAGE);
    return ExitCodes.Ok;
  }
  if (parsed.error) {
    error(`${parsed.error}\n\n${USAGE}`);
    return ExitCodes.Usage;
  }
  const options = parsed.options;
  const routes = loadRoutes(options.routes, (file) => readFileSync(file, 'utf8'));
  if (routes.error) {
    error(routes.error);
    return ExitCodes.Usage;
  }

  let runtime;
  try {
    runtime = await loadRuntime();
  } catch (failure) {
    error(failure instanceof MissingRuntimeError ? failure.message : String(failure));
    return ExitCodes.Environment;
  }
  if (!(await appReachable(options.baseUrl))) {
    error(`The app is not reachable at ${options.baseUrl}. Start it first (run-local.ps1 serves http://localhost:4280).`);
    return ExitCodes.Environment;
  }

  const startedAt = now();
  const actualDir = path.join(options.outDir, ACTUAL_DIR);
  const diffDir = path.join(options.outDir, DIFF_DIR);
  mkdirSync(actualDir, { recursive: true });
  mkdirSync(diffDir, { recursive: true });
  if (options.mode === Modes.Baseline) mkdirSync(options.baselineDir, { recursive: true });
  const languageStorageKey = resolveLanguageStorageKey(options.languageStorageKey, routes.languageStorageKey);

  let browser;
  try {
    browser = await launchBrowser(runtime.chromium);
  } catch (failure) {
    error(`Microsoft Edge could not start through playwright-core (channel msedge): ${failure.message}`);
    return ExitCodes.Environment;
  }

  const results = [];
  try {
    for (const shot of planShots(routes.routes, options.languages, options.widths)) {
      const files = {
        actual: path.join(actualDir, `${shot.id}${PNG_EXTENSION}`),
        reference: referenceFile(options, shot),
        diff: path.join(diffDir, `${shot.id}${PNG_EXTENSION}`),
      };
      try {
        const capture = await captureShot(browser, shot, {
          baseUrl: options.baseUrl,
          settleMs: options.settleMs,
          languageStorageKey,
          maskSelectors: [...options.maskSelectors, ...routes.mask, ...shot.route.mask],
          axeSource: options.axe ? runtime.axeSource : null,
        });
        writeFileSync(files.actual, capture.png);
        const actual = readPng(runtime.PNG, capture.png);
        const updating = options.mode === Modes.Baseline && options.update && capture.htmlLang === shot.language;
        if (updating) writeFileSync(files.reference, capture.png);
        const reference = !updating && existsSync(files.reference) ? readPng(runtime.PNG, readFileSync(files.reference)) : null;
        const judged = judgeShot({ shot, capture, actual, reference, options, pixelmatch: runtime.pixelmatch, files });
        if (judged.diff) writeFileSync(files.diff, writePng(runtime.PNG, judged.diff));
        results.push(judged.result);
      } catch (failure) {
        results.push({ id: shot.id, route: shot.route.name, path: shot.route.path, language: shot.language, width: shot.width, status: ShotStatus.Error, ratio: null, axe: null, note: failure.message.split('\n')[0] });
      }
      const last = results.at(-1);
      log(`${last.status.padEnd(ShotStatus.MissingReference.length)} ${shot.id} ${formatRatio(last.ratio)}${last.axe ? ` axe ${last.axe.blocking} blocking` : ''}`);
    }
  } finally {
    await browser.close();
  }

  const report = buildReport({ options, shots: results, startedAt, finishedAt: now() });
  writeFileSync(path.join(options.outDir, REPORT_JSON), `${JSON.stringify(report, null, JSON_INDENT)}\n`);
  writeFileSync(path.join(options.outDir, REPORT_MARKDOWN), renderMarkdown(report));
  log(`${report.passed ? 'PASS' : 'FAIL'}: ${report.totals.passed}/${report.totals.shots} screenshots, worst ${formatRatio(report.totals.worstRatio)}, ${report.totals.axeBlocking} blocking axe violation(s). Report: ${path.join(options.outDir, REPORT_MARKDOWN)}`);
  return report.passed ? ExitCodes.Ok : ExitCodes.Failed;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = await main(process.argv.slice(FIRST_USER_ARGUMENT));
}
