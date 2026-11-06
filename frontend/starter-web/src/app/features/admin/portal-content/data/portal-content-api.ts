import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../../../core/api/api.service';
import { API_PREFIX } from '../../../../core/api/clients/api-paths';
import { quietContext } from '../../../../core/http/http-context';
import type { PortalContentDocument, PortalContentPage, ReplacePortalContentBody } from './portal-content.models';

const CONTENT_ROOT = `${API_PREFIX}/admin/portal/content`;

/** The portal content administration endpoints, relative to the API root (no leading slash). */
export const PortalContentApiPaths = {
  page: (page: PortalContentPage): string => `${CONTENT_ROOT}/${encodeURIComponent(page)}`,
} as const;

/**
 * The portal content administration client (`portal.content.view` / `portal.content.manage`), a thin wrapper over
 * `ApiService`. Quiet calls: the editor shows a failed load or a refused save inline with its reference code.
 */
@Injectable({ providedIn: 'root' })
export class PortalContentAdminClient {
  private readonly api = inject(ApiService);

  get(page: PortalContentPage): Observable<PortalContentDocument> {
    return this.api.get<PortalContentDocument>(PortalContentApiPaths.page(page), { context: quietContext() });
  }

  /** Replaces the page's sections with `body` (the full ordered list) and answers the stored page. */
  replace(page: PortalContentPage, body: ReplacePortalContentBody): Observable<PortalContentDocument> {
    return this.api.put<PortalContentDocument>(PortalContentApiPaths.page(page), body, { context: quietContext() });
  }
}
