/**
 * Every translation key the page patterns render on their own (screens pass their own keys for titles, headers and
 * labels). All live under `patterns.*` in `src/assets/i18n/ar.json` and `en.json`; the spec checks both bundles.
 */
export const PatternTranslationKeys = {
  Common: {
    /** `{{code}}`: the reference code the user reads out to support. */
    ReferenceCode: 'patterns.common.reference-code',
  },
  List: {
    Search: 'patterns.list.search',
    SearchPlaceholder: 'patterns.list.search-placeholder',
    Loading: 'patterns.list.loading',
    Empty: 'patterns.list.empty',
    Error: 'patterns.list.error',
    Retry: 'patterns.list.retry',
    Actions: 'patterns.list.actions',
  },
  Pager: {
    Label: 'patterns.pager.label',
    Previous: 'patterns.pager.previous',
    Next: 'patterns.pager.next',
    /** `{{page}}`. */
    Page: 'patterns.pager.page',
    /** `{{from}}`, `{{to}}`, `{{total}}`. */
    Summary: 'patterns.pager.summary',
  },
  Dialog: {
    Confirm: 'patterns.dialog.confirm',
    Cancel: 'patterns.dialog.cancel',
    Close: 'patterns.dialog.close',
    ErrorsTitle: 'patterns.dialog.errors-title',
  },
  Tree: {
    Search: 'patterns.tree.search',
    ExpandAll: 'patterns.tree.expand-all',
    CollapseAll: 'patterns.tree.collapse-all',
    SelectAll: 'patterns.tree.select-all',
    ClearAll: 'patterns.tree.clear-all',
    /** `{{label}}`. */
    Expand: 'patterns.tree.expand',
    /** `{{label}}`. */
    Collapse: 'patterns.tree.collapse',
    /** `{{selected}}`, `{{total}}`. */
    SelectedCount: 'patterns.tree.selected-count',
    NoMatch: 'patterns.tree.no-match',
    Empty: 'patterns.tree.empty',
  },
} as const;

/** The flat list of every pattern key. */
export const PATTERN_TRANSLATION_KEYS: readonly string[] = Object.values(PatternTranslationKeys).flatMap(
  (group) => Object.values(group) as string[],
);
