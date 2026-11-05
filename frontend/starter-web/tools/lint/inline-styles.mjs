// Inline styles are refused (base.fe-design-tokens, base.fe-logical-css). Stylelint checks only .scss and .css
// files, so a component's `styles`, a template's `style="..."`, `[style.x]`, `[ngStyle]` or a host style binding
// would carry raw colours and physical left/right CSS past both gates. All styling goes in the component's .scss file.
//
// Two checks share the pattern below:
// - ESLint (`inlineStyleConfigs`) on .ts files: the @Component `styles` property, style bindings in an inline
//   `template` or in `host`, and the NgStyle import;
// - tools/no-inline-styles.mjs on the .html templates (ESLint has no template parser in the starter).

/** Shown next to every refused inline style; starts with the base rule ids like the hook and CI messages. */
export const INLINE_STYLE_MESSAGE =
  '[base.fe-design-tokens] [base.fe-logical-css] Inline styles bypass the design-token and logical-CSS checks. Put the styles in the component .scss file (styleUrl) and use classes.';

/**
 * A style attribute or binding in template text: `style=`, `[style]`, `[style.x]`, `[attr.style]`, `[ngStyle]`.
 * The lookbehind keeps names that merely end in "style" (`data-style=`, `liststyle=`) out.
 */
export const TEMPLATE_STYLE_SOURCE = String.raw`(?<![\w.-])(?:style\s*=|\[(?:style|attr\.style|ngStyle)[\].])`;

/** The `TEMPLATE_STYLE_SOURCE` pattern, case-insensitive (HTML attribute names are). */
export function templateStylePattern(flags = 'i') {
  return new RegExp(TEMPLATE_STYLE_SOURCE, flags);
}

/** A `host` metadata key that sets a style: `style`, `[style]`, `[style.x]`, `[attr.style]`. */
const HOST_STYLE_KEY_SOURCE = String.raw`^(?:style|\[(?:style|attr\.style)[\].])`;

const ERROR = 'error';
const TYPESCRIPT_FILES = '**/*.ts';
const SPEC_FILES = '**/*.spec.ts';
const COMPONENT = 'Decorator[expression.callee.name="Component"]';
const METADATA = `${COMPONENT} > CallExpression > ObjectExpression`;

/** The ESLint `no-restricted-syntax` selectors, one per inline-style form. */
export const INLINE_STYLE_SELECTORS = Object.freeze([
  `${METADATA} > Property[key.name="styles"]`,
  `${METADATA} > Property[key.name="template"] Literal[value=/${TEMPLATE_STYLE_SOURCE}/i]`,
  `${METADATA} > Property[key.name="template"] TemplateElement[value.raw=/${TEMPLATE_STYLE_SOURCE}/i]`,
  `${METADATA} > Property[key.name="host"] > ObjectExpression > Property[key.value=/${HOST_STYLE_KEY_SOURCE}/i]`,
  `${METADATA} > Property[key.name="host"] > ObjectExpression > Property[key.name="style"]`,
  'ImportDeclaration[source.value="@angular/common"] > ImportSpecifier[imported.name="NgStyle"]',
]);

/**
 * The plugin and rule names. Not `starter` (magic-values.mjs owns that plugin) and not no-restricted-syntax, whose
 * options a later config object (the enum rules) would replace.
 */
export const PLUGIN_NAME = 'starter-styles';
export const INLINE_STYLES_RULE = 'no-inline-styles';
export const INLINE_STYLES_RULE_ID = `${PLUGIN_NAME}/${INLINE_STYLES_RULE}`;

/** Reports every node that matches one of `INLINE_STYLE_SELECTORS`. */
export const noInlineStylesRule = {
  meta: {
    type: 'problem',
    docs: { description: 'Refuse inline styles; styles go in the component .scss file.' },
    messages: { inlineStyle: INLINE_STYLE_MESSAGE },
    schema: [],
  },
  create(context) {
    const report = (node) => context.report({ node, messageId: 'inlineStyle' });
    return Object.fromEntries(INLINE_STYLE_SELECTORS.map((selector) => [selector, report]));
  },
};

export const stylesPlugin = Object.freeze({ rules: { [INLINE_STYLES_RULE]: noInlineStylesRule } });

/** The ESLint config object that refuses inline styles in .ts files (specs may build test hosts freely). */
export function inlineStyleConfigs() {
  return [
    {
      name: 'starter/no-inline-styles',
      files: [TYPESCRIPT_FILES],
      ignores: [SPEC_FILES],
      plugins: { [PLUGIN_NAME]: stylesPlugin },
      rules: { [INLINE_STYLES_RULE_ID]: ERROR },
    },
  ];
}
