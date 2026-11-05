// Every default and limit of the visual + accessibility harness (e2e/visual.mjs). Nothing else in e2e/ writes a
// bare threshold, width, selector or key.

/** Exit codes of e2e/visual.mjs. */
export const ExitCodes = Object.freeze({
  /** Every screenshot within its threshold and no serious or critical axe violation. */
  Ok: 0,
  /** A screenshot over its threshold, a missing baseline, a language that did not apply, or an axe failure. */
  Failed: 1,
  /** Bad arguments or an unreadable routes file. */
  Usage: 2,
  /** The browser or a dependency could not start, or the app was unreachable. */
  Environment: 3,
});

/** The two harness modes. */
export const Modes = Object.freeze({
  /** Compare each screenshot with its stored baseline (or store it with --update). */
  Baseline: 'baseline',
  /** Compare each screenshot with a design target image (the design-agent loop). */
  Target: 'target',
});

/** Share of differing pixels allowed per screenshot against a baseline: 0.1 %. */
export const BASELINE_MAX_DIFF_RATIO = 0.001;
/** Share of differing pixels allowed per screenshot against a design target: 5 %. */
export const TARGET_MAX_DIFF_RATIO = 0.05;
/** pixelmatch per-pixel colour tolerance (0 strict ... 1 lenient), with anti-aliased pixels ignored. */
export const PIXEL_THRESHOLD = 0.1;

export const DEFAULT_LANGUAGES = Object.freeze(['ar', 'en']);
export const SUPPORTED_LANGUAGES = Object.freeze(['ar', 'en']);
/** Desktop and phone widths. */
export const DEFAULT_WIDTHS = Object.freeze([1440, 390]);
/** Viewport height per width; full-page screenshots grow beyond it. */
export const VIEWPORT_HEIGHTS = Object.freeze({ 1440: 900, 390: 844 });
export const FALLBACK_VIEWPORT_HEIGHT = 900;
export const MIN_WIDTH = 240;
export const MAX_WIDTH = 3840;

/** Regions whose content changes between runs (clocks, counters, generated ids) carry this attribute. */
export const DEFAULT_MASK_SELECTORS = Object.freeze(['[data-visual-mask]']);
/** Masked regions are painted with this opaque RGBA colour in both images before comparing. */
export const MASK_COLOUR = Object.freeze({ r: 255, g: 0, b: 255, a: 255 });
/** Pixels added to pad a smaller image to the size of the other one: never equal to page content. */
export const PADDING_COLOUR = Object.freeze({ r: 0, g: 0, b: 0, a: 0 });

/** The browser storage key the app reads its language from (core/session/storage-keys.ts StorageKeys.Language). */
export const DEFAULT_LANGUAGE_STORAGE_KEY = 'starter.language';

/** Installed Microsoft Edge through playwright-core: no browser download. */
export const BROWSER_CHANNEL = 'msedge';
/** The browser locale per app language (number and date formatting in the page). */
export const BROWSER_LOCALES = Object.freeze({ ar: 'ar-SA', en: 'en-US' });
export const COLOUR_SCHEME = 'light';
export const NAVIGATION_TIMEOUT_MS = 30000;
/** Quiet time after load, fonts and network idle, before the screenshot. */
export const DEFAULT_SETTLE_MS = 500;
export const DEVICE_SCALE_FACTOR = 1;

/** axe-core rule tags that are run: WCAG 2.0 / 2.1 A and AA. */
export const AXE_TAGS = Object.freeze(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']);
/** Impacts that fail the run. */
export const AXE_BLOCKING_IMPACTS = Object.freeze(['serious', 'critical']);
export const AXE_IMPACTS = Object.freeze(['minor', 'moderate', 'serious', 'critical']);

/** Placeholders a --target path may contain, replaced per screenshot. */
export const TargetPlaceholders = Object.freeze({ Route: '{route}', Language: '{lang}', Width: '{width}' });

export const REPORT_JSON = 'visual-report.json';
export const REPORT_MARKDOWN = 'visual-report.md';
export const ACTUAL_DIR = 'actual';
export const DIFF_DIR = 'diff';
export const PNG_EXTENSION = '.png';
/** The default output folder, below the OS temp folder. */
export const DEFAULT_OUTPUT_FOLDER = 'codelab-visual';

/** CSS injected before each screenshot: no motion, no blinking caret. */
export const STILL_PAGE_CSS =
  '*, *::before, *::after { animation: none !important; transition: none !important; caret-color: transparent !important; }';

/** Packages the harness loads at run time (root devDependencies). */
export const RuntimePackages = Object.freeze({
  Playwright: 'playwright-core',
  Pixelmatch: 'pixelmatch',
  Png: 'pngjs',
  Axe: 'axe-core',
});
