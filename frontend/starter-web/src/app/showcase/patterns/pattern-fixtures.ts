import type { DgaStatusTone } from '@codelab/ng-dga';
import type { Clock } from '../../core/session/clock';
import {
  ActionTone,
  SortDirection,
  TocDepth,
  type AllowedAction,
  type KpiTile,
  type PortalSection,
  type QueueTab,
  type QuickAction,
  type SortState,
  type StatusHistoryEntry,
} from '../../patterns';
import { hoursFromNow } from '../showcase-clock';
import { ShowcasePageId, showcaseLink } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';

const P = ShowcaseTranslationKeys.Patterns;
const Demo = ShowcaseTranslationKeys.Demo;

/** Translates a key; fixture helpers take it as a function so they stay plain and testable. */
export type Translate = (key: string) => string;

// ---- Admin list + editor dialog -------------------------------------------------------------------------------

/** One organisation row of the admin-list demo. The name is a translation key so the row reads in ar and en. */
export interface OrgRow {
  readonly id: string;
  readonly nameKey: string;
  readonly code: string;
  readonly active: boolean;
}

export const ORG_FIXTURE: readonly OrgRow[] = [
  { id: 'org-health', nameKey: P.OrgHealth, code: 'HLT-01', active: true },
  { id: 'org-education', nameKey: P.OrgEducation, code: 'EDU-02', active: true },
  { id: 'org-transport', nameKey: P.OrgTransport, code: 'TRN-03', active: false },
  { id: 'org-housing', nameKey: P.OrgHousing, code: 'HSG-04', active: true },
  { id: 'org-culture', nameKey: P.OrgCulture, code: 'CUL-05', active: true },
];

/** The admin list's page size in the demo: small, so the pager shows with the fixture rows. */
export const ADMIN_LIST_PAGE_SIZE = 5;

/** The total the demo reports, so the pager draws several pages (the fixture is one page of it). */
export const ADMIN_LIST_TOTAL = 23;

/** The search and sort a list demo applies to its fixture rows, as a server would. */
export interface ListQuery {
  readonly search: string;
  readonly sort: SortState | null;
}

/**
 * The rows that match the search (case-insensitive, on `textOf`), in the sort order: rows sorted by the value
 * `sortValue(row, key)` returns, ascending or descending. No sort keeps the fixture order.
 */
export function applyListQuery<T>(
  rows: readonly T[],
  query: ListQuery,
  accessors: { readonly textOf: (row: T) => string; readonly sortValue: (row: T, key: string) => string },
): readonly T[] {
  const needle = query.search.trim().toLocaleLowerCase();
  const matching = needle === '' ? rows : rows.filter(row => accessors.textOf(row).toLocaleLowerCase().includes(needle));
  const sort = query.sort;
  if (sort === null) {
    return matching;
  }
  const direction = sort.direction === SortDirection.Descending ? -1 : 1;
  return [...matching].sort(
    (first, second) => direction * accessors.sortValue(first, sort.key).localeCompare(accessors.sortValue(second, sort.key)),
  );
}

// ---- Tree editor ----------------------------------------------------------------------------------------------

/** The label of every node of the tree demo (groups and leaves), keyed by its dotted key. */
export const TREE_LABELS: Readonly<Record<string, string>> = {
  requests: P.TreeRequests,
  'requests.view': P.TreeRequestsView,
  'requests.create': P.TreeRequestsCreate,
  'requests.approve': P.TreeRequestsApprove,
  reports: P.TreeReports,
  'reports.view': P.TreeReportsView,
  'reports.export': P.TreeReportsExport,
  admin: P.TreeAdmin,
  'admin.users': P.TreeAdminUsers,
  'admin.roles': P.TreeAdminRoles,
};

/** The leaf keys of the tree demo (the groups come from the dotted prefixes). */
export const TREE_KEYS: readonly string[] = [
  'requests.view',
  'requests.create',
  'requests.approve',
  'reports.view',
  'reports.export',
  'admin.users',
  'admin.roles',
];

/** The keys the tree demo starts with. */
export const TREE_INITIAL: ReadonlySet<string> = new Set(['requests.view', 'requests.create', 'reports.view']);

// ---- Work queue -----------------------------------------------------------------------------------------------

export const QueueView = { Mine: 'mine', Team: 'team', All: 'all' } as const;

export type QueueView = (typeof QueueView)[keyof typeof QueueView];

export const QueueStatus = { New: 'new', Review: 'review', Returned: 'returned' } as const;

export type QueueStatus = (typeof QueueStatus)[keyof typeof QueueStatus];

/** How each queue status reads: its label and tag tone. */
export const QUEUE_STATUS_DISPLAY: Readonly<Record<QueueStatus, { readonly labelKey: string; readonly tone: DgaStatusTone }>> = {
  [QueueStatus.New]: { labelKey: Demo.StatusNew, tone: 'info' },
  [QueueStatus.Review]: { labelKey: Demo.StatusReview, tone: 'progress' },
  [QueueStatus.Returned]: { labelKey: Demo.StatusReturned, tone: 'warning' },
};

/** One row of the work-queue demo; `dueInHours` is relative to the showcase clock (negative: overdue). */
export interface QueueFixture {
  readonly id: string;
  readonly reference: string;
  readonly applicantKey: string;
  readonly status: QueueStatus;
  readonly dueInHours: number | null;
  readonly views: readonly QueueView[];
}

export const QUEUE_FIXTURE: readonly QueueFixture[] = [
  { id: 'task-1', reference: 'REQ-2026-000142', applicantKey: P.ApplicantOne, status: QueueStatus.New, dueInHours: 72, views: [QueueView.Mine, QueueView.All] },
  { id: 'task-2', reference: 'REQ-2026-000139', applicantKey: P.ApplicantTwo, status: QueueStatus.Review, dueInHours: 6, views: [QueueView.Mine, QueueView.Team, QueueView.All] },
  { id: 'task-3', reference: 'REQ-2026-000131', applicantKey: P.ApplicantThree, status: QueueStatus.Returned, dueInHours: -20, views: [QueueView.Team, QueueView.All] },
  { id: 'task-4', reference: 'REQ-2026-000127', applicantKey: P.ApplicantFour, status: QueueStatus.Review, dueInHours: null, views: [QueueView.All] },
];

/** A queue row as the pattern binds it: the due time resolved against the clock. */
export interface QueueRow {
  readonly id: string;
  readonly reference: string;
  readonly applicantKey: string;
  readonly status: QueueStatus;
  readonly dueAt: string | null;
}

/** The rows of one queue view, optionally narrowed to one status, with due times resolved against `clock`. */
export function queueRows(view: string, status: QueueStatus | null, clock: Clock): QueueRow[] {
  return QUEUE_FIXTURE.filter(row => row.views.some(candidate => candidate === view))
    .filter(row => status === null || row.status === status)
    .map(row => ({
      id: row.id,
      reference: row.reference,
      applicantKey: row.applicantKey,
      status: row.status,
      dueAt: row.dueInHours === null ? null : hoursFromNow(clock, row.dueInHours),
    }));
}

/** The queue tabs with their row counts. */
export function queueTabs(): QueueTab[] {
  const count = (view: QueueView) => QUEUE_FIXTURE.filter(row => row.views.includes(view)).length;
  return [
    { value: QueueView.Mine, labelKey: P.QueueMine, count: count(QueueView.Mine) },
    { value: QueueView.Team, labelKey: P.QueueTeam, count: count(QueueView.Team) },
    { value: QueueView.All, labelKey: P.QueueAll, count: count(QueueView.All) },
  ];
}

// ---- Staff detail + decision dialog / requester detail + status tracker ---------------------------------------

/** The action codes of the staff-detail demo (the screen's own constants in a real feature). */
export const DecisionCode = { Approve: 'approve', Return: 'return', Reject: 'reject' } as const;

export const ALLOWED_ACTIONS_FIXTURE: readonly AllowedAction[] = [
  { code: DecisionCode.Approve, labelKey: P.Approve, requiresReason: false, tone: ActionTone.Primary },
  { code: DecisionCode.Return, labelKey: P.Return, requiresReason: true, tone: ActionTone.Neutral },
  { code: DecisionCode.Reject, labelKey: P.Reject, requiresReason: true, tone: ActionTone.Destructive },
];

/** The status keys of the history demos, with their tag tones. */
export const HISTORY_TONES: Readonly<Record<string, DgaStatusTone>> = {
  [P.HistorySubmitted]: 'info',
  [P.HistoryReview]: 'progress',
  [P.HistoryReturned]: 'warning',
  [P.HistoryResubmitted]: 'info',
};

/** The tone of a history status (neutral for an unknown key). */
export function historyTone(statusKey: string): DgaStatusTone {
  return HISTORY_TONES[statusKey] ?? 'neutral';
}

const HOURS_SUBMITTED = -120;
const HOURS_REVIEW = -96;
const HOURS_RETURNED = -50;
const HOURS_RESUBMITTED = -26;

/** A request's status history, relative to the clock, with actor names and the reason translated. */
export function historyFixture(clock: Clock, translate: Translate): StatusHistoryEntry[] {
  return [
    { statusKey: P.HistorySubmitted, at: hoursFromNow(clock, HOURS_SUBMITTED), actorLabel: translate(P.ActorApplicant) },
    { statusKey: P.HistoryReview, at: hoursFromNow(clock, HOURS_REVIEW), actorLabel: translate(P.ActorReviewer) },
    {
      statusKey: P.HistoryReturned,
      at: hoursFromNow(clock, HOURS_RETURNED),
      actorLabel: translate(P.ActorReviewer),
      reasonText: translate(P.HistoryReason),
    },
    { statusKey: P.HistoryResubmitted, at: hoursFromNow(clock, HOURS_RESUBMITTED), actorLabel: translate(P.ActorApplicant) },
  ];
}

/** When the demo request was created (the first history entry). */
export function createdAtFixture(clock: Clock): string {
  return hoursFromNow(clock, HOURS_SUBMITTED);
}

// ---- Portal content -------------------------------------------------------------------------------------------

export const PORTAL_SECTIONS: readonly PortalSection[] = [
  { id: 'about', titleKey: P.PortalAbout },
  { id: 'steps', titleKey: P.PortalSteps },
  { id: 'steps-online', titleKey: P.PortalStepsOnline, depth: TocDepth.Subsection },
  { id: 'documents', titleKey: P.PortalDocuments },
];

// ---- Landing dashboard ----------------------------------------------------------------------------------------

const KPI_OPEN = 3;
const KPI_RETURNED = 1;
const KPI_CLOSED = 12;

/** The KPI tiles; the average is text (already a unit phrase), the others numbers formatted per language. */
export function kpiTiles(translate: Translate): KpiTile[] {
  return [
    { labelKey: P.KpiOpen, value: KPI_OPEN, icon: 'file', link: showcaseLink(ShowcasePageId.WorkQueue) },
    { labelKey: P.KpiReturned, value: KPI_RETURNED, icon: 'warning', link: showcaseLink(ShowcasePageId.RequesterDetail) },
    { labelKey: P.KpiClosed, value: KPI_CLOSED, icon: 'check' },
    { labelKey: P.KpiAverage, value: translate(P.KpiAverageValue), icon: 'clock' },
  ];
}

export const QUICK_ACTIONS: readonly QuickAction[] = [
  { labelKey: P.QuickNew, link: showcaseLink(ShowcasePageId.Wizard), icon: 'plus' },
  { labelKey: P.QuickTrack, link: showcaseLink(ShowcasePageId.RequesterDetail), icon: 'search' },
  { labelKey: P.QuickGuide, link: showcaseLink(ShowcasePageId.PortalContent), icon: 'info' },
];

export const RECENT_FIXTURE: readonly { readonly reference: string; readonly labelKey: string }[] = [
  { reference: 'REQ-2026-000142', labelKey: P.RecentOne },
  { reference: 'REQ-2026-000139', labelKey: P.RecentTwo },
  { reference: 'REQ-2026-000131', labelKey: P.RecentThree },
];
