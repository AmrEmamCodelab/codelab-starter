// @vitest-environment node
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ExitCodes, checkI18nParity, compareKeySets, flattenTranslations, parseArguments, run } from './i18n-parity.mjs';

describe('flattenTranslations', () => {
  it('flattens nested groups into dotted keys', () => {
    const leaves = flattenTranslations({ auth: { login: { title: 'x', submit: 'y' } }, app: { name: 'z' } });
    expect([...leaves.keys()]).toEqual(['auth.login.title', 'auth.login.submit', 'app.name']);
  });

  it('reports arrays, numbers, empty text and empty groups', () => {
    const problems = [];
    flattenTranslations({ a: [], b: 1, c: ' ', d: {}, e: null }, problems);
    expect(problems).toEqual([
      'a: expected text or a group, found an array',
      'b: expected text or a group, found number',
      'c: empty text',
      'd: empty group',
      'e: expected text or a group, found object',
    ]);
  });

  it('reports a root that is not an object', () => {
    const problems = [];
    flattenTranslations('text', problems);
    expect(problems).toEqual([]);
    flattenTranslations([], problems);
    expect(problems).toEqual(['(root): expected text or a group, found an array']);
  });
});

describe('compareKeySets', () => {
  it('lists each key missing from each language', () => {
    expect(compareKeySets([
      { language: 'ar', keys: new Set(['a', 'b']) },
      { language: 'en', keys: new Set(['a', 'c']) },
    ])).toEqual(['ar: missing key c', 'en: missing key b']);
  });
});

describe('checkI18nParity and run', () => {
  let dir;
  const write = (name, value) => fs.writeFileSync(path.join(dir, name), typeof value === 'string' ? value : JSON.stringify(value));
  const quiet = { log: () => {}, error: () => {} };

  beforeEach(() => { dir = fs.mkdtempSync(path.join(os.tmpdir(), 'starter-i18n-parity-')); });
  afterEach(() => fs.rmSync(dir, { recursive: true, force: true }));

  it('passes when ar and en have the same nested keys', () => {
    write('ar.json', { app: { title: 'عنوان' } });
    write('en.json', { app: { title: 'Title' } });
    expect(checkI18nParity(dir)).toEqual({ problems: [], languages: ['ar', 'en'] });
    expect(run(['--dir', dir], { appDir: dir, ...quiet })).toBe(ExitCodes.ok);
  });

  it('fails on a key present in one language only', () => {
    write('ar.json', { app: { title: 'عنوان', extra: 'إضافي' } });
    write('en.json', { app: { title: 'Title' } });
    expect(checkI18nParity(dir).problems).toEqual(['en: missing key app.extra']);
    expect(run(['--dir', dir], { appDir: dir, ...quiet })).toBe(ExitCodes.failure);
  });

  it('fails when a required language is missing', () => {
    write('en.json', { a: 'A' });
    expect(checkI18nParity(dir).problems).toEqual(['ar: ar.json is missing']);
  });

  it('fails on invalid JSON without stopping the other checks', () => {
    write('ar.json', '{ broken');
    write('en.json', { a: 'A' });
    const { problems } = checkI18nParity(dir);
    expect(problems[0]).toMatch(/^ar\.json: not valid JSON/);
    expect(problems).toHaveLength(1);
  });

  it('fails when the folder does not exist', () => {
    expect(checkI18nParity(path.join(dir, 'missing')).problems[0]).toMatch(/translation folder not found/);
  });

  it('parses arguments', () => {
    expect(parseArguments([], dir)).toEqual({ dir: path.resolve(dir, 'src/assets/i18n') });
    expect(parseArguments(['--dir', 'x'], dir)).toEqual({ dir: path.resolve(dir, 'x') });
    expect(parseArguments(['--x'], dir).error).toMatch(/Usage/);
    expect(run(['--x'], { appDir: dir, ...quiet })).toBe(ExitCodes.usage);
  });
});
