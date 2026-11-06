import type { WireId } from '@codelab/ng-security';

/*
 * Hand-written wire shapes of the Service Request sample API (`api/service-requests`, Phase 7 contract). They mirror
 * the backend `Modules/ServiceRequests` exactly, so this layer does not wait on the generated client; the generated
 * types under `core/api/generated` may replace them later without changing the client's signatures.
 */

/** The record type key of the sample: the `{recordType}` segment of the generic records and queues APIs. */
export const SERVICE_REQUEST_RECORD_TYPE = 'service-request';

/** The record type attachments of a service request hang on (`app-record-attachments [recordType]`). */
export const SERVICE_REQUEST_ATTACHABLE_RECORD_TYPE = 'service-request';

/** The number prefix the server allocates (`SR-2026-001`). Display only: the server owns numbering. */
export const SERVICE_REQUEST_NUMBER_PREFIX = 'SR';

/** The lookup type of the request category (`GET api/lookups/{type}`). */
export const SERVICE_REQUEST_CATEGORY_LOOKUP_TYPE = 'service-request-categories';

/** The attachment type keys of the sample (`GET api/attachments/types?recordType=service-request`). */
export const ServiceRequestAttachmentTypes = {
  /** Required, at most 3 files, pdf/jpg/png. Submit is refused (409 ATTACHMENTS_REQUIRED_MISSING) without one. */
  Document: 'service-request-document',
  /** Optional, at most 5 files. */
  Supporting: 'service-request-supporting',
} as const;

/** A service request's PublicId: opaque, sent back exactly as received. */
export type ServiceRequestId = WireId<'service-request'>;

/** A lookup value's PublicId as `GET api/lookups/{type}` lists it (family `lookup-items`). */
export type ServiceRequestCategoryId = WireId<'lookup-items'>;

/** The status of a service request. Mirrors the backend `ServiceRequestStatuses` (the integers the API sends). */
export enum ServiceRequestStatus {
  Draft = 1,
  Submitted = 2,
  UnderReview = 3,
  Returned = 4,
  Resubmitted = 5,
  Approved = 6,
  Rejected = 7,
  Cancelled = 8,
}

/** The statuses no action leaves (the server also sends `isTerminal`; prefer it when the record is at hand). */
export const SERVICE_REQUEST_TERMINAL_STATUSES: ReadonlySet<ServiceRequestStatus> = new Set([
  ServiceRequestStatus.Approved,
  ServiceRequestStatus.Rejected,
  ServiceRequestStatus.Cancelled,
]);

/** True when `status` is terminal. */
export function isTerminalServiceRequestStatus(status: ServiceRequestStatus): boolean {
  return SERVICE_REQUEST_TERMINAL_STATUSES.has(status);
}

/**
 * The action keys of the sample's transition catalog (backend `ServiceRequestActions`): the `action` of
 * `POST api/records/service-request/{id}/transitions` and the `actionKey` of an allowed action.
 */
export const ServiceRequestActions = {
  Submit: 'submit',
  StartReview: 'start-review',
  Return: 'return',
  Resubmit: 'resubmit',
  Approve: 'approve',
  Reject: 'reject',
  Cancel: 'cancel',
} as const;

export type ServiceRequestAction = (typeof ServiceRequestActions)[keyof typeof ServiceRequestActions];

/** The wizard step a draft save validates (`step` of `PUT api/service-requests/{id}/draft`). */
export enum ServiceRequestDraftStep {
  Request = 1,
  Details = 2,
  Attachments = 3,
}

/** The answer of `POST api/service-requests` (201): the new draft's id. */
export interface CreatedServiceRequest {
  readonly id: ServiceRequestId;
}

/**
 * The editable fields of a draft. Every field is null until the requester fills it. `preferredDate` is an ISO date
 * (`YYYY-MM-DD`, no time); `contactPhone` is a Saudi mobile number (`05XXXXXXXX`).
 */
export interface ServiceRequestDraftFields {
  readonly categoryId: ServiceRequestCategoryId | null;
  readonly title: string | null;
  readonly description: string | null;
  readonly contactPhone: string | null;
  readonly preferredDate: string | null;
}

/** `GET api/service-requests/{id}/draft`. */
export interface ServiceRequestDraft extends ServiceRequestDraftFields {
  readonly id: ServiceRequestId;
}

/** The body of `PUT api/service-requests/{id}/draft`: the draft fields plus the step the server validates. */
export interface SaveServiceRequestDraftBody extends ServiceRequestDraftFields {
  readonly step: ServiceRequestDraftStep;
}

/** The answer of `POST api/service-requests/{id}/submit` (200): the allocated number (`SR-2026-001`). */
export interface SubmittedServiceRequest {
  readonly number: string;
}

/** `GET api/service-requests/{id}`: the request's own fields (status, timeline and actions come from api/records). */
export interface ServiceRequestDetails {
  readonly id: ServiceRequestId;
  readonly number: string | null;
  readonly categoryId: ServiceRequestCategoryId;
  readonly categoryNameAr: string;
  readonly categoryNameEn: string;
  readonly title: string;
  readonly description: string;
  readonly contactPhone: string;
  readonly preferredDate: string | null;
}

/** An empty draft (what a new wizard starts from). */
export const EMPTY_SERVICE_REQUEST_DRAFT_FIELDS: ServiceRequestDraftFields = Object.freeze({
  categoryId: null,
  title: null,
  description: null,
  contactPhone: null,
  preferredDate: null,
});
