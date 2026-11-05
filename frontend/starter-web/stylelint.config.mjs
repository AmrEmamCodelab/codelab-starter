// Stylelint for the starter web app (`npm run lint`): the @codelab/ng-tools preset (design tokens only, logical CSS
// only via stylelint-use-logical), with the SCSS syntax for .scss files.
import codelabStylelintConfig from '@codelab/ng-tools/stylelint';

const SCSS_SYNTAX = 'postcss-scss';

export default {
  ...codelabStylelintConfig,
  ignoreFiles: ['dist/**', '.angular/**', 'coverage/**', 'node_modules/**', 'lint-fixtures/**'],
  overrides: [
    { files: ['**/*.scss'], customSyntax: SCSS_SYNTAX },
    ...codelabStylelintConfig.overrides,
  ],
};
