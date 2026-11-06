import { SERVICE_REQUESTS_PERMISSION_KEYS } from '../data';

/** The requester screens' gates: generated permission keys, never an empty (fail-closed) placeholder. */
export const RequesterGates = {
  /** Start, edit, submit a request (`service-requests.create`). */
  Create: SERVICE_REQUESTS_PERMISSION_KEYS.Create,
  /** See own requests (`service-requests.view-own`). */
  ViewOwn: SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn,
} as const;
