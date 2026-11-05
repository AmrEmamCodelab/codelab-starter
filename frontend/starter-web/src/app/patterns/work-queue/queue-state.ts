import type { DgaStatusTone, DgaTab } from '@codelab/ng-dga';
import type { TranslationParams } from '../../core/i18n/translation.service';
import { MILLISECONDS_PER_MINUTE } from '../../core/session/time-units';
import { TreeCheckState } from '../tree-editor/key-tree';
import { WorkQueueTranslationKeys } from './work-queue-translation-keys';

/** A row id as the list tracks it (the row's PublicId). */
export type QueueRowId = string | number;

/**
 * One queue tab the screen offers ("assigned to me", "my team", "all"). The pattern never decides which tabs exist or
 * what they load: the screen builds the list (usually gated by permission) and reloads when the bound `tab` changes.
 *
 * - `value` is the screen's own constant for the view, and the value of the two-way `tab` model.
 * - `count`, when known, is shown after the label ("Assigned to me (4)"); leave it unset while it is not loaded.
 */
export interface QueueTab {
  readonly value: string;
  readonly labelKey: string;
  readonly count?: number | null;
  readonly disabled?: boolean;
}

/** Where a row stands against its due time (SLA). Integers, like every enum the starter owns. */
export enum DueState {
  OnTrack = 1,
  DueSoon = 2,
  Overdue = 3,
}

const MINUTES_PER_HOUR = 60;
const HOURS_PER_DAY = 24;

/** How close to its due time a row turns "due soon" when the screen sets no window: one day (milliseconds). */
export const DEFAULT_DUE_SOON_WINDOW_MS = HOURS_PER_DAY * MINUTES_PER_HOUR * MILLISECONDS_PER_MINUTE;

/**
 * The due (SLA) column of a queue. The server computes the due time from the record's deadline rules; the pattern
 * only compares it with the clock and shows the date with an on-track / due-soon / overdue tag.
 *
 * - `dueAt(row)` is an ISO 8601 date-time, or null/undefined when the row has no deadline.
 * - `soonWindowMs` (default {@link DEFAULT_DUE_SOON_WINDOW_MS}) is how long before the due time "due soon" starts.
 * - `sortable` makes the header a sort button with `key` as the sort key, like any admin-list column.
 */
export interface DueColumnDef<T> {
  readonly key: string;
  readonly headerKey: string;
  readonly dueAt: (row: T) => string | null | undefined;
  readonly soonWindowMs?: number;
  readonly sortable?: boolean;
}

/** The translation key and status-tag tone shown for each due state. */
export const DUE_STATE_DISPLAY: Readonly<Record<DueState, { readonly labelKey: string; readonly tone: DgaStatusTone }>> = {
  [DueState.OnTrack]: { labelKey: WorkQueueTranslationKeys.DueOnTrack, tone: 'neutral' },
  [DueState.DueSoon]: { labelKey: WorkQueueTranslationKeys.DueSoon, tone: 'warning' },
  [DueState.Overdue]: { labelKey: WorkQueueTranslationKeys.Overdue, tone: 'error' },
};

/** Epoch milliseconds of an ISO date-time, or null when it is missing or unreadable. */
export function dueTime(dueAt: string | null | undefined): number | null {
  if (!dueAt) {
    return null;
  }
  const time = Date.parse(dueAt);
  return Number.isNaN(time) ? null : time;
}

/**
 * Where a row stands: overdue once `nowMs` has reached the due time, due soon within `soonWindowMs` before it, else on
 * track. Null when the row has no (readable) due time.
 */
export function dueState(dueAt: string | null | undefined, nowMs: number, soonWindowMs = DEFAULT_DUE_SOON_WINDOW_MS): DueState | null {
  const due = dueTime(dueAt);
  if (due === null) {
    return null;
  }
  if (nowMs >= due) {
    return DueState.Overdue;
  }
  return due - nowMs <= soonWindowMs ? DueState.DueSoon : DueState.OnTrack;
}

/** Translates a key with optional parameters (the pattern passes the translation service's `translate`). */
export type Translate = (key: string, params?: TranslationParams) => string;

/** The screen's queue tabs as `dga-tabs` tabs, labels translated and counts appended when known. */
export function queueTabsFor(tabs: readonly QueueTab[], translate: Translate): DgaTab[] {
  return tabs.map((tab) => {
    const label = translate(tab.labelKey);
    const hasCount = tab.count !== null && tab.count !== undefined;
    return {
      value: tab.value,
      label: hasCount ? translate(WorkQueueTranslationKeys.TabCount, { label, count: tab.count as number }) : label,
      disabled: tab.disabled ?? false,
    };
  });
}

/** The selection with `id` added, or removed when it was selected. */
export function toggleSelection(selection: ReadonlySet<QueueRowId>, id: QueueRowId): ReadonlySet<QueueRowId> {
  const next = new Set(selection);
  if (!next.delete(id)) {
    next.add(id);
  }
  return next;
}

/** How much of the current page is selected: none, some (mixed) or every row. An empty page is unchecked. */
export function pageSelectionState(selection: ReadonlySet<QueueRowId>, pageIds: readonly QueueRowId[]): TreeCheckState {
  const held = pageIds.filter((id) => selection.has(id)).length;
  if (held === 0) {
    return TreeCheckState.Unchecked;
  }
  return held === pageIds.length ? TreeCheckState.Checked : TreeCheckState.Mixed;
}

/** The selection with every row of the page added (`selected`) or removed; rows on other pages are kept. */
export function setPageSelection(
  selection: ReadonlySet<QueueRowId>,
  pageIds: readonly QueueRowId[],
  selected: boolean,
): ReadonlySet<QueueRowId> {
  const next = new Set(selection);
  for (const id of pageIds) {
    if (selected) {
      next.add(id);
    } else {
      next.delete(id);
    }
  }
  return next;
}

/** No rows selected. */
export const NO_SELECTION: ReadonlySet<QueueRowId> = new Set<QueueRowId>();

const DUE_DATE_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short', calendar: 'gregory' };

/** A due time as the queue shows it, in the active language (`ar` or `en`); empty when it is unreadable. */
export function formatDueAt(dueAt: string | null | undefined, language: string): string {
  const due = dueTime(dueAt);
  return due === null ? '' : new Intl.DateTimeFormat(language, DUE_DATE_FORMAT).format(due);
}
