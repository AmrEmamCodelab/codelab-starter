// The Angular app's element types and the dependency policies between them (build plan 4.8, base rule
// base.fe-ui-library): features -> core, layouts, patterns, shared; shared -> core only. The app's UI comes from
// @codelab/ng-dga, an external package that boundaries does not police; no-restricted-imports bans every other UI kit.

/** The element types, one folder each under the app root. */
export const ElementTypes = Object.freeze({
  core: 'core',
  layouts: 'layouts',
  patterns: 'patterns',
  shared: 'shared',
  features: 'features',
  showcase: 'showcase',
});

/** The captured folder name of a feature, used to scope feature-to-feature policies. */
export const FEATURE_CAPTURE = 'feature';

/**
 * Which element types each type may import. A type may always import itself (boundaries skips dependencies inside
 * one element; a feature importing ANOTHER feature is a separate policy, see compiled-rules.mjs).
 */
export const ALLOWED_DEPENDENCIES = Object.freeze({
  [ElementTypes.core]: Object.freeze([]),
  [ElementTypes.shared]: Object.freeze([ElementTypes.core]),
  [ElementTypes.layouts]: Object.freeze([ElementTypes.core, ElementTypes.shared]),
  [ElementTypes.patterns]: Object.freeze([ElementTypes.core, ElementTypes.shared]),
  [ElementTypes.features]: Object.freeze([ElementTypes.core, ElementTypes.layouts, ElementTypes.patterns, ElementTypes.shared]),
  [ElementTypes.showcase]: Object.freeze([ElementTypes.core, ElementTypes.layouts, ElementTypes.patterns, ElementTypes.shared]),
});

const SEPARATOR = '/';
const ANY_FOLDER = '*';

/** Joins an app root and a folder into a forward-slash pattern without duplicate separators. */
function under(appRoot, folder) {
  const root = appRoot.replaceAll('\\', SEPARATOR).replace(/\/+$/, '');
  return root === '' ? folder : root + SEPARATOR + folder;
}

/**
 * The `boundaries/elements` descriptors for an app rooted at `appRoot` (relative to the ESLint root path).
 * Full-path matching (`partialMatch: false`), so a nested folder that happens to be called `core` is not an element.
 */
export function elementDescriptors(appRoot) {
  const single = [ElementTypes.core, ElementTypes.layouts, ElementTypes.patterns, ElementTypes.shared, ElementTypes.showcase];
  return [
    ...single.map((type) => ({ type, pattern: under(appRoot, type), partialMatch: false })),
    {
      type: ElementTypes.features,
      pattern: under(appRoot, `${ElementTypes.features}${SEPARATOR}${ANY_FOLDER}`),
      partialMatch: false,
      capture: [FEATURE_CAPTURE],
    },
  ];
}

/** The base policies: one allow list per element type, from {@link ALLOWED_DEPENDENCIES}. */
export function basePolicies() {
  return Object.entries(ALLOWED_DEPENDENCIES)
    .filter(([, allowed]) => allowed.length > 0)
    .map(([type, allowed]) => ({
      from: { element: { type } },
      allow: { to: { element: { types: [...allowed] } } },
    }));
}
