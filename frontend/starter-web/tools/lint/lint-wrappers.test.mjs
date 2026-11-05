// @vitest-environment node
//
// The app wrappers around `codelab-ng-tools rtl-lint` (tools/rtl-lint.mjs) and `icons-subset` (tools/icons-subset.mjs):
// their folders, the ng-dga icon map they point at, and their exit codes. The checks themselves are tested in
// @codelab/ng-tools (tests/rtl-lint.test.mjs, tests/icons-subset.test.mjs).
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import * as iconsSubset from '../icons-subset.mjs';
import * as rtlLint from '../rtl-lint.mjs';

const MAP = { fontFamily: 'Codelab Icons', icons: { home: { codepoint: 'E001' }, 'chevron-end': { codepoint: 'E002', mirrorInRtl: true } } };

function quiet() {
  const sink = { logs: [], errors: [] };
  return { sink, io: { log: (line) => sink.logs.push(line), error: (line) => sink.errors.push(line) } };
}

let dir;
const write = (relative, value) => {
  const file = path.join(dir, relative);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, typeof value === 'string' ? value : JSON.stringify(value));
  return file;
};

beforeEach(() => { dir = fs.mkdtempSync(path.join(os.tmpdir(), 'starter-lint-wrappers-')); });
afterEach(() => fs.rmSync(dir, { recursive: true, force: true }));

describe('tools/rtl-lint.mjs', () => {
  it('defaults to src and accepts --dir', () => {
    expect(rtlLint.parseArguments([], dir)).toEqual({ dir: path.resolve(dir, rtlLint.DEFAULT_SOURCE_DIR) });
    expect(rtlLint.parseArguments(['--dir', 'x'], dir)).toEqual({ dir: path.resolve(dir, 'x') });
    expect(rtlLint.parseArguments(['--x'], dir).error).toMatch(/Usage/);
    expect(rtlLint.run(['--x'], { appDir: dir, ...quiet().io })).toBe(rtlLint.ExitCodes.usage);
  });

  it('points at the icon map @codelab/ng-dga ships and lets index.html fix the direction', () => {
    const args = rtlLint.lintArguments('src');
    expect(args[0]).toBe('src');
    expect(args[args.indexOf('--icons') + 1].replaceAll('\\', '/')).toMatch(/assets\/icons\/codelab-icons\.json$/);
    expect(fs.existsSync(args[args.indexOf('--icons') + 1])).toBe(true);
    expect(args.slice(args.indexOf('--direction-file'))).toEqual(['--direction-file', 'index.html']);
  });

  it('passes logical markup and fails a physical side', () => {
    write('src/index.html', '<html lang="ar" dir="rtl"></html>');
    write('src/app/a.component.html', '<p class="x"></p>');
    expect(rtlLint.run([], { appDir: dir, ...quiet().io })).toBe(rtlLint.ExitCodes.ok);
    write('src/app/b.component.html', '<p style="margin-left: 4px"></p>');
    const { sink, io } = quiet();
    expect(rtlLint.run([], { appDir: dir, ...io })).toBe(rtlLint.ExitCodes.failure);
    expect(sink.errors[0]).toMatch(/app\/b\.component\.html:1:1 names a physical side/);
  });
});

describe('tools/icons-subset.mjs', () => {
  it('parses --out and --check, and refuses --check without --out', () => {
    expect(iconsSubset.parseArguments([], dir)).toEqual({ check: false });
    expect(iconsSubset.parseArguments(['--out', 'o', '--check'], dir)).toEqual({ check: true, out: path.resolve(dir, 'o') });
    expect(iconsSubset.parseArguments(['--check'], dir).error).toMatch(/Usage/);
    expect(iconsSubset.parseArguments(['--x'], dir).error).toMatch(/Usage/);
  });

  it('scans the app and the ng-dga component sources against the shipped map, and builds the font only with --out', () => {
    const ngDga = path.join(dir, 'ng-dga');
    write('ng-dga/src/lib/a.html', '');
    write('ng-dga/scripts/lib/icon-font.mjs', '');
    write('ng-dga/icons/svg/home.svg', '');
    const plain = iconsSubset.subsetArguments({ check: false }, { appDir: dir, ngDga });
    expect(plain).toEqual([
      '--dir', path.resolve(dir, iconsSubset.DEFAULT_SOURCE_DIR),
      '--dir', path.join(ngDga, iconsSubset.NgDgaPaths.componentSources),
      '--map', path.join(ngDga, iconsSubset.NgDgaPaths.iconMap),
    ]);
    const withOut = iconsSubset.subsetArguments({ out: 'o', check: true }, { appDir: dir, ngDga });
    expect(withOut.slice(plain.length)).toEqual([
      '--out', 'o',
      '--stem', iconsSubset.SUBSET_STEM,
      '--generator', path.join(ngDga, iconsSubset.NgDgaPaths.generator),
      '--svg-dir', path.join(ngDga, iconsSubset.NgDgaPaths.svgSources),
      '--check',
    ]);
  });

  it('leaves out the library sources and the font when the package has none', () => {
    const args = iconsSubset.subsetArguments({ out: 'o', check: false }, { appDir: dir, ngDga: path.join(dir, 'empty') });
    expect(args.filter((arg) => arg === '--dir')).toHaveLength(1);
    expect(args).not.toContain('--generator');
  });

  it('resolves the installed @codelab/ng-dga package, which ships the icon map', () => {
    expect(fs.existsSync(path.join(iconsSubset.ngDgaFolder(), iconsSubset.NgDgaPaths.iconMap))).toBe(true);
  });

  it('returns usage for bad arguments', async () => {
    expect(await iconsSubset.run(['--check'], { appDir: dir, ...quiet().io })).toBe(iconsSubset.ExitCodes.usage);
  });

  it('runs the subset on the app folder and fails an unknown icon', async () => {
    write('map.json', MAP);
    write('src/app/a.component.html', '<dga-icon name="home" />');
    const args = ['--dir', path.join(dir, 'src', 'app'), '--map', path.join(dir, 'map.json')];
    const { runIconsSubset } = await import('@codelab/ng-tools');
    expect(await runIconsSubset(args, quiet().io)).toBe(iconsSubset.ExitCodes.ok);
    write('src/app/b.component.html', '<dga-icon name="hom" />');
    expect(await runIconsSubset(args, quiet().io)).toBe(iconsSubset.ExitCodes.failure);
  });
});
