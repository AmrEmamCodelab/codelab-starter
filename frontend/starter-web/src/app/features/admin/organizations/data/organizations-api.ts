import { HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../../../core/api/api.service';
import { API_PREFIX } from '../../../../core/api/clients/api-paths';
import { quietContext } from '../../../../core/http/http-context';
import type {
  CreateOrganizationBody,
  Organization,
  OrganizationId,
  OrganizationListQuery,
  OrganizationPage,
  UpdateOrganizationBody,
} from './organization.models';

const ORGANIZATIONS_ROOT = `${API_PREFIX}/admin/organizations`;

function organizationPath(id: OrganizationId): string {
  return `${ORGANIZATIONS_ROOT}/${encodeURIComponent(id)}`;
}

/** The organization administration endpoints, relative to the API root (no leading slash). */
export const OrganizationApiPaths = {
  Organizations: ORGANIZATIONS_ROOT,
  organization: organizationPath,
  deactivate: (id: OrganizationId): string => `${organizationPath(id)}/deactivate`,
  reactivate: (id: OrganizationId): string => `${organizationPath(id)}/reactivate`,
} as const;

/** Query parameter names of `GET api/admin/organizations`. */
export const OrganizationApiQueryParams = {
  Search: 'search',
  Kind: 'kind',
  IsActive: 'isActive',
  Page: 'page',
  PageSize: 'pageSize',
} as const;

/** The page size the user editor asks for when it lists organizations to pick from (the server's maximum). */
export const ORGANIZATION_PICKER_PAGE_SIZE = 100;

/** The query string of a list request; empty filters are left out. */
export function organizationListParams(query: OrganizationListQuery): HttpParams {
  let params = new HttpParams()
    .set(OrganizationApiQueryParams.Page, query.page)
    .set(OrganizationApiQueryParams.PageSize, query.pageSize);
  if (query.search !== '') {
    params = params.set(OrganizationApiQueryParams.Search, query.search);
  }
  if (query.kind !== null) {
    params = params.set(OrganizationApiQueryParams.Kind, query.kind);
  }
  if (query.isActive !== null) {
    params = params.set(OrganizationApiQueryParams.IsActive, query.isActive);
  }
  return params;
}

/**
 * The organization administration client (`organizations.view` / `organizations.manage`), a thin wrapper over
 * `ApiService`. Every call is quiet (no error dialog): the list shows its own error state with the
 * reference code and the editors show their refusals inline (field errors, conflicts). A 401 still ends the session.
 */
@Injectable({ providedIn: 'root' })
export class OrganizationsAdminClient {
  private readonly api = inject(ApiService);

  list(query: OrganizationListQuery): Observable<OrganizationPage> {
    return this.api.get<OrganizationPage>(OrganizationApiPaths.Organizations, {
      params: organizationListParams(query),
      context: quietContext(),
    });
  }

  create(body: CreateOrganizationBody): Observable<Organization> {
    return this.api.post<Organization>(OrganizationApiPaths.Organizations, body, { context: quietContext() });
  }

  update(id: OrganizationId, body: UpdateOrganizationBody): Observable<Organization> {
    return this.api.put<Organization>(OrganizationApiPaths.organization(id), body, { context: quietContext() });
  }

  deactivate(id: OrganizationId): Observable<void> {
    return this.api.post<void>(OrganizationApiPaths.deactivate(id), null, { context: quietContext() });
  }

  reactivate(id: OrganizationId): Observable<void> {
    return this.api.post<void>(OrganizationApiPaths.reactivate(id), null, { context: quietContext() });
  }
}
