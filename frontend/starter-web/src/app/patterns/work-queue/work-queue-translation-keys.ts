/**
 * Every translation key the work-queue pattern renders on its own, under `patterns.work-queue.*` in
 * `src/assets/i18n/ar.json` and `en.json`. Tab labels, column headers and the title are the screen's keys.
 */
export const WorkQueueTranslationKeys = {
  /** Accessible name of the queue tab list. */
  Tabs: 'patterns.work-queue.tabs',
  /** `{{label}}`, `{{count}}`: a tab label with its item count. */
  TabCount: 'patterns.work-queue.tab-count',
  /** Header of the bulk-select column. */
  SelectColumn: 'patterns.work-queue.select-column',
  /** `{{label}}`: accessible name of one row's checkbox. */
  SelectRow: 'patterns.work-queue.select-row',
  SelectPage: 'patterns.work-queue.select-page',
  /** `{{count}}`. */
  SelectedCount: 'patterns.work-queue.selected-count',
  ClearSelection: 'patterns.work-queue.clear-selection',
  DueOnTrack: 'patterns.work-queue.due-on-track',
  DueSoon: 'patterns.work-queue.due-soon',
  Overdue: 'patterns.work-queue.overdue',
  NoDueDate: 'patterns.work-queue.no-due-date',
} as const;

/** The flat list of every work-queue key. */
export const WORK_QUEUE_TRANSLATION_KEYS: readonly string[] = Object.values(WorkQueueTranslationKeys);
