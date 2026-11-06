import { HttpStatus, tryAsId, type ApiError } from '@codelab/ng-security';
import { RecordsErrorCodes } from '../../../shared/records';
import type { ServiceRequestId } from '../data';

/** The id domain of a service request's PublicId (`ServiceRequestId`). */
const SERVICE_REQUEST_ID_DOMAIN = 'service-request';

/**
 * The refusal a detail page shows for a route id that is not even shaped like a PublicId: the same 404
 * `RECORDS_NOT_FOUND` the API would answer, without the call.
 */
export const SERVICE_REQUEST_NOT_FOUND: ApiError = Object.freeze({
  status: HttpStatus.notFound,
  code: RecordsErrorCodes.NotFound,
  fieldErrors: [],
});

/** The route's `:id` as a service request PublicId, or null when it is not shaped like one (shape only). */
export function serviceRequestIdFromRoute(raw: string | null | undefined): ServiceRequestId | null {
  return tryAsId(SERVICE_REQUEST_ID_DOMAIN, raw);
}
