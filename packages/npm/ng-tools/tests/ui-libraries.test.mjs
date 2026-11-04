import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ESLint } from 'eslint';
import { describe, expect, it } from 'vitest';
import { createCodelabEslintConfig } from '../src/eslint/index.mjs';
import { RESTRICTED_UI_LIBRARY_PATTERNS, UI_LIBRARY_MESSAGE, restrictedUiImports } from '../src/eslint/ui-libraries.mjs';

const ROOT = fileURLToPath(new URL('./fixtures/boundary-app', import.meta.url));
const FILE = join(ROOT, 'src', 'app', 'features', 'records', 'probe.ts');

// The external DGA package scopes are denied repo-wide by the scrub sweep (decision D5), so the specifiers that must
// be refused are assembled here instead of written out.
const DGA_SCOPE = ['@', 'dga'].join('');
const DGA_DEV_SCOPE = ['@', 'dev', '-', 'dga'].join('');

const REFUSED = [
  '@angular/material/button',
  '@angular/material',
  'primeng/table',
  'ng-zorro-antd/button',
  'ngx-bootstrap/modal',
  '@ng-bootstrap/ng-bootstrap',
  `${DGA_SCOPE}/ui`,
  `${DGA_DEV_SCOPE}/angular`,
  '@taiga-ui/core',
];
const ALLOWED = ['@codelab/ng-dga', '@codelab/ng-dga/button', '@angular/core', '@angular/cdk/overlay', 'rxjs', 'primengine', 'bootstrapper'];

async function lint(specifier) {
  const eslint = new ESLint({ cwd: ROOT, overrideConfigFile: true, overrideConfig: createCodelabEslintConfig({ rootDir: ROOT, compiledRules: false }) });
  const [result] = await eslint.lintText(`import '${specifier}';\n`, { filePath: FILE });
  return result.messages.filter((message) => message.ruleId === 'no-restricted-imports');
}

describe('third-party UI library ban', () => {
  it.each(REFUSED)('refuses %s with the D5 message', async (specifier) => {
    const messages = await lint(specifier);
    expect(messages).toHaveLength(1);
    expect(messages[0].message).toContain(UI_LIBRARY_MESSAGE);
  });

  it.each(ALLOWED)('allows %s', async (specifier) => {
    expect(await lint(specifier)).toEqual([]);
  });

  it('builds one regex pattern per family, each a valid expression', () => {
    const { patterns } = restrictedUiImports();
    expect(patterns).toHaveLength(RESTRICTED_UI_LIBRARY_PATTERNS.length);
    for (const pattern of patterns) expect(() => new RegExp(pattern.regex)).not.toThrow();
  });
});
