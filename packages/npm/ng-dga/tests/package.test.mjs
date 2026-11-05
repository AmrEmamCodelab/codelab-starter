// The package shape: both manifests agree, every component is exported and tested, every component is standalone
// and OnPush, and no component template carries raw (untranslated) text.
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { readJson } from '../scripts/lib/files.mjs';
import { PACKAGE_ROOT, SOURCE_DIR } from '../scripts/lib/package-paths.mjs';

const LIB_DIR = path.join(SOURCE_DIR, 'lib');
const INTERNAL_FOLDER = 'internal';
const COMPONENT_SUFFIX = '.component.ts';
const TEMPLATE_SUFFIX = '.component.html';
const EXPECTED_SELECTORS = [
  'dga-breadcrumb',
  'dga-button',
  'dga-card',
  'dga-checkbox',
  'dga-icon',
  'dga-info-field',
  'dga-radio-group',
  'dga-status-tag',
  'dga-stepper',
  'dga-tabs',
  'dga-tag',
  'dga-text-input',
];
/** Text between tags that is not whitespace, an interpolation, a control-flow brace or a lone required marker. */
const RAW_TEXT = />([^<>{}@]*[A-Za-z؀-ۿ][^<>{}@]*)</g;

function componentFiles() {
  return readdirSync(LIB_DIR, { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && entry.name !== INTERNAL_FOLDER)
    .flatMap((entry) => readdirSync(path.join(LIB_DIR, entry.name)).filter((file) => file.endsWith(COMPONENT_SUFFIX)).map((file) => path.join(LIB_DIR, entry.name, file)));
}

describe('package manifests', () => {
  it('keep the same name and peer dependencies in the published and the ng-packagr manifest', () => {
    const published = readJson(path.join(PACKAGE_ROOT, 'package.json'));
    const input = readJson(path.join(SOURCE_DIR, 'package.json'));
    expect(input.name).toBe(published.name);
    expect(input.peerDependencies).toEqual(published.peerDependencies);
  });

  it('ship the styles, the assets and the module documentation', () => {
    const published = readJson(path.join(PACKAGE_ROOT, 'package.json'));
    expect(published.files).toEqual(expect.arrayContaining(['dist', 'styles', 'assets', 'README.md', 'MODULE.md']));
    expect(published.exports['./styles']).toBeDefined();
    for (const file of ['README.md', 'MODULE.md', 'module.rules.yaml']) expect(existsSync(path.join(PACKAGE_ROOT, file))).toBe(true);
  });
});

describe('components', () => {
  const files = componentFiles();
  const publicApi = readFileSync(path.join(SOURCE_DIR, 'public-api.ts'), 'utf8');

  it('cover exactly the 0.1 component set', () => {
    const selectors = files.map((file) => /selector: '([a-z-]+)'/.exec(readFileSync(file, 'utf8'))?.[1]).sort();
    expect(selectors).toEqual(EXPECTED_SELECTORS);
  });

  it.each(componentFiles().map((file) => [path.basename(file), file]))('%s is exported, OnPush, signal-based and has a spec', (_name, file) => {
    const source = readFileSync(file, 'utf8');
    const relative = `./${path.relative(SOURCE_DIR, file).split(path.sep).join('/').replace(/\.ts$/, '')}`;
    expect(publicApi).toContain(`export * from '${relative}';`);
    expect(source).toContain('ChangeDetectionStrategy.OnPush');
    expect(source).not.toContain('@Input(');
    expect(source).not.toContain('standalone: false');
    expect(existsSync(file.replace(/\.ts$/, '.spec.ts')) || readdirSync(path.dirname(file)).some((entry) => entry.endsWith('.spec.ts'))).toBe(true);
  });

  it.each(componentFiles().map((file) => [path.basename(file), file.replace(COMPONENT_SUFFIX, TEMPLATE_SUFFIX)]))('%s template has no raw text', (_name, template) => {
    const html = readFileSync(template, 'utf8');
    const raw = [...html.matchAll(RAW_TEXT)].map((match) => match[1].trim()).filter((text) => text.length > 0);
    expect(raw).toEqual([]);
  });
});
