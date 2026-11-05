// The package's own frontend gates: the Stylelint preset (design tokens only, logical CSS only) over src/ and styles/,
// and the RTL lint over src/. `npm test` ends with `npm run lint`, so CI step 11 (npm test --workspaces) runs both.
import { spawnSync } from 'node:child_process';
import { createRequire } from 'node:module';
import path from 'node:path';
import stylelint from 'stylelint';
import { describe, expect, it } from 'vitest';
import { readJson } from '../scripts/lib/files.mjs';
import { PACKAGE_ROOT } from '../scripts/lib/package-paths.mjs';

const STYLE_GLOBS = ['src/**/*.{css,scss}', 'styles/**/*.scss'];
const NG_TOOLS_MANIFEST = '@codelab/ng-tools/package.json';
const NG_TOOLS_BIN = 'codelab-ng-tools';
const ICON_MAP = path.join('assets', 'icons', 'codelab-icons.json');
const SCSS_FILE = path.join(PACKAGE_ROOT, 'styles', '_lint-probe.scss');
const EXIT_OK = 0;

function lintScss(code) {
  return stylelint.lint({ code, codeFilename: SCSS_FILE, configBasedir: PACKAGE_ROOT, cwd: PACKAGE_ROOT });
}

function ngToolsBin() {
  const manifestPath = createRequire(import.meta.url).resolve(NG_TOOLS_MANIFEST);
  const manifest = readJson(manifestPath);
  return path.join(path.dirname(manifestPath), manifest.bin[NG_TOOLS_BIN]);
}

describe('package lint wiring', () => {
  const scripts = readJson(path.join(PACKAGE_ROOT, 'package.json')).scripts;

  it('npm test ends with the lint, and the lint runs the RTL lint and Stylelint', () => {
    expect(scripts.test.endsWith('npm run lint')).toBe(true);
    expect(scripts.lint).toBe('npm run lint:rtl && npm run lint:styles');
    expect(scripts['lint:rtl']).toContain(`${NG_TOOLS_BIN} rtl-lint src`);
    for (const glob of STYLE_GLOBS) expect(scripts['lint:styles']).toContain(`"${glob}"`);
  });
});

describe('Stylelint over the package styles', () => {
  it('finds nothing in src/ and styles/', async () => {
    const result = await stylelint.lint({ files: STYLE_GLOBS, cwd: PACKAGE_ROOT });
    const findings = result.results.flatMap((file) =>
      file.warnings.map((warning) => `${path.relative(PACKAGE_ROOT, file.source)}:${warning.line} ${warning.text}`));
    expect(findings).toEqual([]);
  });

  it('rejects an interpolated token on a spacing property (the old _grid.scss shape)', async () => {
    const result = await lintScss('@each $step in (1, 2) {\n  .x-#{$step} {\n    gap: var(--dga-space-#{$step});\n  }\n}\n');
    expect(result.errored).toBe(true);
  });

  it('accepts a step token set through a custom property (the _grid.scss shape)', async () => {
    const result = await lintScss('@each $step in (1, 2) {\n  .x-#{$step} {\n    --dga-layout-gap: var(--dga-space-#{$step});\n\n    gap: var(--dga-layout-gap);\n  }\n}\n');
    expect(result.errored).toBe(false);
  });

  it('rejects a physical side and a literal colour', async () => {
    const result = await lintScss('.x {\n  margin-left: var(--dga-space-2);\n  color: #fff;\n}\n');
    const rules = result.results[0].warnings.map((warning) => warning.rule);
    expect(rules).toContain('csstools/use-logical');
    expect(rules).toContain('color-no-hex');
  });
});

describe('RTL lint over the package sources', () => {
  it('finds no physical direction in src/', () => {
    const run = spawnSync(process.execPath, [ngToolsBin(), 'rtl-lint', 'src', '--icons', ICON_MAP], {
      cwd: PACKAGE_ROOT,
      encoding: 'utf8',
    });
    expect(run.stderr).toBe('');
    expect(run.status).toBe(EXIT_OK);
  });
});
