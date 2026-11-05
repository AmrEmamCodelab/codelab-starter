import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { Subcommands, main } from '../src/cli/main.mjs';
import { ExitCodes, parseArguments, splitList } from '../src/shared/arguments.mjs';
import { blank, blankRanges, lexScript } from '../src/shared/script-lexer.mjs';
import { globToRegExp, isTemplateOrScript, listSourceFiles, matchesAnyGlob, positionFinder, toPosixRelative } from '../src/shared/source-files.mjs';
import { literalValuesOf, quotedLiterals } from '../src/shared/template-values.mjs';

const BIN = fileURLToPath(new URL('../src/bin/codelab-ng-tools.mjs', import.meta.url));

function capture() {
  const sink = { logs: [], errors: [] };
  return { sink, io: { log: (line) => sink.logs.push(line), error: (line) => sink.errors.push(line) } };
}

describe('parseArguments', () => {
  const spec = { values: ['--map'], repeatable: ['--dir'], flags: ['--check'], positionals: 1 };

  it('reads values, repeatable values, flags and positionals', () => {
    expect(parseArguments(['src', '--dir', 'a', '--map', 'm', '--dir', 'b', '--check'], spec)).toEqual({
      options: { dir: ['a', 'b'], map: 'm', check: true },
      positionals: ['src'],
    });
    expect(parseArguments([], spec)).toEqual({ options: { dir: [], check: false }, positionals: [] });
  });

  it('refuses unknown options, missing values and extra positionals', () => {
    expect(parseArguments(['--nope'], spec).error).toBe('Unexpected argument: --nope');
    expect(parseArguments(['--map'], spec).error).toBe('--map needs a value.');
    expect(parseArguments(['--map', '--check'], spec).error).toBe('--map needs a value.');
    expect(parseArguments(['a', 'b'], spec).error).toBe('Unexpected argument: b');
  });

  it('splits comma-separated lists', () => {
    expect(splitList(['a, b', 'c', ' ,'])).toEqual(['a', 'b', 'c']);
  });
});

describe('the script lexer', () => {
  it('blanks comments, keeps code and lists string literals with offsets', () => {
    const source = "const a = 'x'; // note\n/* block\n */ const b = `t${a}`;\nconst re = /'/g;";
    const { code, strings } = lexScript(source);
    expect(code.length).toBe(source.length);
    expect(code).not.toMatch(/note|block/);
    expect(code.split('\n')).toHaveLength(source.split('\n').length);
    expect(strings.map(({ raw, templateLiteral, substitution }) => ({ raw, templateLiteral, substitution }))).toEqual([
      { raw: 'x', templateLiteral: false, substitution: false },
      { raw: 't${a}', templateLiteral: true, substitution: true },
    ]);
    expect(source.slice(strings[0].offset, strings[0].end)).toBe("'x'");
  });

  it('blanks text and ranges without moving offsets or lines', () => {
    expect(blank('ab\ncd')).toBe('  \n  ');
    expect(blankRanges('abcdef', [{ offset: 1, end: 3 }])).toBe('a  def');
  });
});

describe('source files', () => {
  let dir;
  const write = (relative) => {
    const file = join(dir, relative);
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, '');
  };
  beforeEach(() => { dir = mkdtempSync(join(tmpdir(), 'ng-tools-files-')); });
  afterEach(() => rmSync(dir, { recursive: true, force: true }));

  it('lists templates and sources, skipping tests, declarations and dependency folders', () => {
    for (const file of ['a/x.ts', 'a/x.html', 'a/x.spec.ts', 'a/x.d.ts', 'a/x.testing.ts', 'a/x.css', 'node_modules/y.ts', 'dist/z.html']) write(file);
    expect(listSourceFiles(dir).map((file) => toPosixRelative(dir, file))).toEqual(['a/x.html', 'a/x.ts']);
    expect(isTemplateOrScript('x.test.ts')).toBe(false);
  });

  it('maps offsets to 1-based lines and columns', () => {
    const positionOf = positionFinder('ab\ncd\n\nef');
    expect([0, 1, 3, 6, 7, 8].map((offset) => positionOf(offset))).toEqual([
      { line: 1, column: 1 }, { line: 1, column: 2 }, { line: 2, column: 1 }, { line: 3, column: 1 }, { line: 4, column: 1 }, { line: 4, column: 2 },
    ]);
  });

  it('matches globs on forward-slash paths', () => {
    expect(globToRegExp('index.html').test('src/index.html')).toBe(true);
    expect(globToRegExp('index.html').test('index.html')).toBe(true);
    expect(globToRegExp('src/*.html').test('src/a/b.html')).toBe(false);
    expect(globToRegExp('src/**/b.html').test('src/a/c/b.html')).toBe(true);
    expect(globToRegExp('src/**/b.html').test('src/b.html')).toBe(true);
    expect(globToRegExp('a.b').test('axb')).toBe(false);
    expect(matchesAnyGlob('x/y.ts', ['*.html', 'x/**'])).toBe(true);
  });

  it('reads literal values of attributes', () => {
    expect(quotedLiterals("a ? 'x' : \"y\"")).toEqual(['x', 'y']);
    expect(literalValuesOf('title', ' t ')).toEqual(['t']);
    expect(literalValuesOf('[title]', "'t'")).toEqual(['t']);
    expect(literalValuesOf('title', null)).toEqual([]);
  });
});

describe('the codelab-ng-tools dispatcher', () => {
  let dir;
  beforeEach(() => {
    dir = mkdtempSync(join(tmpdir(), 'ng-tools-cli-'));
    mkdirSync(join(dir, 'i18n'));
    writeFileSync(join(dir, 'i18n', 'ar.json'), JSON.stringify({ a: { b: 'نص' } }));
    writeFileSync(join(dir, 'i18n', 'en.json'), JSON.stringify({ a: { b: 'Text' } }));
    writeFileSync(join(dir, 'map.json'), JSON.stringify({ fontFamily: 'F', icons: { home: { codepoint: 'E001' } } }));
    writeFileSync(join(dir, 'a.html'), '<dga-icon name="home" />');
  });
  afterEach(() => rmSync(dir, { recursive: true, force: true }));

  it('routes every subcommand', async () => {
    expect(Object.values(Subcommands)).toEqual(['permission-gates', 'rtl-lint', 'icons-subset', 'i18n-check']);
    expect(await main([Subcommands.permissionGates, '--dir', dir], capture().io)).toBe(ExitCodes.ok);
    expect(await main([Subcommands.rtlLint, dir], capture().io)).toBe(ExitCodes.ok);
    expect(await main([Subcommands.iconsSubset, '--dir', dir, '--map', join(dir, 'map.json')], capture().io)).toBe(ExitCodes.ok);
    expect(await main([Subcommands.i18nCheck, '--dir', join(dir, 'i18n')], capture().io)).toBe(ExitCodes.ok);
  });

  it('prints every usage for an unknown or missing subcommand', async () => {
    const { sink, io } = capture();
    expect(await main(['nope'], io)).toBe(ExitCodes.usage);
    expect(sink.errors[0]).toMatch(/Unknown subcommand: nope/);
    for (const subcommand of Object.values(Subcommands)) expect(sink.errors[0]).toContain(subcommand);
    expect(await main([], capture().io)).toBe(ExitCodes.usage);
  });

  it('runs as the bin and sets the exit code', () => {
    writeFileSync(join(dir, 'b.html'), '<p dir="ltr"></p>');
    expect(spawnSync(process.execPath, [BIN, 'rtl-lint', dir], { encoding: 'utf8' }).status).toBe(ExitCodes.failure);
    expect(spawnSync(process.execPath, [BIN, 'i18n-check', '--dir', join(dir, 'i18n')], { encoding: 'utf8' }).status).toBe(ExitCodes.ok);
  });
});
