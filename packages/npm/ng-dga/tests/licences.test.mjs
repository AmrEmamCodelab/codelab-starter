// ADR-009 as a test: every file under assets/ has a LICENCES.json entry, copied files match their recorded sha256,
// and the manifest sources agree with the ADR-009 sources table. The rule functions are also tested on fixtures.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { listFilesRelative, normalizedBytes, readJson, sha256 } from '../scripts/lib/files.mjs';
import { compareWithAdr, parseAdrSources, validateLicences } from '../scripts/lib/licences.mjs';
import { ADR_FILE, ASSETS_DIR, LICENCES_FILE, LICENCES_FILE_NAME } from '../scripts/lib/package-paths.mjs';

function realAssetFiles() {
  return listFilesRelative(ASSETS_DIR).filter((file) => file !== LICENCES_FILE_NAME);
}

function hashOf(relativePath) {
  const file = path.join(ASSETS_DIR, relativePath);
  return sha256(normalizedBytes(file, readFileSync(file)));
}

const SOURCE = {
  id: 'demo',
  title: 'Demo',
  package: 'demo-package',
  version: '1.0.0',
  licence: 'MIT',
  copyright: 'Copyright Demo',
  licenceFile: 'demo/LICENSE.txt',
};

function fixtureManifest(files) {
  return { sources: [SOURCE], files };
}

const LICENCE_ENTRY = { path: 'demo/LICENSE.txt', source: 'demo', from: 'LICENSE', sha256: 'h-licence' };
const FONT_ENTRY = { path: 'demo/font.woff2', source: 'demo', from: 'font.woff2', sha256: 'h-font' };
const FIXTURE_HASHES = { 'demo/LICENSE.txt': 'h-licence', 'demo/font.woff2': 'h-font' };
const fixtureHash = (file) => FIXTURE_HASHES[file];

describe('the committed assets (ADR-009)', () => {
  it('every asset has a licence entry and every copied asset matches its recorded sha256', () => {
    const errors = validateLicences({ manifest: readJson(LICENCES_FILE), assetFiles: realAssetFiles(), hashOf });
    expect(errors).toEqual([]);
  });

  it('the manifest sources agree with the ADR-009 sources table', () => {
    const rows = parseAdrSources(readFileSync(ADR_FILE, 'utf8'));
    expect(rows.length).toBeGreaterThan(0);
    expect(compareWithAdr(readJson(LICENCES_FILE), rows)).toEqual([]);
  });
});

describe('validateLicences', () => {
  it('accepts a complete manifest', () => {
    const errors = validateLicences({
      manifest: fixtureManifest([LICENCE_ENTRY, FONT_ENTRY]),
      assetFiles: ['demo/LICENSE.txt', 'demo/font.woff2'],
      hashOf: fixtureHash,
    });
    expect(errors).toEqual([]);
  });

  it('fails when an asset has no entry', () => {
    const errors = validateLicences({
      manifest: fixtureManifest([LICENCE_ENTRY]),
      assetFiles: ['demo/LICENSE.txt', 'demo/extra.woff2'],
      hashOf: fixtureHash,
    });
    expect(errors.some((error) => error.includes('demo/extra.woff2 has no entry'))).toBe(true);
  });

  it('fails on a stale hash, a missing file, a duplicate, an unknown source and a bad entry kind', () => {
    const errors = validateLicences({
      manifest: fixtureManifest([
        LICENCE_ENTRY,
        { ...FONT_ENTRY, sha256: 'stale' },
        { path: 'demo/gone.woff2', source: 'demo', generatedBy: 'x' },
        { ...LICENCE_ENTRY },
        { path: 'demo/other.txt', source: 'nobody', from: 'a', generatedBy: 'b' },
      ]),
      assetFiles: ['demo/LICENSE.txt', 'demo/font.woff2', 'demo/other.txt'],
      hashOf: fixtureHash,
    });
    const text = errors.join('\n');
    expect(text).toContain('demo/font.woff2 does not match its recorded sha256');
    expect(text).toContain('demo/gone.woff2 is listed but missing');
    expect(text).toContain('demo/LICENSE.txt is listed twice');
    expect(text).toContain("unknown source 'nobody'");
    expect(text).toContain('needs exactly one of');
  });

  it('fails when a source misses a field, ships no licence file or is unused', () => {
    const errors = validateLicences({
      manifest: { sources: [{ ...SOURCE, copyright: '' }, { ...SOURCE, id: 'unused', licenceFile: 'unused/LICENSE' }], files: [LICENCE_ENTRY] },
      assetFiles: ['demo/LICENSE.txt'],
      hashOf: fixtureHash,
    });
    const text = errors.join('\n');
    expect(text).toContain("source 'demo' has no copyright");
    expect(text).toContain("source 'unused' licence file unused/LICENSE is not a listed asset");
    expect(text).toContain("source 'unused' is used by no asset");
  });
});

describe('parseAdrSources and compareWithAdr', () => {
  const ADR = [
    '# ADR',
    '',
    '| Other | Table |',
    '| --- | --- |',
    '| x | y |',
    '',
    '| Source | Package | Version | Licence | Notes |',
    '| --- | --- | --- | --- | --- |',
    '| `demo` | `demo-package` | 1.0.0 | MIT | fonts |',
    '',
  ].join('\n');

  it('reads the sources table and skips other tables', () => {
    expect(parseAdrSources(ADR)).toEqual([{ source: 'demo', package: 'demo-package', version: '1.0.0', licence: 'MIT' }]);
    expect(parseAdrSources('no table here')).toEqual([]);
  });

  it('agrees with a matching manifest', () => {
    expect(compareWithAdr(fixtureManifest([]), parseAdrSources(ADR))).toEqual([]);
  });

  it('reports disagreements in both directions', () => {
    const manifest = { sources: [{ ...SOURCE, version: '2.0.0' }, { ...SOURCE, id: 'extra' }] };
    const rows = [...parseAdrSources(ADR), { source: 'adr-only', package: 'p', version: '1', licence: 'MIT' }];
    const text = compareWithAdr(manifest, rows).join('\n');
    expect(text).toContain("source 'demo' version: LICENCES.json '2.0.0', ADR-009 '1.0.0'");
    expect(text).toContain("source 'extra' is in LICENCES.json but not in the ADR-009 sources table");
    expect(text).toContain("ADR-009 lists source 'adr-only'");
  });
});
