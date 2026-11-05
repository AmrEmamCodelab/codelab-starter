import { HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../../../core/api/api.service';
import { API_PREFIX } from '../../../../core/api/clients/api-paths';
import { quietContext } from '../../../../core/http/http-context';
import type {
  AccessUser,
  CatalogPermission,
  CreateRoleBody,
  PutUserGrantsBody,
  RoleDetail,
  RoleId,
  RoleSummary,
  UpdateRoleBody,
  UserGrants,
} from './access.models';

const ACCESS_ADMIN_ROOT = `${API_PREFIX}/admin/access`;

/** The Access administration endpoints, relative to the API root (no leading slash). */
export const AccessApiPaths = {
  Permissions: `${ACCESS_ADMIN_ROOT}/permissions`,
  Roles: `${ACCESS_ADMIN_ROOT}/roles`,
  Users: `${ACCESS_ADMIN_ROOT}/users`,
  role: (id: RoleId): string => `${ACCESS_ADMIN_ROOT}/roles/${encodeURIComponent(id)}`,
  userGrants: (userId: string): string => `${ACCESS_ADMIN_ROOT}/users/${encodeURIComponent(userId)}/grants`,
} as const;

/** Query parameters of the Access administration endpoints. */
export const AccessApiQueryParams = {
  Search: 'search',
} as const;

/**
 * The Access administration client (roles, the permission catalog, user grants), a thin wrapper over
 * `ApiService`. Reads keep the default interceptors (a 403 opens the 403 page, a fault opens the error dialog);
 * writes are quiet because the screens show their refusals inline (field errors, the last-manager confirmation).
 */
@Injectable({ providedIn: 'root' })
export class AccessAdminClient {
  private readonly api = inject(ApiService);

  permissions(): Observable<readonly CatalogPermission[]> {
    return this.api.get<readonly CatalogPermission[]>(AccessApiPaths.Permissions);
  }

  roles(): Observable<readonly RoleSummary[]> {
    return this.api.get<readonly RoleSummary[]>(AccessApiPaths.Roles);
  }

  role(id: RoleId): Observable<RoleDetail> {
    return this.api.get<RoleDetail>(AccessApiPaths.role(id));
  }

  createRole(body: CreateRoleBody): Observable<RoleDetail> {
    return this.api.post<RoleDetail>(AccessApiPaths.Roles, body, { context: quietContext() });
  }

  updateRole(id: RoleId, body: UpdateRoleBody): Observable<RoleDetail> {
    return this.api.put<RoleDetail>(AccessApiPaths.role(id), body, { context: quietContext() });
  }

  /** At most 20 accounts matching `search` (the server's cap), active and deactivated. */
  searchUsers(search: string): Observable<readonly AccessUser[]> {
    const params = new HttpParams().set(AccessApiQueryParams.Search, search);
    return this.api.get<readonly AccessUser[]>(AccessApiPaths.Users, { params });
  }

  /** `userId` is the user's PublicId from any user list (the Access and Users modules share the label). */
  userGrants(userId: string): Observable<UserGrants> {
    return this.api.get<UserGrants>(AccessApiPaths.userGrants(userId));
  }

  putUserGrants(userId: string, body: PutUserGrantsBody): Observable<UserGrants> {
    return this.api.put<UserGrants>(AccessApiPaths.userGrants(userId), body, { context: quietContext() });
  }
}
