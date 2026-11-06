import { DgaAlertVariant, type DgaStatusTone } from '@codelab/ng-dga';
import type { StatusHistoryEntry, StatusToneResolver } from '../../../patterns';
import {
  DecisionTone,
  WorkflowAudience,
  statusWordingResolver,
  type DecisionToneResolver,
  type RecordDetail,
  type TimelineEntry,
} from '../../../shared/records';
import { ServiceRequestActions, ServiceRequestStatus } from '../data';
import { ServiceRequestsRequesterTranslationKeys as Keys } from './requester-translation-keys';

/*
 * How the requester screens present a service request's status and actions. The workflow itself (which actions exist,
 * which are allowed now) is the server's: these helpers only map server values to wording keys, tones and banners.
 */

/** The audience every requester screen reads the status wording for. */
export const REQUESTER_AUDIENCE = WorkflowAudience.Requester;

/** The prefix of the catalog's wording keys: `serviceRequests.status.<statusKey>.<requester|staff|admin>`. */
export const SERVICE_REQUEST_STATUS_WORDING_PREFIX = 'serviceRequests.status';

/** The prefix of the action labels: `serviceRequests.action.<actionKey>`. */
export const SERVICE_REQUEST_ACTION_LABEL_PREFIX = 'serviceRequests.action';

/**
 * The status key of each status, as the backend catalog names it (`WorkflowStatusDefinition.Key`). The list and detail
 * screens use the key the API sends; this map serves the places that start from the integer (dashboard tiles, filter).
 */
export const SERVICE_REQUEST_STATUS_KEYS: Readonly<Record<ServiceRequestStatus, string>> = Object.freeze({
  [ServiceRequestStatus.Draft]: 'draft',
  [ServiceRequestStatus.Submitted]: 'submitted',
  [ServiceRequestStatus.UnderReview]: 'under-review',
  [ServiceRequestStatus.Returned]: 'returned',
  [ServiceRequestStatus.Resubmitted]: 'resubmitted',
  [ServiceRequestStatus.Approved]: 'approved',
  [ServiceRequestStatus.Rejected]: 'rejected',
  [ServiceRequestStatus.Cancelled]: 'cancelled',
});

/** Status key + audience to wording key (the timeline and the list carry only the status key). */
export const serviceRequestStatusWording = statusWordingResolver(SERVICE_REQUEST_STATUS_WORDING_PREFIX);

/** The requester's wording key of a status key. */
export function requesterStatusKey(statusKey: string): string {
  return serviceRequestStatusWording(statusKey, REQUESTER_AUDIENCE);
}

/** The label key of an action key (the timeline's action line). */
export function serviceRequestActionLabelKey(actionKey: string): string {
  return `${SERVICE_REQUEST_ACTION_LABEL_PREFIX}.${actionKey}`;
}

const STATUS_TONES: Readonly<Record<string, DgaStatusTone>> = Object.freeze({
  [SERVICE_REQUEST_STATUS_KEYS[ServiceRequestStatus.Draft]]: 'neutral',
  [SERVICE_REQUEST_STATUS_KEYS[ServiceRequestStatus.Submitted]]: 'info',
  [SERVICE_REQUEST_STATUS_KEYS[ServiceRequestStatus.UnderReview]]: 'progress',
  [SERVICE_REQUEST_STATUS_KEYS[ServiceRequestStatus.Returned]]: 'warning',
  [SERVICE_REQUEST_STATUS_KEYS[ServiceRequestStatus.Resubmitted]]: 'info',
  [SERVICE_REQUEST_STATUS_KEYS[ServiceRequestStatus.Approved]]: 'success',
  [SERVICE_REQUEST_STATUS_KEYS[ServiceRequestStatus.Rejected]]: 'error',
  [SERVICE_REQUEST_STATUS_KEYS[ServiceRequestStatus.Cancelled]]: 'neutral',
});

const NEUTRAL_TONE: DgaStatusTone = 'neutral';

/** The tag tone of a status key; an unknown key is neutral. */
export function serviceRequestStatusTone(statusKey: string): DgaStatusTone {
  return STATUS_TONES[statusKey] ?? NEUTRAL_TONE;
}

const WORDING_KEY_TONES: ReadonlyMap<string, DgaStatusTone> = new Map(
  Object.entries(STATUS_TONES).map(([statusKey, tone]) => [requesterStatusKey(statusKey), tone]),
);

/** The status tracker's resolver: its entries carry the requester's wording KEY, not the status key. */
export const requesterTrackerTone: StatusToneResolver = (wordingKey) => WORDING_KEY_TONES.get(wordingKey) ?? NEUTRAL_TONE;

/** The requester's decision tones: resubmit is the expected step, cancel ends the request. */
export const requesterActionTone: DecisionToneResolver = (actionKey) => {
  switch (actionKey) {
    case ServiceRequestActions.Resubmit:
      return DecisionTone.Primary;
    case ServiceRequestActions.Cancel:
      return DecisionTone.Destructive;
    default:
      return DecisionTone.Neutral;
  }
};

/** The record timeline as the status tracker's history, in the requester's wording. Actor and reason are data. */
export function requesterStatusHistory(entries: readonly TimelineEntry[]): readonly StatusHistoryEntry[] {
  return entries.map((entry) => ({
    statusKey: requesterStatusKey(entry.toStatusKey),
    at: entry.at,
    actorLabel: entry.actorDisplayName,
    reasonText: entry.reason ?? undefined,
  }));
}

/** The banner above a returned or rejected request: why staff sent it back or refused it. */
export interface RequesterDetailBanner {
  readonly variant: DgaAlertVariant;
  readonly titleKey: string;
  readonly reason: string;
}

/** The banner of `detail`, or null: only a returned or rejected request with a reason has one. */
export function requesterDetailBanner(detail: RecordDetail): RequesterDetailBanner | null {
  const reason = detail.lastReason?.trim();
  if (!reason) return null;
  switch (detail.status) {
    case ServiceRequestStatus.Returned:
      return { variant: DgaAlertVariant.Warning, titleKey: Keys.Detail.ReturnedTitle, reason };
    case ServiceRequestStatus.Rejected:
      return { variant: DgaAlertVariant.Error, titleKey: Keys.Detail.RejectedTitle, reason };
    default:
      return null;
  }
}

/** True while the request is a draft: it is edited in the wizard, not on the detail page. */
export function isDraftStatus(status: number): boolean {
  return status === ServiceRequestStatus.Draft;
}

/**
 * True when the requester may still change the request's files on the detail page: only while it is returned for
 * changes (a draft is edited in the wizard). The server's attachment access stays the authority.
 */
export function attachmentsEditable(detail: RecordDetail): boolean {
  return !detail.isTerminal && detail.status === ServiceRequestStatus.Returned;
}
