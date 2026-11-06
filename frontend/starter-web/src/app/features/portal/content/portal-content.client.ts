import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../../core/api/api.service';
import { API_PREFIX } from '../../../core/api/clients/api-paths';
import { selfHandledContext } from '../../../core/http/http-context';

/** The portal pages whose content the server keeps (the `{page}` segment of `GET api/portal/content/{page}`). */
export const PortalContentPages = {
  Faq: 'faq',
  ServiceGuide: 'service-guide',
  Privacy: 'privacy',
  Terms: 'terms',
} as const;

export type PortalContentPage = (typeof PortalContentPages)[keyof typeof PortalContentPages];

const CONTENT_PATH = `${API_PREFIX}/portal/content`;
const PATH_SEPARATOR = '/';

/** `api/portal/content/<page>`, relative to the API root (no leading slash). */
export function portalContentPath(page: PortalContentPage): string {
  return `${CONTENT_PATH}${PATH_SEPARATOR}${encodeURIComponent(page)}`;
}

/**
 * One bilingual section as the server sends it. Titles and bodies are plain text; a body's paragraphs are separated by
 * a blank line. `sortOrder` orders the sections (ascending).
 */
export interface PortalContentSectionDto {
  readonly id: string;
  readonly titleAr: string;
  readonly titleEn: string;
  readonly bodyAr: string;
  readonly bodyEn: string;
  readonly sortOrder: number;
}

/** `GET api/portal/content/{page}`: the page key, when it was last edited (ISO 8601 UTC) and its sections. */
export interface PortalContentDto {
  readonly page: PortalContentPage;
  readonly updatedAt: string | null;
  readonly sections: readonly PortalContentSectionDto[];
}

/**
 * Reads the public portal content through `ApiService`. The call is anonymous (no bearer, unsigned: the pages work
 * without a session) and self-handled: a page shows its own error state, never the app-wide error dialog, and a 401
 * never ends a session.
 */
@Injectable({ providedIn: 'root' })
export class PortalContentClient {
  private readonly api = inject(ApiService);

  /** The content of one page; errors arrive as the `HttpErrorResponse` (404 for an unknown page, 5xx, status 0). */
  page(page: PortalContentPage): Observable<PortalContentDto> {
    return this.api.get<PortalContentDto>(portalContentPath(page), {
      anonymous: true,
      context: selfHandledContext(),
    });
  }
}
