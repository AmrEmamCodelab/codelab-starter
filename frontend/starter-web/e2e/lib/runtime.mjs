// The impure edge of e2e/visual.mjs: loads the root devDependencies (playwright-core, pixelmatch, pngjs, axe-core),
// drives the installed Microsoft Edge and reads and writes PNG files. The decisions live in the pure modules.
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import {
  AXE_TAGS,
  BROWSER_CHANNEL,
  BROWSER_LOCALES,
  COLOUR_SCHEME,
  DEVICE_SCALE_FACTOR,
  FALLBACK_VIEWPORT_HEIGHT,
  NAVIGATION_TIMEOUT_MS,
  RuntimePackages,
  STILL_PAGE_CSS,
  VIEWPORT_HEIGHTS,
} from './constants.mjs';
import { routeUrl } from './routes.mjs';

const require = createRequire(import.meta.url);
const AXE_SCRIPT = 'axe.min.js';
const NETWORK_IDLE = 'networkidle';
const VISIBLE = 'visible';
const AXE_RESULT_TYPES = ['violations'];

/** Thrown when a runtime package is missing; the message says how to install it. */
export class MissingRuntimeError extends Error {}

/** Loads the runtime packages: { chromium, pixelmatch, PNG, axeSource }. */
export async function loadRuntime() {
  try {
    const playwright = await import(RuntimePackages.Playwright);
    const pixelmatch = (await import(RuntimePackages.Pixelmatch)).default;
    const png = await import(RuntimePackages.Png);
    const axeFolder = require.resolve(`${RuntimePackages.Axe}/package.json`).replace(/package\.json$/, '');
    return {
      chromium: playwright.chromium ?? playwright.default.chromium,
      pixelmatch,
      PNG: png.PNG ?? png.default.PNG,
      axeSource: readFileSync(`${axeFolder}${AXE_SCRIPT}`, 'utf8'),
    };
  } catch (error) {
    throw new MissingRuntimeError(
      `The visual harness needs ${Object.values(RuntimePackages).join(', ')} (root devDependencies): run npm install at the repository root. (${error.message})`,
    );
  }
}

/** Launches the installed Microsoft Edge, headless. */
export function launchBrowser(chromium) {
  return chromium.launch({ channel: BROWSER_CHANNEL, headless: true });
}

/**
 * Opens one route in a fresh context at the shot's width and language, waits until it is still, and returns
 * { png (Buffer), htmlLang, maskRects, axe (raw axe results or null) }. The language is seeded into browser storage
 * before the app starts, so the app boots in it.
 */
export async function captureShot(browser, shot, { baseUrl, settleMs, languageStorageKey, maskSelectors, axeSource }) {
  const context = await browser.newContext({
    viewport: { width: shot.width, height: VIEWPORT_HEIGHTS[shot.width] ?? FALLBACK_VIEWPORT_HEIGHT },
    deviceScaleFactor: DEVICE_SCALE_FACTOR,
    reducedMotion: 'reduce',
    colorScheme: COLOUR_SCHEME,
    locale: BROWSER_LOCALES[shot.language],
    bypassCSP: true,
  });
  try {
    await context.addInitScript(
      ({ key, language }) => {
        try {
          window.localStorage.setItem(key, language);
        } catch {
          // Storage refused: the app falls back to its default language and the language check reports it.
        }
      },
      { key: languageStorageKey, language: shot.language },
    );
    const page = await context.newPage();
    page.setDefaultTimeout(NAVIGATION_TIMEOUT_MS);
    await page.goto(routeUrl(baseUrl, shot.route.path), { waitUntil: NETWORK_IDLE });
    if (shot.route.waitFor) await page.waitForSelector(shot.route.waitFor, { state: VISIBLE });
    await page.addStyleTag({ content: STILL_PAGE_CSS });
    await page.evaluate(() => document.fonts.ready.then(() => true));
    await page.waitForTimeout(settleMs);
    const htmlLang = await page.evaluate(() => document.documentElement.lang);
    const maskRects = await page.evaluate(
      (selectors) =>
        selectors.flatMap((selector) =>
          [...document.querySelectorAll(selector)].map((element) => {
            const box = element.getBoundingClientRect();
            return { x: box.x + window.scrollX, y: box.y + window.scrollY, width: box.width, height: box.height };
          }),
        ),
      maskSelectors,
    );
    const png = await page.screenshot({ fullPage: true, animations: 'disabled', caret: 'hide' });
    let axe = null;
    if (axeSource) {
      await page.addScriptTag({ content: axeSource });
      axe = await page.evaluate(
        ({ tags, resultTypes }) => window.axe.run(document, { runOnly: { type: 'tag', values: tags }, resultTypes }),
        { tags: AXE_TAGS, resultTypes: AXE_RESULT_TYPES },
      );
    }
    return { png, htmlLang, maskRects, axe };
  } finally {
    await context.close();
  }
}

/** Decodes a PNG buffer into { width, height, data }. */
export function readPng(PNG, buffer) {
  return PNG.sync.read(buffer);
}

/** Encodes { width, height, data } as a PNG buffer. */
export function writePng(PNG, image) {
  const png = new PNG({ width: image.width, height: image.height });
  png.data = Buffer.from(image.data.buffer, image.data.byteOffset, image.data.byteLength);
  return PNG.sync.write(png);
}
