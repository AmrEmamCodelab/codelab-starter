import { InjectionToken } from '@angular/core';
import { tryAsId } from '@codelab/ng-security';
import type { ServiceRequestId } from '../data';

/**
 * The requester area of the Service Request sample: its mount path in the requester shell and its child paths. Every
 * link the requester screens build comes from here (no route literal anywhere else).
 */
export const SERVICE_REQUESTS_REQUESTER_PATH = 'service-requests';

/** The route parameter that carries the request's PublicId (bound to the pages' `id` input). */
export const SERVICE_REQUEST_ID_PARAM = 'id';

/** The child paths under {@link SERVICE_REQUESTS_REQUESTER_PATH}. */
export const ServiceRequestsRequesterPaths = {
  /** The requester dashboard (`/service-requests`). */
  Dashboard: '',
  /** My requests, paged (`/service-requests/mine`). */
  Mine: 'mine',
  /** A new request: the wizard on a fresh draft (`/service-requests/new`). */
  New: 'new',
  /** Resume a saved draft in the wizard (`/service-requests/drafts/:id`). */
  Drafts: 'drafts',
  /** One request's detail (`/service-requests/:id`). */
  Detail: `:${SERVICE_REQUEST_ID_PARAM}`,
} as const;

const SEPARATOR = '/';

/** The area's absolute link when the requester shell is mounted at the app root. */
export const SERVICE_REQUESTS_REQUESTER_DEFAULT_BASE_LINK = `${SEPARATOR}${SERVICE_REQUESTS_REQUESTER_PATH}`;

/**
 * The absolute router link of the requester area. The default assumes the requester shell is mounted at the app root;
 * an app that mounts it under a prefix provides this token with that prefix (`/my/service-requests`).
 */
export const SERVICE_REQUESTS_REQUESTER_BASE_LINK = new InjectionToken<string>('SERVICE_REQUESTS_REQUESTER_BASE_LINK', {
  providedIn: 'root',
  factory: () => SERVICE_REQUESTS_REQUESTER_DEFAULT_BASE_LINK,
});

/** The requester links of the area, built from its base link. Ids are encoded as one path segment each. */
export interface ServiceRequestsRequesterLinks {
  readonly dashboard: string;
  readonly mine: string;
  readonly create: string;
  readonly draft: (id: string) => string;
  readonly detail: (id: string) => string;
}

/** The links under `base` (no trailing slash). */
export function serviceRequestsRequesterLinks(base: string): ServiceRequestsRequesterLinks {
  const under = (...segments: string[]): string => [base, ...segments].join(SEPARATOR);
  return {
    dashboard: base,
    mine: under(ServiceRequestsRequesterPaths.Mine),
    create: under(ServiceRequestsRequesterPaths.New),
    draft: (id) => under(ServiceRequestsRequesterPaths.Drafts, encodeURIComponent(id)),
    detail: (id) => under(encodeURIComponent(id)),
  };
}

/** The id domain of a service request token (the `ServiceRequestId` brand). */
const SERVICE_REQUEST_ID_DOMAIN = 'service-request';

/** A route parameter as a service request id, or null when it is absent or not shaped like one. */
export function serviceRequestIdOf(raw: string | null | undefined): ServiceRequestId | null {
  return tryAsId(SERVICE_REQUEST_ID_DOMAIN, raw);
}
