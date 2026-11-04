import stylelint from 'stylelint';
import { describe, expect, it } from 'vitest';
import codelabStylelintConfig from '../src/stylelint/index.mjs';

// CSS fixtures stay inline: a .css fixture file with physical properties or raw colours would itself break the
// repository's content rules (base.fe-logical-css, base.fe-design-tokens).

async function lint(code, codeFilename = 'src/app/features/records/record-page.scss') {
  const result = await stylelint.lint({ code, codeFilename, config: structuredClone(codelabStylelintConfig) });
  return result.results[0].warnings.map((warning) => warning.rule);
}

describe('the Stylelint preset', () => {
  it('accepts token colours, token spacing and logical properties', async () => {
    const css = [
      '.card {',
      '  color: var(--codelab-color-text);',
      '  background-color: var(--codelab-color-surface, transparent);',
      '  border-inline-start-color: currentColor;',
      '  margin-inline-start: var(--codelab-space-2);',
      '  padding-block: 0 var(--codelab-space-1);',
      '  gap: calc(var(--codelab-space-1) * 2);',
      '  inset-inline-end: 0;',
      '  text-align: start;',
      '}',
    ].join('\n');
    expect(await lint(css)).toEqual([]);
  });

  it.each([
    ['a physical margin', '.a { margin-left: var(--codelab-space-1); }', 'csstools/use-logical'],
    ['a physical offset', '.a { left: 0; }', 'csstools/use-logical'],
    ['a physical text alignment', '.a { text-align: right; }', 'csstools/use-logical'],
    ['a hex colour', '.a { color: #fff; }', 'color-no-hex'],
    ['a named colour', '.a { color: red; }', 'color-named'],
    ['an rgb() colour', '.a { background-color: rgb(0 0 0); }', 'function-disallowed-list'],
    ['a literal spacing value', '.a { padding-inline: 12px; }', 'declaration-property-value-allowed-list'],
    ['a literal gap', '.a { gap: 1rem; }', 'declaration-property-value-allowed-list'],
  ])('refuses %s', async (_label, css, rule) => {
    expect(await lint(css)).toContain(rule);
  });

  it('lets the token definition files hold literal values', async () => {
    const css = ':root { --codelab-color-primary: #00754a; --codelab-space-1: 4px; }\n.a { color: rgb(0 0 0); }';
    expect(await lint(css, 'src/styles/tokens/_colors.scss')).toEqual([]);
    expect(await lint(css, 'src/styles/_tokens.scss')).toEqual([]);
  });

  it('loads the logical-CSS plugin by name, resolvable from the package', () => {
    expect(codelabStylelintConfig.plugins).toEqual(['stylelint-use-logical']);
    expect(codelabStylelintConfig.rules['csstools/use-logical']).toBe('always');
  });
});
