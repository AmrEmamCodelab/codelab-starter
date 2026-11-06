import type { QueueTab } from '../../../patterns';
import { QueueView } from '../../../shared/records';
import { ServiceRequestsStaffTranslationKeys } from './service-requests-staff-translation-keys';

const Keys = ServiceRequestsStaffTranslationKeys.Queue;

/** One queue view the screen may offer, and whether it needs `assignments.work`. */
interface ViewTab {
  readonly value: QueueView;
  readonly labelKey: string;
  readonly needsWork: boolean;
}

/** The queue views in tab order. */
const VIEW_TABS: readonly ViewTab[] = [
  { value: QueueView.Unassigned, labelKey: Keys.TabUnassigned, needsWork: true },
  { value: QueueView.Mine, labelKey: Keys.TabMine, needsWork: true },
  { value: QueueView.All, labelKey: Keys.TabAll, needsWork: false },
];

/**
 * The queue views the user may open: "unassigned" and "mine" only for a holder of `assignments.work` (claiming is what
 * makes them useful), "all" for every queue reader.
 */
export function staffQueueTabs(canWork: boolean): readonly QueueTab[] {
  return VIEW_TABS.filter((tab) => canWork || !tab.needsWork).map(({ value, labelKey }) => ({ value, labelKey }));
}

/** `value` as a queue view, or null when it is not one. */
export function asQueueView(value: string | null | undefined): QueueView | null {
  return Object.values(QueueView).find((view) => view === value) ?? null;
}

/** The view the queue opens on: the first tab the user has (unassigned for a worker, else all). */
export function initialQueueView(tabs: readonly QueueTab[]): QueueView {
  return asQueueView(tabs[0]?.value) ?? QueueView.All;
}
