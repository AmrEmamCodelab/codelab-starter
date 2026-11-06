import { WorkflowAudience } from '../../../shared/records';
import { ServiceRequestActions } from '../data';
import {
  SERVICE_REQUEST_STATUS_KEYS,
  serviceRequestActionLabelKey,
  serviceRequestStatusWording,
} from './service-request-status-display';

/**
 * Every translation key the staff screens render, under `serviceRequests.staff.*` and `serviceRequests.record.*`
 * (the record card the staff and admin details share) in `src/assets/i18n/ar.json` and `en.json`.
 */
export const ServiceRequestsStaffTranslationKeys = {
  /** The shell navigation entry of the staff queue. */
  Nav: 'serviceRequests.staff.nav',
  Queue: {
    Title: 'serviceRequests.staff.queue.title',
    Empty: 'serviceRequests.staff.queue.empty',
    Search: 'serviceRequests.staff.queue.search',
    TabUnassigned: 'serviceRequests.staff.queue.tabs.unassigned',
    TabMine: 'serviceRequests.staff.queue.tabs.mine',
    TabAll: 'serviceRequests.staff.queue.tabs.all',
    StatusFilter: 'serviceRequests.staff.queue.filters.status',
    AnyStatus: 'serviceRequests.staff.queue.filters.any-status',
    ColumnNumber: 'serviceRequests.staff.queue.columns.number',
    ColumnStatus: 'serviceRequests.staff.queue.columns.status',
    ColumnOwner: 'serviceRequests.staff.queue.columns.owner',
    ColumnSubmittedAt: 'serviceRequests.staff.queue.columns.submitted-at',
    ColumnAssignee: 'serviceRequests.staff.queue.columns.assignee',
    ColumnSla: 'serviceRequests.staff.queue.columns.sla',
    Unassigned: 'serviceRequests.staff.queue.unassigned',
    NoSla: 'serviceRequests.staff.queue.no-sla',
    Notifications: 'serviceRequests.staff.queue.notifications',
    /** `{{number}}`. */
    Claimed: 'serviceRequests.staff.queue.claimed',
    /** `{{number}}`. */
    Released: 'serviceRequests.staff.queue.released',
  },
  Actions: {
    Claim: 'serviceRequests.staff.actions.claim',
    Release: 'serviceRequests.staff.actions.release',
  },
  Detail: {
    /** `{{number}}`. */
    Title: 'serviceRequests.staff.detail.title',
    Back: 'serviceRequests.staff.detail.back',
    /** `{{name}}`. */
    AssignedTo: 'serviceRequests.staff.detail.assigned-to',
    NotAssigned: 'serviceRequests.staff.detail.not-assigned',
    Attachments: 'serviceRequests.staff.detail.attachments',
    Steps: 'serviceRequests.staff.detail.steps',
    ReasonMessage: 'serviceRequests.staff.detail.reason-message',
    DecisionDone: 'serviceRequests.staff.detail.decision-done',
  },
  Record: {
    Card: 'serviceRequests.record.card',
    Number: 'serviceRequests.record.number',
    Owner: 'serviceRequests.record.owner',
    SubmittedAt: 'serviceRequests.record.submitted-at',
    LastReason: 'serviceRequests.record.last-reason',
  },
  Common: {
    /** `{{code}}`. */
    ReferenceCode: 'serviceRequests.staff.reference-code',
  },
} as const;

/** The status wording keys the staff and admin screens show (`serviceRequests.status.<key>.staff|admin`). */
export const SERVICE_REQUEST_STAFF_ADMIN_WORDING_KEYS: readonly string[] = Object.values(SERVICE_REQUEST_STATUS_KEYS).flatMap((statusKey) => [
  serviceRequestStatusWording(statusKey, WorkflowAudience.Staff),
  serviceRequestStatusWording(statusKey, WorkflowAudience.Admin),
]);

/** The label keys of every catalog action (`serviceRequests.action.<key>`): decision buttons and timeline. */
export const SERVICE_REQUEST_ACTION_LABEL_KEYS: readonly string[] = Object.values(ServiceRequestActions).map(serviceRequestActionLabelKey);

/** The flat list of every staff-screen key. */
export const SERVICE_REQUESTS_STAFF_TRANSLATION_KEYS: readonly string[] = Object.values(ServiceRequestsStaffTranslationKeys).flatMap(
  (group) => (typeof group === 'string' ? [group] : Object.values(group)),
);
