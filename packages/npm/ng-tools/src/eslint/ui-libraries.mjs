// Third-party UI libraries are not allowed in a Codelab app (decision D5 and base.fe-ui-library): screens use
// @codelab/ng-dga components and the page patterns only. Each refused family is a regular expression over the import
// specifier, so sub-paths and sibling packages of a family are covered by one entry.

/** Why an import was refused; shown by ESLint next to the import. */
export const UI_LIBRARY_MESSAGE =
  'Third-party UI libraries are not allowed: use @codelab/ng-dga components and the page patterns (ask the platform owner for a missing one).';

/** One regular expression per refused library family, matched against the import specifier. */
export const RESTRICTED_UI_LIBRARY_PATTERNS = Object.freeze([
  '^@angular/material(?:/|$)',
  '^primeng(?:/|$)',
  '^primeicons(?:/|$)',
  '^ng-zorro-antd(?:/|$)',
  '^ngx-bootstrap(?:/|$)',
  '^@ng-bootstrap/',
  '^@(?:dev-)?dga(?:-[a-z]+)?/',
  '^@angular-material-components/',
  '^@swimlane/ngx-',
  '^@clr/',
  '^@progress/kendo-angular',
  '^devextreme-angular(?:/|$)',
  '^@syncfusion/ej2-angular',
  '^@ionic/angular(?:/|$)',
  '^@nebular/',
  '^@taiga-ui/',
  '^bootstrap(?:/|$)',
]);

/** The `no-restricted-imports` options that refuse every pattern above. */
export function restrictedUiImports() {
  return {
    patterns: RESTRICTED_UI_LIBRARY_PATTERNS.map((regex) => ({ regex, message: UI_LIBRARY_MESSAGE })),
  };
}
