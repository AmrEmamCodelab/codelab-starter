import type { DgaStatusTone } from '@codelab/ng-dga';
import { OpsHealthStatus, type OpsHealthCheck } from '../../core/api/ops-api.models';

/** Translation keys of the Overview screen. */
export const OverviewKeys = {
  Title: 'overview.title',
  Subtitle: 'overview.subtitle',
  Caption: 'overview.caption',
  Empty: 'overview.empty',
  Overall: 'overview.overall',
  /** `{{time}}`. */
  CheckedAt: 'overview.checkedAt',
  /** `{{count}}`. */
  Duration: 'overview.duration',
  Columns: {
    Name: 'overview.columns.name',
    Status: 'overview.columns.status',
    Duration: 'overview.columns.duration',
    Description: 'overview.columns.description',
  },
  Status: {
    Healthy: 'overview.status.healthy',
    Degraded: 'overview.status.degraded',
    Unhealthy: 'overview.status.unhealthy',
    Unknown: 'overview.status.unknown',
  },
} as const;

/** How a health status renders: the status-tag tone and its label key. */
export interface HealthStatusView {
  readonly tone: DgaStatusTone;
  readonly labelKey: string;
}

const STATUS_VIEWS: ReadonlyMap<OpsHealthStatus, HealthStatusView> = new Map([
  [OpsHealthStatus.Healthy, { tone: 'success', labelKey: OverviewKeys.Status.Healthy }],
  [OpsHealthStatus.Degraded, { tone: 'warning', labelKey: OverviewKeys.Status.Degraded }],
  [OpsHealthStatus.Unhealthy, { tone: 'error', labelKey: OverviewKeys.Status.Unhealthy }],
]);

const UNKNOWN_STATUS_VIEW: HealthStatusView = Object.freeze({ tone: 'neutral', labelKey: OverviewKeys.Status.Unknown });

/** The tone and label of a status; a value this build does not know renders neutral as "unknown", never as healthy. */
export function healthStatusView(status: OpsHealthStatus): HealthStatusView {
  return STATUS_VIEWS.get(status) ?? UNKNOWN_STATUS_VIEW;
}

/** Worst first (Unhealthy, Degraded, Healthy), then by name, so a problem is the first row an operator reads. */
export function byWorstFirst(left: OpsHealthCheck, right: OpsHealthCheck): number {
  return right.status - left.status || left.name.localeCompare(right.name);
}
