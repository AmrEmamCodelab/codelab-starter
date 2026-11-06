import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../../core/api/api.service';
import { API_PREFIX } from '../../../core/api/clients/api-paths';
import { quietContext } from '../../../core/http/http-context';
import type {
  CreatedServiceRequest,
  SaveServiceRequestDraftBody,
  ServiceRequestDetails,
  ServiceRequestDraft,
  ServiceRequestDraftFields,
  ServiceRequestDraftStep,
  ServiceRequestId,
  SubmittedServiceRequest,
} from './service-request.models';

const SERVICE_REQUESTS_ROOT = `${API_PREFIX}/service-requests`;

function requestPath(id: ServiceRequestId): string {
  return `${SERVICE_REQUESTS_ROOT}/${encodeURIComponent(id)}`;
}

/** The typed Service Request endpoints, relative to the API root (no leading slash). */
export const ServiceRequestApiPaths = {
  Create: SERVICE_REQUESTS_ROOT,
  details: requestPath,
  draft: (id: ServiceRequestId): string => `${requestPath(id)}/draft`,
  submit: (id: ServiceRequestId): string => `${requestPath(id)}/submit`,
} as const;

/** What `saveDraft` sends: the fields the wizard holds and the step the server validates. */
export interface SaveServiceRequestDraft {
  readonly fields: ServiceRequestDraftFields;
  readonly step: ServiceRequestDraftStep;
}

/**
 * The Service Request sample client (typed draft/submit/details API), a thin wrapper over `ApiService`. Every call is
 * quiet (no error dialog): the wizard and the detail page show a refusal inline with its reference code (a 400
 * `VALIDATION_FAILED` maps to fields with `serviceRequestFieldErrorsFrom`). A 401 still ends the session.
 *
 * Resubmit, cancel and the staff decisions are not here: they go through the generic
 * `POST api/records/service-request/{id}/transitions` (shared/records `RecordsClient`).
 */
@Injectable({ providedIn: 'root' })
export class ServiceRequestsClient {
  private readonly api = inject(ApiService);

  /** Creates an empty draft owned by the caller (`service-requests.create`); 201 with its id. */
  create(): Observable<CreatedServiceRequest> {
    return this.api.post<CreatedServiceRequest>(ServiceRequestApiPaths.Create, {}, { context: quietContext() });
  }

  /** The draft's fields, to resume the wizard. 404 when it is not the caller's draft. */
  getDraft(id: ServiceRequestId): Observable<ServiceRequestDraft> {
    return this.api.get<ServiceRequestDraft>(ServiceRequestApiPaths.draft(id), { context: quietContext() });
  }

  /** Saves the draft (204). The server validates only `step` (400 `VALIDATION_FAILED` with field errors). */
  saveDraft(id: ServiceRequestId, draft: SaveServiceRequestDraft): Observable<void> {
    const body: SaveServiceRequestDraftBody = { ...draft.fields, step: draft.step };
    return this.api.put<void>(ServiceRequestApiPaths.draft(id), body, { context: quietContext() });
  }

  /**
   * Submits the draft: 200 with the allocated number. Refusals: 400 `VALIDATION_FAILED` (every step re-validated),
   * 409 `ATTACHMENTS_REQUIRED_MISSING` (no required document linked), 409 `RECORDS_NOT_SUBMITTABLE`.
   */
  submit(id: ServiceRequestId): Observable<SubmittedServiceRequest> {
    return this.api.post<SubmittedServiceRequest>(ServiceRequestApiPaths.submit(id), {}, { context: quietContext() });
  }

  /** The request's own fields for the detail cards. */
  get(id: ServiceRequestId): Observable<ServiceRequestDetails> {
    return this.api.get<ServiceRequestDetails>(ServiceRequestApiPaths.details(id), { context: quietContext() });
  }
}
