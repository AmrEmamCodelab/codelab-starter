// @vitest-environment node
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ExitCodes, checkTemplates, findInlineStyles, listTemplates, parseArguments, run } from './no-inline-styles.mjs';

describe('findInlineStyles', () => {
  it.each([
    ['<p style="margin-left:4px;color:#ff0000">x</p>', 'style='],
    ['<p STYLE = "color: red">x</p>', 'STYLE ='],
    ['<p [style.margin-left.px]="4">x</p>', '[style.'],
    ['<p [style]="styles">x</p>', '[style]'],
    ['<p [attr.style]="styles">x</p>', '[attr.style]'],
    ['<p [ngStyle]="{ color: colour }">x</p>', '[ngStyle]'],
  ])('refuses %s', (template, text) => {
    expect(findInlineStyles(template)).toEqual([{ line: 1, column: template.indexOf(text) + 1, text }]);
  });

  it.each([
    '<p class="note" [class.active]="on">x</p>',
    '<p data-style="plain" liststyle="none">x</p>',
    '<p>{{ \'style.title\' | translate }}</p>',
  ])('allows %s', (template) => {
    expect(findInlineStyles(template)).toEqual([]);
  });

  it('reports every finding with its line and column', () => {
    expect(findInlineStyles('<div>\r\n  <p style="a" [ngStyle]="b">x</p>\n</div>')).toEqual([
      { line: 2, column: 6, text: 'style=' },
      { line: 2, column: 16, text: '[ngStyle]' },
    ]);
  });
});

describe('checkTemplates and run', () => {
  let dir;
  const write = (relative, text) => {
    const file = path.join(dir, relative);
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, text);
  };

  beforeEach(() => {
    dir = fs.mkdtempSync(path.join(os.tmpdir(), 'no-inline-styles-'));
  });

  afterEach(() => {
    fs.rmSync(dir, { recursive: true, force: true });
  });

  it('lists only .html files and skips build output and dependencies', () => {
    write('app/a.component.html', '<p>a</p>');
    write('app/a.component.ts', 'style="x"');
    write('node_modules/x/b.html', '<p style="x">b</p>');
    write('dist/c.html', '<p style="x">c</p>');
    expect(listTemplates(dir)).toEqual([path.join(dir, 'app', 'a.component.html')]);
  });

  it('reports each inline style with its relative path', () => {
    write('app/clean.component.html', '<p class="x">ok</p>');
    write('app/bad.component.html', '<p>ok</p>\n<p [style.color]="c">x</p>');
    expect(checkTemplates(dir)).toEqual({ problems: ['app/bad.component.html:2:4 [style.'], fileCount: 2 });
  });

  it('reports a missing folder', () => {
    expect(checkTemplates(path.join(dir, 'missing')).problems).toHaveLength(1);
  });

  it('exits ok, failure and usage', () => {
    const log = [];
    const error = [];
    const sink = { appDir: dir, log: (line) => log.push(line), error: (line) => error.push(line) };
    write('src/app/ok.component.html', '<p class="x">ok</p>');
    expect(run([], sink)).toBe(ExitCodes.ok);
    write('src/app/bad.component.html', '<p style="color:#fff">x</p>');
    expect(run([], sink)).toBe(ExitCodes.failure);
    expect(error.at(-1)).toContain('base.fe-design-tokens');
    expect(error.at(-1)).toContain('app/bad.component.html:1:4 style=');
    expect(run(['--bogus'], sink)).toBe(ExitCodes.usage);
  });

  it('resolves --dir against the app folder', () => {
    expect(parseArguments(['--dir', 'x'], dir)).toEqual({ dir: path.join(dir, 'x') });
    expect(parseArguments([], dir)).toEqual({ dir: path.join(dir, 'src') });
  });
});
