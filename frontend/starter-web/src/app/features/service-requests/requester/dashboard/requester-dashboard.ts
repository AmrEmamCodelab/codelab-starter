import type { DgaIconName } from '@codelab/ng-dga';
import type { KpiTile, QuickAction } from '../../../../patterns';
import type { RecordListItem } from '../../../../shared/records';
import { SERVICE_REQUESTS_PERMISSION_KEYS, ServiceRequestStatus } from '../../data';
import type { ServiceRequestsRequesterLinks } from '../requester-paths';
import { isDraftStatus } from '../requester-status';
import { ServiceRequestsRequesterTranslationKeys as Keys } from '../requester-translation-keys';

/** How many of the requester's newest requests the dashboard lists. */
export const RECENT_REQUESTS_COUNT = 5;

/** One KPI of the dashboard: the requester's requests in one status, or all of them (`status` null). */
export interface RequesterKpi {
  readonly labelKey: string;
  readonly status: ServiceRequestStatus | null;
  readonly icon: DgaIconName;
}

/** The dashboard's KPIs, in display order. */
export const REQUESTER_DASHBOARD_KPIS: readonly RequesterKpi[] = Object.freeze([
  { labelKey: Keys.Dashboard.KpiAll, status: null, icon: 'file' },
  { labelKey: Keys.Dashboard.KpiDraft, status: ServiceRequestStatus.Draft, icon: 'clock' },
  { labelKey: Keys.Dashboard.KpiSubmitted, status: ServiceRequestStatus.Submitted, icon: 'upload' },
  { labelKey: Keys.Dashboard.KpiUnderReview, status: ServiceRequestStatus.UnderReview, icon: 'search' },
  { labelKey: Keys.Dashboard.KpiReturned, status: ServiceRequestStatus.Returned, icon: 'warning' },
  { labelKey: Keys.Dashboard.KpiApproved, status: ServiceRequestStatus.Approved, icon: 'success' },
  { labelKey: Keys.Dashboard.KpiRejected, status: ServiceRequestStatus.Rejected, icon: 'error' },
]);

/** The totals the server answered, by KPI label key (absent while loading or after a failure). */
export type KpiCounts = ReadonlyMap<string, number>;

/** The KPI tiles: each figure (null until it is known) links to my requests. */
export function requesterKpiTiles(counts: KpiCounts, mineLink: string): readonly KpiTile[] {
  return REQUESTER_DASHBOARD_KPIS.map((kpi) => ({
    labelKey: kpi.labelKey,
    value: counts.get(kpi.labelKey) ?? null,
    icon: kpi.icon,
    link: mineLink,
  }));
}

/** What the quick actions need to know: the links, the held permissions and the notifications page (null: none). */
export interface QuickActionContext {
  readonly links: ServiceRequestsRequesterLinks;
  readonly has: (permissionKey: string) => boolean;
  readonly notificationsLink: string | null;
}

/** The quick actions the requester may follow: a new request (create), my requests (view own), notifications. */
export function requesterQuickActions({ links, has, notificationsLink }: QuickActionContext): readonly QuickAction[] {
  const actions: QuickAction[] = [];
  if (has(SERVICE_REQUESTS_PERMISSION_KEYS.Create)) {
    actions.push({ labelKey: Keys.Dashboard.ActionNew, link: links.create, icon: 'plus' });
  }
  if (has(SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn)) {
    actions.push({ labelKey: Keys.Dashboard.ActionMine, link: links.mine, icon: 'file' });
  }
  if (notificationsLink !== null) {
    actions.push({ labelKey: Keys.Dashboard.ActionNotifications, link: notificationsLink, icon: 'bell' });
  }
  return actions;
}

/** Where a listed request opens: a draft resumes in the wizard, any other request opens its detail page. */
export function requestLink(item: Pick<RecordListItem, 'id' | 'status'>, links: ServiceRequestsRequesterLinks): string {
  return isDraftStatus(item.status) ? links.draft(item.id) : links.detail(item.id);
}
