import type { DgaStatusTone } from '@codelab/ng-dga';
import type { AppLanguage } from '../../core/i18n/language';

/**
 * One change in a record's status history, as the screen maps it from its API.
 *
 * - `statusKey`: the translation key of the status name (for example `requests.status.submitted`); the screen builds
 *   it from the status lookup, never from a display label.
 * - `at`: when the change happened, an ISO-8601 instant from the API.
 * - `actorLabel` / `reasonText`: who made the change and why, as the API sent them (names and free text are data, not
 *   translation keys).
 */
export interface StatusHistoryEntry {
  readonly statusKey: string;
  readonly at: string;
  readonly actorLabel?: string;
  readonly reasonText?: string;
}

/** The order the timeline lists its entries in. */
export const HistoryOrder = {
  NewestFirst: 'newest-first',
  OldestFirst: 'oldest-first',
} as const;
export type HistoryOrder = (typeof HistoryOrder)[keyof typeof HistoryOrder];

/** The tone of the pill for a status: the screen maps its workflow statuses; unknown ones fall back to neutral. */
export type StatusToneResolver = (statusKey: string) => DgaStatusTone;

/** The default resolver: every status is neutral. */
export const NEUTRAL_STATUS_TONE: StatusToneResolver = () => 'neutral';

const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' };
const ORDER_SIGN: Readonly<Record<HistoryOrder, number>> = { [HistoryOrder.NewestFirst]: -1, [HistoryOrder.OldestFirst]: 1 };

/** Milliseconds since the epoch, or -Infinity for a value that does not parse (it sorts as the oldest). */
function instantOf(entry: StatusHistoryEntry): number {
  const time = Date.parse(entry.at);
  return Number.isNaN(time) ? Number.NEGATIVE_INFINITY : time;
}

function compareInstants(left: StatusHistoryEntry, right: StatusHistoryEntry): number {
  const a = instantOf(left);
  const b = instantOf(right);
  if (a === b) return 0;
  return a < b ? -1 : 1;
}

/** The entries sorted by `at` in `order`; entries with the same instant keep their API order. Never mutates the input. */
export function sortHistory(entries: readonly StatusHistoryEntry[], order: HistoryOrder): readonly StatusHistoryEntry[] {
  const sign = ORDER_SIGN[order];
  return entries
    .map((entry, index) => ({ entry, index }))
    .sort((left, right) => sign * compareInstants(left.entry, right.entry) || left.index - right.index)
    .map(({ entry }) => entry);
}

/** The latest entry (the current status), or null for an empty history. Ties go to the entry the API sent last. */
export function latestEntry(entries: readonly StatusHistoryEntry[]): StatusHistoryEntry | null {
  let latest: StatusHistoryEntry | null = null;
  for (const entry of entries) {
    if (latest === null || compareInstants(entry, latest) >= 0) latest = entry;
  }
  return latest;
}

/** An ISO-8601 instant as local date and time text in `language`, or the raw value when it does not parse. */
export function formatHistoryInstant(iso: string, language: AppLanguage): string {
  const instant = new Date(iso);
  return Number.isNaN(instant.getTime()) ? iso : new Intl.DateTimeFormat(language, INSTANT_FORMAT).format(instant);
}
