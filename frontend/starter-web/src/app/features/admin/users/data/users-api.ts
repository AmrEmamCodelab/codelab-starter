import { HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../../../core/api/api.service';
import { API_PREFIX } from '../../../../core/api/clients/api-paths';
import { quietContext } from '../../../../core/http/http-context';
import type {
  Activation,
  AdminUser,
  CreateUserBody,
  CreatedUser,
  UpdateUserBody,
  UserId,
  UserListQuery,
  UserPage,
} from './user.models';

const USERS_ROOT = `${API_PREFIX}/admin/users`;

function userPath(id: UserId): string {
  return `${USERS_ROOT}/${encodeURIComponent(id)}`;
}

/** The user administration endpoints, relative to the API root (no leading slash). */
export const UserApiPaths = {
  Users: USERS_ROOT,
  user: userPath,
  deactivate: (id: UserId): string => `${userPath(id)}/deactivate`,
  reactivate: (id: UserId): string => `${userPath(id)}/reactivate`,
  unlock: (id: UserId): string => `${userPath(id)}/unlock`,
  activation: (id: UserId): string => `${userPath(id)}/activation`,
} as const;

/** Query parameter names of `GET api/admin/users`. */
export const UserApiQueryParams = {
  Search: 'search',
  OrganizationId: 'organizationId',
  Kind: 'kind',
  IsActive: 'isActive',
  Page: 'page',
  PageSize: 'pageSize',
} as const;

/** The query string of a list request; empty filters are left out. */
export function userListParams(query: UserListQuery): HttpParams {
  let params = new HttpParams().set(UserApiQueryParams.Page, query.page).set(UserApiQueryParams.PageSize, query.pageSize);
  if (query.search !== '') {
    params = params.set(UserApiQueryParams.Search, query.search);
  }
  if (query.organizationId !== null) {
    params = params.set(UserApiQueryParams.OrganizationId, query.organizationId);
  }
  if (query.kind !== null) {
    params = params.set(UserApiQueryParams.Kind, query.kind);
  }
  if (query.isActive !== null) {
    params = params.set(UserApiQueryParams.IsActive, query.isActive);
  }
  return params;
}

/**
 * The user administration client (`users.view` / `users.manage`), a thin wrapper over `ApiService`. Every call is
 * quiet (no error dialog): the list shows its own error state with the reference code, and the dialogs show their
 * refusals inline. A 401 still ends the session.
 */
@Injectable({ providedIn: 'root' })
export class UsersAdminClient {
  private readonly api = inject(ApiService);

  list(query: UserListQuery): Observable<UserPage> {
    return this.api.get<UserPage>(UserApiPaths.Users, { params: userListParams(query), context: quietContext() });
  }

  /** 201 with the account and, for a password binding, its first activation link. */
  create(body: CreateUserBody): Observable<CreatedUser> {
    return this.api.post<CreatedUser>(UserApiPaths.Users, body, { context: quietContext() });
  }

  update(id: UserId, body: UpdateUserBody): Observable<AdminUser> {
    return this.api.put<AdminUser>(UserApiPaths.user(id), body, { context: quietContext() });
  }

  deactivate(id: UserId): Observable<void> {
    return this.api.post<void>(UserApiPaths.deactivate(id), null, { context: quietContext() });
  }

  reactivate(id: UserId): Observable<void> {
    return this.api.post<void>(UserApiPaths.reactivate(id), null, { context: quietContext() });
  }

  unlock(id: UserId): Observable<void> {
    return this.api.post<void>(UserApiPaths.unlock(id), null, { context: quietContext() });
  }

  /** A new activation link (password binding only); it clears any password and ends the user's sessions. */
  issueActivation(id: UserId): Observable<Activation> {
    return this.api.post<Activation>(UserApiPaths.activation(id), null, { context: quietContext() });
  }
}
