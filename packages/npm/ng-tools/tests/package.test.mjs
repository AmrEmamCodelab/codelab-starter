import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import * as tools from '../src/index.mjs';
import { codelabPlugin, createCodelabEslintConfig } from '../src/eslint/index.mjs';

const PACKAGE_ROOT = fileURLToPath(new URL('..', import.meta.url));
const manifest = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf8'));

describe('the package', () => {
  it('exports the presets, the plugin, the sync API and the permission gates from its root', () => {
    const names = ['createCodelabEslintConfig', 'codelabPlugin', 'codelabStylelintConfig', 'syncFolders', 'compareFolders',
      'assertRoutesGated', 'findUngatedRoutes', 'checkPermissionGates', 'findUngatedButtons', 'extractInlineTemplates'];
    for (const name of names) {
      expect(tools[name], name).toBeDefined();
    }
  });

  it('exports the 0.5 tools (RTL lint, icon subset, i18n check) and the CLI dispatcher from its root', () => {
    const names = ['checkRtl', 'runRtlLint', 'RtlFindingKinds', 'collectIconUsage', 'subsetOutputs', 'runIconsSubset', 'readIconMap',
      'checkI18n', 'runI18nCheck', 'I18nProblemKinds', 'runCodelabNgTools', 'Subcommands', 'ExitCodes'];
    for (const name of names) {
      expect(tools[name], name).toBeDefined();
    }
  });

  it('names the plugin after the package', () => {
    expect(codelabPlugin.meta).toEqual({ name: '@codelab/ng-tools', version: manifest.version });
    expect(Object.keys(codelabPlugin.rules)).toEqual(['wire-id-cast']);
  });

  it('returns named flat-config objects in a fixed order', () => {
    const names = createCodelabEslintConfig({ compiledRules: false }).map((config) => config.name);
    expect(names).toEqual(['codelab/typescript', 'codelab/boundaries', 'codelab/ui-libraries', 'codelab/wire-id']);
  });

  it('points every export and the bin into dist, and ships only dist, the README and MODULE.md', () => {
    const targets = [...Object.entries(manifest.exports).filter(([key]) => key !== './package.json').map(([, value]) => value), ...Object.values(manifest.bin)];
    for (const target of targets) expect(target.replace(/^\.\//, '')).toMatch(/^dist\//);
    expect(manifest.files).toEqual(['dist', 'README.md', 'MODULE.md']);
  });

  it('ships the vectors and permission-gate bins and the routes and permission-gates sub-paths', () => {
    expect(Object.keys(manifest.bin)).toEqual(['codelab-vectors-sync', 'codelab-ng-tools']);
    expect(manifest.exports['./routes']).toBe('./dist/routes/route-gates.mjs');
    expect(manifest.exports['./permission-gates']).toBe('./dist/permission-gates/index.mjs');
    expect(existsSync(fileURLToPath(new URL('../src/routes/route-gates.d.mts', import.meta.url)))).toBe(true);
  });

  it('pins every dependency exactly and never depends on an external DGA package', () => {
    for (const version of Object.values(manifest.dependencies)) expect(version).toMatch(/^\d+\.\d+\.\d+$/);
    expect(JSON.stringify(manifest)).not.toMatch(/dga\//);
  });

  it('builds dist as a copy of src, with Node built-ins only', () => {
    const result = spawnSync(process.execPath, ['scripts/build.mjs'], { cwd: PACKAGE_ROOT, encoding: 'utf8' });
    expect(result.status).toBe(0);
    for (const target of Object.values(manifest.exports).filter((value) => value.startsWith('./dist/'))) {
      expect(existsSync(fileURLToPath(new URL(`../${target}`, import.meta.url))), target).toBe(true);
    }
    expect(readFileSync(new URL('../scripts/build.mjs', import.meta.url), 'utf8')).not.toMatch(/from '(?!node:)/);
  });
});
