import type { DgaStatusTone } from '@codelab/ng-dga';
import { ActionTone } from '../../../patterns';
import { statusWordingResolver, type StatusWordingResolver } from '../../../shared/records';
import { ServiceRequestActions, ServiceRequestStatus } from '../data';

/*
 * How the sample's statuses and actions look on the staff and admin screens: the catalog's status keys (for the
 * status filter labels), the status-tag tones and the decision-button tones. The SERVER owns the workflow (statuses,
 * allowed actions, wording keys); this file only maps its integers and keys to presentation.
 */

/** The prefix of the sample's status wording keys: `serviceRequests.status.<statusKey>.<requester|staff|admin>`. */
export const SERVICE_REQUEST_STATUS_WORDING_PREFIX = 'serviceRequests.status';

/** The prefix of the sample's action labels: `serviceRequests.action.<actionKey>`. */
export const SERVICE_REQUEST_ACTION_LABEL_PREFIX = 'serviceRequests.action';

/** Resolves a status key and an audience to the wording key (`serviceRequests.status.under-review.staff`). */
export const serviceRequestStatusWording: StatusWordingResolver = statusWordingResolver(SERVICE_REQUEST_STATUS_WORDING_PREFIX);

/** The catalog key of each status (backend `ServiceRequestStatuses` definitions): the `<statusKey>` of its wording. */
export const SERVICE_REQUEST_STATUS_KEYS: Readonly<Record<ServiceRequestStatus, string>> = {
  [ServiceRequestStatus.Draft]: 'draft',
  [ServiceRequestStatus.Submitted]: 'submitted',
  [ServiceRequestStatus.UnderReview]: 'under-review',
  [ServiceRequestStatus.Returned]: 'returned',
  [ServiceRequestStatus.Resubmitted]: 'resubmitted',
  [ServiceRequestStatus.Approved]: 'approved',
  [ServiceRequestStatus.Rejected]: 'rejected',
  [ServiceRequestStatus.Cancelled]: 'cancelled',
};

/** The status-tag tone of each status; a status the app does not know is neutral. */
const STATUS_TONES: Readonly<Record<ServiceRequestStatus, DgaStatusTone>> = {
  [ServiceRequestStatus.Draft]: 'neutral',
  [ServiceRequestStatus.Submitted]: 'info',
  [ServiceRequestStatus.UnderReview]: 'progress',
  [ServiceRequestStatus.Returned]: 'warning',
  [ServiceRequestStatus.Resubmitted]: 'info',
  [ServiceRequestStatus.Approved]: 'success',
  [ServiceRequestStatus.Rejected]: 'error',
  [ServiceRequestStatus.Cancelled]: 'neutral',
};

/** The tone of the status tag for `status` (the integer the API sends). */
export function serviceRequestStatusTone(status: number): DgaStatusTone {
  return STATUS_TONES[status as ServiceRequestStatus] ?? 'neutral';
}

/** The statuses a request can have while it sits in the staff queue (the queue's status filter, in order). */
export const STAFF_QUEUE_STATUSES: readonly ServiceRequestStatus[] = [
  ServiceRequestStatus.Submitted,
  ServiceRequestStatus.UnderReview,
  ServiceRequestStatus.Returned,
  ServiceRequestStatus.Resubmitted,
];

/** Every status, for the admin oversight filter (in workflow order). */
export const ALL_SERVICE_REQUEST_STATUSES: readonly ServiceRequestStatus[] = [
  ServiceRequestStatus.Draft,
  ServiceRequestStatus.Submitted,
  ServiceRequestStatus.UnderReview,
  ServiceRequestStatus.Returned,
  ServiceRequestStatus.Resubmitted,
  ServiceRequestStatus.Approved,
  ServiceRequestStatus.Rejected,
  ServiceRequestStatus.Cancelled,
];

/** The tone of each staff decision button: the expected step is primary, refusals are destructive. */
const ACTION_TONES: Readonly<Record<string, ActionTone>> = {
  [ServiceRequestActions.StartReview]: ActionTone.Primary,
  [ServiceRequestActions.Approve]: ActionTone.Primary,
  [ServiceRequestActions.Return]: ActionTone.Neutral,
  [ServiceRequestActions.Reject]: ActionTone.Destructive,
  [ServiceRequestActions.Cancel]: ActionTone.Destructive,
};

/** The decision-button tone of an action key; an action the screens do not know is a neutral side step. */
export function serviceRequestActionTone(actionKey: string): ActionTone {
  return ACTION_TONES[actionKey] ?? ActionTone.Neutral;
}

/** The label key of an action (`serviceRequests.action.approve`), for the timeline. */
export function serviceRequestActionLabelKey(actionKey: string): string {
  return `${SERVICE_REQUEST_ACTION_LABEL_PREFIX}.${actionKey}`;
}
