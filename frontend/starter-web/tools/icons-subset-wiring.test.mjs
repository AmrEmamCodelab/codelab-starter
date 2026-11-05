// @vitest-environment node
//
// Regression: the icon subset is wired into the build, not only reported. `npm run build` / `npm start` regenerate it,
// `npm run lint` (CI step 17) fails on a stale one, and src/styles.scss serves it in place of ng-dga's full font.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { DEFAULT_SUBSET_STEM } from '@codelab/ng-tools';
import { ExitCodes, NgDgaPaths, SUBSET_OUT_DIR, SUBSET_STEM, ngDgaFolder, run } from './icons-subset.mjs';

const APP_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const STYLES_FILE = path.join(APP_DIR, 'src/styles.scss');
const NG_DGA_FONT_FACE_FILE = 'styles/_icon-font.scss';
const SUBSET_SCRIPT = 'icons:subset';
const FONT_FILE = `${SUBSET_STEM}.woff2`;
const MAP_FILE = `${SUBSET_STEM}.json`;

const readText = (file) => fs.readFileSync(file, 'utf8');
const scripts = () => JSON.parse(readText(path.join(APP_DIR, 'package.json'))).scripts;

/** Each @font-face block with its descriptors, in source order. */
function fontFaces(scss) {
  return [...scss.matchAll(/@font-face\s*\{([^}]*)\}/g)].map((match) => ({
    index: match.index,
    descriptors: Object.fromEntries([...match[1].matchAll(/([\w-]+)\s*:\s*([^;]+);/g)].map(([, name, value]) => [name, value.trim()])),
  }));
}

const unquote = (value) => value.replace(/^['"]|['"]$/g, '');

describe('icon subset wiring', () => {
  it('regenerates the subset before build and serve, and checks it in lint', () => {
    const all = scripts();
    expect(all[SUBSET_SCRIPT]).toBe(`node tools/icons-subset.mjs --out ${SUBSET_OUT_DIR}`);
    expect(all.prebuild).toBe(`npm run ${SUBSET_SCRIPT}`);
    expect(all.prestart).toBe(`npm run ${SUBSET_SCRIPT}`);
    expect(all.lint).toContain(`node tools/icons-subset.mjs --out ${SUBSET_OUT_DIR} --check`);
  });

  it('serves the subset font in place of ng-dga\'s full font', () => {
    const styles = readText(STYLES_FILE);
    const faces = fontFaces(styles);
    const subsetFace = faces.at(-1);
    expect(subsetFace).toBeDefined();
    expect(subsetFace.index).toBeGreaterThan(styles.indexOf("@use '@codelab/ng-dga/styles'"));

    const map = JSON.parse(readText(path.join(ngDgaFolder(), NgDgaPaths.iconMap)));
    const [libraryFace] = fontFaces(readText(path.join(ngDgaFolder(), NG_DGA_FONT_FACE_FILE)));
    for (const descriptor of ['font-style', 'font-weight', 'font-display']) {
      expect(subsetFace.descriptors[descriptor]).toBe(libraryFace.descriptors[descriptor]);
    }
    expect(unquote(subsetFace.descriptors['font-family'])).toBe(map.fontFamily);

    const url = /url\(['"]?([^'")]+)['"]?\)/.exec(subsetFace.descriptors.src)[1];
    expect(path.resolve(path.dirname(STYLES_FILE), url)).toBe(path.resolve(APP_DIR, SUBSET_OUT_DIR, FONT_FILE));
  });

  it('commits a subset that is current and smaller than the full font', async () => {
    const sink = [];
    const io = { log: (line) => sink.push(line), error: (line) => sink.push(line) };
    expect(await run(['--out', SUBSET_OUT_DIR, '--check'], { appDir: APP_DIR, ...io }), sink.join('\n')).toBe(ExitCodes.ok);

    const subsetMap = JSON.parse(readText(path.resolve(APP_DIR, SUBSET_OUT_DIR, MAP_FILE)));
    const fullMap = JSON.parse(readText(path.join(ngDgaFolder(), NgDgaPaths.iconMap)));
    expect(Object.keys(subsetMap.icons).length).toBeLessThan(Object.keys(fullMap.icons).length);
    const fullFont = path.join(ngDgaFolder(), path.dirname(NgDgaPaths.iconMap), `${DEFAULT_SUBSET_STEM}.woff2`);
    expect(fs.statSync(path.resolve(APP_DIR, SUBSET_OUT_DIR, FONT_FILE)).size).toBeLessThan(fs.statSync(fullFont).size);
  });
});
