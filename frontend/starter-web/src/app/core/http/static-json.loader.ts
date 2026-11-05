import { InjectionToken } from '@angular/core';

/**
 * Fetches a static JSON file served with the app (runtime config, public translation bundles).
 *
 * It lives in core/http because it is raw transport: these files are not the API, carry no session and
 * are not signed, so they bypass `ApiService` on purpose. `cache: 'no-store'` because a deployment
 * overwrites them in place.
 */
export type StaticJsonFetch = (path: string) => Promise<unknown>;

/** Status reported when the request itself failed (network, CORS) and there is no HTTP status. */
export const NO_RESPONSE_STATUS = 0;

/** A static file did not arrive, or was not JSON. */
export class StaticFileLoadError extends Error {
  constructor(
    readonly path: string,
    readonly status: number,
  ) {
    super(`Static file ${path} could not be loaded (HTTP ${status}).`);
    this.name = 'StaticFileLoadError';
  }
}

/** @throws StaticFileLoadError when the file does not arrive or is not JSON. */
export const fetchStaticJson: StaticJsonFetch = async (path: string) => {
  let response: Response;
  try {
    response = await fetch(path, { cache: 'no-store' });
  } catch {
    throw new StaticFileLoadError(path, NO_RESPONSE_STATUS);
  }
  if (!response.ok) {
    throw new StaticFileLoadError(path, response.status);
  }
  try {
    return (await response.json()) as unknown;
  } catch {
    throw new StaticFileLoadError(path, response.status);
  }
};

/** The static-file fetch services use; specs replace it. */
export const STATIC_JSON_FETCH = new InjectionToken<StaticJsonFetch>('STATIC_JSON_FETCH', {
  providedIn: 'root',
  factory: () => fetchStaticJson,
});
