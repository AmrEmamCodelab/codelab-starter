import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TEST_API_BASE_URL, provideTestAppConfig } from '../../../../core/config/app-config.testing';
import { SKIP_ERROR_DIALOG } from '../../../../core/http/http-context';
import { BROWSER_STORAGE } from '../../../../core/session/app-storage';
import { MemoryStorage } from '../../../../core/session/memory-storage';
import { lookupItem, lookupType } from '../lookups.testing';
import { LookupApiPaths, LookupsAdminClient } from './lookups-api';

const url = (path: string) => `${TEST_API_BASE_URL}/${path}`;

describe('LookupsAdminClient', () => {
  let http: HttpTestingController;
  let client: LookupsAdminClient;

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
    client = TestBed.inject(LookupsAdminClient);
  });

  afterEach(() => http.verify());

  it('lists the types quietly', () => {
    let answer: unknown;
    client.types().subscribe((types) => (answer = types));
    const request = http.expectOne(url('api/admin/lookups'));
    expect(request.request.method).toBe('GET');
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    request.flush([lookupType()]);
    expect(answer).toEqual([lookupType()]);
  });

  it('lists the values of a type, escaping the type key', () => {
    client.items('contact topics').subscribe();
    const request = http.expectOne(url('api/admin/lookups/contact%20topics'));
    expect(request.request.method).toBe('GET');
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    request.flush([lookupItem()]);
  });

  it('activates and deactivates a value with an isActive body', () => {
    client.setActive('contact-topics', 3, false).subscribe();
    const deactivate = http.expectOne(url(LookupApiPaths.itemActive('contact-topics', 3)));
    expect(deactivate.request.url).toBe(url('api/admin/lookups/contact-topics/items/3/active'));
    expect(deactivate.request.method).toBe('PUT');
    expect(deactivate.request.body).toEqual({ isActive: false });
    expect(deactivate.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    deactivate.flush(null, { status: 204, statusText: 'No Content' });

    client.setActive('contact-topics', 3, true).subscribe();
    expect(http.expectOne(url('api/admin/lookups/contact-topics/items/3/active')).request.body).toEqual({ isActive: true });
  });
});
