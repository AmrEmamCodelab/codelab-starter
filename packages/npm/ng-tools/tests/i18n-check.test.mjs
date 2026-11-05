import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  I18nProblemKinds,
  checkI18n,
  compareKeySets,
  findLeavesWithoutArabic,
  findUsedKeys,
  flattenTranslations,
  formatI18nProblems,
  isKeyShaped,
  isLatinAllowed,
  lacksArabic,
  parseI18nArguments,
  readTranslations,
  runI18nCheck,
} from '../src/i18n/index.mjs';
import { ExitCodes } from '../src/shared/arguments.mjs';

const messages = (problems) => problems.map((problem) => problem.message);
const keys = (fileName, text, options) => findUsedKeys(fileName, text, options).map(({ key, line }) => `${key}@${line}`);

describe('flattenTranslations', () => {
  it('flattens nested groups into dotted keys', () => {
    const leaves = flattenTranslations({ auth: { login: { title: 'x', submit: 'y' } }, app: { name: 'z' } });
    expect([...leaves.keys()]).toEqual(['auth.login.title', 'auth.login.submit', 'app.name']);
  });

  it('reports arrays, numbers, null, empty text and empty groups with their kind', () => {
    const problems = [];
    flattenTranslations({ a: [], b: 1, c: ' ', d: {}, e: null }, problems);
    expect(messages(problems)).toEqual([
      'a: expected text or a group, found an array',
      'b: expected text or a group, found number',
      'c: empty text',
      'd: empty group',
      'e: expected text or a group, found object',
    ]);
    expect(problems.map((problem) => problem.kind)).toEqual([
      I18nProblemKinds.invalidValue, I18nProblemKinds.invalidValue, I18nProblemKinds.emptyValue, I18nProblemKinds.emptyGroup, I18nProblemKinds.invalidValue,
    ]);
  });

  it('reports a root that is not an object', () => {
    const problems = [];
    flattenTranslations([], problems);
    expect(messages(problems)).toEqual(['(root): expected text or a group, found an array']);
  });
});

describe('compareKeySets', () => {
  it('lists each key missing from each language', () => {
    expect(messages(compareKeySets([
      { language: 'ar', leaves: new Map([['a', 'x'], ['b', 'x']]) },
      { language: 'en', leaves: new Map([['a', 'x'], ['c', 'x']]) },
    ]))).toEqual(['ar: missing key c', 'en: missing key b']);
  });
});

describe('Arabic text in ar', () => {
  it.each([
    ['عنوان', false],
    ['رقم {{code}}', false],
    ['{{count}}', false],
    ['-', false],
    ['ملف PDF', false],
    ['Title', true],
    ['{{name}} Title', true],
    ['https://example.test', false],
    ['name@example.test', false],
  ])('lacksArabic(%j) is %s', (text, expected) => {
    expect(lacksArabic(text)).toBe(expected);
  });

  it('allows listed keys and groups to be Latin', () => {
    expect(isLatinAllowed('layout.language-switcher.en', ['layout.language-switcher.en'])).toBe(true);
    expect(isLatinAllowed('brand.name', ['brand.*'])).toBe(true);
    expect(isLatinAllowed('brandx.name', ['brand.*'])).toBe(false);
    expect(isLatinAllowed('brand', ['brand.*'])).toBe(false);
  });

  it('reports each Latin-only Arabic leaf that is not allowed', () => {
    const leaves = new Map([['a.title', 'عنوان'], ['a.untranslated', 'Submit'], ['switcher.en', 'English']]);
    expect(messages(findLeavesWithoutArabic(leaves, ['switcher.en']))).toEqual(['ar: a.untranslated has no Arabic text: "Submit"']);
  });
});

describe('findUsedKeys', () => {
  it('reads keys piped through the translate pipe, with lines', () => {
    const text = "<h1>{{ 'app.title' | t }}</h1>\n<p [title]=\"'app.hint' | t: { n: 1 }\"></p>\n<p>{{ \"app.other\" | translate }}</p>";
    expect(keys('a.html', text)).toEqual(['app.title@1', 'app.hint@2', 'app.other@3']);
  });

  it('ignores a logical or, other pipes, keys that are not literal and single words', () => {
    const text = "{{ 'app.a' || 'app.b' }} {{ 'app.c' | date }} {{ key | t }} {{ 'word' | t }} {{ 'app.d' | tx }}";
    expect(keys('a.html', text)).toEqual([]);
  });

  it('honours custom pipe names', () => {
    expect(keys('a.html', "{{ 'app.a' | i18n }}", { pipes: ['i18n'] })).toEqual(['app.a@1']);
  });

  it('reads <x>Key inputs, static and bound', () => {
    const text = '<app-list titleKey="app.list.title" [emptyKey]="\'app.list.empty\'" [labelKey]="label" searchLabelKey="nokey"></app-list>';
    expect(keys('a.html', text)).toEqual(['app.list.title@1', 'app.list.empty@1']);
  });

  it('reads translate() and instant() calls and inline templates in TypeScript, not comments', () => {
    const text = [
      "// this.translations.translate('app.commented')",
      "const title = this.translations.translate('app.title', { n: 1 });",
      "const other = this.i18n.instant(\"app.other\");",
      "@Component({ template: `<p>{{ 'app.inline' | t }}</p>` })",
      "const notAKey = this.translations.translate(key);",
    ].join('\n');
    expect(keys('a.ts', text)).toEqual(['app.inline@4', 'app.title@2', 'app.other@3']);
  });

  it('reads every key-shaped literal of a translation-key catalogue, but not built keys', () => {
    const text = "export const K = {\n  Title: 'patterns.list.title',\n  Prefix: 'errors.',\n  Built: `errors.${code}`,\n  Selector: 'app-x',\n};";
    expect(keys('src/app/patterns/pattern-translation-keys.ts', text)).toEqual(['patterns.list.title@2']);
    expect(keys('src/app/patterns/other.ts', text)).toEqual([]);
  });

  it('knows the key shape', () => {
    for (const key of ['a.b', 'errors.SOME_CODE', 'patterns.work-queue.due-soon']) expect(isKeyShaped(key), key).toBe(true);
    for (const text of ['word', 'a.', '.a', 'a b.c', 'https://x.y']) expect(isKeyShaped(text), text).toBe(false);
  });
});

describe('checkI18n and the command line', () => {
  let dir;
  const write = (relative, value) => {
    const file = join(dir, relative);
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, typeof value === 'string' ? value : JSON.stringify(value));
  };
  const io = () => {
    const sink = { logs: [], errors: [] };
    return { sink, io: { log: (line) => sink.logs.push(line), error: (line) => sink.errors.push(line) } };
  };
  const i18n = () => join(dir, 'i18n');
  const src = () => join(dir, 'src');

  beforeEach(() => {
    dir = mkdtempSync(join(tmpdir(), 'ng-tools-i18n-'));
    write('i18n/ar.json', { app: { title: 'عنوان' } });
    write('i18n/en.json', { app: { title: 'Title' } });
    write('src/a.component.html', "<h1>{{ 'app.title' | t }}</h1>");
    write('src/a.component.spec.ts', "translate('app.only-in-a-spec')");
  });
  afterEach(() => rmSync(dir, { recursive: true, force: true }));

  it('passes consistent translations and code', () => {
    expect(checkI18n({ dir: i18n(), sourceDirs: [src()] })).toEqual({ problems: [], languages: ['ar', 'en'], usedKeyCount: 1 });
  });

  it('fails a key present in one language only', () => {
    write('i18n/ar.json', { app: { title: 'عنوان', extra: 'إضافي' } });
    expect(messages(checkI18n({ dir: i18n() }).problems)).toEqual(['en: missing key app.extra']);
  });

  it('fails invalid JSON once, without stopping the other checks', () => {
    write('i18n/ar.json', '{ broken');
    const problems = checkI18n({ dir: i18n() }).problems;
    expect(problems).toHaveLength(1);
    expect(problems[0]).toMatchObject({ kind: I18nProblemKinds.invalidJson, language: 'ar' });
    expect(problems[0].message).toMatch(/^ar\.json: not valid JSON/);
  });

  it('fails a missing required language', () => {
    rmSync(join(dir, 'i18n', 'ar.json'));
    expect(messages(checkI18n({ dir: i18n() }).problems)).toEqual(['ar: ar.json is missing']);
  });

  it('fails Latin Arabic text unless allowed', () => {
    write('i18n/ar.json', { app: { title: 'Title' } });
    expect(messages(checkI18n({ dir: i18n() }).problems)).toEqual(['ar: app.title has no Arabic text: "Title"']);
    expect(checkI18n({ dir: i18n(), allowLatin: ['app.*'] }).problems).toEqual([]);
  });

  it('fails a used key that a language lacks, once per key, relative to baseDir', () => {
    write('src/b.component.html', "<p>{{ 'app.missing' | t }}</p><p>{{ 'app.missing' | t }}</p>");
    const { problems } = checkI18n({ dir: i18n(), sourceDirs: [src()], baseDir: dir });
    expect(problems).toEqual([{ kind: I18nProblemKinds.unknownKey, key: 'app.missing', message: 'app.missing is used in src/b.component.html:1 but missing from ar, en' }]);
  });

  it('fails a missing translation or source folder', () => {
    expect(messages(checkI18n({ dir: join(dir, 'nope') }).problems)[0]).toMatch(/translation folder not found/);
    expect(messages(checkI18n({ dir: i18n(), sourceDirs: [join(dir, 'nope')] }).problems)).toEqual([`source folder not found: ${join(dir, 'nope')}`]);
  });

  it('formats problems with the rule id and the fix', () => {
    expect(formatI18nProblems([{ message: 'en: missing key a.b' }], 'fix it')).toBe('i18n check (base.fe-no-raw-text) failed (1):\n  en: missing key a.b\nFix: fix it');
  });

  it('parses its options', () => {
    expect(parseI18nArguments(['--dir', 'i', '--src', 'a', '--src', 'b', '--allow-latin', 'x.y,z.*', '--pipe', 'i18n'])).toEqual({
      dir: 'i', sourceDirs: ['a', 'b'], allowLatin: ['x.y', 'z.*'], pipes: ['i18n'], fix: undefined,
    });
    expect(parseI18nArguments(['--dir', 'i']).pipes).toEqual(['t', 'translate']);
    expect(parseI18nArguments([]).error).toMatch(/--dir is required/);
    expect(parseI18nArguments(['--dir', 'i', 'extra']).error).toMatch(/Unexpected/);
  });

  it('returns 0 clean, 1 on a problem and 2 on usage', () => {
    const clean = io();
    expect(runI18nCheck(['--dir', i18n(), '--src', src()], clean.io)).toBe(ExitCodes.ok);
    expect(clean.sink.logs[0]).toBe('i18n check ok: ar, en; 1 key(s) used in code, all present');
    write('i18n/en.json', { app: { title: '' } });
    const failed = io();
    expect(runI18nCheck(['--dir', i18n()], failed.io)).toBe(ExitCodes.failure);
    expect(failed.sink.errors[0]).toMatch(/en: app\.title: empty text/);
    expect(runI18nCheck(['--src', src()], io().io)).toBe(ExitCodes.usage);
  });

  it('reads the translations with readTranslations for other tools', () => {
    const { languages, problems } = readTranslations(i18n());
    expect(problems).toEqual([]);
    expect(languages.map(({ language, leaves }) => [language, [...leaves]])).toEqual([['ar', [['app.title', 'عنوان']]], ['en', [['app.title', 'Title']]]]);
  });
});
