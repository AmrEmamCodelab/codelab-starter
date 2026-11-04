// Fake source trees for argument and layout tests: no restore ever runs against them.
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { writeFile } from './powershell.mjs';

export const PROJECT = 'src/App/App.csproj';
export const NPM_WORKSPACE_MEMBER = 'packages/npm/widget';
export const NPM_STANDALONE = 'apps/portal';
export const FEED_TARBALL = 'feed/npm/codelab-widget-0.1.0-ci.1.tgz';

const CSPROJ = '<Project Sdk="Microsoft.NET.Sdk"></Project>\n';

/** A source root with one csproj, a feed, an npm workspace (root lock) and a standalone npm app (own lock). */
export function createSourceTree(root) {
  writeFile(root, PROJECT, CSPROJ);
  mkdirSync(join(root, 'feed', 'nuget'), { recursive: true });
  writeFile(root, FEED_TARBALL, 'tarball');
  writeFile(root, 'global.json', JSON.stringify({ sdk: { version: '10.0.403', rollForward: 'latestFeature' } }));
  writeFile(root, '.nvmrc', '24\n');
  writeFile(root, 'package.json', JSON.stringify({ name: 'root', private: true, workspaces: ['packages/npm/*'] }));
  writeFile(root, 'package-lock.json', JSON.stringify({
    name: 'root',
    lockfileVersion: 3,
    packages: {
      '': { name: 'root', workspaces: ['packages/npm/*'] },
      'node_modules/@codelab/widget': { resolved: 'packages/npm/widget', link: true },
      'node_modules/@codelab/feed-widget': { version: '0.1.0-ci.1', resolved: `file:${FEED_TARBALL}` },
      'node_modules/@playwright/test': { version: '1.58.0', resolved: 'https://registry.example.test/p.tgz' },
    },
  }));
  writeFile(root, `${NPM_WORKSPACE_MEMBER}/package.json`, JSON.stringify({ name: '@codelab/widget', scripts: { build: 'node -e 0' } }));
  writeFile(root, `${NPM_STANDALONE}/package.json`, JSON.stringify({ name: 'portal' }));
  writeFile(root, `${NPM_STANDALONE}/package-lock.json`, JSON.stringify({ name: 'portal', lockfileVersion: 3, packages: { '': { name: 'portal' } } }));
  writeFile(root, 'tools/loose/package.json', JSON.stringify({ name: 'loose' }));
  return root;
}
