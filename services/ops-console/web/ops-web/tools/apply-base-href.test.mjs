import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { BaseHrefError, CONFIG_FILE, INDEX_FILE, applyBaseHref, isValidBasePath, withBaseHref } from './apply-base-href.mjs';

const PAGE = '<html><head><base href="/"><title>x</title></head></html>';
const folders = [];

function webFolder(config, html = PAGE) {
  const folder = mkdtempSync(path.join(tmpdir(), 'ops-web-base-'));
  folders.push(folder);
  writeFileSync(path.join(folder, INDEX_FILE), html);
  writeFileSync(path.join(folder, CONFIG_FILE), JSON.stringify(config));
  return folder;
}

describe('apply-base-href', () => {
  afterEach(() => {
    for (const folder of folders.splice(0)) rmSync(folder, { recursive: true, force: true });
  });

  it.each(['/', '/ops/', '/tools/ops/'])('accepts %s', (value) => {
    expect(isValidBasePath(value)).toBe(true);
  });

  it.each(['ops/', '/ops', '//evil/', '/a/../', '/"x/', '/<x>/', '\\ops\\', 7, null])('refuses %s', (value) => {
    expect(isValidBasePath(value)).toBe(false);
  });

  it('replaces only the base href value', () => {
    expect(withBaseHref(PAGE, '/ops/')).toBe('<html><head><base href="/ops/"><title>x</title></head></html>');
  });

  it('refuses a page without a base tag and a bad path', () => {
    expect(() => withBaseHref('<html></html>', '/ops/')).toThrow(BaseHrefError);
    expect(() => withBaseHref(PAGE, 'ops')).toThrow(BaseHrefError);
  });

  it('writes index.html from config.json', () => {
    const folder = webFolder({ basePath: '/ops/' });
    expect(applyBaseHref(folder)).toBe('/ops/');
    expect(readFileSync(path.join(folder, INDEX_FILE), 'utf8')).toContain('<base href="/ops/">');
  });

  it('reports missing files as a BaseHrefError', () => {
    const folder = webFolder({ basePath: '/ops/' });
    rmSync(path.join(folder, CONFIG_FILE));
    expect(() => applyBaseHref(folder)).toThrow(BaseHrefError);
  });
});
