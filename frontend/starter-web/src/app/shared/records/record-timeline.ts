import { instantMs, type InstantFormatter } from './record-instant';
import { TRANSITION_SIDE_KEYS } from './records-translation-keys';
import type { TimelineEntry, WorkflowAudience } from './records.models';
import type { StatusWordingResolver } from './status-wording';

/** The order the timeline lists its entries in. Integers, like every enum the starter owns. */
export enum TimelineOrder {
  NewestFirst = 1,
  OldestFirst = 2,
}

/** Maps an action key to the translation key of its label, or null to show no action label. */
export type ActionLabelResolver = (actionKey: string) => string | null;

/** One timeline row as the component renders it: every key resolved, the instant formatted. */
export interface TimelineRow {
  readonly entry: TimelineEntry;
  /** The status wording for the audience (translation key). */
  readonly statusLabelKey: string;
  /** The side's label (translation key). */
  readonly sideLabelKey: string | null;
  /** The action's label (translation key), when the screen resolves one. */
  readonly actionLabelKey: string | null;
  readonly atText: string;
  /** The reason, trimmed; null when none was given. */
  readonly reason: string | null;
}

/** What the rows need from the screen. */
export interface TimelineRowOptions {
  readonly audience: WorkflowAudience;
  readonly statusKeyFor: StatusWordingResolver;
  readonly actionLabelKeyFor: ActionLabelResolver | null;
  readonly order: TimelineOrder;
  readonly formatAt: InstantFormatter;
}

/** -1 sorts newer entries first; 1 sorts older entries first. */
const ORDER_SIGN: Readonly<Record<TimelineOrder, number>> = {
  [TimelineOrder.NewestFirst]: -1,
  [TimelineOrder.OldestFirst]: 1,
};

function compareInstants(left: TimelineEntry, right: TimelineEntry): number {
  return Math.sign(instantMs(left.at) - instantMs(right.at)) || 0;
}

/**
 * The entries sorted by `at` in `order` (newest first by default); entries with the same instant, or two unreadable
 * instants, keep the API order. Never mutates the input.
 */
export function sortTimeline(entries: readonly TimelineEntry[], order: TimelineOrder = TimelineOrder.NewestFirst): readonly TimelineEntry[] {
  const sign = ORDER_SIGN[order] ?? ORDER_SIGN[TimelineOrder.NewestFirst];
  return entries
    .map((entry, index) => ({ entry, index }))
    .sort((left, right) => sign * compareInstants(left.entry, right.entry) || left.index - right.index)
    .map(({ entry }) => entry);
}

/** The rendered rows of the timeline, sorted, in the audience's wording. */
export function timelineRows(entries: readonly TimelineEntry[], options: TimelineRowOptions): readonly TimelineRow[] {
  return sortTimeline(entries, options.order).map((entry) => ({
    entry,
    statusLabelKey: options.statusKeyFor(entry.toStatusKey, options.audience),
    sideLabelKey: TRANSITION_SIDE_KEYS[entry.side] ?? null,
    actionLabelKey: options.actionLabelKeyFor?.(entry.actionKey) ?? null,
    atText: options.formatAt(entry.at),
    reason: entry.reason?.trim() || null,
  }));
}
