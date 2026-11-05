import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  RtlFindingKinds,
  checkRtl,
  findPhysicalStyleText,
  findRtlFindingsInSource,
  findRtlTemplateFindings,
  findUnmirroredIcons,
  formatRtlFindings,
  isDirectionalIcon,
  isPhysicalProperty,
  mirroredIconsOf,
  parseRtlArguments,
  runRtlLint,
} from '../src/rtl/index.mjs';
import { ExitCodes } from '../src/shared/arguments.mjs';

const K = RtlFindingKinds;
const kinds = (findings) => findings.map((finding) => finding.kind);
const template = (text, options) => kinds(findRtlTemplateFindings(text, options));
const script = (text, options) => kinds(findRtlFindingsInSource('a.component.ts', text, options));

const ICON_MAP = {
  fontFamily: 'Codelab Icons',
  icons: {
    'chevron-start': { codepoint: 'E001', mirrorInRtl: true },
    logout: { codepoint: 'E002', mirrorInRtl: true },
    search: { codepoint: 'E003', mirrorInRtl: false },
  },
};

describe('style text', () => {
  it.each([
    ['margin-left: 4px', K.physicalStyle],
    ['padding-right:0', K.physicalStyle],
    ['border-top-left-radius: 2px', K.physicalStyle],
    ['border-left-color: red', K.physicalStyle],
    ['left: 0', K.physicalStyle],
    ['text-align: left', K.physicalAlignment],
    ['float: right', K.physicalAlignment],
    ['clear: left', K.physicalAlignment],
  ])('flags %s', (text, kind) => {
    expect(kinds(findPhysicalStyleText(text))).toEqual([kind]);
  });

  it.each(['margin-inline-start: 4px', 'inset-inline-end: 0', 'text-align: start', 'float: inline-start', 'top: 0', 'padding-block: 2px'])('passes logical %s', (text) => {
    expect(findPhysicalStyleText(text)).toEqual([]);
  });

  it('reads camelCase and quoted keys only in expressions', () => {
    expect(kinds(findPhysicalStyleText("{ marginLeft: x, 'right.px': 4 }", { expression: true }))).toEqual([K.physicalStyle, K.physicalStyle]);
    expect(findPhysicalStyleText('marginLeft')).toEqual([]);
    expect(kinds(findPhysicalStyleText("{ textAlign: 'right' }", { expression: true }))).toEqual([K.physicalAlignment]);
  });

  it('names physical properties in kebab and camel case', () => {
    for (const name of ['left', 'right', 'float', 'cssFloat', 'margin-left', 'paddingRight', 'borderTopLeftRadius', 'inset-right']) expect(isPhysicalProperty(name), name).toBe(true);
    for (const name of ['top', 'margin-inline-start', 'paddingInlineEnd', 'color', 'text-align']) expect(isPhysicalProperty(name), name).toBe(false);
  });
});

describe('templates', () => {
  it.each([
    ['<div [style.margin-left.px]="gap"></div>', K.physicalStyleBinding],
    ['<div [style.left]="x"></div>', K.physicalStyleBinding],
    ['<div [style.paddingRight]="x"></div>', K.physicalStyleBinding],
    ['<div [style.float]="x"></div>', K.physicalStyleBinding],
    ['<p style="padding-right: 4px"></p>', K.physicalStyle],
    ['<p style="text-align:right"></p>', K.physicalAlignment],
    ['<p [style.text-align]="\'left\'"></p>', K.physicalAlignment],
    ['<p [ngStyle]="{ \'margin-left\': gap }"></p>', K.physicalStyle],
    ['<p [style]="{ marginRight: gap }"></p>', K.physicalStyle],
    ['<p dir="ltr"></p>', K.hardCodedDirection],
    ['<p dir="RTL"></p>', K.hardCodedDirection],
    ['<p [attr.dir]="\'rtl\'"></p>', K.hardCodedDirection],
    ['<dga-icon name="arrow-left" />', K.physicalIconName],
    ['<dga-button leadIcon="chevron-right"></dga-button>', K.physicalIconName],
    ['<dga-icon [name]="open ? \'chevron-down\' : \'chevron-left\'" />', K.physicalIconName],
  ])('flags %s', (text, kind) => {
    expect(template(text)).toEqual([kind]);
  });

  it.each([
    '<div [style.margin-inline-start.px]="gap" style="padding-inline-end: 4px"></div>',
    '<p [style.text-align]="align"></p>',
    '<p dir="auto"></p>',
    '<p [dir]="direction()"></p>',
    '<dga-icon name="chevron-start" />',
    '<dga-icon name="search" flipInRtl="false" />',
    '<dga-button hideIcon="true"></dga-button>',
    '<!-- <p dir="ltr" style="left: 0"></p> -->',
    '<p class="left right">{{ "left: 0" }}</p>',
  ])('passes %s', (text) => {
    expect(template(text)).toEqual([]);
  });

  it('refuses switching off the mirroring of a directional icon, from the map or by name', () => {
    const mirroredIcons = mirroredIconsOf(ICON_MAP);
    expect(template('<dga-icon name="logout" [flipInRtl]="false" />', { mirroredIcons })).toEqual([K.iconMirrorDisabled]);
    expect(template('<dga-icon name="search" [flipInRtl]="false" />', { mirroredIcons })).toEqual([]);
    expect(template('<dga-icon name="arrow-end" flipInRtl="false" />')).toEqual([K.iconMirrorDisabled]);
  });

  it('exempts an element marked data-fixed-direction, and a file allowed to fix the direction', () => {
    expect(template('<input dir="ltr" data-fixed-direction />')).toEqual([]);
    expect(template('<dga-icon name="logout" flipInRtl="false" data-fixed-direction />')).toEqual([]);
    expect(template('<html dir="rtl"></html>', { allowDirection: true })).toEqual([]);
    expect(template('<p data-fixed-direction style="margin-left: 1px"></p>')).toEqual([K.physicalStyle]);
  });

  it('reports the element and its position', () => {
    expect(findRtlTemplateFindings('<p>\n  <span dir="ltr"></span></p>')).toEqual([
      { kind: K.hardCodedDirection, detail: 'dir="ltr"', line: 2, column: 3, element: 'span' },
    ]);
  });
});

describe('TypeScript', () => {
  it.each([
    ["host: { '[style.margin-left.px]': 'gap' }", K.physicalStyleBinding],
    ['el.style.marginLeft = gap;', K.physicalStyleBinding],
    ["renderer.setStyle(el, 'padding-right', '4px');", K.physicalStyle],
    ["const css = 'left: 0; top: 0';", K.physicalStyle],
    ["const styles = { paddingLeft: '4px' };", K.physicalStyle],
    ["el.style.textAlign = 'right';", K.physicalAlignment],
    ["const css = `float: left`;", K.physicalAlignment],
    ["document.documentElement.setAttribute('dir', 'rtl');", K.hardCodedDirection],
    ["host.dir = 'ltr';", K.hardCodedDirection],
  ])('flags %s', (text, kind) => {
    expect(script(text)).toEqual([kind]);
  });

  it.each([
    '// el.style.marginLeft = gap;',
    "/* setAttribute('dir', 'rtl') */",
    "el.style.marginInlineStart = gap;",
    "const align = 'start';",
    "const tree = { left: node, right: node };",
    "document.documentElement.setAttribute('dir', directionOf(language));",
    "const message = 'turn left';",
  ])('passes %s', (text) => {
    expect(script(text)).toEqual([]);
  });

  it('checks inline templates with the template rules and source lines, once', () => {
    const source = "@Component({\n  selector: 'app-x',\n  template: `\n    <p style=\"margin-left: 4px\"></p>`,\n})\nexport class X {}\n";
    expect(findRtlFindingsInSource('x.component.ts', source)).toEqual([
      { kind: K.physicalStyle, detail: 'margin-left', line: 4, column: 5, element: 'p' },
    ]);
  });

  it('honours allowDirection in TypeScript too', () => {
    expect(script("el.setAttribute('dir', 'rtl');", { allowDirection: true })).toEqual([]);
  });
});

describe('the icon map', () => {
  it('knows directional icons by name', () => {
    for (const name of ['chevron-start', 'arrow-end', 'logout', 'login']) expect(isDirectionalIcon(name), name).toBe(true);
    for (const name of ['chevron-up', 'search', 'send-mail', 'endpoint']) expect(isDirectionalIcon(name), name).toBe(false);
  });

  it('refuses a directional icon that does not mirror', () => {
    expect(findUnmirroredIcons(ICON_MAP)).toEqual([]);
    const map = { icons: { ...ICON_MAP.icons, 'arrow-end': { codepoint: 'E004', mirrorInRtl: false }, login: { codepoint: 'E005' } } };
    expect(findUnmirroredIcons(map)).toEqual([
      { kind: K.iconNotMirrored, detail: 'arrow-end' },
      { kind: K.iconNotMirrored, detail: 'login' },
    ]);
  });
});

describe('checkRtl and the command line', () => {
  let dir;
  const write = (relative, text) => {
    const file = join(dir, relative);
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, typeof text === 'string' ? text : JSON.stringify(text));
  };
  const io = () => {
    const sink = { logs: [], errors: [] };
    return { sink, io: { log: (line) => sink.logs.push(line), error: (line) => sink.errors.push(line) } };
  };

  beforeEach(() => { dir = mkdtempSync(join(tmpdir(), 'ng-tools-rtl-')); });
  afterEach(() => rmSync(dir, { recursive: true, force: true }));

  it('checks templates and sources, skips specs, and lets index.html fix the direction', () => {
    write('src/index.html', '<html lang="ar" dir="rtl"></html>');
    write('src/app/a.component.html', '<p style="margin-right: 1px"></p>');
    write('src/app/a.component.ts', "export const css = 'float: left';");
    write('src/app/a.component.spec.ts', "el.setAttribute('dir', 'ltr');");
    write('src/app/node_modules/x.html', '<p dir="ltr"></p>');
    const result = checkRtl(join(dir, 'src'));
    expect(result.fileCount).toBe(3);
    expect(result.findings.map(({ file, kind }) => `${file} ${kind}`)).toEqual([
      `app/a.component.html ${K.physicalStyle}`,
      `app/a.component.ts ${K.physicalAlignment}`,
    ]);
  });

  it('reports an unknown folder', () => {
    expect(checkRtl(join(dir, 'missing')).error).toMatch(/folder not found/);
  });

  it('formats findings with the rule id and the fix', () => {
    const text = formatRtlFindings([{ kind: K.hardCodedDirection, file: 'a.html', line: 1, column: 2, detail: 'dir="ltr"' }], 'see the recipe');
    expect(text).toMatch(/base\.fe-tokens-logical-css/);
    expect(text).toMatch(/a\.html:1:2 hard-codes the text direction \(hard-coded-direction\): dir="ltr"/);
    expect(text).toMatch(/Fix: see the recipe$/);
  });

  it('parses a folder, an icon map and direction files', () => {
    expect(parseRtlArguments(['src'])).toEqual({ dir: 'src', icons: undefined, fix: undefined, directionFiles: ['index.html'] });
    expect(parseRtlArguments(['src', '--icons', 'm.json', '--direction-file', 'a.html', '--direction-file', 'b/**'])).toMatchObject({ icons: 'm.json', directionFiles: ['a.html', 'b/**'] });
    expect(parseRtlArguments([]).error).toMatch(/folder/);
    expect(parseRtlArguments(['a', 'b']).error).toMatch(/Unexpected/);
    expect(parseRtlArguments(['a', '--icons']).error).toMatch(/needs a value/);
  });

  it('returns 0 clean, 1 on a finding and 2 on usage or a bad map', () => {
    write('src/a.html', '<p class="x"></p>');
    write('map.json', ICON_MAP);
    write('bad.json', '{ "icons": [] }');
    expect(runRtlLint([join(dir, 'src'), '--icons', join(dir, 'map.json')], io().io)).toBe(ExitCodes.ok);
    write('src/b.html', '<dga-icon name="logout" flipInRtl="false" />');
    const failed = io();
    expect(runRtlLint([join(dir, 'src'), '--icons', join(dir, 'map.json')], failed.io)).toBe(ExitCodes.failure);
    expect(failed.sink.errors[0]).toMatch(/icon-mirror-disabled/);
    expect(runRtlLint([join(dir, 'src'), '--icons', join(dir, 'bad.json')], io().io)).toBe(ExitCodes.usage);
    expect(runRtlLint([join(dir, 'missing')], io().io)).toBe(ExitCodes.usage);
    expect(runRtlLint([], io().io)).toBe(ExitCodes.usage);
  });
});
