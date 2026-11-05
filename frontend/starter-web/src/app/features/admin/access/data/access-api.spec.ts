import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { provideTestAppConfig, TEST_API_BASE_URL } from '../../../../core/config/app-config.testing';
import { SKIP_ERROR_DIALOG } from '../../../../core/http/http-context';
import { BROWSER_STORAGE } from '../../../../core/session/app-storage';
import { MemoryStorage } from '../../../../core/session/memory-storage';
import { ROLE_REVIEWER_ID, USER_ID } from '../access.testing';
import { AccessAdminClient, AccessApiPaths } from './access-api';
import { PermissionScope } from './access.models';

const url = (path: string) => `${TEST_API_BASE_URL}/${path}`;

describe('AccessAdminClient', () => {
  let http: HttpTestingController;
  let client: AccessAdminClient;

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
    client = TestBed.inject(AccessAdminClient);
  });

  it('uses relative paths under api/admin/access', () => {
    expect(AccessApiPaths.Permissions).toBe('api/admin/access/permissions');
    expect(AccessApiPaths.role(ROLE_REVIEWER_ID)).toBe(`api/admin/access/roles/${ROLE_REVIEWER_ID}`);
    expect(AccessApiPaths.userGrants(USER_ID)).toBe(`api/admin/access/users/${USER_ID}/grants`);
  });

  it('reads the catalog, the roles and one role with the default interceptors', () => {
    client.permissions().subscribe();
    client.roles().subscribe();
    client.role(ROLE_REVIEWER_ID).subscribe();
    for (const path of [AccessApiPaths.Permissions, AccessApiPaths.Roles, AccessApiPaths.role(ROLE_REVIEWER_ID)]) {
      const request = http.expectOne(url(path));
      expect(request.request.method).toBe('GET');
      expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(false);
    }
  });

  it('creates and updates roles quietly (the screens show the refusal)', () => {
    client.createRole({ code: 'c', nameEn: 'n', nameAr: 'ن', permissions: [] }).subscribe();
    const create = http.expectOne(url(AccessApiPaths.Roles));
    expect(create.request.method).toBe('POST');
    expect(create.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);

    client.updateRole(ROLE_REVIEWER_ID, {
      nameEn: 'n', nameAr: 'ن', permissions: ['dashboard.view'], isActive: true, confirmBreakGlassOnly: false,
    }).subscribe();
    const update = http.expectOne(url(AccessApiPaths.role(ROLE_REVIEWER_ID)));
    expect(update.request.method).toBe('PUT');
    expect(update.request.body.permissions).toEqual(['dashboard.view']);
    expect(update.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
  });

  it('searches users with the search parameter', () => {
    client.searchUsers('sara').subscribe();
    const request = http.expectOne((candidate) => candidate.url === url(AccessApiPaths.Users));
    expect(request.request.params.get('search')).toBe('sara');
  });

  it('reads and replaces a user\'s grants', () => {
    client.userGrants(USER_ID).subscribe();
    expect(http.expectOne(url(AccessApiPaths.userGrants(USER_ID))).request.method).toBe('GET');

    client.putUserGrants(USER_ID, {
      roles: [{ roleId: ROLE_REVIEWER_ID, scope: PermissionScope.OwnOrganization }],
      permissions: [],
      confirmBreakGlassOnly: false,
    }).subscribe();
    const put = http.expectOne(url(AccessApiPaths.userGrants(USER_ID)));
    expect(put.request.method).toBe('PUT');
    expect(put.request.body.roles).toEqual([{ roleId: ROLE_REVIEWER_ID, scope: PermissionScope.OwnOrganization }]);
    expect(put.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
  });
});
