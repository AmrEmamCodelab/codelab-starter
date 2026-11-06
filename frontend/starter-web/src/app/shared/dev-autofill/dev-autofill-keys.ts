/** The texts of `<dga-dev-autofill>`, under `shared.dev-autofill.*` in both bundles. */
export const DevAutofillTranslationKeys = {
  /** The accessible name of the button group. */
  Title: 'shared.dev-autofill.title',
} as const;

/** The stable `data-testid` hooks of `<dga-dev-autofill>` (specs and Playwright use them). */
export const DevAutofillTestIds = {
  /** The group around the buttons; rendered only when a set exists for the form. */
  Group: 'dev-autofill',
  /** One button per data set. */
  Fill: 'dev-autofill-fill',
} as const;

/**
 * The i18n key segment every data-set `labelKey` carries (`serviceRequests.devAutofill.valid`). The production
 * check (`tools/ci/no-dev-autofill.mjs`, `DEV_AUTOFILL_SET_MARKER`) finds a leaked set by `.devAutofill.`.
 */
export const DEV_AUTOFILL_LABEL_SEGMENT = 'devAutofill';
