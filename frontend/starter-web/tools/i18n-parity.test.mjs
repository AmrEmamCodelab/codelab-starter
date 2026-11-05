// @vitest-environment node
//
// The app wrapper around `codelab-ng-tools i18n-check`: its folders, its Latin allow list and its exit codes. The
// check itself is tested in @codelab/ng-tools (tests/i18n-check.test.mjs).
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
  DEFAULT_I18N_DIR,
  DEFAULT_SOURCE_DIR,
  ExitCodes,
  LATIN_ALLOWED_KEYS,
  checkArguments,
  parseArguments,
  run,
} from './i18n-parity.mjs';

describe('the i18n-check wrapper', () => {
  let dir;
  const sink = () => {
    const lines = { logs: [], errors: [] };
    return { lines, io: { log: (line) => lines.logs.push(line), error: (line) => lines.errors.push(line) } };
  };
  const write = (relative, value) => {
    const file = path.join(dir, relative);
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, typeof value === 'string' ? value : JSON.stringify(value));
  };

  beforeEach(() => { dir = fs.mkdtempSync(path.join(os.tmpdir(), 'starter-i18n-check-')); });
  afterEach(() => fs.rmSync(dir, { recursive: true, force: true }));

  it('defaults to the app translations and sources, and accepts --dir and --src', () => {
    expect(parseArguments([], dir)).toEqual({ dir: path.resolve(dir, DEFAULT_I18N_DIR), src: path.resolve(dir, DEFAULT_SOURCE_DIR) });
    expect(parseArguments(['--src', 's', '--dir', 'd'], dir)).toEqual({ dir: path.resolve(dir, 'd'), src: path.resolve(dir, 's') });
  });

  it('refuses unknown flags and a flag without a value', () => {
    expect(parseArguments(['--x', 'y'], dir).error).toMatch(/Usage/);
    expect(parseArguments(['--dir'], dir).error).toMatch(/Usage/);
    expect(run(['--x'], { appDir: dir, ...sink().io })).toBe(ExitCodes.usage);
  });

  it('passes the Latin allow list to the check', () => {
    const args = checkArguments({ dir: 'd', src: 's' });
    expect(args.slice(0, 4)).toEqual(['--dir', 'd', '--src', 's']);
    for (const key of LATIN_ALLOWED_KEYS) expect(args).toContain(key);
  });

  it('passes a consistent app and fails a key the code uses but the files lack', () => {
    write('src/assets/i18n/ar.json', { app: { title: 'عنوان' }, layout: { 'language-switcher': { en: 'English' } } });
    write('src/assets/i18n/en.json', { app: { title: 'Title' }, layout: { 'language-switcher': { en: 'English' } } });
    write('src/app/a.component.html', "<h1>{{ 'app.title' | t }}</h1>");
    expect(run([], { appDir: dir, ...sink().io })).toBe(ExitCodes.ok);

    write('src/app/b.component.html', "<p>{{ 'app.missing' | t }}</p>");
    const { lines, io } = sink();
    expect(run([], { appDir: dir, ...io })).toBe(ExitCodes.failure);
    expect(lines.errors.join('\n')).toMatch(/app\.missing is used in .*b\.component\.html:1 but missing from ar, en/);
  });
});
