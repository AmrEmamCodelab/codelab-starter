import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  DEFAULT_SUBSET_STEM,
  collectIconUsage,
  driftedSubsetOutputs,
  findUnknownIconNames,
  findUsedIconNames,
  iconNamesOfAttribute,
  isIconNameInput,
  isIconSource,
  parseIconArguments,
  readIconMap,
  renderIconMap,
  runIconsSubset,
  subsetIconMap,
  subsetManifest,
  subsetOutputs,
  validateIconMap,
} from '../src/icons/index.mjs';
import { ExitCodes } from '../src/shared/arguments.mjs';

const MAP = {
  fontFamily: 'Codelab Icons',
  icons: {
    menu: { codepoint: 'E001', mirrorInRtl: false },
    'chevron-start': { codepoint: 'E002', mirrorInRtl: true },
    'chevron-down': { codepoint: 'E003', mirrorInRtl: false },
    home: { codepoint: 'E004', mirrorInRtl: false },
    search: { codepoint: 'E005', mirrorInRtl: false },
    close: { codepoint: 'E006', mirrorInRtl: false },
  },
};
const KNOWN = new Set(Object.keys(MAP.icons));
const used = (fileName, text) => [...findUsedIconNames(fileName, text, KNOWN)].sort();

/** A stand-in for @codelab/ng-dga's generator: the "font" is the manifest it was given, plus each SVG source. */
const FAKE_GENERATOR = `export async function buildIconFont(manifest, readSvg) {
  const glyphs = manifest.icons.map((icon) => icon.name + '=' + icon.codepoint + ':' + readSvg(icon.name));
  return { woff2: Buffer.from(manifest.fontFamily + '|' + manifest.fileStem + '|' + glyphs.join(','), 'utf8'), codepointMap: '', typescript: '' };
}
`;

describe('the icon map', () => {
  it('accepts the shipped shape and lists problems otherwise', () => {
    expect(validateIconMap(MAP)).toEqual([]);
    expect(validateIconMap([])).toEqual(['the icon map is not a JSON object.']);
    expect(validateIconMap({ icons: {} })).toEqual(['fontFamily is missing.', 'icons must be an object with at least one icon.']);
    expect(validateIconMap({ fontFamily: 'F', icons: { Bad_Name: { codepoint: 'e001' } } })).toEqual([
      "icon name 'Bad_Name' must be kebab case.",
      "icon 'Bad_Name' needs a four-digit upper-case hex codepoint.",
    ]);
  });
});

describe('icon inputs', () => {
  it('knows which attributes take an icon name', () => {
    expect(isIconNameInput('dga-icon', 'name')).toBe(true);
    expect(isIconNameInput('dga-icon', '[name]')).toBe(true);
    expect(isIconNameInput('input', 'name')).toBe(false);
    for (const name of ['icon', '[icon]', 'leadIcon', '[trailIcon]']) expect(isIconNameInput('dga-button', name), name).toBe(true);
    expect(isIconNameInput('dga-button', 'iconPosition')).toBe(false);
  });

  it('reads static names and the literals of a binding, never booleans or expressions', () => {
    expect(iconNamesOfAttribute('name', ' home ')).toEqual(['home']);
    expect(iconNamesOfAttribute('[name]', "open ? 'chevron-down' : \"menu\"")).toEqual(['chevron-down', 'menu']);
    expect(iconNamesOfAttribute('[name]', 'item.icon')).toEqual([]);
    expect(iconNamesOfAttribute('hideIcon', 'true')).toEqual([]);
    expect(iconNamesOfAttribute('name', null)).toEqual([]);
  });
});

describe('icon usage', () => {
  it('counts every quoted known name in a template, ignoring comments', () => {
    const text = `<dga-icon name="home" /><dga-icon [name]="open ? 'chevron-down' : 'chevron-start'" />
<!-- <dga-icon name="search" /> --><p class="menu-bar">menu</p>`;
    expect(used('a.component.html', text)).toEqual(['chevron-down', 'chevron-start', 'home']);
  });

  it('counts string literals and inline templates in TypeScript, ignoring comments', () => {
    const text = `// <dga-icon name="close" />
/** @example <dga-icon name="search" /> */
export const NAV = [{ icon: 'home' }, { icon: "menu" }];
@Component({ template: '<dga-icon name="chevron-down" />' })
export class X {}`;
    expect(used('a.ts', text)).toEqual(['chevron-down', 'home', 'menu']);
  });

  it('skips generated TypeScript, specs and declarations', () => {
    expect(isIconSource('dga-icon-names.generated.ts')).toBe(false);
    expect(isIconSource('a.spec.ts')).toBe(false);
    expect(isIconSource('a.d.ts')).toBe(false);
    expect(isIconSource('a.component.ts')).toBe(true);
    expect(isIconSource('a.component.html')).toBe(true);
  });

  it('reports literal names the map does not know, with source lines', () => {
    expect(findUnknownIconNames('a.html', '<p>\n<dga-icon name="serch" /><dga-button leadIcon="home" trailIcon="pluss"></dga-button>', KNOWN)).toEqual([
      { line: 2, column: 1, element: 'dga-icon', detail: 'serch' },
      { line: 2, column: 26, element: 'dga-button', detail: 'pluss' },
    ]);
    const source = "@Component({\n  template: `\n<dga-icon [name]=\"on ? 'home' : 'hom'\" />`,\n})\nexport class X {}";
    expect(findUnknownIconNames('a.ts', source, KNOWN)).toEqual([{ line: 3, column: 1, element: 'dga-icon', detail: 'hom' }]);
    expect(findUnknownIconNames('a.html', '<dga-icon [name]="item.icon" />', KNOWN)).toEqual([]);
  });
});

describe('the subset', () => {
  it('keeps the font order and the code points', () => {
    expect(subsetIconMap(MAP, ['search', 'menu'])).toEqual({
      fontFamily: 'Codelab Icons',
      icons: { menu: MAP.icons.menu, search: MAP.icons.search },
    });
    expect(subsetManifest(MAP, ['chevron-start'], 'stem')).toEqual({
      fontFamily: 'Codelab Icons',
      fileStem: 'stem',
      icons: [{ name: 'chevron-start', codepoint: 'E002', mirrorInRtl: true }],
    });
    expect(renderIconMap({ a: 1 })).toBe('{\n  "a": 1\n}\n');
  });
});

describe('collectIconUsage, outputs and the command line', () => {
  let dir;
  const write = (relative, text) => {
    const file = join(dir, relative);
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, typeof text === 'string' ? text : JSON.stringify(text));
    return file;
  };
  const io = () => {
    const sink = { logs: [], errors: [] };
    return { sink, io: { log: (line) => sink.logs.push(line), error: (line) => sink.errors.push(line) } };
  };

  beforeEach(() => {
    dir = mkdtempSync(join(tmpdir(), 'ng-tools-icons-'));
    write('map.json', MAP);
    write('app/a.component.html', '<dga-icon name="home" />');
    write('app/nav.ts', "export const NAV = [{ icon: 'search' }];");
    write('lib/dga-icon-names.generated.ts', "export const ALL = ['menu', 'close', 'chevron-down', 'chevron-start'];");
    write('lib/accordion.component.html', '<dga-icon name="chevron-down" />');
    for (const name of Object.keys(MAP.icons)) write(`svg/${name}.svg`, `<svg id="${name}"/>`);
    write('generator.mjs', FAKE_GENERATOR);
  });
  afterEach(() => rmSync(dir, { recursive: true, force: true }));

  it('collects the used icons over several folders in font order, plus kept names', () => {
    const result = collectIconUsage([join(dir, 'app'), join(dir, 'lib')], MAP, { keep: ['close'], baseDir: dir });
    expect(result).toEqual({ used: ['chevron-down', 'home', 'search', 'close'], unknown: [], fileCount: 3 });
    expect(collectIconUsage([join(dir, 'app')], MAP, { keep: ['nope'] }).unknown).toEqual([{ file: '--keep', line: 0, column: 0, detail: 'nope' }]);
    expect(collectIconUsage([join(dir, 'missing')], MAP).error).toMatch(/folder not found/);
  });

  it('builds the subset map and font through the generator contract, and detects drift', async () => {
    const outputs = await subsetOutputs({ map: MAP, used: ['home', 'search'], outDir: join(dir, 'out'), fileStem: 'icons', generator: join(dir, 'generator.mjs'), svgDir: join(dir, 'svg') });
    expect(outputs.map(({ file }) => file)).toEqual([join(dir, 'out', 'icons.json'), join(dir, 'out', 'icons.woff2')]);
    expect(JSON.parse(outputs[0].bytes.toString('utf8')).icons).toEqual({ home: MAP.icons.home, search: MAP.icons.search });
    expect(outputs[1].bytes.toString('utf8')).toBe('Codelab Icons|icons|home=E004:<svg id="home"/>,search=E005:<svg id="search"/>');
    expect(driftedSubsetOutputs(outputs)).toHaveLength(2);
    mkdirSync(join(dir, 'out'));
    for (const { file, bytes } of outputs) writeFileSync(file, bytes);
    expect(driftedSubsetOutputs(outputs)).toEqual([]);
    writeFileSync(outputs[0].file, outputs[0].bytes.toString('utf8').replace(/\n/g, '\r\n'));
    expect(driftedSubsetOutputs(outputs)).toEqual([]);
  });

  it('refuses a generator module without buildIconFont', async () => {
    write('not-a-generator.mjs', 'export const x = 1;\n');
    await expect(subsetOutputs({ map: MAP, used: ['home'], outDir: dir, fileStem: 's', generator: join(dir, 'not-a-generator.mjs'), svgDir: join(dir, 'svg') }))
      .rejects.toThrow(/does not export buildIconFont/);
  });

  it('parses the options and refuses inconsistent ones', () => {
    expect(parseIconArguments(['--dir', 'a', '--dir', 'b', '--map', 'm.json', '--keep', 'x,y', '--keep', 'z'])).toMatchObject({
      dirs: ['a', 'b'], map: 'm.json', keep: ['x', 'y', 'z'], stem: DEFAULT_SUBSET_STEM, check: false,
    });
    expect(parseIconArguments(['--map', 'm']).error).toMatch(/--dir is required/);
    expect(parseIconArguments(['--dir', 'a']).error).toMatch(/--map is required/);
    expect(parseIconArguments(['--dir', 'a', '--map', 'm', '--out', 'o', '--generator', 'g']).error).toMatch(/go together/);
    expect(parseIconArguments(['--dir', 'a', '--map', 'm', '--check']).error).toMatch(/need --out/);
  });

  it('reports the used set, writes, checks and fails on unknown icons', async () => {
    const base = ['--dir', join(dir, 'app'), '--dir', join(dir, 'lib'), '--map', join(dir, 'map.json')];
    const report = io();
    expect(await runIconsSubset(base, report.io)).toBe(ExitCodes.ok);
    expect(report.sink.logs[0]).toBe('icons: 3 of 6 used in 3 file(s): chevron-down, home, search');

    const out = join(dir, 'out');
    const font = ['--out', out, '--generator', join(dir, 'generator.mjs'), '--svg-dir', join(dir, 'svg')];
    expect(await runIconsSubset([...base, ...font, '--check'], io().io)).toBe(ExitCodes.failure);
    expect(await runIconsSubset([...base, ...font], io().io)).toBe(ExitCodes.ok);
    expect(existsSync(join(out, `${DEFAULT_SUBSET_STEM}.woff2`))).toBe(true);
    expect(Object.keys(readIconMap(join(out, `${DEFAULT_SUBSET_STEM}.json`)).map.icons)).toEqual(['chevron-down', 'home', 'search']);
    expect(await runIconsSubset([...base, ...font, '--check'], io().io)).toBe(ExitCodes.ok);
    expect(readFileSync(join(out, `${DEFAULT_SUBSET_STEM}.json`), 'utf8')).toMatch(/"fontFamily": "Codelab Icons"/);

    write('app/b.component.html', '<dga-icon name="serch" />');
    const failed = io();
    expect(await runIconsSubset(base, failed.io)).toBe(ExitCodes.failure);
    expect(failed.sink.errors[0]).toMatch(/b\.component\.html:1:1 unknown icon 'serch'/);
  });

  it('returns usage for bad arguments, a bad map or a failing generator', async () => {
    write('bad.json', '{');
    expect(await runIconsSubset([], io().io)).toBe(ExitCodes.usage);
    expect(await runIconsSubset(['--dir', join(dir, 'app'), '--map', join(dir, 'bad.json')], io().io)).toBe(ExitCodes.usage);
    expect(await runIconsSubset(['--dir', join(dir, 'missing'), '--map', join(dir, 'map.json')], io().io)).toBe(ExitCodes.usage);
    const broken = ['--dir', join(dir, 'app'), '--map', join(dir, 'map.json'), '--out', join(dir, 'o'), '--generator', join(dir, 'generator.mjs'), '--svg-dir', join(dir, 'nosvg')];
    expect(await runIconsSubset(broken, io().io)).toBe(ExitCodes.usage);
  });
});
