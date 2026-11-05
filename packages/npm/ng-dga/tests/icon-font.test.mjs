// The owned icon font regenerates deterministically and offline: a fresh build is byte-identical to a second build and
// to the committed outputs (assets/icons/*.woff2 and *.json, src/lib/icon/dga-icon-names.generated.ts).
import { describe, expect, it } from 'vitest';
import { driftedOutputs, generateOutputs, main, svgSourceNames } from '../scripts/generate-icon-font.mjs';
import { readJson } from '../scripts/lib/files.mjs';
import { PRIVATE_USE_FIRST, renderCodepointMap, renderTypeScript, validateIconManifest } from '../scripts/lib/icon-font.mjs';
import { ICON_MANIFEST_FILE } from '../scripts/lib/package-paths.mjs';

const EXIT_OK = 0;
const EXIT_USAGE = 2;

describe('icon font regeneration', () => {
  it('builds the same bytes twice', async () => {
    const first = await generateOutputs();
    const second = await generateOutputs();
    expect(second.map((output) => output.file)).toEqual(first.map((output) => output.file));
    first.forEach((output, index) => expect(Buffer.compare(output.bytes, second[index].bytes)).toBe(0));
  });

  it('matches the committed outputs', async () => {
    const drifted = driftedOutputs(await generateOutputs());
    expect(drifted.map((output) => output.file)).toEqual([]);
  });

  it('passes --check and refuses unknown options', async () => {
    expect(await main(['--check'])).toBe(EXIT_OK);
    expect(await main(['--bogus'])).toBe(EXIT_USAGE);
  });

  it('has an SVG source for every icon in the manifest and nothing else', () => {
    const manifest = readJson(ICON_MANIFEST_FILE);
    expect(validateIconManifest(manifest, svgSourceNames())).toEqual([]);
  });
});

describe('validateIconManifest', () => {
  const svgNames = new Set(['menu', 'user']);
  const valid = { icons: [{ name: 'menu', codepoint: 'E001' }, { name: 'user', codepoint: 'E002' }] };

  it('accepts a valid manifest', () => {
    expect(validateIconManifest(valid, svgNames)).toEqual([]);
  });

  it('rejects bad names, duplicates, bad or reused code points and missing or extra sources', () => {
    const manifest = {
      icons: [
        { name: 'Menu', codepoint: 'E001' },
        { name: 'user', codepoint: 'E001' },
        { name: 'user', codepoint: '0041' },
        { name: 'lost', codepoint: 'e00x' },
      ],
    };
    const text = validateIconManifest(manifest, svgNames).join('\n');
    expect(text).toContain("icon name 'Menu' must be kebab-case");
    expect(text).toContain("icon 'user' is listed twice");
    expect(text).toContain('reuses code point U+E001');
    expect(text).toContain('outside the Private Use Area');
    expect(text).toContain('must be four upper-case hex digits');
    expect(text).toContain("icon 'lost' has no source");
    expect(text).toContain('icons/svg/menu.svg is not in icons.json');
  });

  it('rejects an empty manifest', () => {
    expect(validateIconManifest({ icons: [] }, new Set())).toContain('icons.json lists no icons.');
  });
});

describe('generated icon modules', () => {
  const manifest = readJson(ICON_MANIFEST_FILE);

  it('maps every icon to its Private Use Area code point', () => {
    const map = JSON.parse(renderCodepointMap(manifest));
    expect(JSON.stringify(map)).toContain('E001');
    expect(PRIVATE_USE_FIRST).toBe(0xe000);
  });

  it('lists every icon name and the mirrored icons in the TypeScript module', () => {
    const source = renderTypeScript(manifest);
    for (const icon of manifest.icons) expect(source).toContain(`'${icon.name}'`);
    expect(source).toContain('DGA_ICONS_MIRRORED_IN_RTL');
  });
});
