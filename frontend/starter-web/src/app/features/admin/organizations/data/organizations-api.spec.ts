import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TEST_API_BASE_URL, provideTestAppConfig } from '../../../../core/config/app-config.testing';
import { SKIP_ERROR_DIALOG } from '../../../../core/http/http-context';
import { BROWSER_STORAGE } from '../../../../core/session/app-storage';
import { MemoryStorage } from '../../../../core/session/memory-storage';
import { OrganizationKind, type OrganizationId, type OrganizationListQuery } from './organization.models';
import { OrganizationApiPaths, OrganizationsAdminClient, organizationListParams } from './organizations-api';

const url = (path: string) => `${TEST_API_BASE_URL}/${path}`;
const ID = 'org/1' as OrganizationId;
const QUERY: OrganizationListQuery = { search: '', kind: null, isActive: null, page: 1, pageSize: 20 };

describe('OrganizationsAdminClient', () => {
  let http: HttpTestingController;
  let client: OrganizationsAdminClient;

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
    client = TestBed.inject(OrganizationsAdminClient);
  });

  afterEach(() => http.verify());

  it('lists quietly with the page and only the filters that are set', () => {
    client.list({ ...QUERY, search: 'hq', kind: OrganizationKind.ExternalOrganization, isActive: false, page: 2 }).subscribe();
    const request = http.expectOne((candidate) => candidate.url === url(OrganizationApiPaths.Organizations));
    expect(request.request.method).toBe('GET');
    expect(request.request.params.toString()).toBe('page=2&pageSize=20&search=hq&kind=2&isActive=false');
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
  });

  it('leaves empty filters out of the query string', () => {
    expect(organizationListParams(QUERY).keys()).toEqual(['page', 'pageSize']);
  });

  it('creates and updates with the body, escaping the id', () => {
    const body = { code: 'HQ', nameEn: 'Head office', nameAr: 'Arabic name', kind: OrganizationKind.InternalOrganization };
    client.create(body).subscribe();
    const created = http.expectOne(url(OrganizationApiPaths.Organizations));
    expect(created.request.method).toBe('POST');
    expect(created.request.body).toEqual(body);
    expect(created.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);

    client.update(ID, body).subscribe();
    const updated = http.expectOne(url('api/admin/organizations/org%2F1'));
    expect(updated.request.method).toBe('PUT');
    expect(updated.request.body).toEqual(body);
  });

  it('deactivates and reactivates with an empty body', () => {
    client.deactivate(ID).subscribe();
    const deactivate = http.expectOne(url(OrganizationApiPaths.deactivate(ID)));
    expect(deactivate.request.method).toBe('POST');
    expect(deactivate.request.body).toBeNull();
    client.reactivate(ID).subscribe();
    expect(http.expectOne(url('api/admin/organizations/org%2F1/reactivate')).request.method).toBe('POST');
  });
});
