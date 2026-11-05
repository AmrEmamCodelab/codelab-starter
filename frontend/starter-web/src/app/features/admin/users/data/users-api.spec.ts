import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import type { Observable } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TEST_API_BASE_URL, provideTestAppConfig } from '../../../../core/config/app-config.testing';
import { SKIP_ERROR_DIALOG } from '../../../../core/http/http-context';
import { BROWSER_STORAGE } from '../../../../core/session/app-storage';
import { MemoryStorage } from '../../../../core/session/memory-storage';
import type { OrganizationId } from '../../organizations/data/organization.models';
import { AccountBinding, UserKind, type UserId, type UserListQuery } from './user.models';
import { UserApiPaths, UsersAdminClient, userListParams } from './users-api';

const url = (path: string) => `${TEST_API_BASE_URL}/${path}`;
const ID = 'user-1' as UserId;
const QUERY: UserListQuery = { search: '', organizationId: null, kind: null, isActive: null, page: 1, pageSize: 20 };

describe('UsersAdminClient', () => {
  let http: HttpTestingController;
  let client: UsersAdminClient;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideTestAppConfig(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: BROWSER_STORAGE, useValue: new MemoryStorage() },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(UsersAdminClient);
  });

  afterEach(() => http.verify());

  it('lists quietly with every filter that is set', () => {
    const query = { ...QUERY, search: 'sa', organizationId: 'org-1' as OrganizationId, kind: UserKind.ExternalUser, isActive: true };
    client.list(query).subscribe();
    const request = http.expectOne((candidate) => candidate.url === url(UserApiPaths.Users));
    expect(request.request.params.toString()).toBe('page=1&pageSize=20&search=sa&organizationId=org-1&kind=2&isActive=true');
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    expect(userListParams(QUERY).keys()).toEqual(['page', 'pageSize']);
  });

  it('creates with the flat body and updates by id', () => {
    const body = {
      displayName: 'Sara',
      email: null,
      loginName: null,
      upn: null,
      kind: UserKind.InternalUser,
      binding: AccountBinding.Password,
      organizationId: null,
    };
    client.create({ userName: 'sara', ...body }).subscribe();
    const created = http.expectOne(url(UserApiPaths.Users));
    expect(created.request.method).toBe('POST');
    expect(created.request.body).toEqual({ userName: 'sara', ...body });
    client.update(ID, body).subscribe();
    const updated = http.expectOne(url(UserApiPaths.user(ID)));
    expect(updated.request.method).toBe('PUT');
    expect(updated.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
  });

  it.each([
    ['deactivate', 'api/admin/users/user-1/deactivate'],
    ['reactivate', 'api/admin/users/user-1/reactivate'],
    ['unlock', 'api/admin/users/user-1/unlock'],
    ['issueActivation', 'api/admin/users/user-1/activation'],
  ] as const)('%s posts to %s with no body', (method, path) => {
    (client[method](ID) as Observable<unknown>).subscribe();
    const request = http.expectOne(url(path));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
  });
});
