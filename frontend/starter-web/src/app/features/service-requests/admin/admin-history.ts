import type { DgaStatusTone } from '@codelab/ng-dga';
import type { StatusHistoryEntry, StatusToneResolver } from '../../../patterns';
import { WorkflowAudience, type TimelineEntry } from '../../../shared/records';
import {
  ALL_SERVICE_REQUEST_STATUSES,
  SERVICE_REQUEST_STATUS_KEYS,
  serviceRequestStatusTone,
  serviceRequestStatusWording,
} from '../staff/service-request-status-display';

/**
 * The record's timeline as the status tracker's history, in the ADMIN wording: each step's status, when, who took it
 * and why (the actor and the reason are data, shown as sent).
 */
export function adminHistory(timeline: readonly TimelineEntry[]): readonly StatusHistoryEntry[] {
  return timeline.map((entry) => ({
    statusKey: serviceRequestStatusWording(entry.toStatusKey, WorkflowAudience.Admin),
    at: entry.at,
    actorLabel: entry.actorDisplayName,
    ...(entry.reason !== null && entry.reason.trim().length > 0 ? { reasonText: entry.reason } : {}),
  }));
}

/** The tone of each admin wording key (the tracker resolves tones by the entry's translation key). */
const ADMIN_WORDING_TONES: ReadonlyMap<string, DgaStatusTone> = new Map(
  ALL_SERVICE_REQUEST_STATUSES.map((status) => [
    serviceRequestStatusWording(SERVICE_REQUEST_STATUS_KEYS[status], WorkflowAudience.Admin),
    serviceRequestStatusTone(status),
  ]),
);

/** The tracker's tone resolver for the admin wording; an unknown key is neutral. */
export const adminHistoryTone: StatusToneResolver = (statusKey) => ADMIN_WORDING_TONES.get(statusKey) ?? 'neutral';
