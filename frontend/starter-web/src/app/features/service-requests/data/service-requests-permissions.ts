import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';

/**
 * The permission keys of the Service Request sample, taken from the generated catalog
 * (`core/permissions/permission-keys.generated.ts`, written by `npm run api:gen` from the backend
 * `ServiceRequestsPermissionKeys`). Every value is a generated `PermissionKey`, so routes and `*hasPermission` take
 * them with no cast; a key the backend renames or drops breaks the build after the next `api:gen`.
 */
export const SERVICE_REQUESTS_PERMISSION_KEYS = {
  /** Create, edit and submit an own draft; resubmit and cancel an own request. */
  Create: PermissionKeys.ServiceRequestsCreate,
  /** See own requests (my requests, detail, timeline). */
  ViewOwn: PermissionKeys.ServiceRequestsViewOwn,
  /** See the staff work queue (the record type's queue permission). */
  Queue: PermissionKeys.ServiceRequestsQueue,
  /** Start the review of a submitted or resubmitted request. */
  Review: PermissionKeys.ServiceRequestsReview,
  /** Approve, reject or return a request under review. */
  Decide: PermissionKeys.ServiceRequestsDecide,
} as const;

export type ServiceRequestsPermissionKey =
  (typeof SERVICE_REQUESTS_PERMISSION_KEYS)[keyof typeof SERVICE_REQUESTS_PERMISSION_KEYS];
