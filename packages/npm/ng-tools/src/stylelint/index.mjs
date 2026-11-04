// The Codelab Stylelint preset: design tokens only, logical CSS only (base rules base.fe-design-tokens and
// base.fe-logical-css, enforced for the browser where the content rules only see source text).
//
//   // .stylelintrc.json
//   { "extends": ["@codelab/ng-tools/stylelint"] }
//
// Tokens only: a colour or a spacing value is a `var(--token)`, never a literal. The keywords below (and 0 for
// spacing) stay allowed because they carry no design decision.

const ALWAYS = 'always';
const VAR_TOKEN = '^var\\(--[A-Za-z0-9-]+(?:,[^()]*)?\\)$';
const CALC_OF_TOKENS = '^calc\\((?:[^()]|var\\(--[A-Za-z0-9-]+\\))*\\)$';
const COLOR_KEYWORDS = Object.freeze(['transparent', 'currentcolor', 'currentColor', 'inherit', 'initial', 'unset', 'revert', 'none']);
const SPACING_KEYWORDS = Object.freeze(['0', 'auto', 'inherit', 'initial', 'unset', 'revert', 'normal']);

/** Colour functions that would introduce a literal colour. */
export const COLOR_FUNCTIONS = Object.freeze(['rgb', 'rgba', 'hsl', 'hsla', 'hwb', 'lab', 'lch', 'oklab', 'oklch', 'color', 'color-mix']);

/** Properties whose value must be a colour token. */
export const COLOR_PROPERTIES = '/^(?:color|background-color|border(?:-(?:block|inline)(?:-(?:start|end))?)?-color|outline-color|fill|stroke|caret-color|accent-color|text-decoration-color|column-rule-color)$/';

/** Properties whose value must be built from spacing tokens. */
export const SPACING_PROPERTIES = '/^(?:margin|padding|inset)(?:-(?:block|inline)(?:-(?:start|end))?)?$|^(?:row-|column-)?gap$/';

/** A value list where every space-separated part is a token, `calc()` of tokens or a neutral keyword. */
function tokenValues(keywords) {
  const part = `(?:${VAR_TOKEN.slice(1, -1)}|${CALC_OF_TOKENS.slice(1, -1)}|${keywords.join('|')})`;
  return [`/^${part}(?:\\s+${part})*$/`];
}

/** The preset (a Stylelint config object; not frozen, Stylelint normalises configs it is given). */
export const codelabStylelintConfig = {
  plugins: ['stylelint-use-logical'],
  rules: {
    'csstools/use-logical': ALWAYS,
    'color-no-hex': true,
    'color-named': 'never',
    'function-disallowed-list': [...COLOR_FUNCTIONS],
    'declaration-property-value-allowed-list': {
      [COLOR_PROPERTIES]: tokenValues(COLOR_KEYWORDS),
      [SPACING_PROPERTIES]: tokenValues(SPACING_KEYWORDS),
    },
  },
  overrides: [
    {
      // The token definitions themselves are where literal colours and sizes live.
      files: ['**/tokens/**', '**/_tokens*.scss', '**/_tokens*.css'],
      rules: {
        'color-no-hex': null,
        'color-named': null,
        'function-disallowed-list': null,
        'declaration-property-value-allowed-list': null,
      },
    },
  ],
};

export default codelabStylelintConfig;
