// The styles compile offline with no remote URL (fonts and icons are bundled), the remote-URL finder works, and the
// committed third-party assets are byte-identical to their pinned npm sources.
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { compileStyles, remoteUrlsInDist } from '../scripts/build.mjs';
import { main as importAssets } from '../scripts/import-assets.mjs';
import { findRemoteUrls } from '../scripts/lib/remote-urls.mjs';

const EXIT_OK = 0;
const EXIT_USAGE = 2;
const LOCAL_FONT = /url\(['"]?\.\.\/assets\/fonts\/[a-z0-9-]+\.woff2/;
const LOCAL_ICON_FONT = /url\(['"]?\.\.\/assets\/icons\/codelab-icons\.woff2/;

describe('compiled styles', () => {
  const css = compileStyles();

  it('reference no remote URL', () => {
    expect(findRemoteUrls(css)).toEqual([]);
  });

  it('declare the self-hosted fonts and the icon font from the package assets', () => {
    expect(css).toMatch(LOCAL_FONT);
    expect(css).toMatch(LOCAL_ICON_FONT);
  });

  it('emit the design tokens on :root', () => {
    expect(css).toContain(':root');
    expect(css).toContain('--dga-color-primary:');
    expect(css).toContain('--dga-space-4:');
  });
});

describe('findRemoteUrls', () => {
  it('finds http, https and protocol-relative url() and @import targets in order', () => {
    const text = [
      "@import 'https://cdn.example.test/a.css';",
      '.a { background: url(http://example.test/b.png); }',
      ".b { src: url('//example.test/c.woff2'); }",
      ".c { src: url('../assets/local.woff2'); }",
    ].join('\n');
    expect(findRemoteUrls(text)).toEqual(['https://cdn.example.test/a.css', 'http://example.test/b.png', '//example.test/c.woff2']);
  });

  it('ignores local and data URLs', () => {
    expect(findRemoteUrls(".a { src: url(data:font/woff2;base64,AAAA); } .b { src: url('./x.woff2'); }")).toEqual([]);
  });
});

describe('remoteUrlsInDist', () => {
  let folder;

  afterEach(() => {
    if (folder) rmSync(folder, { recursive: true, force: true });
    folder = undefined;
  });

  it('reports remote URLs in built CSS and JavaScript only', () => {
    folder = mkdtempSync(path.join(tmpdir(), 'ng-dga-dist-'));
    mkdirSync(path.join(folder, 'fesm2022'));
    writeFileSync(path.join(folder, 'ng-dga.css'), ".a { src: url('https://cdn.example.test/f.woff2'); }");
    writeFileSync(path.join(folder, 'fesm2022', 'lib.mjs'), 'const ok = 1;');
    writeFileSync(path.join(folder, 'README.md'), 'url(https://example.test/ignored)');
    expect(remoteUrlsInDist(folder)).toEqual([{ file: 'ng-dga.css', url: 'https://cdn.example.test/f.woff2' }]);
  });
});

describe('third-party asset import', () => {
  it('has every committed asset in sync with its pinned source package', () => {
    expect(importAssets(['--check'])).toBe(EXIT_OK);
  });

  it('refuses unknown options', () => {
    expect(importAssets(['--force'])).toBe(EXIT_USAGE);
  });
});
