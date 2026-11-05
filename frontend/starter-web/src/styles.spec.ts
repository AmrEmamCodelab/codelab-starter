// @vitest-environment node
//
// Regression: the app's icon-subset @font-face reused ng-dga's font basename (codelab-icons.woff2). The bundler
// flattens every font into media/ by basename, so a development build (no output hashing: `npm start`, ng serve on
// :4280 and the visual harness) failed with "Two output files share the same path but have different contents".
import { readFileSync, readdirSync } from 'node:fs';
import { createRequire } from 'node:module';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const SRC_DIR = dirname(fileURLToPath(import.meta.url));
const APP_STYLES = join(SRC_DIR, 'styles.scss');
const NG_DGA_MANIFEST = '@codelab/ng-dga/package.json';
const NG_DGA_STYLES_DIR = 'styles';
const SCSS_EXTENSION = '.scss';
/** The committed subset font that src/styles.scss serves (written by `npm run icons:subset`). */
const SUBSET_FONT = join(SRC_DIR, 'generated', 'icons', 'codelab-icons-subset.woff2');
const FONT_URL = /url\(\s*['"]?([^'")]+\.woff2)['"]?\s*\)/g;

/** Every .woff2 file a stylesheet references, as written in its url(). */
function fontUrls(scss: string): string[] {
  return [...scss.matchAll(FONT_URL)].map(match => match[1] ?? '');
}

/** Every .woff2 basename ng-dga's own styles reference (the files a build emits next to the app's). */
function ngDgaFontNames(): string[] {
  const stylesDir = join(dirname(createRequire(import.meta.url).resolve(NG_DGA_MANIFEST)), NG_DGA_STYLES_DIR);
  return readdirSync(stylesDir)
    .filter(file => file.endsWith(SCSS_EXTENSION))
    .flatMap(file => fontUrls(readFileSync(join(stylesDir, file), 'utf8')))
    .map(url => basename(url));
}

describe('src/styles.scss fonts', () => {
  const appUrls = fontUrls(readFileSync(APP_STYLES, 'utf8'));

  it('serves the committed icon subset', () => {
    expect(appUrls.map(url => resolve(SRC_DIR, url))).toContain(SUBSET_FONT);
  });

  it('never reuses a font basename ng-dga emits, so builds without output hashing do not collide', () => {
    const libraryNames = ngDgaFontNames();
    expect(libraryNames.length).toBeGreaterThan(0);
    const collisions = appUrls.map(url => basename(url)).filter(name => libraryNames.includes(name));
    expect(collisions).toEqual([]);
  });
});
