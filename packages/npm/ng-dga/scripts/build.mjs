#!/usr/bin/env node
// Builds @codelab/ng-dga into dist/:
//   1. the Angular library in Angular package format (ng-packagr, partial compilation) from src/ng-package.json;
//   2. dist/ng-dga.css, the styles entry precompiled for consumers without Sass (its font URLs resolve to ../assets);
//   3. a check that no built CSS or JavaScript references a remote URL (fonts and icons are bundled, no CDN).
//
// Usage: node scripts/build.mjs
// Exit codes: 0 built, 1 build failed or a remote URL was found.
import { readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import * as sass from 'sass';
import { listFilesRelative } from './lib/files.mjs';
import { DIST_DIR, LIBRARY_TSCONFIG, NG_PACKAGE_FILE, PREBUILT_CSS_FILE, STYLES_ENTRY } from './lib/package-paths.mjs';
import { findRemoteUrls } from './lib/remote-urls.mjs';

export const EXIT_OK = 0;
export const EXIT_FAILED = 1;
const SCANNED_EXTENSIONS = Object.freeze(['.css', '.mjs', '.js']);

async function buildLibrary() {
  const require = createRequire(import.meta.url);
  const { ngPackagr } = require('ng-packagr');
  await ngPackagr().forProject(NG_PACKAGE_FILE).withTsConfig(LIBRARY_TSCONFIG).build();
}

/** Compiles the styles entry to plain CSS. The output sits one folder below the package root, like styles/. */
export function compileStyles() {
  return sass.compile(STYLES_ENTRY, { style: 'expanded', sourceMap: false }).css + '\n';
}

/** Remote URLs found in the built files: [{ file, url }]. */
export function remoteUrlsInDist(distDir = DIST_DIR) {
  return listFilesRelative(distDir)
    .filter((file) => SCANNED_EXTENSIONS.includes(path.extname(file)))
    .flatMap((file) => findRemoteUrls(readFileSync(path.join(distDir, file), 'utf8')).map((url) => ({ file, url })));
}

export async function main() {
  try {
    await buildLibrary();
  } catch (error) {
    console.error(`ng-packagr failed: ${error.message ?? error}`);
    return EXIT_FAILED;
  }
  writeFileSync(PREBUILT_CSS_FILE, compileStyles());
  const remote = remoteUrlsInDist();
  for (const { file, url } of remote) console.error(`dist/${file} references the remote URL ${url}; bundle the resource instead.`);
  if (remote.length > 0) return EXIT_FAILED;
  console.log('@codelab/ng-dga built: dist/ (Angular package format) and dist/ng-dga.css; no remote URLs.');
  return EXIT_OK;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = await main();
}
