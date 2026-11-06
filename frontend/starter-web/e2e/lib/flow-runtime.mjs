// The impure edge of e2e/flows.mjs: drives the installed Microsoft Edge, records each step with a screenshot, reads
// the e-mail pickup folder. The pass rules and data live in the pure modules (flow-checks, mail, flow-log).
import { mkdirSync, readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { BROWSER_CHANNEL, BROWSER_LOCALES, COLOUR_SCHEME, RuntimePackages } from './constants.mjs';
import {
  AppKeys,
  AppPaths,
  FINAL_RUN_TONES,
  FLOW_SCREENSHOT_DIR,
  FLOW_VIEWPORT,
  FlowTimings,
  LANGUAGE_STORAGE_KEY,
  MAIL_EXTENSION,
  Sel,
  StepStatus,
} from './flow-constants.mjs';
import { redact, screenshotName } from './flow-log.mjs';
import { parseEml, pickMail } from './mail.mjs';
import { routeUrl } from './routes.mjs';

/** Thrown when playwright-core is missing. */
export class MissingFlowRuntimeError extends Error {}

/** Loads playwright-core's chromium (root devDependency). */
export async function loadChromium() {
  try {
    const playwright = await import(RuntimePackages.Playwright);
    return playwright.chromium ?? playwright.default.chromium;
  } catch (error) {
    throw new MissingFlowRuntimeError(`The flows need ${RuntimePackages.Playwright} (root devDependency): run npm install at the repository root. (${error.message})`);
  }
}

export function launchFlowBrowser(chromium, headed) {
  return chromium.launch({ channel: BROWSER_CHANNEL, headless: !headed });
}

/**
 * One flow's recorder: opens browser contexts in a language, runs named steps, screenshots each step and stops the
 * flow at the first failure (later steps are logged as skipped).
 */
export class FlowRecorder {
  constructor({ browser, name, baseUrl, outDir, secrets }) {
    this.browser = browser;
    this.name = name;
    this.baseUrl = baseUrl;
    this.shotDir = path.join(outDir, FLOW_SCREENSHOT_DIR);
    this.secrets = secrets;
    this.steps = [];
    this.contexts = [];
    this.failed = false;
    mkdirSync(this.shotDir, { recursive: true });
  }

  /** A fresh browser context (no session) whose app boots in the language, at the viewport (default desktop); returns its page. */
  async openPage(language, viewport = FLOW_VIEWPORT) {
    const context = await this.browser.newContext({
      viewport: { width: viewport.width, height: viewport.height },
      locale: BROWSER_LOCALES[language],
      colorScheme: COLOUR_SCHEME,
      reducedMotion: 'reduce',
    });
    await context.addInitScript(
      ({ key, value }) => {
        try {
          window.localStorage.setItem(key, value);
        } catch {
          // Storage refused: the app keeps its default language and the language-specific checks report it.
        }
      },
      { key: LANGUAGE_STORAGE_KEY, value: language },
    );
    this.contexts.push(context);
    const page = await context.newPage();
    page.setDefaultTimeout(FlowTimings.StepTimeoutMs);
    return page;
  }

  url(appPath) {
    return routeUrl(this.baseUrl, appPath);
  }

  /** Runs one step; fn returns an optional detail string. The page (if given) is screenshotted after the step. */
  async step(title, page, fn) {
    if (this.failed) {
      this.steps.push({ title, status: StepStatus.Skipped, detail: 'an earlier step failed', screenshot: null });
      return undefined;
    }
    const entry = { title, status: StepStatus.Passed, detail: '', screenshot: null };
    let result;
    try {
      result = await fn();
      if (typeof result === 'string') entry.detail = redact(result, this.secrets);
    } catch (error) {
      entry.status = StepStatus.Failed;
      entry.detail = redact(error.message.split('\n')[0], this.secrets);
      this.failed = true;
    }
    if (page && !page.isClosed()) {
      const file = screenshotName(this.name, this.steps.length + 1);
      try {
        await page.screenshot({ path: path.join(this.shotDir, file), fullPage: false });
        entry.screenshot = file;
      } catch {
        // A closed or crashed page has no screenshot; the step result still stands.
      }
    }
    this.steps.push(entry);
    return result;
  }

  /**
   * A clean-up step: runs when `needed` is true even after a failed step (it undoes what the flow changed), and is
   * logged like any step. It does not clear an earlier failure.
   */
  async cleanup(title, page, needed, fn) {
    if (!needed) {
      this.steps.push({ title, status: StepStatus.Skipped, detail: 'nothing to undo', screenshot: null });
      return;
    }
    const failedBefore = this.failed;
    this.failed = false;
    await this.step(title, page, fn);
    this.failed ||= failedBefore;
  }

  /** Logs an error raised outside a step (opening a browser context) as a failed step. */
  fail(title, error) {
    this.steps.push({ title, status: StepStatus.Failed, detail: redact(String(error?.message ?? error).split('\n')[0], this.secrets), screenshot: null });
    this.failed = true;
  }

  async close() {
    await Promise.allSettled(this.contexts.map((context) => context.close()));
  }
}

/** Signs in through the login page's password form and waits until the app leaves /login. */
export async function signIn(page, recorder, { user, password }) {
  await page.goto(recorder.url(AppPaths.Login));
  await page.locator(Sel.UserNameInput).fill(user);
  await page.locator(Sel.PasswordInput).fill(password);
  await page.locator(Sel.SubmitButton).click();
  await page.waitForURL((url) => !url.pathname.endsWith(AppPaths.Login));
}

/** The row of an admin list whose text contains the given text. */
export function rowWith(page, text) {
  return page.locator(Sel.ListRow).filter({ hasText: text });
}

/** Starts a job with "Run now" from the catalog (confirming the dialog) and waits on its run history for a final state. */
export async function runJobAndWait(page, recorder, jobKey) {
  await page.goto(recorder.url(AppPaths.AdminJobs));
  await rowWith(page, jobKey).locator(Sel.JobRun).click();
  await page.locator(Sel.OpenDialogConfirm).click();
  await page.waitForURL((url) => url.pathname.includes(`${AppPaths.AdminJobs}/${jobKey}`));
  await page.locator(Sel.RunsJob).waitFor();
  await page.waitForFunction(
    ({ selector, finalTones }) => finalTones.includes(document.querySelector(selector)?.getAttribute('data-tone')),
    { selector: `${Sel.ListRow} ${Sel.RunStatus} ${Sel.StatusTone}`, finalTones: [...FINAL_RUN_TONES] },
    { timeout: FlowTimings.JobRunWaitMs },
  );
}

/** Reads every .eml in the pickup folder: [{ mtimeMs, mail }]. A missing folder is empty. */
export function readMails(mailDir) {
  let names;
  try {
    names = readdirSync(mailDir).filter((name) => name.toLowerCase().endsWith(MAIL_EXTENSION));
  } catch {
    return [];
  }
  return names.map((name) => {
    const file = path.join(mailDir, name);
    return { mtimeMs: statSync(file).mtimeMs, mail: parseEml(readFileSync(file, 'utf8')) };
  });
}

/**
 * Waits for a dropped mail matching the filter. When none is there at once, runs the outbox drain job through the
 * admin page (so the flow does not wait for the 5-minute schedule) and polls until the timeout.
 */
export async function waitForMail({ mailDir, filter, adminPage, recorder }) {
  const found = pickMail(readMails(mailDir), filter);
  if (found) return found;
  if (adminPage) await runJobAndWait(adminPage, recorder, AppKeys.OutboxDrainJob);
  const deadline = Date.now() + FlowTimings.MailWaitMs;
  while (Date.now() < deadline) {
    const mail = pickMail(readMails(mailDir), filter);
    if (mail) return mail;
    await delay(FlowTimings.MailPollMs);
  }
  throw new Error(`No e-mail in ${mailDir} matching ${JSON.stringify(filter)} within ${FlowTimings.MailWaitMs} ms.`);
}
