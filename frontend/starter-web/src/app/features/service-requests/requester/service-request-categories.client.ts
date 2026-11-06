import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../../core/api/api.service';
import { API_PREFIX } from '../../../core/api/clients/api-paths';
import { quietContext } from '../../../core/http/http-context';
import { AppLanguages, type AppLanguage } from '../../../core/i18n/language';
import { SERVICE_REQUEST_CATEGORY_LOOKUP_TYPE, type ServiceRequestCategoryId } from '../data';

/** One active request category, as `GET api/lookups/service-request-categories` lists it (both names). */
export interface ServiceRequestCategory {
  readonly id: ServiceRequestCategoryId;
  readonly code: string;
  readonly nameAr: string;
  readonly nameEn: string;
  readonly sortOrder: number | string;
}

/** The public lookup endpoint of the request categories, relative to the API root. */
export const SERVICE_REQUEST_CATEGORIES_PATH = `${API_PREFIX}/lookups/${encodeURIComponent(SERVICE_REQUEST_CATEGORY_LOOKUP_TYPE)}`;

/**
 * The request categories for the wizard's category select (`GET api/lookups/{type}`, active values in display
 * order), a thin wrapper over `ApiService`. Quiet: the wizard shows a failed load inline.
 */
@Injectable({ providedIn: 'root' })
export class ServiceRequestCategoriesClient {
  private readonly api = inject(ApiService);

  list(): Observable<readonly ServiceRequestCategory[]> {
    return this.api.get<readonly ServiceRequestCategory[]>(SERVICE_REQUEST_CATEGORIES_PATH, { context: quietContext() });
  }
}

/** The category's name in `language`. */
export function categoryName(category: ServiceRequestCategory, language: AppLanguage): string {
  return language === AppLanguages.English ? category.nameEn : category.nameAr;
}
